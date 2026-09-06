using System.Runtime.InteropServices;

namespace Netch.Core.Interops;

/// <summary>
///     P/Invoke bindings for <c>Redirector.bin</c>, the native shim over the netfilter2 driver used by
///     Process mode. Ported from the original Netch <c>Netch/Interops/Redirector.cs</c>. The enum order
///     must stay byte-for-byte in sync with the native <c>AIO_TYPE</c> in <c>Redirector/Redirector.cpp</c>.
/// </summary>
public static partial class RedirectorInterop
{
    private const string Dll = "Redirector.bin";

    public enum NameList
    {
        AIO_FILTERLOOPBACK,
        AIO_FILTERINTRANET,
        AIO_FILTERPARENT,
        AIO_FILTERICMP,
        AIO_FILTERTCP,
        AIO_FILTERUDP,
        AIO_FILTERDNS,
        AIO_ICMPING,
        AIO_DNSONLY,
        AIO_DNSPROX,
        AIO_DNSHOST,
        AIO_DNSPORT,
        AIO_TGTHOST,
        AIO_TGTPORT,
        AIO_TGTUSER,
        AIO_TGTPASS,
        AIO_CLRNAME,
        AIO_ADDNAME,
        AIO_BYPNAME
    }

    public static bool Dial(NameList name, bool value) => aio_dial(name, value ? "true" : "false");

    public static bool Dial(NameList name, string value) => aio_dial(name, value);

    public static Task<bool> InitAsync() => Task.Run(aio_init);

    public static Task<bool> FreeAsync() => Task.Run(() => aio_free());

    public static ulong GetUploadBytes() => aio_getUP();

    public static ulong GetDownloadBytes() => aio_getDL();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool aio_register(string value);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool aio_unregister(string value);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool aio_dial(NameList name, string value);

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool aio_init();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool aio_free();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    private static partial ulong aio_getUP();

    [UnmanagedCallConv(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
    [LibraryImport(Dll)]
    private static partial ulong aio_getDL();
}
