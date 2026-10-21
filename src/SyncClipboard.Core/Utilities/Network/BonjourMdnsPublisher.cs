using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace SyncClipboard.Core.Utilities.Network;

/// <summary>Publishes an explicit host A record and DNS-SD service using an installed Apple Bonjour daemon.</summary>
public sealed class BonjourMdnsPublisher : IAsyncDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RecordReply(IntPtr service, IntPtr record, uint flags, int error, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ServiceReply(IntPtr service, uint flags, int error, IntPtr name,
        IntPtr type, IntPtr domain, IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        public UIntPtr Socket;
        public short Events;
        public short ReturnedEvents;
    }

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DNSServiceCreateConnection(out IntPtr service);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DNSServiceRegisterRecord(IntPtr service, out IntPtr record, uint flags,
        uint interfaceIndex, [MarshalAs(UnmanagedType.LPWStr)] string name, ushort type, ushort recordClass,
        ushort dataLength, byte[] data, uint ttl, RecordReply callback, IntPtr context);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DNSServiceRegister(out IntPtr service, uint flags, uint interfaceIndex,
        [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string type,
        [MarshalAs(UnmanagedType.LPWStr)] string domain, [MarshalAs(UnmanagedType.LPWStr)] string host,
        ushort networkPort, ushort txtLength, byte[] txt, ServiceReply callback, IntPtr context);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern UIntPtr DNSServiceRefSockFD(IntPtr service);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DNSServiceProcessResult(IntPtr service);

    [DllImport("dnssd.dll", CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void DNSServiceRefDeallocate(IntPtr service);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSAPoll([In, Out] PollDescriptor[] descriptors, uint count, int timeout);

    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private RecordReply? _recordCallback;
    private ServiceReply? _serviceCallback;
    private int _registrationCount;
    public string Status { get; private set; } = "Stopped";
    public bool IsRunning => _worker is { IsCompleted: false } && _ready.Task.IsCompletedSuccessfully;

    public static bool IsValidHostName(string hostName)
    {
        if (!hostName.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var label = hostName[..^6];
        return label.Length is > 0 and <= 63 && char.IsAsciiLetterOrDigit(label[0])
            && char.IsAsciiLetterOrDigit(label[^1])
            && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    public async Task RegisterAsync(IPAddress address, uint interfaceIndex, string hostName, ushort port,
        string deviceId, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Apple Bonjour Windows publisher requires Windows.");
        }
        if (_worker is not null)
        {
            throw new InvalidOperationException("Publisher already started.");
        }
        if (address.AddressFamily != AddressFamily.InterNetwork || interfaceIndex == 0 || port == 0
            || !IsValidHostName(hostName) || !Guid.TryParse(deviceId, out _))
        {
            throw new ArgumentException("An IPv4 address, explicit interface, .local hostname, port and UUID are required.");
        }
        _worker = Task.Run(() => Run(address, interfaceIndex, hostName, port, deviceId), token);
        try
        {
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private void OnRegistered(int error)
    {
        if (error != 0)
        {
            Status = error == -65548 ? "Hostname/service name conflict" : $"Bonjour error {error}";
            _ready.TrySetException(new InvalidOperationException(Status));
            _stop.Cancel();
            return;
        }
        if (++_registrationCount == 2)
        {
            Status = "Bonjour host A and DNS-SD service registered";
            _ready.TrySetResult();
        }
    }

    private void Run(IPAddress address, uint interfaceIndex, string hostName, ushort port, string deviceId)
    {
        IntPtr host = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;
        try
        {
            _recordCallback = (_, _, _, error, _) => OnRegistered(error);
            _serviceCallback = (_, _, error, _, _, _, _) => OnRegistered(error);
            Check(DNSServiceCreateConnection(out host));
            // Unique A record: Bonjour probes ownership and reports conflicts instead of silently renaming.
            Check(DNSServiceRegisterRecord(host, out _, 0x20, interfaceIndex, hostName + ".", 1, 1,
                4, address.GetAddressBytes(), 120, _recordCallback, IntPtr.Zero));
            var txt = BuildTxtRecord(deviceId, port);
            Check(DNSServiceRegister(out service, 0x8, interfaceIndex, "ClipBridge-" + deviceId,
                "_syncclipboard._tcp", "local.", hostName + ".",
                unchecked((ushort)IPAddress.HostToNetworkOrder((short)port)), (ushort)txt.Length, txt,
                _serviceCallback, IntPtr.Zero));
            var references = new[] { host, service };
            var descriptors = references.Select(reference => new PollDescriptor
            {
                Socket = DNSServiceRefSockFD(reference),
                Events = 0x0300 // POLLRDNORM | POLLRDBAND
            }).ToArray();
            while (!_stop.IsCancellationRequested)
            {
                var count = WSAPoll(descriptors, (uint)descriptors.Length, 250);
                if (count < 0)
                {
                    throw new SocketException(Marshal.GetLastPInvokeError());
                }
                for (var index = 0; index < descriptors.Length && !_stop.IsCancellationRequested; index++)
                {
                    if ((descriptors[index].ReturnedEvents & 0x0007) != 0)
                    {
                        throw new IOException("Bonjour daemon connection closed.");
                    }
                    if ((descriptors[index].ReturnedEvents & 0x0300) != 0)
                    {
                        Check(DNSServiceProcessResult(references[index]));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Status = ex is DllNotFoundException ? "Apple Bonjour dnssd.dll is not installed" : ex.Message;
            _ready.TrySetException(ex);
        }
        finally
        {
            // Deallocate on the same worker as ProcessResult: no native callback/free race.
            if (service != IntPtr.Zero)
            {
                DNSServiceRefDeallocate(service);
            }
            if (host != IntPtr.Zero)
            {
                DNSServiceRefDeallocate(host);
            }
            GC.KeepAlive(_recordCallback);
            GC.KeepAlive(_serviceCallback);
        }
    }

    internal static byte[] BuildTxtRecord(string deviceId, ushort port)
    {
        using var stream = new MemoryStream();
        foreach (var text in new[] { "version=1", "deviceId=" + deviceId, "port=" + port })
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.WriteByte(checked((byte)bytes.Length));
            stream.Write(bytes);
        }
        return stream.ToArray();
    }

    private static void Check(int error)
    {
        if (error != 0)
        {
            throw new InvalidOperationException($"Bonjour API error {error}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_worker is not null)
        {
            await _worker;
        }
    }
}
