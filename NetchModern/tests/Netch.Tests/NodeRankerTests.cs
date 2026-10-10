using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class NodeRankerTests
{
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

    // --- region detection ---

    [Theory]
    [InlineData("香港 CN2", "HK")]
    [InlineData("HK-01", "HK")]
    [InlineData("🇭🇰 节点", "HK")]
    [InlineData("台湾 台北", "TW")]
    [InlineData("日本 东京 01", "JP")]
    [InlineData("韩国 首尔", "KR")]
    [InlineData("新加坡 01", "SG")]
    [InlineData("美国 洛杉矶", "US")]
    [InlineData("德国 法兰克福", "EU")]
    [InlineData("上海 电信", "CN")]
    public void DetectsRegionFromRemark(string remark, string expected)
    {
        var region = NodeRegions.Detect(remark, null);
        Assert.NotNull(region);
        Assert.Equal(expected, region!.Value.Code);
    }

    [Theory]
    [InlineData("JP01", "JP")]
    [InlineData("HK-01", "HK")]
    [InlineData("香港HK2", "HK")]
    [InlineData("US1 洛杉矶", "US")]
    [InlineData("日本 Tokyo", "JP")]
    [InlineData("🇺🇸 US1", "US")]
    public void DetectsShortCodeAdjacentToDigitsOrCjk(string remark, string expected)
    {
        var region = NodeRegions.Detect(remark, null);
        Assert.NotNull(region);
        Assert.Equal(expected, region!.Value.Code);
    }

    [Theory]
    [InlineData("us-west")]
    [InlineData("hk node")]
    [InlineData("jp-01")]
    public void LowercaseShortCodesDoNotMatch(string remark)
    {
        Assert.Null(NodeRegions.Detect(remark, null));
    }

    [Fact]
    public void RussiaIsNotUnitedStates()
    {
        var region = NodeRegions.Detect("Russia node", null);
        Assert.Null(region);
    }

    [Fact]
    public void PlusIsNotUnitedStates()
    {
        var region = NodeRegions.Detect("plus plan", null);
        Assert.Null(region);
    }

    [Fact]
    public void HkBeatsCnWhenBothPresent()
    {
        var region = NodeRegions.Detect("香港 CN2", null);
        Assert.Equal("HK", region!.Value.Code);
    }

    [Fact]
    public void UnknownReturnsNull()
    {
        Assert.Null(NodeRegions.Detect("node-42", "1.2.3.4"));
    }

    // --- preferences ---

    [Fact]
    public void AsiaPreferenceIncludesHkTwJpKrSg()
    {
        var pref = GameRegionPreference.For(GameWith("亚服"));
        Assert.NotNull(pref);
        Assert.Equal("亚服", pref!.Value.Label);
        Assert.Contains("HK", pref.Value.Regions);
        Assert.Contains("JP", pref.Value.Regions);
    }

    [Fact]
    public void CnAndIntlHasNoPreference()
    {
        Assert.Null(GameRegionPreference.For(GameWith("国服", "国际服")));
    }

    [Fact]
    public void IntlAloneHasNoPreference()
    {
        Assert.Null(GameRegionPreference.For(GameWith("国际服")));
    }

    [Fact]
    public void NoRegionsHasNoPreference()
    {
        Assert.Null(GameRegionPreference.For(GameWith()));
    }

    // --- ranking ---

    [Fact]
    public void PreferredNodeWithinPenaltyBeatsLowerUnmatched()
    {
        var game = GameWith("日服");
        var jp = Node("日本 东京", 58);
        var us = Node("美国 洛杉矶", 42);

        var ranking = NodeRanker.Rank(game, new[] { us, jp });

        Assert.Same(jp, ranking.Recommended!.Server);
    }

    [Fact]
    public void MuchLowerUnmatchedBeatsPreferred()
    {
        // JP 80 ms (score 80) vs US 10 ms (score 10 + 60 = 70): the unmatched node wins.
        var game = GameWith("日服");
        var jp = Node("日本 东京", 80);
        var us = Node("美国 洛杉矶", 10);

        var ranking = NodeRanker.Rank(game, new[] { us, jp });

        Assert.Same(us, ranking.Recommended!.Server);
        Assert.Contains("比最低延迟线路慢 60 ms 以上", ranking.Reason);
    }

    [Fact]
    public void UntestedSortsAfterTested()
    {
        var game = GameWith("日服");
        var tested = Node("日本 东京", 90);
        var untested = Node("美国 洛杉矶", -1);

        var ranking = NodeRanker.Rank(game, new[] { untested, tested });

        Assert.Same(tested, ranking.Recommended!.Server);
        Assert.Equal(0, ranking.Candidates.ToList().IndexOf(ranking.Candidates.First(c => ReferenceEquals(c.Server, tested))));
    }

    [Fact]
    public void FailedSortsAfterUntested()
    {
        var game = GameWith("日服");
        var untested = Node("日本 东京", -1);
        var failed = Node("美国 洛杉矶", -4);

        var ranking = NodeRanker.Rank(game, new[] { failed, untested });

        Assert.Same(untested, ranking.Candidates[0].Server);
        Assert.Same(failed, ranking.Candidates[1].Server);
    }

    [Fact]
    public void UntestedAndFailedHaveScoreMinusOne()
    {
        var ranking = NodeRanker.Rank(GameWith("日服"), new[] { Node("日本 东京", -1), Node("美国 洛杉矶", -4) });
        Assert.All(ranking.Candidates, c => Assert.Equal(-1, c.Score));
    }

    [Fact]
    public void NotesAreTags()
    {
        var game = GameWith("日服");
        var jp = Node("日本 东京", 42);
        var us = Node("美国 洛杉矶", 90);
        var untested = Node("德国 法兰克福", -1);
        var failed = Node("英国 伦敦", -4);

        var ranking = NodeRanker.Rank(game, new[] { jp, us, untested, failed });

        var jpNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, jp));
        var usNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, us));
        var untestedNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, untested));
        var failedNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, failed));

        Assert.Equal("延迟最低 · 区服匹配", jpNode.Note);
        // US is not the lowest-delay node and does not match 日服, so it carries no tags.
        Assert.Equal("", usNode.Note);
        Assert.Equal("未测速", untestedNode.Note);
        Assert.Equal("超时/不可达", failedNode.Note);
    }

    [Fact]
    public void NotesLowestUnmatchedNodeGetsDelayTag()
    {
        // KR 30 ms is the lowest-delay node but does not match 日服; JP 42 ms matches but is not lowest.
        var game = GameWith("日服");
        var jp = Node("日本 东京", 42);
        var kr = Node("韩国 首尔", 30);

        var ranking = NodeRanker.Rank(game, new[] { jp, kr });

        var jpNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, jp));
        var krNode = ranking.Candidates.First(c => ReferenceEquals(c.Server, kr));

        Assert.Equal("区服匹配", jpNode.Note);
        Assert.Equal("延迟最低", krNode.Note);
    }

    [Fact]
    public void ReasonNoPreferenceIsLowestDelay()
    {
        var ranking = NodeRanker.Rank(GameWith(), new[] { Node("A", 42), Node("B", 90) });
        Assert.Contains("延迟最低（42 ms）", ranking.Reason);
    }

    [Fact]
    public void ReasonCnAndIntlNotesAmbiguity()
    {
        var ranking = NodeRanker.Rank(GameWith("国服", "国际服"), new[] { Node("A", 42) });
        Assert.Contains("国服和国际服", ranking.Reason);
    }

    [Fact]
    public void ReasonPreferredAndLowest()
    {
        var ranking = NodeRanker.Rank(GameWith("日服"), new[] { Node("日本 东京", 42), Node("美国 洛杉矶", 90) });
        Assert.Contains("延迟最低（42 ms）", ranking.Reason);
        Assert.Contains("日服", ranking.Reason);
    }

    [Fact]
    public void ReasonPreferredButNotLowest()
    {
        var ranking = NodeRanker.Rank(GameWith("日服"), new[] { Node("日本 东京", 58), Node("美国 洛杉矶", 42) });
        Assert.Contains("仅比最低延迟线路高 16 ms", ranking.Reason);
    }

    [Fact]
    public void ReasonNoMatchingRegion()
    {
        var ranking = NodeRanker.Rank(GameWith("日服"), new[] { Node("美国 洛杉矶", 42) });
        Assert.Contains("没有日服所在地区的可用线路", ranking.Reason);
    }

    [Fact]
    public void ReasonNothingTested()
    {
        var ranking = NodeRanker.Rank(GameWith("日服"), new[] { Node("日本 东京", -1), Node("美国 洛杉矶", -4) });
        Assert.Equal("所有线路均未测通，已选择列表中的第一条线路", ranking.Reason);
    }

    [Fact]
    public void ReasonDescribesRecommendedNodeNotLowest()
    {
        // Unmatched 10 ms (score 70) vs matched 80 ms (score 80): the unmatched node is recommended,
        // so the reason must describe the unmatched node, not the matched one.
        var game = GameWith("日服");
        var us = Node("美国 洛杉矶", 10);
        var jp = Node("日本 东京", 80);

        var ranking = NodeRanker.Rank(game, new[] { us, jp });

        Assert.Same(us, ranking.Recommended!.Server);
        Assert.Contains("已选择延迟最低的线路（10 ms）", ranking.Reason);
        Assert.DoesNotContain("仅比最低延迟线路高", ranking.Reason);
    }
}
