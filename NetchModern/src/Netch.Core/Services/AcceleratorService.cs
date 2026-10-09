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

    private IServerController? _controller;
    private IModeController? _modeController;
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
        ISystemProxyService systemProxy, IModeControllerFactory modeControllerFactory)
    {
        _log = log;
        _controllerFactory = controllerFactory;
        _systemProxy = systemProxy;
        _modeControllerFactory = modeControllerFactory;
    }

    /// <summary>Convenience overload using the default <see cref="ModeControllerFactory" /> (real drivers).</summary>
    public AcceleratorService(ILogService log, Func<Server, IServerController> controllerFactory, ISystemProxyService systemProxy)
        : this(log, controllerFactory, systemProxy, new ModeControllerFactory())
    {
    }

    public AccelerationStatus Status { get; } = new();

    public event EventHandler<AccelerationState>? StateChanged;

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

            // Games use raw sockets that ignore the Windows system proxy. To actually redirect their
            // traffic we capture it at driver level: by process (netfilter Redirector) when process names
            // are configured, else by destination IP (WinTUN + tun2socks). In those cases xray runs in
            // "global" mode (proxy everything it receives) and the capture layer does the filtering.
            // Preference order (see StartCaptureAsync): process capture first (most complete — covers every
            // domain/IP the game touches), then IP/TUN capture (WinTUN is Microsoft-signed and loads on
            // Win11, whereas the netfilter driver used by process capture is often blocked by
            // driver-signature enforcement), and finally domain system-proxy routing when neither driver
            // is available and the game lists domains.
            var hasProcess = game.ProxyProcesses.Count > 0;
            var hasIp = game.ProxyIps.Count > 0;
            var hasDomain = game.ProxyDomains.Count > 0;
            var useCapture = hasProcess || hasIp;

            _controller = _controllerFactory(node);
            if (!useCapture)
            {
                // Domain-only game: system proxy with xray domain/IP routing (works for apps that honour
                // the Windows proxy — launchers/browsers; raw-socket games need a process/IP rule instead).
                _controller.ProxyDomains = game.ProxyDomains;
                _controller.ProxyIps = game.ProxyIps;
            }

            var socks5 = await _controller.StartAsync(node, ct);

            if (useCapture)
                await StartCaptureAsync(game, node, socks5, hasProcess, hasIp, hasDomain, ct);
            else if (hasDomain)
            {
                _systemProxy.Enable("127.0.0.1", _controller.HttpPort);
                Status.CaptureMode = "proxy";
                _log.Log(LogLevel.Info, $"System-proxy routing {game.ProxyDomains.Count} domain(s) via {node.Hostname}");
            }
            else
                throw new InvalidOperationException("该游戏没有配置任何加速规则（进程/IP/域名），请先在游戏设置里填写。");

            _startedUtc = DateTime.UtcNow;
            _statsTimer = new Timer(PollStats, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _pingTimer = new Timer(PollPing, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

            SetState(AccelerationState.Accelerating);
        }
        catch (Exception e)
        {
            _log.Log(LogLevel.Error, $"Accelerate failed: {e.Message}");
            // Clean up with a fresh token: the failure may itself be a cancellation, and cleanup must still run.
            await StopAsync(CancellationToken.None);
            throw;
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
                await _modeController.StartAsync(socks5, BuildMode(ModeType.ProcessMode, game.ProxyProcesses), ct);
                Status.CaptureMode = "process";
                _log.Log(LogLevel.Info,
                    $"Capturing {game.ProxyProcesses.Count} process(es) via {_modeController.Name} → {node.Hostname}");
                return;
            }
            catch (Exception e)
            {
                processError = e;
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

        _controller.ProxyDomains = game.ProxyDomains;
        _controller.ProxyIps = game.ProxyIps;
        await _controller.StartAsync(node, ct);

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
