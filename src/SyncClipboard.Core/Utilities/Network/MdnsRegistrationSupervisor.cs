namespace SyncClipboard.Core.Utilities.Network;

/// <summary>Recovers lost records even when the daemon's registration socket stays alive.</summary>
internal sealed class MdnsRegistrationSupervisor(Func<IBonjourPublisher> createPublisher,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private readonly List<IBonjourPublisher> _publishers = [];
    private string _signature = string.Empty;
    private DateTimeOffset _nextCheck;
    public string Status { get; private set; } = "Bonjour not registered";

    public async Task EnsureAsync(IReadOnlyList<LanAddress> addresses, string hostName, ushort port,
        string deviceId, bool force, CancellationToken token)
    {
        var signature = $"{hostName}/{port}/{deviceId}/"
            + string.Join(',', addresses.Select(item => $"{item.Address}:{item.InterfaceIndex}"));
        var replace = force || signature != _signature || _publishers.Count != addresses.Count
            || _publishers.Any(item => !item.IsRunning);
        try
        {
            if (!replace && timeProvider.GetUtcNow() >= _nextCheck)
            {
                for (var index = 0; index < addresses.Count; index++)
                {
                    var address = addresses[index];
                    if (!await _publishers[index].VerifyAsync(address.Address, address.InterfaceIndex, hostName, token))
                    {
                        replace = true;
                        break;
                    }
                }
                _nextCheck = timeProvider.GetUtcNow().AddSeconds(30);
            }
            if (!replace)
            {
                return;
            }
            await DisposePublishersAsync();
            Status = "Bonjour recovering / 正在恢复名称发布";
            for (var index = 0; index < addresses.Count; index++)
            {
                var publisher = createPublisher();
                _publishers.Add(publisher);
                var address = addresses[index];
                await publisher.RegisterAsync(address.Address, address.InterfaceIndex, hostName, port, deviceId, token);
                if (!await publisher.VerifyAsync(address.Address, address.InterfaceIndex, hostName, token))
                {
                    throw new IOException("Bonjour address registration could not be verified.");
                }
            }
            _signature = signature;
            _nextCheck = timeProvider.GetUtcNow().AddSeconds(30);
            Status = addresses.Count == 0 ? "Bonjour offline" : "Bonjour address verified / 名称地址已验证";
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            await DisposePublishersAsync();
            _signature = string.Empty;
            Status = $"Bonjour retrying / 等待重试: {ex.GetType().Name}";
        }
    }

    private async Task DisposePublishersAsync()
    {
        foreach (var publisher in _publishers)
        {
            try
            {
                await publisher.DisposeAsync();
            }
            catch
            {
                // A broken daemon connection must not prevent replacing the remaining registrations.
            }
        }
        _publishers.Clear();
    }

    public async ValueTask DisposeAsync() => await DisposePublishersAsync();
}
