using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;

namespace SyncClipboard.Server.Core.Services;

/// <summary>In-memory request routing: no clipboard upload or disk write on idle/LAN activity.</summary>
public sealed class LanRelay(IHubContext<SyncClipboardHub> hub)
{
    private sealed record Pending(string ConnectionId, TaskCompletionSource<LanResponse> Completion);
    private readonly ConcurrentDictionary<string, string> _devices = new();
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private readonly SemaphoreSlim _capacity = new(32, 32);

    public void Register(string deviceId, string connectionId)
    {
        if (!Guid.TryParse(deviceId, out _))
        {
            throw new HubException("Invalid device ID");
        }
        _devices[deviceId] = connectionId;
    }

    public void Disconnect(string connectionId)
    {
        foreach (var item in _devices.Where(item => item.Value == connectionId))
        {
            ((ICollection<KeyValuePair<string, string>>)_devices).Remove(item);
        }
        foreach (var item in _pending.Where(item => item.Value.ConnectionId == connectionId))
        {
            item.Value.Completion.TrySetException(new IOException("Device disconnected"));
        }
    }

    public void Complete(string connectionId, LanResponse response)
    {
        if (_pending.TryGetValue(response.RequestId, out var pending) && pending.ConnectionId == connectionId)
        {
            pending.Completion.TrySetResult(response);
        }
    }

    public async Task<LanResponse> RequestAsync(string deviceId, LanRequest request, CancellationToken token)
    {
        if (!LanProtocol.IsValid(request))
        {
            return new(request.RequestId, deviceId, "invalid-request");
        }
        if (!await _capacity.WaitAsync(0, token))
        {
            return new(request.RequestId, deviceId, "busy");
        }
        try
        {
            if (!_devices.TryGetValue(deviceId, out var connection))
            {
                return new(request.RequestId, deviceId, "offline");
            }
            var pending = new Pending(connection, new(TaskCreationOptions.RunContinuationsAsynchronously));
            if (!_pending.TryAdd(request.RequestId, pending))
            {
                return new(request.RequestId, deviceId, "busy");
            }
            try
            {
                await hub.Clients.Client(connection).SendAsync(LanProtocol.RequestEvent, request, token);
                return await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(12), token);
            }
            catch (TimeoutException)
            {
                return new(request.RequestId, deviceId, "timeout");
            }
            catch (IOException)
            {
                return new(request.RequestId, deviceId, "offline");
            }
            finally
            {
                _pending.TryRemove(request.RequestId, out _);
            }
        }
        finally
        {
            _capacity.Release();
        }
    }
}
