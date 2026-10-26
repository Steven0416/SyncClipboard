using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Network;

namespace SyncClipboard.Core.UserServices.ServerService;

public sealed class LanFirstService(ConfigManager configManager, LanClipboardBridge clipboard,
    Interfaces.ILogger logger) : Service
{
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private int _republishRequested;
    public string Status { get; private set; } = "Stopped";
    public string Addresses { get; private set; } = string.Empty;

    protected override void StartService()
    {
        // Persist identity once, before any broadcast. Never regenerate on a network change.
        configManager.SetConfig(configManager.GetConfig<LanFirstConfig>());
        _lifetime = new();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        _worker = Task.Run(() => RunAsync(_lifetime.Token));
    }

    private void OnNetworkChanged(object? sender, EventArgs args) => Interlocked.Exchange(ref _republishRequested, 1);

    private async Task RunAsync(CancellationToken token)
    {
        WebApplication? app = null;
        await using var discovery = new MdnsRegistrationSupervisor(() => new BonjourMdnsPublisher(), TimeProvider.System);
        string previous = string.Empty;
        string previousStatus = string.Empty;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var config = configManager.GetConfig<LanFirstConfig>();
                    var addresses = config.Enabled
                        ? LanNetworkPolicy.GetAddresses(config.AllowPublicNetworks, config.InterfaceId) : [];
                    var signature = $"{config.Enabled}/{config.Port}/{config.DeviceId}/{config.AllowPublicNetworks}/{config.InterfaceId}/"
                        + string.Join(',', addresses.Select(item => $"{item.Address}:{item.InterfaceIndex}"));
                    if (signature != previous || app is null && addresses.Count > 0)
                    {
                        if (app is not null)
                        {
                            await StopListenerAsync(app);
                            app = null;
                        }
                        Addresses = string.Join(", ", addresses.Select(item => item.Address));
                        if (addresses.Count > 0)
                        {
                            if (config.Port == 0 || !Guid.TryParse(config.DeviceId, out _) || config.Token.Length < 64)
                            {
                                throw new InvalidOperationException("Invalid LAN configuration");
                            }
                            var builder = WebApplication.CreateSlimBuilder();
                            // HTTP diagnostics must never include authentication headers or clipboard payloads.
                            builder.Logging.ClearProviders();
                            builder.WebHost.ConfigureKestrel(options =>
                            {
                                options.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
                                foreach (var address in addresses)
                                {
                                    options.Listen(address.Address, config.Port);
                                }
                            });
                            app = builder.Build();
                            LanApi.Map(app, configManager, clipboard, local =>
                            {
                                var current = configManager.GetConfig<LanFirstConfig>();
                                return LanNetworkPolicy.GetAddresses(current.AllowPublicNetworks, current.InterfaceId)
                                    .Any(item => item.Address.Equals(local));
                            });
                            await app.StartAsync(token);
                        }
                        previous = signature;
                    }
                    // Losing Bonjour must not close a healthy HTTP listener. Recovery only replaces DNS-SD handles.
                    await discovery.EnsureAsync(addresses, config.HostName, config.Port, config.DeviceId,
                        Interlocked.Exchange(ref _republishRequested, 0) != 0, token);
                    Status = app is not null ? $"LAN listening; {discovery.Status}"
                        : config.Enabled ? "No permitted network / Offline" : "Disabled";
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    Status = $"LAN retrying: {ex.GetType().Name}";
                    previous = string.Empty;
                }
                if (Status != previousStatus)
                {
                    logger.Write("LAN", Status);
                    previousStatus = Status;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (app is not null)
            {
                await StopListenerAsync(app);
            }
            Status = "Stopped";
        }
    }

    private static async Task StopListenerAsync(WebApplication app)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    protected override async Task StopSerivceAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _lifetime?.Cancel();
        if (_worker is not null)
        {
            await _worker;
        }
        _lifetime?.Dispose();
        _lifetime = null;
    }
}
