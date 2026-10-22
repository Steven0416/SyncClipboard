using System.Net.NetworkInformation;
using System.Net.Sockets;
using SyncClipboard.Core.Utilities.Network;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Windows with Apple Bonjour is required.");
    return 1;
}
var options = new Dictionary<string, string>();
for (var index = 0; index < args.Length; index += 2)
{
    if (index + 1 == args.Length || !args[index].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: --interface-id UUID --hostname clipbridge-test.local --port 5033 --state-dir PATH");
        return 1;
    }
    options[args[index]] = args[index + 1];
}
var hostName = options.GetValueOrDefault("--hostname", "clipbridge-test.local");
if (!Guid.TryParse(options.GetValueOrDefault("--interface-id"), out var adapterId)
    || !ushort.TryParse(options.GetValueOrDefault("--port", "5033"), out var port) || port == 0
    || !BonjourMdnsPublisher.IsValidHostName(hostName))
{
    Console.Error.WriteLine("Invalid adapter ID, hostname or port.");
    return 1;
}
var stateDirectory = Path.GetFullPath(options.GetValueOrDefault("--state-dir", AppContext.BaseDirectory));
Directory.CreateDirectory(stateDirectory);
var identityPath = Path.Combine(stateDirectory, "device-id.txt");
var deviceId = File.Exists(identityPath) ? File.ReadAllText(identityPath).Trim() : Guid.NewGuid().ToString("N");
if (!Guid.TryParse(deviceId, out _))
{
    Console.Error.WriteLine("Invalid persisted device identity.");
    return 1;
}
File.WriteAllText(identityPath, deviceId);
using var instance = new Semaphore(1, 1, "Local\\ClipBridge.Discovery.Lease." + hostName);
if (!instance.WaitOne(0))
{
    Console.Error.WriteLine("An announcement process for this hostname is already running.");
    return 1;
}
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    lifetime.Cancel();
};
BonjourMdnsPublisher? publisher = null;
var previous = string.Empty;
try
{
    while (!lifetime.IsCancellationRequested)
    {
        if (File.Exists(Path.Combine(stateDirectory, "stop.request")))
        {
            File.Delete(Path.Combine(stateDirectory, "stop.request"));
            break;
        }
        try
        {
            var network = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
                Guid.TryParse(item.Id, out var id) && id == adapterId && item.OperationalStatus == OperationalStatus.Up);
            var properties = network?.GetIPProperties();
            var address = properties?.UnicastAddresses.Select(item => item.Address).FirstOrDefault(item =>
                item.AddressFamily == AddressFamily.InterNetwork && !item.ToString().StartsWith("169.254."));
            var interfaceIndex = (uint)(properties?.GetIPv4Properties()?.Index ?? 0);
            var signature = address is null ? "offline" : $"{interfaceIndex}/{address}";
            if (signature != previous || publisher is { IsRunning: false })
            {
                if (publisher is not null)
                {
                    await publisher.DisposeAsync();
                    publisher = null;
                }
                if (address is not null)
                {
                    publisher = new();
                    await publisher.RegisterAsync(address, interfaceIndex, hostName, port, deviceId, lifetime.Token);
                    Console.WriteLine($"{DateTimeOffset.Now:O} {hostName}:{port} -> {address}; {publisher.Status}");
                    await File.WriteAllTextAsync(Path.Combine(stateDirectory, "status.txt"),
                        $"{hostName}:{port}\n{address}\n{publisher.Status}\n", lifetime.Token);
                }
                else
                {
                    Console.WriteLine("Selected adapter offline; announcement withdrawn.");
                    await File.WriteAllTextAsync(Path.Combine(stateDirectory, "status.txt"), "offline\n", lifetime.Token);
                }
                previous = signature;
            }
        }
        catch (Exception ex) when (!lifetime.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Discovery failed: {ex.GetType().Name}: {ex.Message}");
            previous = string.Empty;
        }
        await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
{
}
finally
{
    if (publisher is not null)
    {
        await publisher.DisposeAsync();
    }
    instance.Release();
}
return 0;
