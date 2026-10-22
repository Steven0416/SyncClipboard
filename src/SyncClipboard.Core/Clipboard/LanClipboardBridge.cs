using CommunityToolkit.Mvvm.Messaging;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

/// <summary>Uses the existing OS clipboard implementation and lock order. Does not install a listener.</summary>
public sealed class LanClipboardBridge(IClipboardFactory factory, LocalClipboardSetter setter,
    ConfigManager configManager, IMessenger messenger)
{
    private readonly Dictionary<Guid, (LanRequest Request, LanResponse Response)> _completed = [];
    private readonly Queue<Guid> _order = [];
    private readonly SemaphoreSlim _requests = new(1, 1);
    public string LastTransport { get; private set; } = "Offline / no successful transfer";

    public async Task<LanResponse> ExecuteAsync(LanRequest request, CancellationToken token, string transport = "LAN")
    {
        var config = configManager.GetConfig<LanFirstConfig>();
        if (!LanProtocol.IsValid(request))
        {
            return new(request.RequestId, config.DeviceId, "invalid-request");
        }
        await _requests.WaitAsync(token);
        try
        {
            if (_completed.TryGetValue(request.RequestId, out var completed))
            {
                return completed.Request == request ? completed.Response with { IsCache = true }
                    : new(request.RequestId, config.DeviceId, "request-id-conflict");
            }
            await SyncService.remoteProfilemutex.WaitAsync(token);
            try
            {
                await LocalClipboard.Semaphore.WaitAsync(token);
                try
                {
                    var meta = await factory.GetMetaInfomation(token);
                    var sync = configManager.GetConfig<SyncConfig>();
                    if (!sync.EnableUploadText || !sync.IgnoreExcludeForSyncSuggestion && meta.ExcludeForSync == true
                        || sync.DoNotUploadWhenCut && (meta.Effects & DragDropEffects.Move) == DragDropEffects.Move
                        || ClipboardOwnerFilterHelper.ShouldFilter(configManager.GetConfig<ClipboardOwnerFilterConfig>(), meta.Owner))
                    {
                        return new(request.RequestId, config.DeviceId, "filtered");
                    }
                    var current = await factory.CreateProfileFromMeta(meta, token);
                    if (current is not TextProfile)
                    {
                        return new(request.RequestId, config.DeviceId, "unsupported-type");
                    }
                    var dto = await current.ToProfileDto(token);
                    if (dto.Text.Length > LanProtocol.MaxTextLength)
                    {
                        return new(request.RequestId, config.DeviceId, "too-large");
                    }
                    if (request.Operation == "set")
                    {
                        if (!string.Equals(request.ExpectedHash, dto.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            return new(request.RequestId, config.DeviceId, "conflict", dto);
                        }
                        // Re-read immediately before writing: the OS can change outside our semaphore.
                        var latest = await factory.CreateProfileFromLocal(token);
                        if (!await Profile.Same(current, latest, token))
                        {
                            return new(request.RequestId, config.DeviceId, "conflict");
                        }
                        var incoming = new TextProfile(request.Text!);
                        var applied = false;
                        messenger.Send(EmptyMessage.Instance, SyncService.PULL_START_ENENT_NAME);
                        try
                        {
                            if (!await Profile.Same(current, incoming, token))
                            {
                                await setter.Set(incoming, token, false);
                            }
                            dto = await incoming.ToProfileDto(token);
                            applied = true;
                        }
                        finally
                        {
                            messenger.Send((Profile)(applied ? incoming : current), SyncService.PULL_STOP_ENENT_NAME);
                        }
                    }
                    var result = new LanResponse(request.RequestId, config.DeviceId, "ok", dto, DateTimeOffset.UtcNow);
                    LastTransport = transport;
                    _completed.Add(request.RequestId, (request, result));
                    _order.Enqueue(request.RequestId);
                    while (_order.Count > 256)
                    {
                        _completed.Remove(_order.Dequeue());
                    }
                    return result;
                }
                finally
                {
                    LocalClipboard.Semaphore.Release();
                }
            }
            finally
            {
                SyncService.remoteProfilemutex.Release();
            }
        }
        finally
        {
            _requests.Release();
        }
    }
}
