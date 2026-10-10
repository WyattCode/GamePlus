using System.Text.Json;
using Netch.Core.Controllers;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class AdvancedSettingsTests
{
    // --- DNS validation ---

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("223.5.5.5:53")]
    [InlineData("[2606:4700:4700::1111]:53")]
    [InlineData("  1.1.1.1  ")]
    public void ValidateDns_AcceptsIpAddresses(string value)
    {
        var result = AdvancedSettingsValidator.ValidateDns(value);
        Assert.Equal(value.Trim(), result);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("2606:4700:4700::1111")]
    public void ValidateDns_BareIpv6_ReturnsBracketedForm(string value)
    {
        var result = AdvancedSettingsValidator.ValidateDns(value);
        Assert.Equal($"[{value}]", result);
    }

    [Fact]
    public void ValidateDns_BareIpv6_NormalizesToHostPort()
    {
        var validated = AdvancedSettingsValidator.ValidateDns("::1");
        Assert.Equal("[::1]", validated);
        Assert.Equal("[::1]:53", TunModeController.NormalizeDns(validated));
    }

    [Theory]
    [InlineData("1.1.1.1:0")]
    [InlineData("1.1.1.1:70000")]
    [InlineData("dns.google")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.1.1.1,8.8.8.8")]
    [InlineData("not-an-ip")]
    [InlineData("8")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3.256")]
    [InlineData("8:53")]
    public void ValidateDns_RejectsInvalid(string value)
    {
        Assert.Throws<InvalidOperationException>(() => AdvancedSettingsValidator.ValidateDns(value));
    }

    // --- region penalty ---

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(300)]
    public void ValidateRegionPenalty_AcceptsInRange(int value)
    {
        Assert.Equal(value, AdvancedSettingsValidator.ValidateRegionPenalty(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public void ValidateRegionPenalty_RejectsOutOfRange(int value)
    {
        Assert.Throws<InvalidOperationException>(() => AdvancedSettingsValidator.ValidateRegionPenalty(value));
    }

    // --- local port ---

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(65535)]
    public void ValidateLocalPort_AcceptsInRange(int value)
    {
        Assert.Equal(value, AdvancedSettingsValidator.ValidateLocalPort(value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void ValidateLocalPort_RejectsOutOfRange(int value)
    {
        Assert.Throws<InvalidOperationException>(() => AdvancedSettingsValidator.ValidateLocalPort(value));
    }

    // --- NodeRanker penalty parameter ---

    private static ShadowsocksServer Node(string remark, int delay, string host = "h.example.net")
        => new()
        {
            Group = "NONE",
            Hostname = host,
            Port = 8388,
            Remark = remark,
            Delay = delay,
            Password = "pw",
            EncryptMethod = "aes-256-gcm"
        };

    private static Game GameWith(params string[] regions) => new()
    {
        Id = "g",
        Name = "G",
        Regions = regions.ToList()
    };

    [Fact]
    public void Rank_WithZeroPenalty_PicksLowestDelayRegardlessOfRegion()
    {
        var game = GameWith("日服");
        var jp = Node("日本 东京", 80);
        var us = Node("美国 洛杉矶", 10);

        var ranking = NodeRanker.Rank(game, new[] { us, jp }, 0);

        Assert.Same(us, ranking.Recommended!.Server);
    }

    [Fact]
    public void Rank_WithMaxPenalty_PrefersRegionNode()
    {
        var game = GameWith("日服");
        var jp = Node("日本 东京", 80);
        var us = Node("美国 洛杉矶", 10);

        var ranking = NodeRanker.Rank(game, new[] { us, jp }, 300);

        Assert.Same(jp, ranking.Recommended!.Server);
        // The region node IS recommended here, so the reason describes it (not the "慢 N ms 以上" fallback).
        Assert.Contains("位于日服所在地区（日本）", ranking.Reason);
        Assert.Contains("延迟 80 ms", ranking.Reason);
        Assert.Contains("仅比最低延迟线路高 70 ms", ranking.Reason);
    }

    [Fact]
    public void Rank_WithPenalty30_RegionNodeTooSlow_PicksLowestDelay()
    {
        var game = GameWith("日服");
        var jp = Node("日本 东京", 80);
        var us = Node("美国 洛杉矶", 10);

        // JP scores 80 + 30 = 110, US scores 10 → the lowest-delay node wins.
        var ranking = NodeRanker.Rank(game, new[] { us, jp }, 30);

        Assert.Same(us, ranking.Recommended!.Server);
        Assert.Contains("慢 30 ms 以上", ranking.Reason);
    }

    // --- EffectMonitor.Disable ---

    private sealed class FakeProbe : IEffectProbe
    {
        public Task<int> DirectAsync(string host, int port, CancellationToken ct) => Task.FromResult(10);
        public Task<int> ViaSocksAsync(string socksHost, int socksPort, string host, int port, CancellationToken ct)
            => Task.FromResult(5);
    }

    [Fact]
    public void EffectMonitor_Disable_ReportShowsDisabled()
    {
        var monitor = new EffectMonitor(new FakeProbe());
        monitor.Disable();

        var report = monitor.Report();

        Assert.False(report.HasTarget);
        Assert.Equal("已关闭加速效果测量", report.Summary);
        Assert.False(report.Measuring);
    }

    [Fact]
    public async Task EffectMonitor_MeasureBaseline_ResetsDisabled()
    {
        var monitor = new EffectMonitor(new FakeProbe());
        monitor.Disable();
        Assert.Equal("已关闭加速效果测量", monitor.Report().Summary);

        var game = new Game { Id = "g", Name = "G", ProxyDomains = new List<string> { "game.example.com" } };
        await monitor.MeasureBaselineAsync(game, CancellationToken.None);

        Assert.NotEqual("已关闭加速效果测量", monitor.Report().Summary);
    }

    // --- settings JSON round-trip ---

    [Fact]
    public void AdvancedSettings_RoundTripsThroughJson()
    {
        var settings = new AppSettings();
        settings.Advanced.MeasureEffect = false;
        settings.Advanced.RegionPenaltyMs = 120;
        settings.Advanced.LocalSocksPort = 10808;
        settings.Dns.OtherDns = "223.5.5.5:53";

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(loaded);
        Assert.False(loaded!.Advanced.MeasureEffect);
        Assert.Equal(120, loaded.Advanced.RegionPenaltyMs);
        Assert.Equal(10808, loaded.Advanced.LocalSocksPort);
        Assert.Equal("223.5.5.5:53", loaded.Dns.OtherDns);
    }
}
