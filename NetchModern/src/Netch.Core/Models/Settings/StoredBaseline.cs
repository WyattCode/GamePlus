namespace Netch.Core.Models.Settings;

/// <summary>
///     A persisted direct-baseline measurement for one game, reused for the effect comparison on later
///     acceleration sessions (the accelerated sampling keeps running every session).
/// </summary>
public sealed class StoredBaseline
{
    /// <summary>Mean latency (ms) of the successful baseline samples; -1 when none succeeded.</summary>
    public int Latency { get; set; }

    /// <summary>Loss percentage (1 decimal) of the baseline samples.</summary>
    public double Loss { get; set; }

    /// <summary>Jitter (ms) of the baseline samples.</summary>
    public int Jitter { get; set; }

    /// <summary>Number of baseline samples taken.</summary>
    public int Samples { get; set; }

    /// <summary>The probed target ("host:port") the baseline was measured against.</summary>
    public string Target { get; set; } = "";

    /// <summary>UTC timestamp of the measurement.</summary>
    public DateTime MeasuredUtc { get; set; }
}
