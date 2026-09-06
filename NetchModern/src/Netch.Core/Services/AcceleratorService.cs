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

    public AcceleratorService(ILogService log, Func<Server, IServerController> controllerFactory, ISystemProxyService systemProxy)
    {
        _log = log;
        _controllerFactory = controllerFactory;
        _systemProxy = systemProxy;
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
            // Prefer IP/TUN capture (WinTUN is Microsoft-signed and loads on Win11; the netfilter driver
            // used by process capture is often blocked by driver-signature enforcement). Fall back to
            // process capture only when no IPs are configured.
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
                await StartCaptureAsync(game, node, socks5, hasProcess, hasIp, ct);
            else if (hasDomain)
            {
                _systemProxy.Enable("127.0.0.1", _controller.HttpPort);
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
    ///     Brings up traffic capture. Prefers process capture (netfilter2 captures the whole game process,
    ///     covering every domain/IP it touches — the most complete method for raw-socket games); falls back
    ///     to IP/TUN capture (WinTUN, Microsoft-signed, always loads) when the driver is unavailable and the
    ///     game also lists server IPs.
    /// </summary>
    private async Task StartCaptureAsync(Game game, Server node, Socks5Server socks5, bool hasProcess, bool hasIp, CancellationToken ct)
    {
        if (hasProcess)
        {
            try
            {
                _modeController = new RedirectorModeController();
                await _modeController.StartAsync(socks5, BuildMode(ModeType.ProcessMode, game.ProxyProcesses), ct);
                _log.Log(LogLevel.Info,
                    $"Capturing {game.ProxyProcesses.Count} process(es) via {_modeController.Name} → {node.Hostname}");
                return;
            }
            catch (Exception e)
            {
                _log.Log(LogLevel.Warning, $"Process capture failed: {e.Message}");
                if (_modeController != null)
                {
                    try { await _modeController.StopAsync(ct); } catch { /* ignore */ }
                    _modeController = null;
                }

                if (!hasIp)
                    throw;
                _log.Log(LogLevel.Info, "Falling back to IP/TUN capture");
            }
        }

        _modeController = new TunModeController();
        await _modeController.StartAsync(socks5, BuildMode(ModeType.TunMode, game.ProxyIps), ct);
        _log.Log(LogLevel.Info, $"Capturing {game.ProxyIps.Count} IP rule(s) via {_modeController.Name} → {node.Hostname}");
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
