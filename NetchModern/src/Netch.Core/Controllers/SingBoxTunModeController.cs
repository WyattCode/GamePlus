using Netch.Core.Abstractions;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

namespace Netch.Core.Controllers;

/// <summary>
///     TUN mode: launches a second <c>sing-box.exe</c> instance that owns a WinTUN interface and routes
///     per process, per sniffed domain and per IP. Matching traffic goes to the node core's local SOCKS5,
///     everything else goes direct. sing-box embeds wintun, so no <c>wintun.dll</c> is needed.
/// </summary>
public sealed class SingBoxTunModeController : ProcessGuard, IModeController, ITunRoutingTarget
{
    private string? _configPath;

    /// <summary>The full TUN routing (process / domain / IP rules). Must be set before <see cref="StartAsync" />.</summary>
    public TunRouting? Routing { get; set; }

    protected override string ExecutablePath { get; } = ResolveSingBox();

    public string Name => "sing-box TUN";

    // IPv6 is not captured by the TUN, so SupportIPv6 would be misleading.
    public ModeFeature Features => ModeFeature.SupportSocks5Auth;

    public async Task StartAsync(Socks5Server socks5, Mode mode, CancellationToken ct = default)
    {
        if (Routing is not { IsEmpty: false })
            throw new InvalidOperationException("TUN 模式没有可用的进程、域名或 IP 规则");

        var dir = Path.Combine(AppPaths.DataDirectory, "data");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "singbox-tun.json");

        Trace($"START admin={IsAdmin()} socks={socks5.Hostname}:{socks5.Port} " +
              $"processes={Routing.ProxyProcesses.Count} domains={Routing.ProxyDomains.Count} ips={Routing.ProxyIps.Count}");

        var config = SingBoxTunConfigGenerator.Generate(Routing, socks5);
        await File.WriteAllTextAsync(_configPath, config, ct);

        Trace($"launch: {ExecutablePath} run -c \"{_configPath}\"");
        StartProcess($"run -c \"{_configPath}\"");

        // Readiness: sing-box logs "sing-box started" once all inbounds are up.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var started = false;
        while (DateTime.UtcNow < deadline)
        {
            if (HasExited)
                break;

            if (RecentOutput.Any(l => l.Contains("sing-box started", StringComparison.OrdinalIgnoreCase)))
            {
                started = true;
                break;
            }

            await Task.Delay(100, ct);
        }

        if (!started)
        {
            var tail = string.Join("\n", RecentOutput.TakeLast(8));
            Trace("start failed" + (HasExited ? " (process exited)" : " (timeout)") + ": " + tail.Replace('\n', ' '));
            await StopProcessAsync(ct);

            var denied = RecentOutput.Any(l =>
                l.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("administrator", StringComparison.OrdinalIgnoreCase));

            throw new InvalidOperationException(
                (denied
                    ? "TUN 模式需要管理员权限：请以管理员身份运行 Game+。"
                    : "sing-box TUN 启动失败：") + $"\n{tail}");
        }

        Trace("sing-box TUN up");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await StopProcessAsync(ct);

        // Best-effort cleanup so stale configs don't pile up in the data folder.
        try
        {
            if (_configPath != null && File.Exists(_configPath))
                File.Delete(_configPath);
        }
        catch
        {
            // ignored
        }
    }

    private static string ResolveSingBox()
    {
        var inBin = Path.Combine(AppContext.BaseDirectory, "bin", "sing-box.exe");
        return File.Exists(inBin) ? inBin : Path.Combine(AppContext.BaseDirectory, "sing-box.exe");
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
        TraceLog.Append(Path.Combine(AppPaths.DataDirectory, "tun-trace.log"), s);
    }
}
