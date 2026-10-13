using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Network;

/// <summary>Windows DNS-SD registration includes an explicit host A record, not just service browsing.</summary>
public sealed class WindowsMdnsPublisher : IAsyncDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Completion(uint status, IntPtr context, IntPtr instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct Request
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr Instance;
        public IntPtr Callback;
        public IntPtr Context;
        public IntPtr Credentials;
        public int Unicast;
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string service, string host, ref uint ipv4,
        IntPtr ipv6, ushort port, ushort priority, ushort weight, uint count,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceRegister(ref Request request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceDeRegister(ref Request request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(IntPtr instance);

    private Request _request;
    private Completion? _callback;

    public async Task RegisterAsync(LanAddress address, string hostName, string deviceId, ushort port)
    {
        var ipv4 = BitConverter.ToUInt32(address.Address.GetAddressBytes());
        var instance = DnsServiceConstructInstance($"syncclip-{deviceId}._syncclipboard._tcp.local",
            hostName, ref ipv4, IntPtr.Zero, port, 0, 0, 3,
            ["version", "deviceId", "port"], ["1", deviceId, port.ToString()]);
        if (instance == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot construct mDNS instance");
        }
        _request = new Request { Version = 1, InterfaceIndex = address.InterfaceIndex, Instance = instance };
        try
        {
            await InvokeAsync(false);
        }
        catch
        {
            DnsServiceFreeInstance(instance);
            _request.Instance = IntPtr.Zero;
            throw;
        }
    }

    private async Task InvokeAsync(bool remove)
    {
        var completion = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _callback = (status, _, instance) =>
        {
            if (instance != IntPtr.Zero)
            {
                DnsServiceFreeInstance(instance);
            }
            completion.TrySetResult(status);
        };
        _request.Callback = Marshal.GetFunctionPointerForDelegate(_callback);
        var result = remove ? DnsServiceDeRegister(ref _request, IntPtr.Zero)
            : DnsServiceRegister(ref _request, IntPtr.Zero);
        if (result != 9506) // DNS_REQUEST_PENDING. Callback owns completion; keep delegate alive until then.
        {
            throw new Win32Exception((int)result);
        }
        var status = await completion.Task;
        GC.KeepAlive(_callback);
        if (status != 0)
        {
            throw new Win32Exception((int)status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_request.Instance == IntPtr.Zero)
        {
            return;
        }
        try
        {
            await InvokeAsync(true);
        }
        finally
        {
            DnsServiceFreeInstance(_request.Instance);
            _request.Instance = IntPtr.Zero;
        }
    }
}
