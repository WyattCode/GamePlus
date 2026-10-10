using Netch.Core.Models.Games;
using Netch.Core.Services;

namespace Netch.Tests;

public class EffectMonitorTests
{
    private sealed class FakeProbe : IEffectProbe
    {
        public int DirectValue { get; set; } = 120;
        public int SocksValue { get; set; } = 74;
        public int DirectCalls { get; private set; }
        public int SocksCalls { get; private set; }

        public Task<int> DirectAsync(string host, int port, CancellationToken ct)
        {
            DirectCalls++;
            return Task.FromResult(DirectValue);
        }

        public Task<int> ViaSocksAsync(string socksHost, int socksPort, string host, int port, CancellationToken ct)
        {
            SocksCalls++;
            return Task.FromResult(SocksValue);
        }
    }

    private static Game GameWithTarget() =>
        new() { Id = "g", Name = "G", ProxyDomains = new List<string> { "game.example.com" } };

    // Tests drive SampleOnceAsync manually, so the background timer must be disabled.
    private static EffectMonitor NewMonitor(FakeProbe probe) =>
        new(probe, sampleInterval: Timeout.InfiniteTimeSpan);

    [Fact]
    public async Task MeasureBaseline_TakesFiveSamples()
    {
        var probe = new FakeProbe();
        var monitor = NewMonitor(probe);
        await monitor.MeasureBaselineAsync(GameWithTarget(), CancellationToken.None);

        Assert.Equal(5, probe.DirectCalls);
        var report = monitor.Report();
        Assert.True(report.HasTarget);
        Assert.NotNull(report.Baseline);
        Assert.Equal(120, report.Baseline!.Latency);
        Assert.Equal(5, report.Baseline.Samples);
    }

    [Fact]
    public async Task SampleOnce_AppendsPointsAndComputesImprovement()
    {
        var probe = new FakeProbe();
        var monitor = NewMonitor(probe);
        await monitor.MeasureBaselineAsync(GameWithTarget(), CancellationToken.None);
        monitor.Begin("127.0.0.1", 2801);

        for (var i = 0; i < 5; i++)
            await monitor.SampleOnceAsync();

        var report = monitor.Report();
        Assert.Equal(5, report.Series.Count);
        Assert.NotNull(report.Current);
        Assert.Equal(74, report.Current!.Latency);
        Assert.Equal(38, report.LatencyPct);
        Assert.Equal("延迟降低 38%", report.Summary);
        monitor.End();
    }

    [Fact]
    public async Task NoTarget_ReportsHasTargetFalse_AndBeginIsNoOp()
    {
        var probe = new FakeProbe();
        var monitor = NewMonitor(probe);
        await monitor.MeasureBaselineAsync(new Game { Id = "g", Name = "G" }, CancellationToken.None);

        var report = monitor.Report();
        Assert.False(report.HasTarget);
        Assert.Contains("未配置可测量的域名", report.Summary);

        monitor.Begin("127.0.0.1", 2801);
        await monitor.SampleOnceAsync();
        Assert.Equal(0, probe.SocksCalls);
        Assert.Empty(monitor.Report().Series);
    }

    [Fact]
    public async Task SeriesWindow_IsCappedAt90()
    {
        var probe = new FakeProbe();
        var monitor = NewMonitor(probe);
        await monitor.MeasureBaselineAsync(GameWithTarget(), CancellationToken.None);
        monitor.Begin("127.0.0.1", 2801);

        for (var i = 0; i < 100; i++)
            await monitor.SampleOnceAsync();

        Assert.Equal(90, monitor.Report().Series.Count);
        monitor.End();
    }
}
