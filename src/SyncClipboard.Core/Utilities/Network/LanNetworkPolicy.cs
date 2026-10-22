using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Network;

public sealed record LanAddress(IPAddress Address, uint InterfaceIndex);

public static class LanNetworkPolicy
{
    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetNetwork();
        bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        uint GetConnectivity();
        Guid GetConnectionId();
        Guid GetAdapterId();
        uint GetDomainType();
    }

    // Fail closed if Windows cannot determine network categories. No SSID/location permission needed.
    public static IReadOnlyList<LanAddress> GetAddresses(bool allowPublic, string? interfaceId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        Guid? selectedAdapter = null;
        if (!string.IsNullOrWhiteSpace(interfaceId))
        {
            if (!Guid.TryParse(interfaceId, out var adapter))
            {
                return [];
            }
            selectedAdapter = adapter;
        }
        var trusted = new HashSet<Guid>();
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            manager = Activator.CreateInstance(type!);
            foreach (dynamic connection in ((dynamic)manager!).GetNetworkConnections())
            {
                try
                {
                    dynamic network = connection.GetNetwork();
                    try
                    {
                        int category = network.GetCategory();
                        if (allowPublic || category == 1) // Private only; domain/public require opt-in.
                        {
                            // GUID return values cannot be invoked through COM IDispatch on Windows.
                            trusted.Add(((INetworkConnection)connection).GetAdapterId());
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(network);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(connection);
                }
            }
        }
        catch
        {
            return [];
        }
        finally
        {
            if (manager is not null)
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                && Guid.TryParse(item.Id, out var id) && trusted.Contains(id)
                && (selectedAdapter is null || selectedAdapter == id))
            .SelectMany(item => item.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254."))
                .Select(address => new LanAddress(address.Address, (uint)item.GetIPProperties().GetIPv4Properties().Index)))
            .ToArray();
    }
}
