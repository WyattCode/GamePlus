using System.Net;
using System.Text.RegularExpressions;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

namespace Netch.Core.Services.Diagnostics;

/// <summary>
///     Runs a set of independent environment checks and explains each result in plain Chinese with a
///     suggested action. A failing check never aborts the run — it becomes an Error item instead.
/// </summary>
public sealed class DiagnosticsService
{
    /// <summary>Platform domains commonly rewritten by accelerator tools such as Steam++ (Watt Toolkit).</summary>
    private static readonly string[] CommonPlatformDomains =
    {
        "steampowered.com", "steamcommunity.com", "steamcontent.com", "steamstatic.com",
        "epicgames.com", "battle.net", "blizzard.com", "riotgames.com", "ea.com",
        "ubisoft.com", "xboxlive.com"
    };

    private readonly IDiagnosticEnvironment _env;

    public DiagnosticsService(IDiagnosticEnvironment env) => _env = env;

    /// <summary>Runs every check. Network checks run concurrently; each check is isolated.</summary>
    public async Task<DiagnosticReport> RunAsync(DiagnosticContext ctx, CancellationToken ct)
    {
        var items = new List<DiagnosticItem>();

        items.Add(Guard("core", "加速核心", CheckCore));
        items.Add(Guard("admin", "管理员权限", CheckAdmin));
        items.Add(Guard("netfilter", "进程模式驱动", CheckNetfilter));
        items.Add(Guard("wintun", "TUN 模式组件", CheckWintun));
        items.Add(Guard("port", "本地端口", CheckPort));
        items.Add(Guard("hosts", "hosts 文件", () => CheckHosts(ctx)));
        items.Add(Guard("firewall", "Windows 防火墙", () => CheckFirewall(ctx)));
        items.Add(Guard("netfilterLoad", "进程模式驱动加载", () => CheckNetfilterLoad(ctx)));

        // Network checks run concurrently — DNS resolution and node probing can each take seconds.
        var dnsTask = GuardAsync("dns", "DNS 解析", () => CheckDnsAsync(ctx, ct));
        var nodeTask = GuardAsync("node", "线路连通性", () => CheckNodeAsync(ctx, ct));
        var proxyTask = GuardAsync("proxy", "系统代理", () => Task.FromResult(CheckProxy(ctx)));

        var networkItems = await Task.WhenAll(dnsTask, nodeTask, proxyTask);
        items.AddRange(networkItems);

        var errors = items.Count(i => i.Status == DiagnosticStatus.Error);
        var warnings = items.Count(i => i.Status == DiagnosticStatus.Warning);
        var summary = errors == 0 && warnings == 0
            ? "未发现问题"
            : $"发现 {errors} 个错误、{warnings} 个警告";

        string? explanation = string.IsNullOrWhiteSpace(ctx.LastError)
            ? null
            : ErrorExplainer.Explain(ctx.LastError!);

        return new DiagnosticReport(items, summary, DateTime.UtcNow, explanation);
    }

    /// <summary>
    ///     One-click fix for a dead loopback system proxy: only acts when the proxy currently points at a
    ///     loopback port that nothing is listening on. Returns true when it disabled the proxy.
    /// </summary>
    public Task<bool> FixProxyAsync()
    {
        var (server, _, enable) = _env.ReadSystemProxy();
        if (enable == 0)
            return Task.FromResult(false);

        var port = ExtractLoopbackPort(server);
        if (port is null || _env.IsLoopbackPortListening(port.Value))
            return Task.FromResult(false);

        _env.DisableSystemProxy();
        return Task.FromResult(true);
    }

    /// <summary>Runs a check, converting any thrown exception into an Error item instead of aborting.</summary>
    private static DiagnosticItem Guard(string id, string title, Func<DiagnosticItem> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return new DiagnosticItem(id, title, DiagnosticStatus.Error, $"检测出错：{ex.Message}", null, null);
        }
    }

    /// <summary>
    ///     Runs an asynchronous check, converting any thrown exception into an Error item instead of
    ///     aborting. <see cref="OperationCanceledException"/> is rethrown when the caller's token is
    ///     cancelled so cancellation propagates rather than being reported as a check failure.
    /// </summary>
    private static async Task<DiagnosticItem> GuardAsync(string id, string title, Func<Task<DiagnosticItem>> check)
    {
        try
        {
            return await check().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DiagnosticItem(id, title, DiagnosticStatus.Error, $"检测出错：{ex.Message}", null, null);
        }
    }

    /// <summary>
    ///     NetFilter files (deliberately not shipped — commercial licence) that are missing from
    ///     <c>&lt;baseDirectory&gt;\bin</c>.
    /// </summary>
    private string[] MissingNetfilterFiles()
    {
        var bin = Path.Combine(_env.BaseDirectory, "bin");
        return ProcessModeSupport.RequiredFiles
            .Where(f => !_env.FileExists(Path.Combine(bin, f)))
            .ToArray();
    }

    private DiagnosticItem CheckCore()
    {
        var bin = Path.Combine(_env.BaseDirectory, "bin");
        var required = new[] { "xray.exe", "sing-box.exe", "geoip.dat", "geosite.dat" };
        var missing = required.Where(f => !_env.FileExists(Path.Combine(bin, f))).ToArray();

        if (missing.Length == 0)
            return new DiagnosticItem("core", "加速核心", DiagnosticStatus.Ok, "加速核心文件完整", null, null);

        return new DiagnosticItem("core", "加速核心", DiagnosticStatus.Error,
            $"缺少文件：{string.Join("、", missing)}",
            "安装包不完整，请重新安装 Game+（或检查杀毒软件是否隔离了这些文件）", null);
    }

    private DiagnosticItem CheckAdmin()
    {
        if (_env.IsAdministrator())
            return new DiagnosticItem("admin", "管理员权限", DiagnosticStatus.Ok, "已以管理员身份运行", null, null);

        return new DiagnosticItem("admin", "管理员权限", DiagnosticStatus.Warning,
            "当前未以管理员身份运行，进程模式和 TUN 模式不可用，只能使用系统代理模式（大部分游戏无法被加速）",
            "退出 Game+ 后重新打开，在系统弹出的权限确认窗口中选择\"是\"", null);
    }

    private DiagnosticItem CheckNetfilter()
    {
        var missing = MissingNetfilterFiles();

        if (missing.Length > 0)
            return new DiagnosticItem("netfilter", "进程模式驱动", DiagnosticStatus.Skipped,
                $"{ProcessModeSupport.UnavailableMessage}，「自动」模式会改用 TUN 或系统代理", null, null);

        if (!_env.IsAdministrator())
            return new DiagnosticItem("netfilter", "进程模式驱动", DiagnosticStatus.Skipped,
                "需要管理员权限才能加载驱动", null, null);

        var installed = _env.DriverFileInstalled();
        var service = _env.NetfilterServiceState();

        if (installed && service is DriverServiceState.Running or DriverServiceState.Stopped or DriverServiceState.Unknown)
            return new DiagnosticItem("netfilter", "进程模式驱动", DiagnosticStatus.Ok, "进程模式驱动已就绪", null, null);

        return new DiagnosticItem("netfilter", "进程模式驱动", DiagnosticStatus.Ok,
            "首次加速时会自动安装驱动", null, null);
    }

    private DiagnosticItem CheckWintun()
    {
        var bin = Path.Combine(_env.BaseDirectory, "bin");
        var required = new[] { "wintun.dll", "tun2socks.bin", "RouteHelper.bin" };
        var missing = required.Where(f => !_env.FileExists(Path.Combine(bin, f))).ToArray();

        if (missing.Length > 0)
            return new DiagnosticItem("wintun", "TUN 模式组件", DiagnosticStatus.Warning,
                "TUN 模式不可用",
                "安装包不完整，请重新安装 Game+（或检查杀毒软件是否隔离了这些文件）", null);

        if (!_env.IsAdministrator())
            return new DiagnosticItem("wintun", "TUN 模式组件", DiagnosticStatus.Skipped,
                "需要管理员权限才能创建虚拟网卡", null, null);

        return new DiagnosticItem("wintun", "TUN 模式组件", DiagnosticStatus.Ok, "TUN 模式组件已就绪", null, null);
    }

    private DiagnosticItem CheckPort()
    {
        if (_env.CanBindLoopback())
            return new DiagnosticItem("port", "本地端口", DiagnosticStatus.Ok, "可以在本机回环地址上监听端口", null, null);

        return new DiagnosticItem("port", "本地端口", DiagnosticStatus.Error,
            "无法在本机回环地址上监听端口，可能被安全软件拦截",
            "将 Game+ 加入安全软件白名单后重试", null);
    }

    private DiagnosticItem CheckHosts(DiagnosticContext ctx)
    {
        var content = _env.ReadHostsFile();
        if (content is null)
            return new DiagnosticItem("hosts", "hosts 文件", DiagnosticStatus.Skipped, "无法读取 hosts 文件", null, null);

        var domains = CommonPlatformDomains.ToList();
        if (ctx.Game is { } game)
            domains.AddRange(game.ProxyDomains);

        var entries = HostsFileAnalyzer.FindOverrides(content, domains);
        if (entries.Count == 0)
            return new DiagnosticItem("hosts", "hosts 文件", DiagnosticStatus.Ok, "未发现改写游戏/平台域名的记录", null, null);

        var detail = $"hosts 中有 {entries.Count} 条记录改写了游戏/平台域名：" +
                     string.Join("；", entries.Take(5).Select(e => $"第{e.Line}行 {e.Ip} {e.Host}"));

        return new DiagnosticItem("hosts", "hosts 文件", DiagnosticStatus.Warning, detail,
            "以管理员身份用记事本打开 C:\\Windows\\System32\\drivers\\etc\\hosts，删除这些行或在行首加 #，保存后运行 ipconfig /flushdns。如果是 Steam++（Watt Toolkit）等工具写入的，请先在该工具中关闭加速/恢复 hosts。", null);
    }

    private DiagnosticItem CheckFirewall(DiagnosticContext ctx)
    {
        var rules = _env.ReadFirewallBlockRules();
        if (rules is null)
            return new DiagnosticItem("firewall", "Windows 防火墙", DiagnosticStatus.Skipped, "无法读取防火墙规则", null, null);

        var programs = new List<string> { "Game+.exe", "gameplus-host.exe", "xray.exe", "sing-box.exe", "tun2socks.exe" };
        if (ctx.Game is { } game)
            programs.AddRange(game.ProxyProcesses);

        var matches = FirewallRuleMatcher.Match(rules, programs);
        if (matches.Count == 0)
            return new DiagnosticItem("firewall", "Windows 防火墙", DiagnosticStatus.Ok, "没有发现阻止 Game+ 或游戏的防火墙规则", null, null);

        var detail = string.Join("；", matches.Take(5).Select(r =>
            $"「{r.Name}」{r.Direction}阻止 {Path.GetFileName(r.ApplicationPath)}"));

        return new DiagnosticItem("firewall", "Windows 防火墙", DiagnosticStatus.Warning, detail,
            "打开 控制面板 → Windows Defender 防火墙 → 高级设置，在入站/出站规则中找到上述规则并禁用或删除；或在「允许应用通过 Windows 防火墙」中勾选 Game+ 和游戏。第三方安全软件的防火墙也可能拦截，请一并检查。", null);
    }

    private DiagnosticItem CheckNetfilterLoad(DiagnosticContext ctx)
    {
        const string suggestion =
            "netfilter 驱动可能被驱动签名强制、Windows「内存完整性」或安全软件拦截。建议在加速页把加速模式改为「TUN 模式」（WinTUN 为微软签名驱动），或以管理员身份运行；若开启了内存完整性，可在 Windows 安全中心 → 设备安全性 → 内核隔离 中关闭后重启再试。";

        if (MissingNetfilterFiles().Length > 0)
            return new DiagnosticItem("netfilterLoad", "进程模式驱动加载", DiagnosticStatus.Skipped,
                "未包含 NetFilter 组件，无需检查进程模式驱动", null, null);

        if (!string.IsNullOrWhiteSpace(ctx.LastProcessCaptureError))
            return new DiagnosticItem("netfilterLoad", "进程模式驱动加载", DiagnosticStatus.Error,
                $"最近一次进程模式启动失败：{ctx.LastProcessCaptureError}", suggestion, null);

        if (_env.IsMemoryIntegrityEnabled() == true)
            return new DiagnosticItem("netfilterLoad", "进程模式驱动加载", DiagnosticStatus.Warning,
                "已开启 Windows「内存完整性」，进程模式驱动可能无法加载", suggestion, null);

        return new DiagnosticItem("netfilterLoad", "进程模式驱动加载", DiagnosticStatus.Ok,
            "最近没有进程模式驱动加载失败", null, null);
    }

    private async Task<DiagnosticItem> CheckDnsAsync(DiagnosticContext ctx, CancellationToken ct)
    {
        var host = ResolveHost(ctx);
        IPAddress[]? addrs;
        try
        {
            addrs = await _env.ResolveAsync(host, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            addrs = null;
        }

        if (addrs is null || addrs.Length == 0)
            return new DiagnosticItem("dns", "DNS 解析", DiagnosticStatus.Error,
                $"无法解析 {host}",
                "检查网络连接；或在系统网络设置中把 DNS 改为 223.5.5.5 / 119.29.29.29 后重试", null);

        return new DiagnosticItem("dns", "DNS 解析", DiagnosticStatus.Ok,
            $"{host} → {addrs[0]}", null, null);
    }

    private static string ResolveHost(DiagnosticContext ctx)
    {
        if (ctx.Game is { } game)
        {
            var target = Netch.Core.Services.EffectProbeTarget.Resolve(game);
            if (target is { } t && !IPAddress.TryParse(t.Host, out _))
                return t.Host;
        }

        return "www.microsoft.com";
    }

    private async Task<DiagnosticItem> CheckNodeAsync(DiagnosticContext ctx, CancellationToken ct)
    {
        var pool = ctx.Servers;
        if (pool.Count == 0)
            return new DiagnosticItem("node", "线路连通性", DiagnosticStatus.Error,
                "还没有任何线路",
                "前往\"订阅\"页导入订阅，或在\"线路\"页添加分享链接", null);

        List<Server> toProbe;
        if (ctx.Node is { } node)
        {
            toProbe = new List<Server> { node };
        }
        else
        {
            // Lowest known delay first, then untested.
            toProbe = pool
                .OrderBy(s => s.Delay >= 0 ? 0 : 1)
                .ThenBy(s => s.Delay >= 0 ? s.Delay : int.MaxValue)
                .Take(3)
                .ToList();
        }

        // Probe the selected nodes concurrently; keep the original order in the results.
        var probeTasks = toProbe.Select(s => ProbeOneAsync(s, ct)).ToArray();
        var msResults = await Task.WhenAll(probeTasks).ConfigureAwait(false);

        var results = new List<(Server Server, int Ms)>(toProbe.Count);
        for (var i = 0; i < toProbe.Count; i++)
            results.Add((toProbe[i], msResults[i]));

        var ok = results.Where(r => r.Ms >= 0).ToList();
        if (ok.Count == 0)
            return new DiagnosticItem("node", "线路连通性", DiagnosticStatus.Error,
                $"线路均无法连接（{results.Count} 条已测试）",
                "更新订阅或更换线路；若所有线路都不通，请检查本机网络或代理服务商状态", null);

        if (ok.Count < results.Count)
        {
            var best = ok.OrderBy(r => r.Ms).First();
            return new DiagnosticItem("node", "线路连通性", DiagnosticStatus.Warning,
                $"{best.Server.Remark}：{best.Ms} ms（{results.Count - ok.Count} 条不可用）",
                "部分线路不可用，建议更新订阅或更换线路", null);
        }

        var bestOk = ok.OrderBy(r => r.Ms).First();
        return new DiagnosticItem("node", "线路连通性", DiagnosticStatus.Ok,
            $"{bestOk.Server.Remark}：{bestOk.Ms} ms", null, null);
    }

    private async Task<int> ProbeOneAsync(Server server, CancellationToken ct)
    {
        try
        {
            return await _env.ProbeNodeAsync(server, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return -4;
        }
    }

    private DiagnosticItem CheckProxy(DiagnosticContext ctx)
    {
        var (server, _, enable) = _env.ReadSystemProxy();
        if (enable == 0)
            return new DiagnosticItem("proxy", "系统代理", DiagnosticStatus.Ok, "未设置系统代理", null, null);

        var port = ExtractLoopbackPort(server);
        if (port is { } p)
        {
            var listening = _env.IsLoopbackPortListening(p);
            if (!listening)
                return new DiagnosticItem("proxy", "系统代理", DiagnosticStatus.Error,
                    $"系统代理指向已关闭的本地端口 {p}，浏览器等程序可能无法上网",
                    "点击\"修复\"恢复为不使用代理", "fixProxy");

            if (ctx.Accelerating)
                return new DiagnosticItem("proxy", "系统代理", DiagnosticStatus.Ok, "由 Game+ 设置（加速中）", null, null);

            return new DiagnosticItem("proxy", "系统代理", DiagnosticStatus.Warning,
                $"检测到其他程序设置了系统代理（{server}），可能与 Game+ 冲突",
                "如加速异常，请先关闭其他代理/加速软件", null);
        }

        return new DiagnosticItem("proxy", "系统代理", DiagnosticStatus.Warning,
            $"检测到其他程序设置了系统代理（{server}），可能与 Game+ 冲突",
            "如加速异常，请先关闭其他代理/加速软件", null);
    }

    /// <summary>
    ///     Extracts the loopback port from a proxy server string. Handles plain <c>127.0.0.1:port</c> /
    ///     <c>localhost:port</c> and the <c>http=127.0.0.1:port;https=...</c> form. Returns null when the
    ///     proxy does not point at a loopback address.
    /// </summary>
    internal static int? ExtractLoopbackPort(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;

        foreach (var part in server.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = part;
            var eq = value.IndexOf('=');
            if (eq >= 0)
                value = value[(eq + 1)..];

            value = value.Trim();
            if (value.Length == 0)
                continue;

            var colon = value.LastIndexOf(':');
            if (colon <= 0)
                continue;

            var host = value[..colon].Trim();
            var portText = value[(colon + 1)..].Trim();
            if (!int.TryParse(portText, out var port))
                continue;

            if (IsLoopbackHost(host))
                return port;
        }

        return null;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IPAddress.TryParse(host, out var ip))
            return IPAddress.IsLoopback(ip);
        return false;
    }
}
