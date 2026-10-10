namespace Netch.Core.Services;

/// <summary>An aggregated view of a set of latency samples (mean latency, loss %, jitter).</summary>
public sealed record EffectSnapshot(int Latency, double Loss, int Jitter, int Samples);

/// <summary>
///     Pure statistics for the acceleration effect measurement. Kept free of I/O so it is trivially
///     unit-testable.
/// </summary>
public static class EffectStats
{
    /// <summary>
    ///     Aggregates raw samples (ms; negative = failure) into a snapshot. Latency is the mean of the
    ///     successful samples (-1 when there are none), loss is the percentage of failures (1 decimal),
    ///     jitter is the population standard deviation of the successes (0 when fewer than 2).
    /// </summary>
    public static EffectSnapshot Compute(IReadOnlyCollection<int> samples)
    {
        if (samples.Count == 0)
            return new EffectSnapshot(-1, 0, 0, 0);

        var ok = samples.Where(s => s >= 0).ToList();
        var failed = samples.Count - ok.Count;
        var loss = Math.Round(100.0 * failed / samples.Count, 1);

        if (ok.Count == 0)
            return new EffectSnapshot(-1, loss, 0, samples.Count);

        var mean = (int)Math.Round(ok.Average());
        var jitter = ok.Count < 2 ? 0 : (int)Math.Round(StdDev(ok));
        return new EffectSnapshot(mean, loss, jitter, samples.Count);
    }

    /// <summary>
    ///     Compares the direct baseline with the accelerated current snapshot. Returns the latency
    ///     improvement percentage (null when there isn't enough data) and a one-line Chinese summary.
    ///     The wording is deliberately "参考" (reference): this is an in-app TLS-handshake measurement,
    ///     not the latency the game itself shows.
    /// </summary>
    public static (int? LatencyPct, string Summary) Improvement(EffectSnapshot baseline, EffectSnapshot current)
    {
        // Baseline was completely unreachable but the accelerated path works — the headline case.
        if (baseline.Latency < 0 && baseline.Loss >= 100 && current.Latency >= 0)
            return (null, "直连无法连通，加速后可正常连接");

        if (baseline.Latency < 0 || current.Latency < 0 || baseline.Samples < 3 || current.Samples < 3)
            return (null, "正在测量…");

        var pct = (int)Math.Round(100.0 * (baseline.Latency - current.Latency) / baseline.Latency);
        var summary = pct >= 5
            ? $"参考延迟降低 {pct}%"
            : pct <= -5
                ? $"参考延迟升高 {Math.Abs(pct)}%，可尝试切换线路"
                : "参考延迟与直连相近";

        if (baseline.Loss > 0 || current.Loss > 0)
            summary += $"，丢包 {baseline.Loss:0.#}% → {current.Loss:0.#}%";

        return (pct, summary);
    }

    private static double StdDev(IReadOnlyCollection<int> values)
    {
        var avg = values.Average();
        return Math.Sqrt(values.Sum(v => (v - avg) * (v - avg)) / values.Count);
    }
}
