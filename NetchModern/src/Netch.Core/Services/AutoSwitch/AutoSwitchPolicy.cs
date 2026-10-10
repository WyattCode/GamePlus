using Netch.Core.Models.Settings;

namespace Netch.Core.Services.AutoSwitch;

/// <summary>
///     Immutable snapshot of the auto-switch settings, taken when an acceleration session starts
///     (or whenever the caller prefers) so the policy sees a consistent view.
/// </summary>
/// <param name="Enabled">Master switch — when false the policy never acts and keeps no streak.</param>
/// <param name="LossPct">A sample is bad when the measured loss % ≥ this value.</param>
/// <param name="LatencyMs">A sample is bad when the measured latency ≥ this value (or &lt; 0, i.e. timeout).</param>
/// <param name="SustainSec">The bad streak must last this long before a switch is due.</param>
/// <param name="CooldownSec">After a switch, no bad streak is evaluated for this long.</param>
/// <param name="MaxSwitches">Maximum switches per acceleration session.</param>
public sealed record AutoSwitchOptions(
    bool Enabled,
    int LossPct,
    int LatencyMs,
    int SustainSec,
    int CooldownSec,
    int MaxSwitches)
{
    /// <summary>Builds the options from the advanced settings.</summary>
    public static AutoSwitchOptions FromSettings(AdvancedSettings a) => new(
        a.AutoSwitchEnabled,
        a.AutoSwitchLossPct,
        a.AutoSwitchLatencyMs,
        a.AutoSwitchSustainSec,
        a.AutoSwitchCooldownSec,
        a.AutoSwitchMaxSwitches);
}

/// <summary>What the policy wants the caller to do after a quality sample.</summary>
public enum AutoSwitchAction
{
    /// <summary>Keep the current node.</summary>
    None,

    /// <summary>Switch to the next-ranked candidate (call <see cref="AutoSwitchPolicy.RecordSwitch" /> afterwards).</summary>
    Switch,

    /// <summary>A switch would be due but the budget is used up — reported once per session.</summary>
    Exhausted
}

/// <summary>The decision returned by <see cref="AutoSwitchPolicy.Observe" />.</summary>
/// <param name="Action">The action to take.</param>
/// <param name="Reason">Chinese human-readable trigger description, e.g. "丢包 8% ≥ 5%，持续 15 秒".</param>
public sealed record AutoSwitchDecision(AutoSwitchAction Action, string? Reason)
{
    /// <summary>The "do nothing" decision.</summary>
    public static AutoSwitchDecision None { get; } = new(AutoSwitchAction.None, null);
}

/// <summary>
///     Pure, deterministic decision logic for automatic node switching during acceleration.
///     No timers and no I/O: the caller feeds every quality sample into <see cref="Observe" /> and
///     reports performed switches back via <see cref="RecordSwitch" />.
///     A sample is bad when loss ≥ <c>LossPct</c>, latency ≥ <c>LatencyMs</c>, or latency &lt; 0
///     (timeout). A single good sample resets the bad streak; a switch is due once the streak has
///     lasted <c>SustainSec</c>. After each switch the streak is cleared and no streak is evaluated
///     until the cooldown has elapsed AND a fresh full sustain window has accumulated. When the
///     switch budget (<c>MaxSwitches</c>) is used up, a due switch is reported as
///     <see cref="AutoSwitchAction.Exhausted" /> exactly once per session (until <see cref="Reset" />).
/// </summary>
public sealed class AutoSwitchPolicy
{
    private DateTime? _badSince;
    private DateTime? _lastSwitch;
    private bool _exhaustedReported;

    /// <summary>Number of switches performed (via <see cref="RecordSwitch" />) in the current session.</summary>
    public int Switches { get; private set; }

    /// <summary>Starts a fresh session: clears the streak, the cooldown and the switch budget.</summary>
    public void Reset()
    {
        _badSince = null;
        _lastSwitch = null;
        _exhaustedReported = false;
        Switches = 0;
    }

    /// <summary>
    ///     Feeds one quality sample. Returns <see cref="AutoSwitchAction.Switch" /> when the bad
    ///     streak has lasted <c>SustainSec</c> and the policy is not in cooldown,
    ///     <see cref="AutoSwitchAction.Exhausted" /> when a switch would be due but the budget is
    ///     used up (reported once per session), otherwise <see cref="AutoSwitchDecision.None" />.
    /// </summary>
    public AutoSwitchDecision Observe(DateTime now, int latencyMs, double lossPct, AutoSwitchOptions o)
    {
        if (!o.Enabled)
        {
            // Disabled: never act and keep no streak.
            _badSince = null;
            return AutoSwitchDecision.None;
        }

        if (!IsBad(latencyMs, lossPct, o))
        {
            // A single good sample resets the streak.
            _badSince = null;
            return AutoSwitchDecision.None;
        }

        // During the cooldown the bad streak is not evaluated at all: a fresh full sustain
        // window must accumulate after the cooldown before another switch becomes due.
        if (_lastSwitch is { } last && now - last < TimeSpan.FromSeconds(o.CooldownSec))
        {
            _badSince = null;
            return AutoSwitchDecision.None;
        }

        _badSince ??= now;
        var sustained = now - _badSince.Value;
        if (sustained < TimeSpan.FromSeconds(o.SustainSec))
            return AutoSwitchDecision.None;

        if (Switches >= o.MaxSwitches)
        {
            // A switch would be due but the budget is used up: report exhausted exactly once.
            if (_exhaustedReported)
                return AutoSwitchDecision.None;

            _exhaustedReported = true;
            return new AutoSwitchDecision(AutoSwitchAction.Exhausted, BuildReason(latencyMs, lossPct, o, sustained));
        }

        return new AutoSwitchDecision(AutoSwitchAction.Switch, BuildReason(latencyMs, lossPct, o, sustained));
    }

    /// <summary>
    ///     Reports that a switch was performed at <paramref name="now" />: bumps the budget,
    ///     starts the cooldown and clears the bad streak.
    /// </summary>
    public void RecordSwitch(DateTime now)
    {
        Switches++;
        _lastSwitch = now;
        _badSince = null;
    }

    private static bool IsBad(int latencyMs, double lossPct, AutoSwitchOptions o)
        => lossPct >= o.LossPct || latencyMs >= o.LatencyMs || latencyMs < 0;

    private static string BuildReason(int latencyMs, double lossPct, AutoSwitchOptions o, TimeSpan sustained)
    {
        var seconds = FormattableString.Invariant($"{Math.Round(sustained.TotalSeconds):0}");

        if (lossPct >= o.LossPct)
            return FormattableString.Invariant($"丢包 {lossPct:0.##}% ≥ {o.LossPct}%，持续 {seconds} 秒");

        if (latencyMs >= o.LatencyMs)
            return FormattableString.Invariant($"延迟 {latencyMs} ms ≥ {o.LatencyMs} ms，持续 {seconds} 秒");

        return FormattableString.Invariant($"测速超时，持续 {seconds} 秒");
    }
}
