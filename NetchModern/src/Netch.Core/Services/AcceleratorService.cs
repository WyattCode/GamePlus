using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Netch.Core.Abstractions;
using Netch.Core.Controllers;
using Netch.Core.Enums;
using Netch.Core.Models.Games;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;

namespace Netch.Core.Services;

/// <summary>
///     Real game accelerator. Starts xray for the chosen node with the game's domains routed through it
///     (everything else direct), sets the system proxy, and continuously measures latency / jitter / loss
///     with TCP pings to the node.
///
///     <para>Threading: the <see cref="Status" /> timer callbacks and their async continuations run on the
///     ThreadPool, not a dispatcher. <see cref="CommunityToolkit.Mvvm" /> observable properties do no UI
///     marshalling, so consumers must not bind this object directly to a UI thread. The headless host is
///     safe because it funnels events through a thread-safe <c>Channel&lt;string&gt;</c> to the WebSocket;
///     if this service is ever bound to WPF/WinForms, marshal state changes onto the UI thread.</para>
/// </summary>
public sealed partial class AcceleratorService : ObservableObject, IAcceleratorService, IAsyncDisposable
{
    private const int WindowSize = 15;
    private readonly Func<Server, IServerController> _controllerFactory;
    private readonly IModeControllerFactory _modeControllerFactory;
    private readonly ILogService _log;
    private readonly Queue<int> _samples = new();
    private readonly ISystemProxyService _systemProxy;
    private readonly EffectMonitor? _effect;
    private readonly Func<IReadOnlyList<ExtraApp>>? _extraApps;
    private readonly Func<AdvancedSettings>? _advanced;
    private readonly IBaselineStore? _baselines;
    private readonly Func<Game, string>? _captureModeFor;
    private readonly Func<Game, bool>? _downloadViaProxyFor;

    private IServerController? _controller;
    private IModeController? _modeController;
    private Socks5Server? _socks;
    private long _lastDown;
    private long _lastUp;
    private int _pinging;
    private int _pollingStats;
    private DateTime _startedUtc;
    private Timer? _statsTimer;
    private Timer? _pingTimer;

    [ObservableProperty]
    private Game? _currentGame;

    [ObservableProperty]
    private Server? _currentNode;

    [ObservableProperty]
    private AccelerationState _state = AccelerationState.Idle;

    public AcceleratorService(ILogService log, Func<Server, IServerController> controllerFactory,
        ISystemProxyService systemProxy, IModeControllerFactory modeControllerFactory, EffectMonitor? effect = null,
        Func<IReadOnlyList<ExtraApp>>? extraApps = null, Func<AdvancedSettings>? advanced = null,
        IBaselineStore? baselines = null, Func<Game, string>? captureModeFor = null,
        Func<Game, bool>? downloadViaProxyFor = null)
    {
        _log = log;
        _controllerFactory = controllerFactory;
        _systemProxy = systemProxy;
        _modeControllerFactory = modeControllerFactory;
        _effect = effect;
        _extraApps = extraApps;
        _advanced = advanced;
        _baselines = baselines;
        _captureModeFor = captureModeFor;
        _downloadViaProxyFor = downloadViaProxyFor;
    }

    /// <summary>Convenience overload using the default <see cref="ModeControllerFactory" /> (real drivers).</summary>
    public AcceleratorService(ILogService log, Func<Server, IServerController> controllerFactory, ISystemProxyService systemProxy)
        : this(log, controllerFactory, systemProxy, new ModeControllerFactory())
    {
    }

    public AccelerationStatus Status { get; } = new();

    /// <summary>The effect monitor, when one is wired up (null in tests / minimal hosts).</summary>
    public EffectMonitor? Effect => _effect;

    /// <inheritdoc />
    public string? LastProcessCaptureError { get; private set; }

    public event EventHandler<AccelerationState>? StateChanged;

    public EffectReport? EffectReport() => _effect?.Report();

    public async Task StartAsync(Game game, Server node, CancellationToken ct = default)
    {
        if (State is AccelerationState.Accelerating or AccelerationState.Connecting)
            return;

        SetState(AccelerationState.Connecting);
        CurrentGame = game;
        CurrentNode = node;
        Status.Reset();
        _samples.Clear();
        _lastUp = _lastDown = 0;

        try
        {
            _log.Log(LogLevel.Info, $"Accelerating {game.Name} via {node.DisplayName}");

            var adv = _advanced?.Invoke();

            // Resolve the effective capture mode (per-game override ?? global default). Unknown values
            // fall back to "auto" so a hand-edited settings.json can never break acceleration.
            var mode = NormalizeMode(_captureModeFor?.Invoke(game));

            // Validate availability BEFORE any side effect (controller start, driver load, system proxy).
            var hasProcess = game.ProxyProcesses.Count > 0;
            var hasIp = game.ProxyIps.Count > 0;
            var hasDomain = game.ProxyDomains.Count > 0;

            if (mode == "process" && !hasProcess)
                throw new InvalidOperationException("该游戏没有进程规则，无法使用进程模式");
            if (mode == "tun" && !hasIp)
                throw new InvalidOperationException("该游戏没有 IP 规则，无法使用 TUN 模式");
            if (mode == "proxy" && !hasDomain)
                throw new InvalidOperationException("该游戏没有域名规则，无法使用系统代理模式");

            // Baseline policy: the direct baseline (~3.5 s) runs automatically only on the FIRST
            // acceleration of each game (persisted) or when the user explicitly asks for it
            // (MeasureEffect = "measure before EVERY acceleration", or the one-shot remeasure flag).
            // Otherwise the stored baseline is reused — the accelerated sampling still runs every session.
            if (_effect != null)
            {
                // Consume the one-shot flag unconditionally first so it is cleared even when another
                // condition already forces a measurement.
                var remeasure = _baselines?.ConsumeRemeasure(game.Id) ?? false;
                var stored = _baselines?.Get(game.Id);
                var measure = adv is { MeasureEffect: true } || _baselines is null || stored is null || remeasure;

                if (measure)
                {
                    await _effect.MeasureBaselineAsync(game, ct);
                    var snapshot = _effect.CurrentBaseline();
                    if (snapshot is { Samples: >= 3 } && _baselines is not null)
                    {
                        var target = EffectProbeTarget.Resolve(game);
                        _baselines.Save(game.Id, new StoredBaseline
                        {
                            Latency = snapshot.Latency,
                            Loss = snapshot.Loss,
                            Jitter = snapshot.Jitter,
                            Samples = snapshot.Samples,
                            Target = target is null ? "" : $"{target.Value.Host}:{target.Value.Port}",
                            MeasuredUtc = DateTime.UtcNow
                        });
                    }
                }
                else if (stored is not null)
                {
                    _effect.UseStoredBaseline(game,
                        new EffectSnapshot(stored.Latency, stored.Loss, stored.Jitter, stored.Samples),
                        stored.MeasuredUtc);
                    _log.Log(LogLevel.Info,
                        $"已使用 {stored.MeasuredUtc.ToLocalTime():yyyy-MM-dd} 测得的直连基线（如需更新可在“加速效果”中重新测量）");
                }
                else
                {
                    _effect.BeginWithoutBaseline(game);
                }
            }

            // Games use raw sockets that ignore the Windows system proxy. To actually redirect their
            // traffic we capture it at driver level: by process (netfilter Redirector) when process names
            // are configured, else by destination IP (WinTUN + tun2socks). In those cases xray runs in
            // "global" mode (proxy everything it receives) and the capture layer does the filtering.
            // Preference order (see StartCaptureAsync): process capture first (most complete — covers every
            // domain/IP the game touches), then IP/TUN capture (WinTUN is Microsoft-signed and loads on
            // Win11, whereas the netfilter driver used by process capture is often blocked by
            // driver-signature enforcement), and finally domain system-proxy routing when neither driver
            // is available and the game lists domains.
            var useCapture = mode is "auto" or "process" or "tun"
                ? (mode == "auto" ? hasProcess || hasIp : true)
                : false;

            // Steam download split: CDN download domains go DIRECT by default (personal VPS bandwidth is
            // too limited for multi-GB downloads); the per-game 「游戏下载走代理」 toggle puts them through
            // the node again. Capture modes run the core in global mode, so the DIRECT routing rule +
            // sniffing in the config generator handles the split there too.
            var downloadViaProxy = game.DownloadDomains.Count > 0 && (_downloadViaProxyFor?.Invoke(game) ?? false);

            _controller = _controllerFactory(node);
            // Fixed local SOCKS5 port (0 = automatic). Set before StartAsync; persists across the
            // restart in StartDomainProxyAsync because it reuses the same controller instance.
            _controller.PreferredSocksPort = adv?.LocalSocksPort ?? 0;
            if (game.DownloadDomains.Count > 0)
            {
                if (downloadViaProxy)
                {
                    _controller.DirectDomains = null;
                }
                else
                {
                    _controller.DirectDomains = game.DownloadDomains;
                    _log.Log(LogLevel.Info, $"游戏下载（CDN）直连：{game.DownloadDomains.Count} 个域名不经过节点");
                }
            }

            if (mode == "proxy" || (mode == "auto" && !useCapture))
            {
                // Domain-only game: system proxy with xray domain/IP routing (works for apps that honour
                // the Windows proxy — launchers/browsers; raw-socket games need a process/IP rule instead).
                _controller.ProxyDomains = ProxyDomainsFor(game, downloadViaProxy);
                _controller.ProxyIps = game.ProxyIps;
            }

            _socks = await _controller.StartAsync(node, ct);

            if (mode == "auto")
            {
                if (useCapture)
                    await StartCaptureAsync(game, node, _socks, hasProcess, hasIp, hasDomain, ct);
                else if (hasDomain)
                {
                    _systemProxy.Enable("127.0.0.1", _controller.HttpPort);
                    Status.CaptureMode = "proxy";
                    _log.Log(LogLevel.Info, $"System-proxy routing {game.ProxyDomains.Count} domain(s) via {node.Hostname}");
                }
                else
                    throw new InvalidOperationException("该游戏没有配置任何加速规则（进程/IP/域名），请先在游戏设置里填写。");
            }
            else if (mode == "process")
            {
                await StartForcedProcessAsync(game, node, _socks, ct);
            }
            else if (mode == "tun")
            {
                await StartForcedTunAsync(game, node, _socks, ct);
            }
            else // "proxy"
            {
                _systemProxy.Enable("127.0.0.1", _controller.HttpPort);
                Status.CaptureMode = "proxy";
                _log.Log(LogLevel.Info, $"加速模式：系统代理（手动指定）— {game.ProxyDomains.Count} 个域名经 {node.Hostname}");
            }

            _startedUtc = DateTime.UtcNow;
            _statsTimer = new Timer(PollStats, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _pingTimer = new Timer(PollPing, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

            SetState(AccelerationState.Accelerating);

            // Start the accelerated sampling loop against the SOCKS endpoint of the controller that is
            // actually running (StartDomainProxyAsync restarts it, so _socks is re-assigned there).
            if (_effect != null && _socks != null)
                _effect.Begin(_socks.Hostname, _socks.Port);
        }
        catch (Exception e)
        {
            _log.Log(LogLevel.Error, $"Accelerate failed: {e.Message}");
            // Clean up with a fresh token: the failure may itself be a cancellation, and cleanup must still run.
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Normalizes a capture-mode string; unknown/null → "auto".</summary>
    private static string NormalizeMode(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "process" or "tun" or "proxy" ? normalized : "auto";
    }

    /// <summary>
    ///     Proxy domains for the domain-routing paths: when the per-game download-proxy toggle is on,
    ///     the game's CDN download domains are added to the proxied set (case-insensitive union).
    /// </summary>
    private static IReadOnlyCollection<string> ProxyDomainsFor(Game game, bool downloadViaProxy)
    {
        if (!downloadViaProxy || game.DownloadDomains.Count == 0)
            return game.ProxyDomains;

        var merged = new List<string>(game.ProxyDomains);
        foreach (var d in game.DownloadDomains)
            if (!merged.Contains(d, StringComparer.OrdinalIgnoreCase))
                merged.Add(d);
        return merged;
    }

    /// <summary>
    ///     Forced process capture (manual mode). No fallback: any failure is surfaced with a clear
    ///     Chinese message so the user can switch to 自动 / TUN.
    /// </summary>
    private async Task StartForcedProcessAsync(Game game, Server node, Socks5Server socks5, CancellationToken ct)
    {
        try
        {
            _modeController = _modeControllerFactory.Create(ModeType.ProcessMode);
            var extras = _extraApps?.Invoke() ?? Array.Empty<ExtraApp>();
            var mode = SplitRouting.BuildProcessMode(game.ProxyProcesses, extras);
            await _modeController.StartAsync(socks5, mode, ct);
            Status.CaptureMode = "process";
            LastProcessCaptureError = null;

            var extraProxy = extras.Count(a => a is { Enabled: true, Mode: ExtraAppMode.Proxy });
            var extraBypass = extras.Count(a => a is { Enabled: true, Mode: ExtraAppMode.Bypass });
            if (extraProxy > 0 || extraBypass > 0)
                _log.Log(LogLevel.Info, $"分应用分流：额外代理 {extraProxy} 个，绕过 {extraBypass} 个");

            _log.Log(LogLevel.Info,
                $"加速模式：进程模式（手动指定）— 抓取 {game.ProxyProcesses.Count} 个进程 → {node.Hostname}");
        }
        catch (Exception e)
        {
            LastProcessCaptureError = e.Message;
            if (_modeController != null)
            {
                try { await _modeController.StopAsync(ct); } catch { /* ignore */ }
                _modeController = null;
            }
            throw new InvalidOperationException(
                $"进程模式启动失败：{e.Message}。可在加速模式中改用「自动」或「TUN」。", e);
        }
    }

    /// <summary>
    ///     Forced TUN capture (manual mode). No fallback: any failure is surfaced with a clear Chinese
    ///     message so the user can switch to 自动 / 进程模式.
    /// </summary>
    private async Task StartForcedTunAsync(Game game, Server node, Socks5Server socks5, CancellationToken ct)
    {
        try
        {
            _modeController = _modeControllerFactory.Create(ModeType.TunMode);
            await _modeController.StartAsync(socks5, BuildMode(ModeType.TunMode, game.ProxyIps), ct);
            Status.CaptureMode = "tun";
            _log.Log(LogLevel.Info,
                $"加速模式：TUN 模式（手动指定）— 抓取 {game.ProxyIps.Count} 条 IP 规则 → {node.Hostname}");
        }
        catch (Exception e)
        {
            if (_modeController != null)
            {
                try { await _modeController.StopAsync(ct); } catch { /* ignore */ }
                _modeController = null;
            }
            throw new InvalidOperationException(
                $"TUN 模式启动失败：{e.Message}。可在加速模式中改用「自动」或「进程模式」。", e);
        }
    }

    /// <summary>
    ///     Brings up traffic capture with graceful degradation:
    ///     process capture (netfilter2, most complete) → IP/TUN capture (WinTUN, Microsoft-signed) →
    ///     domain system-proxy routing (only apps honouring the Windows proxy). Throws only when no
    ///     strategy is available.
    /// </summary>
    private async Task StartCaptureAsync(Game game, Server node, Socks5Server socks5, bool hasProcess, bool hasIp, bool hasDomain, CancellationToken ct)
    {
        Exception? processError = null;

        if (hasProcess)
        {
            try
            {
                _modeController = _modeControllerFactory.Create(ModeType.ProcessMode);
                var extras = _extraApps?.Invoke() ?? Array.Empty<ExtraApp>();
                var mode = SplitRouting.BuildProcessMode(game.ProxyProcesses, extras);
                await _modeController.StartAsync(socks5, mode, ct);
                Status.CaptureMode = "process";
                LastProcessCaptureError = null;

                var extraProxy = extras.Count(a => a is { Enabled: true, Mode: ExtraAppMode.Proxy });
                var extraBypass = extras.Count(a => a is { Enabled: true, Mode: ExtraAppMode.Bypass });
                if (extraProxy > 0 || extraBypass > 0)
                    _log.Log(LogLevel.Info, $"分应用分流：额外代理 {extraProxy} 个，绕过 {extraBypass} 个");

                _log.Log(LogLevel.Info,
                    $"Capturing {game.ProxyProcesses.Count} process(es) via {_modeController.Name} → {node.Hostname}");
                return;
            }
            catch (Exception e)
            {
                processError = e;
                LastProcessCaptureError = e.Message;
                _log.Log(LogLevel.Warning, $"Process capture failed: {e.Message}");
                if (_modeController != null)
                {
                    try { await _modeController.StopAsync(ct); } catch { /* ignore */ }
                    _modeController = null;
                }
            }
        }

        if (hasIp)
        {
            try
            {
                _modeController = _modeControllerFactory.Create(ModeType.TunMode);
                await _modeController.StartAsync(socks5, BuildMode(ModeType.TunMode, game.ProxyIps), ct);
                Status.CaptureMode = "tun";
                _log.Log(LogLevel.Info, $"Capturing {game.ProxyIps.Count} IP rule(s) via {_modeController.Name} → {node.Hostname}");
                return;
            }
            catch (Exception e)
            {
                _log.Log(LogLevel.Warning, $"IP/TUN capture failed: {e.Message}");
                if (_modeController != null)
                {
                    try { await _modeController.StopAsync(ct); } catch { /* ignore */ }
                    _modeController = null;
                }
            }
        }

        if (hasDomain)
        {
            await StartDomainProxyAsync(game, node, ct);
            return;
        }

        // Nothing worked. Surface the most relevant cause to the user.
        if (processError != null)
            throw new InvalidOperationException(
                "进程抓包失败（通常是没有管理员权限或 netfilter 驱动被系统拦截），且该游戏没有配置 IP 或域名规则，无法加速。请以管理员身份运行，或在游戏设置里补充 IP/域名规则。",
                processError);
        throw new InvalidOperationException(
            "该游戏没有配置任何可用的加速规则（进程/IP/域名），无法加速。请先在游戏设置里填写。");
    }

    /// <summary>
    ///     Degrades to domain system-proxy routing. The controller was started in "global" mode (no
    ///     ProxyDomains/ProxyIps), so it must be stopped and restarted with the game's domain/IP rules
    ///     before the system proxy is enabled.
    /// </summary>
    private async Task StartDomainProxyAsync(Game game, Server node, CancellationToken ct)
    {
        _log.Log(LogLevel.Warning,
            "降级为系统代理模式：仅加速遵循系统代理的程序（启动器/浏览器/HTTP），游戏原始 socket 流量不会被加速。");

        if (_controller == null)
            throw new InvalidOperationException("系统代理模式启动失败：节点控制器不可用。");

        // Restart the controller with domain/IP routing so xray only proxies the game's rules.
        try { await _controller.StopAsync(ct); }
        catch (Exception e) { _log.Log(LogLevel.Warning, $"Node stop error during fallback: {e.Message}"); }

        // Re-apply the fixed local port: the controller instance is reused, but be explicit so a future
        // refactor that swaps the instance can't silently drop the setting.
        _controller.PreferredSocksPort = _advanced?.Invoke()?.LocalSocksPort ?? 0;

        // Re-apply the same download-split decision as the initial start (StartAsync) so the restart
        // keeps DirectDomains/ProxyDomains consistent.
        var downloadViaProxy = game.DownloadDomains.Count > 0 && (_downloadViaProxyFor?.Invoke(game) ?? false);
        _controller.DirectDomains = game.DownloadDomains.Count > 0 && !downloadViaProxy ? game.DownloadDomains : null;
        _controller.ProxyDomains = ProxyDomainsFor(game, downloadViaProxy);
        _controller.ProxyIps = game.ProxyIps;
        _socks = await _controller.StartAsync(node, ct);

        _systemProxy.Enable("127.0.0.1", _controller.HttpPort);
        Status.CaptureMode = "proxy";
        _log.Log(LogLevel.Info, $"System-proxy routing {game.ProxyDomains.Count} domain(s) via {node.Hostname}");
    }

    private static Mode BuildMode(ModeType type, IEnumerable<string> rules)
    {
        var mode = new Mode { Type = type };
        foreach (var r in rules)
            mode.Handle.Add(r);
        return mode;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (State is AccelerationState.Idle)
            return;

        SetState(AccelerationState.Stopping);
        _statsTimer?.Dispose();
        _pingTimer?.Dispose();
        _statsTimer = _pingTimer = null;

        _effect?.End();

        if (_modeController != null)
        {
            try { await _modeController.StopAsync(ct); }
            catch (Exception e) { _log.Log(LogLevel.Warning, $"Capture stop error: {e.Message}"); }
            _modeController = null;
        }

        try
        {
            if (_systemProxy.IsEnabled)
                _systemProxy.Disable();
        }
        catch (Exception e)
        {
            _log.Log(LogLevel.Warning, $"System proxy restore error: {e.Message}");
        }

        if (_controller != null)
        {
            try { await _controller.StopAsync(ct); }
            catch (Exception e) { _log.Log(LogLevel.Warning, $"Node stop error: {e.Message}"); }
            _controller = null;
        }

        _socks = null;

        Status.Reset();
        CurrentGame = null;
        CurrentNode = null;
        SetState(AccelerationState.Idle);
        _log.Log(LogLevel.Info, "Acceleration stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void PollStats(object? state)
    {
        Status.Duration = DateTime.UtcNow - _startedUtc;

        if (_controller == null)
            return;

        // Skip if a previous poll is still running (statsquery spawns a short-lived xray process).
        if (Interlocked.Exchange(ref _pollingStats, 1) == 1)
            return;

        _ = PollTrafficAsync();
    }

    private async Task PollTrafficAsync()
    {
        try
        {
            var controller = _controller;
            if (controller == null)
                return;

            var (up, down) = await controller.QueryTrafficAsync();
            Status.UploadRate = Math.Max(0, up - _lastUp);
            Status.DownloadRate = Math.Max(0, down - _lastDown);
            _lastUp = up;
            _lastDown = down;
        }
        catch
        {
            // best-effort
        }
        finally
        {
            Interlocked.Exchange(ref _pollingStats, 0);
        }
    }

    private void PollPing(object? state)
    {
        if (Interlocked.Exchange(ref _pinging, 1) == 1)
            return;

        _ = PingAsync().ContinueWith(_ => Interlocked.Exchange(ref _pinging, 0));
    }

    private async Task PingAsync()
    {
        var node = CurrentNode;
        if (node == null)
            return;

        // TCP for TCP protocols, ICMP for UDP-based ones (Hysteria2) — a TCP probe to a UDP port reads as 100% loss.
        var latency = await LatencyProbe.MeasureAsync(node);

        _samples.Enqueue(latency);
        while (_samples.Count > WindowSize)
            _samples.Dequeue();

        var ok = _samples.Where(s => s >= 0).ToList();
        Status.Latency = latency >= 0 ? latency : Status.Latency;
        Status.Loss = _samples.Count == 0 ? 0 : Math.Round(100.0 * _samples.Count(s => s < 0) / _samples.Count, 1);
        Status.Jitter = ok.Count < 2 ? 0 : (int)StdDev(ok);
    }

    private static double StdDev(IReadOnlyCollection<int> values)
    {
        var avg = values.Average();
        return Math.Sqrt(values.Sum(v => (v - avg) * (v - avg)) / values.Count);
    }

    private void SetState(AccelerationState value)
    {
        State = value;
        StateChanged?.Invoke(this, value);
    }
}
