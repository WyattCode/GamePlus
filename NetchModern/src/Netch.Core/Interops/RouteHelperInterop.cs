using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Netch.Core.Interops;

/// <summary>
///     P/Invoke bindings for <c>RouteHelper.bin</c> (route-table / unicast-IP helpers used by TUN mode),
///     plus a direct <c>iphlpapi!GetBestRoute</c> import to discover the default gateway.
///     Ported from the original Netch <c>Netch/Interops/RouteHelper.cs</c>.
/// </summary>
public static partial class RouteHelperInterop
{
    private const string Dll = "RouteHelper.bin";

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    public static partial ulong ConvertLuidToIndex(ulong id);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateUnicastIP(AddressFamily inet, string address, byte cidr, ulong index);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RefreshIPTable(AddressFamily inet, ulong index);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateRoute(AddressFamily inet, string address, byte cidr, string gateway, ulong index, int metric);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteRoute(AddressFamily inet, string address, byte cidr, string gateway, ulong index, int metric);

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPFORWARDROW
    {
        public uint dwForwardDest;
        public uint dwForwardMask;
        public uint dwForwardPolicy;
        public uint dwForwardNextHop;
        public uint dwForwardIfIndex;
        public uint dwForwardType;
        public uint dwForwardProto;
        public uint dwForwardAge;
        public uint dwForwardNextHopAS;
        public uint dwForwardMetric1;
        public uint dwForwardMetric2;
        public uint dwForwardMetric3;
        public uint dwForwardMetric4;
        public uint dwForwardMetric5;
    }

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetBestRoute(uint dwDestAddr, uint dwSourceAddr, out MIB_IPFORWARDROW pBestRoute);
}
