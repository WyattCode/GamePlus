using System;
using System.Collections.Generic;
using System.Linq;
using Netch.Core.Models.Games;
using Netch.Core.Services;
using Netch.Core.Services.Rules;
using Xunit;

namespace Netch.Tests.Rules;

/// <summary>
///     Schema / sanity tests for the bundled (compiled-in) game rule library. These guard against
///     regressions in <see cref="SampleGames.Create" /> — duplicate ids, unknown region labels, missing
///     processes/domains, and accidentally re-adding broad shared CDNs that would proxy unrelated traffic.
/// </summary>
public class BundledRulesSchemaTests
{
    /// <summary>The ten titles whose rules were reviewed and tightened in RulesVersion 2.</summary>
    private static readonly string[] ReviewedTitles =
    {
        "valorant", "lol-global", "apex", "cs2", "pubg",
        "genshin", "ow2", "dota2", "ffxiv", "mhwilds"
    };

    /// <summary>Broad shared CDNs that must never appear in the bundled rules.</summary>
    private static readonly string[] BroadCdnDenylist =
    {
        "qq.com", "amazonaws.com", "cloudflare.com", "akamaihd.net",
        "googleapis.com", "azureedge.net", "cloudfront.net"
    };

    private static List<Game> Games() => SampleGames.Create().ToList();

    [Fact]
    public void Export_ParsesWithoutError()
    {
        var now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var json = RulesExporter.Export(SampleGames.Create(), SampleGames.RulesVersion, now);

        var file = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
    }

    [Fact]
    public void Ids_AreUnique()
    {
        var ids = Games().Select(g => g.Id).ToList();
        var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal)
            .Where(grp => grp.Count() > 1)
            .Select(grp => grp.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Regions_AreFromAllowedSet()
    {
        // Derived from every region label actually used across the whole bundled file.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "国服", "国际服", "亚服", "美服", "欧服", "日服", "韩服", "港服", "台服"
        };

        var unknown = Games()
            .SelectMany(g => g.Regions.Select(r => (g.Id, Region: r)))
            .Where(x => !allowed.Contains(x.Region))
            .Select(x => $"{x.Id}:{x.Region}")
            .Distinct()
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void ReviewedTitles_HaveProcessesAndDomains()
    {
        var games = Games().ToDictionary(g => g.Id, StringComparer.Ordinal);

        foreach (var id in ReviewedTitles)
        {
            Assert.True(games.TryGetValue(id, out var game), $"missing reviewed title: {id}");

            Assert.Contains(game!.ProxyProcesses, p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            Assert.True(game.ProxyDomains.Count >= 2, $"{id} must declare at least 2 domains");
        }
    }

    [Fact]
    public void NoBroadCdnDomains()
    {
        var offenders = Games()
            .SelectMany(g => g.ProxyDomains.Select(d => (g.Id, Domain: d)))
            .Where(x => BroadCdnDenylist.Any(bad =>
                x.Domain.Equals(bad, StringComparison.OrdinalIgnoreCase) ||
                x.Domain.EndsWith("." + bad, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Id}:{x.Domain}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("valorant")]
    [InlineData("genshin")]
    [InlineData("cs2")]
    [InlineData("ow2")]
    [InlineData("dota2")]
    [InlineData("ffxiv")]
    public void ServerChoiceTitles_HaveBothRegions(string id)
    {
        var game = Games().Single(g => g.Id == id);
        Assert.True(GameRegionPreference.HasServerChoice(game), $"{id} should offer 国服/国际服 choice");
    }

    [Theory]
    [InlineData("lol-global")]
    [InlineData("mhwilds")]
    public void SingleServerTitles_HaveNoServerChoice(string id)
    {
        var game = Games().Single(g => g.Id == id);
        Assert.False(GameRegionPreference.HasServerChoice(game), $"{id} should not offer 国服/国际服 choice");
    }

    [Fact]
    public void RulesVersion_IsAtLeastThree()
    {
        Assert.True(SampleGames.RulesVersion >= 3, "RulesVersion must be >= 3 after the download-domain split");
    }

    [Fact]
    public void Steam_HasDownloadDomainsAndNoSteamContentInProxy()
    {
        var steam = Games().Single(g => g.Id == "steam");

        Assert.Equal(new[] { "steamcontent.com", "steampipe.akamaized.net" }, steam.DownloadDomains);
        Assert.DoesNotContain(steam.ProxyDomains, d => d.Equals("steamcontent.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OldWrongLeagueProcessName_IsGone()
    {
        var offenders = Games()
            .SelectMany(g => g.ProxyProcesses.Select(p => (g.Id, Process: p)))
            .Where(x => x.Process.Equals("LeagueofLegends.exe", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Id}:{x.Process}")
            .ToList();

        Assert.Empty(offenders);
    }
}
