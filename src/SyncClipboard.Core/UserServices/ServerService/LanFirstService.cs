using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
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
    public string Status { get; private set; } = "Stopped";
    public string Addresses { get; private set; } = string.Empty;

    protected override void StartService()
    {
        // Persist identity once, before any broadcast. Never regenerate on a network change.
        configManager.SetConfig(configManager.GetConfig<LanFirstConfig>());
        _lifetime = new();
        _worker = RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        WebApplication? app = null;
        var publishers = new List<BonjourMdnsPublisher>();
        string previous = string.Empty;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var config = configManager.GetConfig<LanFirstConfig>();
                var addresses = config.Enabled ? LanNetworkPolicy.GetAddresses(config.AllowPublicNetworks) : [];
                var signature = $"{config.Enabled}/{config.Port}/{config.DeviceId}/{config.AllowPublicNetworks}/"
                    + string.Join(',', addresses.Select(item => $"{item.Address}:{item.InterfaceIndex}"));
                if (signature != previous || publishers.Any(publisher => !publisher.IsRunning))
                {
                    if (app is not null)
                    {
                        await app.StopAsync(CancellationToken.None);
                        await app.DisposeAsync();
                        app = null;
                    }
                    foreach (var publisher in publishers)
                    {
                        try
                        {
                            await publisher.DisposeAsync();
                        }
                        catch (Exception ex)
                        {
                            logger.Write("LAN", $"mDNS deregistration failed: {ex.GetType().Name}");
                        }
                    }
                    publishers.Clear();
                    Addresses = string.Join(", ", addresses.Select(item => item.Address));
                    Status = config.Enabled ? "No permitted network / Offline" : "Disabled";
                    if (addresses.Count > 0)
                    {
                        try
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
                                LanNetworkPolicy.GetAddresses(configManager.GetConfig<LanFirstConfig>().AllowPublicNetworks)
                                    .Any(item => item.Address.Equals(local)));
                            await app.StartAsync(token);
                            foreach (var address in addresses)
                            {
                                var publisher = new BonjourMdnsPublisher();
                                publishers.Add(publisher);
                                await publisher.RegisterAsync(address.Address, address.InterfaceIndex,
                                    config.HostName, config.Port, config.DeviceId, token);
                            }
                            Status = "LAN listening; Bonjour host and service registered";
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            Status = $"LAN / mDNS error: {ex.GetType().Name}";
                            logger.Write("LAN", Status);
                        }
                    }
                    previous = Status.StartsWith("LAN / mDNS error:") ? string.Empty : signature;
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
                await app.StopAsync(CancellationToken.None);
                await app.DisposeAsync();
            }
            foreach (var publisher in publishers)
            {
                try
                {
                    await publisher.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.Write("LAN", $"mDNS stop failed: {ex.GetType().Name}");
                }
            }
            Status = "Stopped";
        }
    }

    protected override async Task StopSerivceAsync()
    {
        _lifetime?.Cancel();
        if (_worker is not null)
        {
            await _worker;
        }
        _lifetime?.Dispose();
        _lifetime = null;
    }
}
