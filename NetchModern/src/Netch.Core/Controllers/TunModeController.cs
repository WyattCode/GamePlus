using System.Net.Sockets;
using Netch.Core.Abstractions;
using Netch.Core.Interops;
using Netch.Core.Models;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using static Netch.Core.Interops.Tun2socksInterop;

namespace Netch.Core.Controllers;

/// <summary>
///     TUN mode: creates a WinTUN adapter and forwards L3 packets to the local SOCKS5 via tun2socks,
///     then programs the route table (Handle → TUN, Bypass → physical). Ported from the original
///     <c>TUNController</c>. Requires <c>tun2socks.bin</c>, <c>wintun.dll</c> and <c>RouteHelper.bin</c>.
/// </summary>
public sealed class TunModeController : IModeController
{
    // Private TUN adapter addressing.
    private const string TunAddress = "10.6.7.7";
    private const string TunGateway = "10.6.7.1";
    private const string TunNetmask = "255.255.255.0";

    private Mode? _mode;
    private NetRoute _outbound;
    private NetRoute _tun;

    public string Name => "tun2socks";

    public ModeFeature Features => ModeFeature.SupportSocks5Auth;

    public Task StartAsync(Socks5Server socks5, Mode mode, CancellationToken ct = default)
    {
        Trace($"START admin={IsAdmin()} socks={socks5.Hostname}:{socks5.Port} handle={string.Join(',', mode.Handle)}");
        Trace("EnsureNativeAvailable");
        EnsureNativeAvailable();
        Trace("CheckDriver (install matching wintun.dll into System32)");
        CheckDriver();
        _mode = mode;
        Trace("GetBestRouteTemplate");
        _outbound = NetRoute.GetBestRouteTemplate();
        Trace($"outbound gw={_outbound.Gateway} idx={_outbound.InterfaceIndex}");

        Trace("dial ADAPMTU"); Dial(NameList.TYPE_ADAPMTU, "1500");
        Trace("dial BYPBIND"); Dial(NameList.TYPE_BYPBIND, _outbound.Gateway);
        Trace("dial BYPLIST"); Dial(NameList.TYPE_BYPLIST, "disabled");

        Trace("dial TCPREST"); Dial(NameList.TYPE_TCPREST, string.Empty);
        Trace("dial TCPTYPE"); Dial(NameList.TYPE_TCPTYPE, "Socks5");
        Trace("dial TCPHOST"); Dial(NameList.TYPE_TCPHOST, $"{socks5.Hostname}:{socks5.Port}");

        Trace("dial UDPREST"); Dial(NameList.TYPE_UDPREST, string.Empty);
        Trace("dial UDPTYPE"); Dial(NameList.TYPE_UDPTYPE, "Socks5");
        Trace("dial UDPHOST"); Dial(NameList.TYPE_UDPHOST, $"{socks5.Hostname}:{socks5.Port}");

        if (socks5.Auth())
        {
            Dial(NameList.TYPE_TCPUSER, socks5.Username!);
            Dial(NameList.TYPE_TCPPASS, socks5.Password!);
            Dial(NameList.TYPE_UDPUSER, socks5.Username!);
            Dial(NameList.TYPE_UDPPASS, socks5.Password!);
        }

        Trace("dial DNSADDR"); Dial(NameList.TYPE_DNSADDR, "1.1.1.1:53");

        Trace("Init (create wintun + start tun2socks)");
        if (!Init())
            throw new InvalidOperationException("tun2socks failed to start.");
        Trace("Init OK");

        Trace("SetupRouteTable");
        SetupRouteTable();
        Trace("SetupRouteTable OK — TUN up");
        return Task.CompletedTask;
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void Trace(string s)
    {
        try
        {
            var p = Path.Combine(AppPaths.DataDirectory, "tun-trace.log");
            File.AppendAllText(p, $"{DateTime.Now:HH:mm:ss.fff}  {s}\n");
        }
        catch { /* ignore */ }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        ClearRouteTable();
        await FreeAsync();
    }

    public long GetUploadBytes() => (long)Tun2socksInterop.GetUploadBytes();

    public long GetDownloadBytes() => (long)Tun2socksInterop.GetDownloadBytes();

    private void SetupRouteTable()
    {
        Trace("route: Luid");
        var luid = Luid();
        Trace($"route: ConvertLuidToIndex luid={luid}");
        var tunIndex = (int)RouteHelperInterop.ConvertLuidToIndex(luid);
        Trace($"route: tunIndex={tunIndex}");
        _tun = NetRoute.TemplateBuilder(TunGateway, tunIndex);

        Trace("route: CreateUnicastIP");
        RouteHelperInterop.CreateUnicastIP(AddressFamily.InterNetwork, TunAddress, RouteUtils.SubnetToCidr(TunNetmask), (ulong)tunIndex);

        // Rules: proxied destinations via TUN, bypassed destinations via the physical interface.
        foreach (var (network, cidr) in _mode!.Handle.Select(RouteUtils.ParseCidr))
        {
            var ok = RouteUtils.CreateRoute(_tun.FillTemplate(network, cidr));
            Trace($"route: CreateRoute {network}/{cidr} idx={_tun.InterfaceIndex} gw={_tun.Gateway} -> {ok}");
        }

        RouteUtils.CreateRouteFill(_outbound, _mode.Bypass);
        Trace("route: done");
    }

    private void ClearRouteTable()
    {
        if (_mode != null && _outbound.Gateway != null)
            RouteUtils.DeleteRouteFill(_outbound, _mode.Bypass);
    }

    /// <summary>
    ///     tun2socks.bin loads wintun.dll from System32. Make sure the copy there is the exact version we
    ///     ship — a mismatched wintun.dll left by another VPN (WireGuard/Tailscale/…) crashes tun_init.
    /// </summary>
    private static void CheckDriver()
    {
        var baseDir = AppContext.BaseDirectory;
        var src = File.Exists(Path.Combine(baseDir, "bin", "wintun.dll"))
            ? Path.Combine(baseDir, "bin", "wintun.dll")
            : Path.Combine(baseDir, "wintun.dll");
        var sys = Path.Combine(Environment.SystemDirectory, "wintun.dll");

        try
        {
            if (File.Exists(sys) && HashEquals(src, sys))
            {
                Trace("wintun.dll in System32 already matches");
                return;
            }

            File.Copy(src, sys, true);
            Trace("wintun.dll installed into System32");
        }
        catch (Exception e)
        {
            // A different wintun.dll may be locked by another running VPN.
            Trace($"CheckDriver copy failed: {e.Message}");
            throw new InvalidOperationException(
                $"无法安装 wintun.dll 到系统目录（可能被其它 VPN 占用）：{e.Message}。请先关闭其它加速器/VPN 再试。");
        }
    }

    private static bool HashEquals(string a, string b)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return sha.ComputeHash(fa).AsSpan().SequenceEqual(sha.ComputeHash(fb));
        }
        catch { return false; }
    }

    private static void EnsureNativeAvailable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[] { "tun2socks.bin", "RouteHelper.bin", "wintun.dll" };
        var missing = candidates.Where(f =>
            !File.Exists(Path.Combine(baseDir, "bin", f)) && !File.Exists(Path.Combine(baseDir, f))).ToList();

        if (missing.Count > 0)
            throw new FileNotFoundException(
                $"TUN mode needs native components in .\\bin: {string.Join(", ", missing)}");
    }
}
