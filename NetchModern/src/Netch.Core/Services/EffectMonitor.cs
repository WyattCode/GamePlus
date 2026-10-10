using Netch.Core.Models.Games;

namespace Netch.Core.Services;

/// <summary>A single accelerated sample point (unix ms timestamp + measured latency).</summary>
public sealed record EffectPoint(long TsMs, int Accel);

/// <summary>The full effect report handed to the UI.</summary>
public sealed record EffectReport(bool HasTarget, string? Target, EffectSnapshot? Baseline, EffectSnapshot? Current,
    int? LatencyPct, string Summary, IReadOnlyList<EffectPoint> Series, bool Measuring);

/// <summary>
///     Measures the acceleration effect against the game's own target: a direct baseline taken before any
///     capture is active, then a rolling window of accelerated samples taken through the local SOCKS5
///     inbound. Thread-safe; the lock is only held around in-memory state, never across I/O.
/// </summary>
public sealed class EffectMonitor
{
    private const int BaselineSamples = 5;
    private const int BaselineStaggerMs = 150;
    private const int BaselineBudgetMs = 3500;
    private const int WindowSize = 90;
    private const int CurrentWindow = 15;
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(2);

    private readonly IEffectProbe _probe;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _sampleInterval;
    private readonly object _lock = new();
    private readonly List<int> _baselineSamples = new();
    private readonly List<int> _accelSamples = new();
    private readonly List<EffectPoint> _series = new();

    private (string Host, int Port)? _target;
    private bool _hasTarget;
    private bool _measuring;
    private bool _started;
    private string? _socksHost;
    private int _socksPort;
    private Timer? _timer;
    private int _sampling;

    /// <param name="probe">The probe used for baseline and accelerated samples.</param>
    /// <param name="clock">Clock used for series timestamps; defaults to UTC now.</param>
    /// <param name="sampleInterval">
    ///     Interval of the accelerated sampling loop started by <see cref="Begin" />. Defaults to 2 s.
    ///     <see cref="Timeout.InfiniteTimeSpan" /> disables the timer entirely (Begin only records the
    ///     SOCKS endpoint) — used by tests that drive <see cref="SampleOnceAsync" /> manually.
    /// </param>
    public EffectMonitor(IEffectProbe probe, Func<DateTime>? clock = null, TimeSpan? sampleInterval = null)
    {
        _probe = probe;
        _clock = clock ?? (() => DateTime.UtcNow);
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
    }

    /// <summary>Raised after each sample (baseline or accelerated) so the host can push an update.</summary>
    public event Action? Updated;

    /// <summary>
    ///     Takes the direct baseline. Must run before any capture is active: in TUN mode the capture layer
    ///     would route the probe's own traffic through the tunnel, so the "direct" number would be wrong.
    /// </summary>
    public async Task MeasureBaselineAsync(Game game, CancellationToken ct)
    {
        var target = EffectProbeTarget.Resolve(game);
        lock (_lock)
        {
            _target = target;
            _hasTarget = target is not null;
            _baselineSamples.Clear();
            _accelSamples.Clear();
            _series.Clear();
            _measuring = target is not null;
            _started = true;
        }

        if (target is null)
        {
            Updated?.Invoke();
            return;
        }

        var (host, port) = target.Value;
        var tasks = new List<Task<int>>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(BaselineBudgetMs));

        for (var i = 0; i < BaselineSamples; i++)
        {
            if (i > 0)
            {
                try
                {
                    await Task.Delay(BaselineStaggerMs, budget.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            tasks.Add(_probe.DirectAsync(host, port, budget.Token));
        }

        var results = await Task.WhenAll(tasks.Select(async t =>
        {
            try
            {
                return await t;
            }
            catch (OperationCanceledException)
            {
                return -4;
            }
            catch
            {
                return -4;
            }
        }));

        lock (_lock)
        {
            _baselineSamples.AddRange(results);
            _measuring = false;
        }

        // The internal budget expiring is not a caller cancellation: only rethrow when the caller's own
        // token was cancelled (spec). Reset _measuring first so the report doesn't stay stuck "measuring".
        ct.ThrowIfCancellationRequested();

        Updated?.Invoke();
    }

    /// <summary>Starts the accelerated sampling loop through the local SOCKS5 inbound.</summary>
    public void Begin(string socksHost, int socksPort)
    {
        lock (_lock)
        {
            if (!_hasTarget)
                return;
            _socksHost = socksHost;
            _socksPort = socksPort;
            _timer?.Dispose();
            _timer = _sampleInterval == Timeout.InfiniteTimeSpan
                ? null
                : new Timer(_ => _ = SampleOnceAsync(), null, TimeSpan.Zero, _sampleInterval);
        }
    }

    /// <summary>Stops the sampling loop; the last result stays readable until the next baseline/reset.</summary>
    public void End()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Clears all state (baseline, series, target).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _baselineSamples.Clear();
            _accelSamples.Clear();
            _series.Clear();
            _target = null;
            _hasTarget = false;
            _measuring = false;
            _started = false;
        }
        Updated?.Invoke();
    }

    /// <summary>Takes a single accelerated sample and appends it to the rolling window. Usable by tests.</summary>
    public async Task SampleOnceAsync()
    {
        string host;
        int port;
        string socksHost;
        int socksPort;
        lock (_lock)
        {
            if (!_hasTarget || _target is null || _socksHost is null)
                return;
            if (Interlocked.Exchange(ref _sampling, 1) == 1)
                return;
            (host, port) = _target.Value;
            socksHost = _socksHost;
            socksPort = _socksPort;
        }

        try
        {
            var latency = await _probe.ViaSocksAsync(socksHost, socksPort, host, port, CancellationToken.None);
            lock (_lock)
            {
                _accelSamples.Add(latency);
                while (_accelSamples.Count > WindowSize)
                    _accelSamples.RemoveAt(0);

                _series.Add(new EffectPoint(new DateTimeOffset(_clock()).ToUnixTimeMilliseconds(), latency));
                while (_series.Count > WindowSize)
                    _series.RemoveAt(0);
            }
            Updated?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    /// <summary>Builds the current report (baseline, current window, series, summary).</summary>
    public EffectReport Report()
    {
        lock (_lock)
        {
            var baseline = _baselineSamples.Count > 0 ? EffectStats.Compute(_baselineSamples) : null;
            var currentSamples = _accelSamples.Count > CurrentWindow
                ? _accelSamples.Skip(_accelSamples.Count - CurrentWindow).ToList()
                : _accelSamples.ToList();
            var current = currentSamples.Count > 0 ? EffectStats.Compute(currentSamples) : null;

            int? pct = null;
            string summary;
            if (!_started)
            {
                summary = "尚未开始测量";
            }
            else if (!_hasTarget)
            {
                summary = "该游戏未配置可测量的域名/IP，仅显示线路延迟";
            }
            else if (baseline is null || current is null)
            {
                summary = "正在测量…";
            }
            else
            {
                var (p, s) = EffectStats.Improvement(baseline, current);
                pct = p;
                summary = s;
            }

            var target = _target is null ? null : $"{_target.Value.Host}:{_target.Value.Port}";
            return new EffectReport(_hasTarget, target, baseline, current, pct, summary,
                _series.ToList(), _measuring);
        }
    }
}
