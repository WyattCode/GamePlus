using Netch.Core.Models.Settings;
using Netch.Core.Services.AutoSwitch;
using Xunit;

namespace Netch.Tests;

public class AutoSwitchPolicyTests
{
    // Fixed epoch so every test works with whole-second offsets.
    private static readonly DateTime T0 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int seconds) => T0.AddSeconds(seconds);

    private static AutoSwitchOptions Options(
        bool enabled = true,
        int lossPct = 5,
        int latencyMs = 200,
        int sustainSec = 15,
        int cooldownSec = 60,
        int maxSwitches = 3)
        => new(enabled, lossPct, latencyMs, sustainSec, cooldownSec, maxSwitches);

    // --- disabled ---

    [Fact]
    public void Observe_Disabled_NeverActsAndKeepsNoStreak()
    {
        var policy = new AutoSwitchPolicy();
        var off = Options(enabled: false);

        // Terribly bad quality for minutes — still no decision while disabled.
        for (var s = 0; s <= 600; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), 999, 99, off));

        Assert.Equal(0, policy.Switches);

        // No streak was kept while disabled: the first bad sample after enabling starts fresh.
        var on = Options();
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(605), 999, 99, on));
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(620), 999, 99, on).Action);
    }

    // --- sustained loss ---

    [Fact]
    public void Observe_SustainedLoss_SwitchesWithReason()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options();

        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(0), 50, 8, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(5), 50, 8, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(10), 50, 8, o));

        var decision = policy.Observe(At(15), 50, 8, o);
        Assert.Equal(AutoSwitchAction.Switch, decision.Action);
        Assert.Equal("丢包 8% ≥ 5%，持续 15 秒", decision.Reason);
    }

    // --- a single good sample resets the streak ---

    [Fact]
    public void Observe_GoodSample_ResetsStreak()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options();

        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(0), 50, 8, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(5), 50, 8, o));

        // A single good sample wipes the streak.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(10), 50, 2, o));

        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(15), 50, 8, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(20), 50, 8, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(25), 50, 8, o)); // only 10 s since restart

        var decision = policy.Observe(At(30), 50, 8, o); // 15 s since restart
        Assert.Equal(AutoSwitchAction.Switch, decision.Action);
    }

    // --- latency threshold / timeout ---

    [Fact]
    public void Observe_SustainedHighLatency_SwitchesWithReason()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options();

        for (var s = 0; s <= 10; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), 320, 0, o));

        var decision = policy.Observe(At(15), 320, 0, o);
        Assert.Equal(AutoSwitchAction.Switch, decision.Action);
        Assert.Equal("延迟 320 ms ≥ 200 ms，持续 15 秒", decision.Reason);
    }

    [Fact]
    public void Observe_NegativeLatency_CountsAsTimeout()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options();

        for (var s = 0; s <= 10; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), -1, 0, o));

        var decision = policy.Observe(At(15), -1, 0, o);
        Assert.Equal(AutoSwitchAction.Switch, decision.Action);
        Assert.Equal("测速超时，持续 15 秒", decision.Reason);
    }

    // --- cooldown ---

    [Fact]
    public void Observe_CooldownBlocks_AndRequiresFreshWindow()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options(); // sustain 15 s, cooldown 60 s

        // Switch at t=15.
        for (var s = 0; s <= 15; s += 5)
        {
            var d = policy.Observe(At(s), 320, 0, o);
            Assert.Equal(s == 15 ? AutoSwitchAction.Switch : AutoSwitchAction.None, d.Action);
        }

        policy.RecordSwitch(At(15));
        Assert.Equal(1, policy.Switches);

        // Still bad: blocked for the whole cooldown (t=20..70) ...
        for (var s = 20; s <= 70; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), 320, 0, o));

        // ... and the sustain window only starts after the cooldown, so t=75..85 is not enough yet.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(75), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(80), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(85), 320, 0, o));

        var decision = policy.Observe(At(90), 320, 0, o); // fresh full window
        Assert.Equal(AutoSwitchAction.Switch, decision.Action);
        policy.RecordSwitch(At(90));
        Assert.Equal(2, policy.Switches);
    }

    [Fact]
    public void Observe_CooldownBoundary_ReleasesExactlyAfterCooldownSec()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options(cooldownSec: 60);

        policy.RecordSwitch(At(0));

        // One second before the cooldown ends: still blocked.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(59), 320, 0, o));

        // Exactly at the end the streak restarts from scratch (15 s later the switch fires).
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(60), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(70), 320, 0, o));
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(75), 320, 0, o).Action);
    }

    // --- RecordSwitch clears the streak ---

    [Fact]
    public void RecordSwitch_ClearsStreak_EvenWithoutCooldown()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options(cooldownSec: 0);

        for (var s = 0; s <= 10; s += 5)
            policy.Observe(At(s), 320, 0, o);
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(15), 320, 0, o).Action);
        policy.RecordSwitch(At(15));

        // The streak was cleared: a fresh 15 s window is required even with no cooldown.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(20), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(25), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(30), 320, 0, o));
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(35), 320, 0, o).Action);
    }

    // --- max switches / exhausted ---

    [Fact]
    public void Observe_MaxSwitchesReached_ExhaustedExactlyOnce()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options(maxSwitches: 1);

        // Burn the single switch at t=15.
        for (var s = 0; s <= 10; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), 320, 0, o));
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(15), 320, 0, o).Action);
        policy.RecordSwitch(At(15));

        // Cooldown (t=20..70) plus an incomplete fresh window (t=75..85) → still silent.
        for (var s = 20; s <= 85; s += 5)
            Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(s), 320, 0, o));

        // A switch would be due at t=90 → exhausted, reported exactly once.
        var exhausted = policy.Observe(At(90), 320, 0, o);
        Assert.Equal(AutoSwitchAction.Exhausted, exhausted.Action);
        Assert.NotNull(exhausted.Reason);

        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(95), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(100), 320, 0, o));

        // Even a good sample followed by a new full bad window stays silent until Reset.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(105), 20, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(120), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(135), 320, 0, o));
    }

    // --- reset ---

    [Fact]
    public void Reset_ClearsSwitchesStreakAndExhausted()
    {
        var policy = new AutoSwitchPolicy();
        var o = Options(maxSwitches: 1);

        for (var s = 0; s <= 10; s += 5)
            policy.Observe(At(s), 320, 0, o);
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(15), 320, 0, o).Action);
        policy.RecordSwitch(At(15));

        for (var s = 20; s <= 85; s += 5)
            policy.Observe(At(s), 320, 0, o);
        Assert.Equal(AutoSwitchAction.Exhausted, policy.Observe(At(90), 320, 0, o).Action);

        policy.Reset();
        Assert.Equal(0, policy.Switches);

        // The old streak is gone: a single bad sample right after Reset must not switch.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(100), 320, 0, o));

        // A fresh session works again.
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(105), 320, 0, o));
        Assert.Equal(AutoSwitchDecision.None, policy.Observe(At(110), 320, 0, o));
        Assert.Equal(AutoSwitchAction.Switch, policy.Observe(At(115), 320, 0, o).Action);
    }

    // --- options mapping ---

    [Fact]
    public void FromSettings_MapsAllAdvancedSettingsFields()
    {
        var settings = new AdvancedSettings
        {
            AutoSwitchEnabled = true,
            AutoSwitchLossPct = 10,
            AutoSwitchLatencyMs = 300,
            AutoSwitchSustainSec = 20,
            AutoSwitchCooldownSec = 90,
            AutoSwitchMaxSwitches = 2
        };

        var o = AutoSwitchOptions.FromSettings(settings);

        Assert.True(o.Enabled);
        Assert.Equal(10, o.LossPct);
        Assert.Equal(300, o.LatencyMs);
        Assert.Equal(20, o.SustainSec);
        Assert.Equal(90, o.CooldownSec);
        Assert.Equal(2, o.MaxSwitches);
    }
}
