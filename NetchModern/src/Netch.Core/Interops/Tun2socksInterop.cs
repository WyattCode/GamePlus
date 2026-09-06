using System.Runtime.InteropServices;
using System.Text;

namespace Netch.Core.Interops;

/// <summary>
///     P/Invoke bindings for <c>tun2socks.bin</c>, used by TUN mode to turn L3 packets from the WinTUN
///     adapter into SOCKS5. Ported from the original Netch <c>Netch/Interops/tun2socks.cs</c>.
/// </summary>
public static partial class Tun2socksInterop
{
    private const string Dll = "tun2socks.bin";

    public enum NameList
    {
        TYPE_BYPBIND,
        TYPE_BYPLIST,
        TYPE_DNSADDR,
        TYPE_ADAPMTU,
        TYPE_TCPREST,
        TYPE_TCPTYPE,
        TYPE_TCPHOST,
        TYPE_TCPUSER,
        TYPE_TCPPASS,
        TYPE_TCPMETH,
        TYPE_TCPPROT,
        TYPE_TCPPRPA,
        TYPE_TCPOBFS,
        TYPE_TCPOBPA,
        TYPE_UDPREST,
        TYPE_UDPTYPE,
        TYPE_UDPHOST,
        TYPE_UDPUSER,
        TYPE_UDPPASS,
        TYPE_UDPMETH,
        TYPE_UDPPROT,
        TYPE_UDPPRPA,
        TYPE_UDPOBFS,
        TYPE_UDPOBPA
    }

    public static bool Dial(NameList name, string value) => tun_dial(name, Encoding.UTF8.GetBytes(value + '\0'));

    public static bool Init() => tun_init();

    public static Task<bool> FreeAsync() => Task.Run(() => tun_free());

    public static ulong Luid() => tun_luid();

    public static ulong GetUploadBytes() => tun_getUP();

    public static ulong GetDownloadBytes() => tun_getDL();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool tun_dial(NameList name, byte[] value);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool tun_init();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool tun_free();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    public static partial ulong tun_luid();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    private static partial ulong tun_getUP();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    private static partial ulong tun_getDL();
}
