using System.Text.Json;
using System.Text.RegularExpressions;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class SplitRoutingTests
{
    // --- ToRule ---

    [Fact]
    public void ToRule_EscapesAndAnchors()
    {
        var rule = SplitRouting.ToRule("Discord.exe");
        Assert.Equal("(^|\\\\)Discord\\.exe$", rule);

        var re = new Regex(rule, RegexOptions.IgnoreCase);
        Assert.Matches(re, @"C:\Apps\Discord\Discord.exe");
        Assert.DoesNotMatch(re, @"C:\x\NotDiscord.exe");
        Assert.DoesNotMatch(re, "Discordxexe");
    }

    [Fact]
    public void ToRule_EscapesAllMetacharacters()
    {
        var rule = SplitRouting.ToRule("a+b(c)[d].exe");
        var re = new Regex(rule, RegexOptions.IgnoreCase);
        Assert.Matches(re, @"C:\x\a+b(c)[d].exe");
        Assert.DoesNotMatch(re, @"C:\x\aXbXcXdXexe");
    }

    // --- IsValidProcessName ---

    [Theory]
    [InlineData("Discord.exe")]
    [InlineData("steam.exe")]
    [InlineData("My App (x64).exe")]
    [InlineData("a_b-c+d[1].exe")]
    public void IsValidProcessName_Accepts(string s)
    {
        Assert.True(SplitRouting.IsValidProcessName(s));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Discord")]
    [InlineData("Discord.txt")]
    [InlineData(@"C:\Apps\Discord.exe")]
    [InlineData("Apps/Discord.exe")]
    [InlineData("Discord.exe ")]
    public void IsValidProcessName_Rejects(string s)
    {
        Assert.False(SplitRouting.IsValidProcessName(s));
    }

    // --- BuildProcessMode ---

    [Fact]
    public void BuildProcessMode_KeepsGameRulesVerbatim()
    {
        var mode = SplitRouting.BuildProcessMode(new[] { "steam.exe", "game.exe" }, Array.Empty<ExtraApp>());
        Assert.Equal(ModeType.ProcessMode, mode.Type);
        Assert.Equal(new[] { "steam.exe", "game.exe" }, mode.Handle);
        Assert.Empty(mode.Bypass);
    }

    [Fact]
    public void BuildProcessMode_AddsProxyAndBypassApps()
    {
        var apps = new[]
        {
            new ExtraApp { Name = "Discord", Process = "Discord.exe", Mode = ExtraAppMode.Proxy },
            new ExtraApp { Name = "YY", Process = "YY.exe", Mode = ExtraAppMode.Bypass }
        };
        var mode = SplitRouting.BuildProcessMode(new[] { "steam.exe" }, apps);

        Assert.Equal(new[] { "steam.exe", SplitRouting.ToRule("Discord.exe") }, mode.Handle);
        Assert.Equal(new[] { SplitRouting.ToRule("YY.exe") }, mode.Bypass);
    }

    [Fact]
    public void BuildProcessMode_IgnoresDisabledAndInvalid()
    {
        var apps = new[]
        {
            new ExtraApp { Process = "Off.exe", Mode = ExtraAppMode.Proxy, Enabled = false },
            new ExtraApp { Process = "bad name", Mode = ExtraAppMode.Proxy },
            new ExtraApp { Process = @"C:\x\Path.exe", Mode = ExtraAppMode.Bypass }
        };
        var mode = SplitRouting.BuildProcessMode(new[] { "steam.exe" }, apps);

        Assert.Equal(new[] { "steam.exe" }, mode.Handle);
        Assert.Empty(mode.Bypass);
    }

    [Fact]
    public void BuildProcessMode_DeduplicatesCaseInsensitively()
    {
        var apps = new[]
        {
            new ExtraApp { Process = "Discord.exe", Mode = ExtraAppMode.Proxy },
            new ExtraApp { Process = "discord.EXE", Mode = ExtraAppMode.Proxy }
        };
        var mode = SplitRouting.BuildProcessMode(Array.Empty<string>(), apps);

        Assert.Single(mode.Handle);
        Assert.Equal(SplitRouting.ToRule("Discord.exe"), mode.Handle[0]);
    }

    [Fact]
    public void BuildProcessMode_BypassWinsOverExtraProxy()
    {
        var apps = new[]
        {
            new ExtraApp { Process = "Discord.exe", Mode = ExtraAppMode.Proxy },
            new ExtraApp { Process = "Discord.exe", Mode = ExtraAppMode.Bypass }
        };
        var mode = SplitRouting.BuildProcessMode(Array.Empty<string>(), apps);

        Assert.Empty(mode.Handle);
        Assert.Equal(new[] { SplitRouting.ToRule("Discord.exe") }, mode.Bypass);
    }

    [Fact]
    public void BuildProcessMode_BypassDoesNotRemoveGameRules()
    {
        // A game rule that happens to equal an extra-app bypass rule must stay in Handle.
        var apps = new[] { new ExtraApp { Process = "steam.exe", Mode = ExtraAppMode.Bypass } };
        var mode = SplitRouting.BuildProcessMode(new[] { "steam.exe" }, apps);

        Assert.Contains("steam.exe", mode.Handle);
        Assert.Equal(new[] { SplitRouting.ToRule("steam.exe") }, mode.Bypass);
    }

    // --- settings JSON round-trip ---

    [Fact]
    public void ExtraApps_RoundTripWithStringEnum()
    {
        var settings = new AppSettings
        {
            ExtraApps =
            {
                new ExtraApp { Name = "Discord", Process = "Discord.exe", Mode = ExtraAppMode.Proxy, Enabled = true },
                new ExtraApp { Name = "YY", Process = "YY.exe", Mode = ExtraAppMode.Bypass, Enabled = false }
            }
        };

        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"Proxy\"", json);
        Assert.Contains("\"Bypass\"", json);

        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(2, back.ExtraApps.Count);
        Assert.Equal(ExtraAppMode.Proxy, back.ExtraApps[0].Mode);
        Assert.Equal("Discord.exe", back.ExtraApps[0].Process);
        Assert.True(back.ExtraApps[0].Enabled);
        Assert.Equal(ExtraAppMode.Bypass, back.ExtraApps[1].Mode);
        Assert.False(back.ExtraApps[1].Enabled);
    }
}
