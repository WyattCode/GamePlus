using Netch.Core.Services.Diagnostics;
using Xunit;

namespace Netch.Tests;

public class HostsAndFirewallTests
{
    [Fact]
    public void Comments_AreStripped()
    {
        const string content = "# a comment\n0.0.0.0 steampowered.com # trailing comment\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "steampowered.com" });

        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.Line);
        Assert.Equal("0.0.0.0", entry.Ip);
        Assert.Equal("steampowered.com", entry.Host);
        Assert.Equal("steampowered.com", entry.MatchedDomain);
    }

    [Fact]
    public void Ipv6_Addresses_AreParsed_NonIpLinesIgnored()
    {
        const string content = "::1 steamcontent.com\nnotanip steamstatic.com\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "steamcontent.com", "steamstatic.com" });

        var entry = Assert.Single(entries);
        Assert.Equal("::1", entry.Ip);
        Assert.Equal("steamcontent.com", entry.Host);
    }

    [Fact]
    public void Subdomain_Matches_WatchedDomain()
    {
        const string content = "1.2.3.4 store.steampowered.com\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "steampowered.com" });

        var entry = Assert.Single(entries);
        Assert.Equal("steampowered.com", entry.MatchedDomain);
    }

    [Fact]
    public void NoFalseMatch_OnSimilarButDifferentDomain()
    {
        const string content = "1.2.3.4 notsteampowered.com\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "steampowered.com" });

        Assert.Empty(entries);
    }

    [Fact]
    public void Localhost_Entries_AreIgnored()
    {
        const string content = "127.0.0.1 localhost\n::1 localhost\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "localhost" });

        Assert.Empty(entries);
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        const string content = "1.2.3.4 STORE.SteamPowered.COM\n";

        var entries = HostsFileAnalyzer.FindOverrides(content, new[] { "steampowered.com" });

        Assert.Single(entries);
    }

    [Fact]
    public void Firewall_NullOrEmptyPaths_NeverMatch()
    {
        var rules = new[]
        {
            new FirewallRuleInfo("A", null, "入站"),
            new FirewallRuleInfo("B", "", "出站")
        };

        var matches = FirewallRuleMatcher.Match(rules, new[] { "xray.exe" });

        Assert.Empty(matches);
    }

    [Fact]
    public void Firewall_MatchesByFileName_CaseInsensitive()
    {
        var rules = new[] { new FirewallRuleInfo("Block game", @"C:\Tools\XRAY.EXE", "出站") };

        var matches = FirewallRuleMatcher.Match(rules, new[] { "xray.exe" });

        var match = Assert.Single(matches);
        Assert.Equal("Block game", match.Name);
    }

    [Fact]
    public void Firewall_NonMatchingFileNames_DoNotMatch()
    {
        var rules = new[] { new FirewallRuleInfo("Unrelated", @"C:\Tools\other.exe", "入站") };

        var matches = FirewallRuleMatcher.Match(rules, new[] { "xray.exe" });

        Assert.Empty(matches);
    }
}
