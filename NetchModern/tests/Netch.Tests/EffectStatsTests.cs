using Netch.Core.Services;

namespace Netch.Tests;

public class EffectStatsTests
{
    [Fact]
    public void Compute_MeanLossJitter()
    {
        var snap = EffectStats.Compute(new[] { 100, 120, 110, -4, 130 });
        Assert.Equal(115, snap.Latency);
        Assert.Equal(20.0, snap.Loss);
        Assert.Equal(5, snap.Samples);
        Assert.True(snap.Jitter > 0);
    }

    [Fact]
    public void Compute_AllFail()
    {
        var snap = EffectStats.Compute(new[] { -4, -4, -4 });
        Assert.Equal(-1, snap.Latency);
        Assert.Equal(100.0, snap.Loss);
        Assert.Equal(0, snap.Jitter);
        Assert.Equal(3, snap.Samples);
    }

    [Fact]
    public void Compute_Empty()
    {
        var snap = EffectStats.Compute(Array.Empty<int>());
        Assert.Equal(-1, snap.Latency);
        Assert.Equal(0, snap.Samples);
    }

    [Fact]
    public void Improvement_ThirtyEightPercent()
    {
        var baseline = new EffectSnapshot(120, 0, 0, 5);
        var current = new EffectSnapshot(74, 0, 0, 5);
        var (pct, summary) = EffectStats.Improvement(baseline, current);
        Assert.Equal(38, pct);
        Assert.Equal("延迟降低 38%", summary);
    }

    [Fact]
    public void Improvement_Worse()
    {
        var baseline = new EffectSnapshot(100, 0, 0, 5);
        var current = new EffectSnapshot(150, 0, 0, 5);
        var (pct, summary) = EffectStats.Improvement(baseline, current);
        Assert.Equal(-50, pct);
        Assert.Contains("延迟升高", summary);
        Assert.Contains("建议切换线路", summary);
    }

    [Fact]
    public void Improvement_NearEqual()
    {
        var baseline = new EffectSnapshot(100, 0, 0, 5);
        var current = new EffectSnapshot(102, 0, 0, 5);
        var (pct, summary) = EffectStats.Improvement(baseline, current);
        Assert.Equal(-2, pct);
        Assert.Equal("延迟与直连相近", summary);
    }

    [Fact]
    public void Improvement_InsufficientSamples()
    {
        var baseline = new EffectSnapshot(100, 0, 0, 2);
        var current = new EffectSnapshot(50, 0, 0, 5);
        var (pct, summary) = EffectStats.Improvement(baseline, current);
        Assert.Null(pct);
        Assert.Equal("正在测量…", summary);
    }

    [Fact]
    public void Improvement_BaselineUnreachable()
    {
        var baseline = new EffectSnapshot(-1, 100, 0, 5);
        var current = new EffectSnapshot(80, 0, 0, 5);
        var (pct, summary) = EffectStats.Improvement(baseline, current);
        Assert.Null(pct);
        Assert.Equal("直连无法连通，加速后可正常连接", summary);
    }
}
