using System.Diagnostics;
using Netch.Core.Abstractions;
using Netch.Core.Interops;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using static Netch.Core.Interops.RedirectorInterop;

namespace Netch.Core.Controllers;

/// <summary>
///     Process mode: routes matched processes' traffic through the netfilter2 driver to the local SOCKS5.
///     Ported from the original <c>NFController</c>. Installs/updates the netfilter2 kernel driver
///     (<c>nfdriver.sys</c> → <c>System32\drivers\netfilter2.sys</c>, registered as the "netfilter2"
///     service) before starting, then runs the Dial → Init sequence and traffic counters.
/// </summary>
public sealed class RedirectorModeController : IModeController
{
    private const string ServiceName = "netfilter2";

    public string Name => "Redirector";

    public ModeFeature Features => ModeFeature.SupportIPv6 | ModeFeature.SupportSocks5Auth;

    public async Task StartAsync(Socks5Server socks5, Mode mode, CancellationToken ct = default)
    {
        Trace($"START admin={IsAdmin()} socks={socks5.Hostname}:{socks5.Port} handle={string.Join(',', mode.Handle)}");
        EnsureNativeAvailable();

        // The netfilter2 driver must be installed as a service before aio_init can attach to it.
        Trace("CheckDriver (install/register netfilter2)");
        CheckDriver();

        // Filters (sensible defaults; the original sources these from settings).
        Dial(NameList.AIO_FILTERLOOPBACK, false);
        Dial(NameList.AIO_FILTERINTRANET, false);
        Dial(NameList.AIO_FILTERTCP, true);
        Dial(NameList.AIO_FILTERUDP, true);
        Dial(NameList.AIO_FILTERDNS, true);

        // Target local SOCKS5 outbound.
        Dial(NameList.AIO_TGTHOST, socks5.Hostname);
        Dial(NameList.AIO_TGTPORT, socks5.Port.ToString());
        Dial(NameList.AIO_TGTUSER, socks5.Username ?? string.Empty);
        Dial(NameList.AIO_TGTPASS, socks5.Password ?? string.Empty);

        // Rules.
        Dial(NameList.AIO_CLRNAME, string.Empty);
        foreach (var rule in mode.Bypass)
            Dial(NameList.AIO_BYPNAME, rule);
        foreach (var rule in mode.Handle)
            Dial(NameList.AIO_ADDNAME, rule);
        Trace($"dialed {mode.Handle.Count} handle + {mode.Bypass.Count} bypass rule(s)");

        Trace("Init (attach netfilter2)");
        if (!await InitAsync())
            throw new InvalidOperationException(
                "进程模式启动失败：netfilter2 驱动无法加载。请确认以管理员运行，且系统未被“内核隔离/驱动签名强制”阻止（可在 Windows 安全中心关闭“内核隔离-内存完整性”后重试）。");
        Trace("Init OK — process capture up");
    }

    public Task StopAsync(CancellationToken ct = default) => FreeAsync();

    public long GetUploadBytes() => (long)RedirectorInterop.GetUploadBytes();

    public long GetDownloadBytes() => (long)RedirectorInterop.GetDownloadBytes();

    /// <summary>
    ///     Ensures <c>System32\drivers\netfilter2.sys</c> matches the bundled <c>nfdriver.sys</c> and the
    ///     "netfilter2" service is registered. Mirrors the original NFController.CheckDriver/InstallDriver.
    /// </summary>
    private static void CheckDriver()
    {
        var builtIn = ResolveNative("nfdriver.sys");
        var system = Path.Combine(Environment.SystemDirectory, "drivers", $"{ServiceName}.sys");

        var builtInVer = FileVersion(builtIn);
        var systemVer = FileVersion(system);
        Trace($"driver: built-in={builtInVer} installed={systemVer}");

        var needInstall = !File.Exists(system) || !string.Equals(builtInVer, systemVer, StringComparison.OrdinalIgnoreCase);
        if (!needInstall)
        {
            Trace("driver: up-to-date, ensuring registration");
            // File matches but the service may not be registered (e.g. cleaned manually) — register is idempotent.
            aio_register(ServiceName);
            return;
        }

        try
        {
            if (File.Exists(system))
            {
                Trace("driver: version differs, unregister + delete old");
                try { aio_unregister(ServiceName); } catch { /* ignore */ }
                try { File.Delete(system); } catch { /* ignore */ }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(system)!);
            File.Copy(builtIn, system, true);
            Trace("driver: copied nfdriver.sys → System32\\drivers\\netfilter2.sys");

            if (aio_register(ServiceName))
                Trace("driver: aio_register OK");
            else
                Trace("driver: aio_register returned false");
        }
        catch (Exception e)
        {
            Trace($"driver: install failed: {e.Message}");
            throw new InvalidOperationException(
                $"无法安装 netfilter2 驱动（可能被其它加速器占用或权限不足）：{e.Message}。请以管理员运行并关闭其它加速器后重试。");
        }
    }

    private static string ResolveNative(string file)
    {
        var inBin = Path.Combine(AppContext.BaseDirectory, "bin", file);
        return File.Exists(inBin) ? inBin : Path.Combine(AppContext.BaseDirectory, file);
    }

    private static string FileVersion(string path)
    {
        try { return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "" : ""; }
        catch { return ""; }
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
        TraceLog.Append(Path.Combine(AppPaths.DataDirectory, "redirector-trace.log"), s);
    }

    private static void EnsureNativeAvailable()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var f in new[] { "Redirector.bin", "nfdriver.sys" })
            if (!File.Exists(Path.Combine(baseDir, "bin", f)) && !File.Exists(Path.Combine(baseDir, f)))
                throw new FileNotFoundException(
                    $"进程模式需要 .\\bin 下的原生组件：缺少 {f}。");
    }
}
