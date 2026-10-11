using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class SingBoxTunConfigTests
{
    private static Socks5Server Socks(ushort port = 1080, string? username = null, string? password = null) => new()
    {
        Hostname = "127.0.0.1",
        Port = port,
        Username = username,
        Password = password
    };

    private static TunRouting Routing(
        IReadOnlyList<string>? proxyProcesses = null,
        IReadOnlyList<string>? bypassProcesses = null,
        IReadOnlyList<string>? proxyDomains = null,
        IReadOnlyList<string>? proxyIps = null,
        IReadOnlyList<string>? directDomains = null,
        IReadOnlyList<string>? excludeAddresses = null) => new(
        proxyProcesses ?? Array.Empty<string>(),
        bypassProcesses ?? Array.Empty<string>(),
        proxyDomains ?? Array.Empty<string>(),
        proxyIps ?? Array.Empty<string>(),
        directDomains ?? Array.Empty<string>(),
        excludeAddresses ?? Array.Empty<string>());

    // ---------------------------------------------------------------- ProcessPathRegex

    [Theory]
    [InlineData("steam.exe", "(?i)(^|\\\\)steam(\\.exe)?$")]
    [InlineData("steam", "(?i)(^|\\\\)steam(\\.exe)?$")]
    [InlineData("C:\\Games\\steam.exe", "(?i)(^|\\\\)steam(\\.exe)?$")]
    [InlineData("Game+ (x64).exe", "(?i)(^|\\\\)Game\\+ \\(x64\\)(\\.exe)?$")]
    [InlineData("a.b.exe", "(?i)(^|\\\\)a\\.b(\\.exe)?$")]
    public void ProcessPathRegex_KnownInputs(string input, string expected)
    {
        Assert.Equal(expected, SingBoxTunConfigGenerator.ProcessPathRegex(input));
    }

    [Fact]
    public void ProcessPathRegex_MatchesFullPathsCaseInsensitively()
    {
        var regex = new Regex(SingBoxTunConfigGenerator.ProcessPathRegex("steam.exe"));

        Assert.Matches(regex, @"C:\Program Files\Steam\steam.exe");
        Assert.Matches(regex, @"D:\STEAM.EXE");
        Assert.Matches(regex, @"C:\x\steam");

        Assert.DoesNotMatch(regex, @"C:\x\notsteam.exe");
        Assert.DoesNotMatch(regex, @"C:\x\steam.exe.bak");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessPathRegex_BlankName_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => SingBoxTunConfigGenerator.ProcessPathRegex(input));
    }

    // ---------------------------------------------------------------- Build: inbound

    [Fact]
    public void Build_TunInbound_HasExpectedFields()
    {
        var json = SingBoxTunConfigGenerator.Build(
            Routing(excludeAddresses: new[] { "1.2.3.4", "10.0.0.0/8", "2001:db8::1" }), Socks());

        var inbound = (JsonObject)json!["inbounds"]![0]!;

        Assert.Equal("tun", (string)inbound["type"]!);
        Assert.Equal("tun-in", (string)inbound["tag"]!);
        Assert.Equal("GamePlus", (string)inbound["interface_name"]!);
        Assert.Equal("172.19.0.1/30", (string)((JsonArray)inbound["address"]!)[0]!);
        Assert.True((bool)inbound["auto_route"]!);
        Assert.False((bool)inbound["strict_route"]!);
        Assert.Equal("disabled", (string)inbound["dns_mode"]!);
        Assert.Equal("gvisor", (string)inbound["stack"]!);
    }

    [Fact]
    public void Build_RouteExcludeAddress_IsNormalisedToCidr()
    {
        var json = SingBoxTunConfigGenerator.Build(
            Routing(excludeAddresses: new[] { "1.2.3.4", "10.0.0.0/8", "2001:db8::1" }), Socks());

        var inbound = (JsonObject)json!["inbounds"]![0]!;
        var exclude = (JsonArray)inbound["route_exclude_address"]!;

        Assert.Equal(
            new[] { "1.2.3.4/32", "10.0.0.0/8", "2001:db8::1/128" },
            exclude.Select(n => (string)n!));
    }

    [Fact]
    public void Build_RouteExcludeAddress_OmittedWhenEmpty()
    {
        var json = SingBoxTunConfigGenerator.Build(Routing(), Socks());

        var inbound = (JsonObject)json!["inbounds"]![0]!;
        Assert.Null(inbound["route_exclude_address"]);
    }

    // ---------------------------------------------------------------- Build: outbounds

    [Fact]
    public void Build_SocksOutbound_WithoutAuth_HasNoCredentials()
    {
        var json = SingBoxTunConfigGenerator.Build(Routing(), Socks(7890));

        var outbounds = (JsonArray)json!["outbounds"]!;
        Assert.Equal(2, outbounds.Count);

        var direct = (JsonObject)outbounds[0]!;
        Assert.Equal("direct", (string)direct["type"]!);
        Assert.Equal("direct", (string)direct["tag"]!);

        var proxy = (JsonObject)outbounds[1]!;
        Assert.Equal("socks", (string)proxy["type"]!);
        Assert.Equal("proxy", (string)proxy["tag"]!);
        Assert.Equal("127.0.0.1", (string)proxy["server"]!);
        Assert.Equal(7890, (int)proxy["server_port"]!);
        Assert.Equal("5", (string)proxy["version"]!);
        Assert.Null(proxy["username"]);
        Assert.Null(proxy["password"]);
    }

    [Fact]
    public void Build_SocksOutbound_WithAuth_IncludesCredentials()
    {
        var json = SingBoxTunConfigGenerator.Build(Routing(), Socks(username: "user", password: "pass"));

        var proxy = (JsonObject)((JsonArray)json!["outbounds"]!)[1]!;
        Assert.Equal("user", (string)proxy["username"]!);
        Assert.Equal("pass", (string)proxy["password"]!);
    }

    // ---------------------------------------------------------------- Build: rules

    [Fact]
    public void Build_RuleOrder_FollowsSpecification()
    {
        var routing = Routing(
            proxyProcesses: new[] { "game.exe" },
            bypassProcesses: new[] { "voice.exe" },
            proxyDomains: new[] { "example.com" },
            proxyIps: new[] { "1.1.1.1" },
            directDomains: new[] { "cdn.example.com" });

        var json = SingBoxTunConfigGenerator.Build(routing, Socks());
        var route = (JsonObject)json!["route"]!;
        var rules = (JsonArray)route["rules"]!;

        Assert.Equal(8, rules.Count);

        // 0: sniff on tun-in, ports 80/443.
        var sniff = (JsonObject)rules[0]!;
        Assert.Equal("tun-in", (string)sniff["inbound"]!);
        Assert.Equal("sniff", (string)sniff["action"]!);
        Assert.Equal(new[] { 80, 443 }, ((JsonArray)sniff["port"]!).Select(n => n!.GetValue<int>()));
        Assert.Equal(new[] { "http", "tls", "quic" }, ((JsonArray)sniff["sniffer"]!).Select(n => (string)n!));

        // 1: core processes → direct (before any proxy rule).
        var core = (JsonObject)rules[1]!;
        Assert.Equal("direct", (string)core["outbound"]!);
        var coreRegexes = ((JsonArray)core["process_path_regex"]!).Select(n => (string)n!).ToArray();
        Assert.Contains("(?i)(^|\\\\)xray(\\.exe)?$", coreRegexes);
        Assert.Contains("(?i)(^|\\\\)sing-box(\\.exe)?$", coreRegexes);
        Assert.Contains("(?i)(^|\\\\)gameplus-host(\\.exe)?$", coreRegexes);

        // 2: private IPs → direct.
        var privateRule = (JsonObject)rules[2]!;
        Assert.True((bool)privateRule["ip_is_private"]!);
        Assert.Equal("direct", (string)privateRule["outbound"]!);

        // 3: forced-direct domains.
        var directDomains = (JsonObject)rules[3]!;
        Assert.Equal(new[] { "cdn.example.com" }, ((JsonArray)directDomains["domain_suffix"]!).Select(n => (string)n!));
        Assert.Equal("direct", (string)directDomains["outbound"]!);

        // 4: bypass processes.
        var bypass = (JsonObject)rules[4]!;
        Assert.Equal(new[] { "(?i)(^|\\\\)voice(\\.exe)?$" }, ((JsonArray)bypass["process_path_regex"]!).Select(n => (string)n!));
        Assert.Equal("direct", (string)bypass["outbound"]!);

        // 5: proxy processes.
        var proxyProcesses = (JsonObject)rules[5]!;
        Assert.Equal(new[] { "(?i)(^|\\\\)game(\\.exe)?$" }, ((JsonArray)proxyProcesses["process_path_regex"]!).Select(n => (string)n!));
        Assert.Equal("proxy", (string)proxyProcesses["outbound"]!);

        // 6: proxy domains.
        var proxyDomains = (JsonObject)rules[6]!;
        Assert.Equal(new[] { "example.com" }, ((JsonArray)proxyDomains["domain_suffix"]!).Select(n => (string)n!));
        Assert.Equal("proxy", (string)proxyDomains["outbound"]!);

        // 7: proxy IPs.
        var proxyIps = (JsonObject)rules[7]!;
        Assert.Equal(new[] { "1.1.1.1" }, ((JsonArray)proxyIps["ip_cidr"]!).Select(n => (string)n!));
        Assert.Equal("proxy", (string)proxyIps["outbound"]!);

        // Route-level fields.
        Assert.True((bool)route["auto_detect_interface"]!);
        Assert.True((bool)route["find_process"]!);
        Assert.Equal("direct", (string)route["final"]!);
    }

    [Fact]
    public void Build_EmptyLists_ProduceOnlyFixedRules()
    {
        var json = SingBoxTunConfigGenerator.Build(Routing(), Socks());

        var rules = (JsonArray)json!["route"]!["rules"]!;
        Assert.Equal(3, rules.Count);
        Assert.Equal("sniff", (string)((JsonObject)rules[0]!)["action"]!);
        Assert.Equal("direct", (string)((JsonObject)rules[1]!)["outbound"]!);
        Assert.True((bool)((JsonObject)rules[2]!)["ip_is_private"]!);
    }

    [Fact]
    public void Build_ProcessOnlyRouting_HasProxyProcessRule()
    {
        var json = SingBoxTunConfigGenerator.Build(Routing(proxyProcesses: new[] { "game.exe" }), Socks());

        var rules = (JsonArray)json!["route"]!["rules"]!;
        Assert.Equal(4, rules.Count);

        var proxyRule = (JsonObject)rules[3]!;
        Assert.Equal("proxy", (string)proxyRule["outbound"]!);
        Assert.Equal(new[] { "(?i)(^|\\\\)game(\\.exe)?$" }, ((JsonArray)proxyRule["process_path_regex"]!).Select(n => (string)n!));
    }

    [Fact]
    public void Build_DirectDomains_ComeBeforeProxyProcessRule()
    {
        var json = SingBoxTunConfigGenerator.Build(
            Routing(proxyProcesses: new[] { "game.exe" }, directDomains: new[] { "cdn.steampowered.com" }), Socks());

        var rules = (JsonArray)json!["route"]!["rules"]!;
        Assert.Equal(5, rules.Count);

        var directDomains = (JsonObject)rules[3]!;
        Assert.Equal("direct", (string)directDomains["outbound"]!);
        Assert.Equal(new[] { "cdn.steampowered.com" }, ((JsonArray)directDomains["domain_suffix"]!).Select(n => (string)n!));

        var proxyProcesses = (JsonObject)rules[4]!;
        Assert.Equal("proxy", (string)proxyProcesses["outbound"]!);
    }

    // ---------------------------------------------------------------- BuildTunRouting

    [Fact]
    public void BuildTunRouting_DedupesExeSuffix_KeepingFirstSpelling()
    {
        var routing = SplitRouting.BuildTunRouting(
            new[] { "steam", "steam.exe", " D:\\x\\Steam.exe " },
            Array.Empty<ExtraApp>(),
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        Assert.Equal(new[] { "steam" }, routing.ProxyProcesses);
    }

    [Fact]
    public void BuildTunRouting_GameRulesWinOverBypass()
    {
        var routing = SplitRouting.BuildTunRouting(
            new[] { "game.exe" },
            new[] { new ExtraApp { Process = "game.exe", Mode = ExtraAppMode.Bypass, Enabled = true } },
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        Assert.Equal(new[] { "game.exe" }, routing.ProxyProcesses);
        Assert.Empty(routing.BypassProcesses);
    }

    [Fact]
    public void BuildTunRouting_BypassBeatsExtraProxy()
    {
        var routing = SplitRouting.BuildTunRouting(
            Array.Empty<string>(),
            new[]
            {
                new ExtraApp { Process = "discord.exe", Mode = ExtraAppMode.Proxy, Enabled = true },
                new ExtraApp { Process = "Discord.EXE", Mode = ExtraAppMode.Bypass, Enabled = true }
            },
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        Assert.Empty(routing.ProxyProcesses);
        Assert.Equal(new[] { "Discord.EXE" }, routing.BypassProcesses);
    }

    [Fact]
    public void BuildTunRouting_DisabledOrInvalidExtras_AreIgnored()
    {
        var routing = SplitRouting.BuildTunRouting(
            Array.Empty<string>(),
            new[]
            {
                new ExtraApp { Process = "disabled.exe", Enabled = false },
                new ExtraApp { Process = "bad name!.exe", Enabled = true },
                new ExtraApp { Process = "valid.exe", Enabled = true }
            },
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        Assert.Equal(new[] { "valid.exe" }, routing.ProxyProcesses);
        Assert.Empty(routing.BypassProcesses);
    }

    [Fact]
    public void BuildTunRouting_ProxyAppsAreAppendedAfterGames_AndDeduped()
    {
        var routing = SplitRouting.BuildTunRouting(
            new[] { "game.exe" },
            new[]
            {
                new ExtraApp { Process = "game.exe", Mode = ExtraAppMode.Proxy, Enabled = true },
                new ExtraApp { Process = "discord.exe", Mode = ExtraAppMode.Proxy, Enabled = true }
            },
            Array.Empty<string>(), Array.Empty<string>(), null, null);

        Assert.Equal(new[] { "game.exe", "discord.exe" }, routing.ProxyProcesses);
    }

    [Fact]
    public void BuildTunRouting_ListsAreTrimmedDeduped_AndNullsBecomeEmpty()
    {
        var routing = SplitRouting.BuildTunRouting(
            Array.Empty<string>(),
            Array.Empty<ExtraApp>(),
            new[] { " example.com ", "EXAMPLE.com", "" },
            new[] { " 1.1.1.1 ", "1.1.1.1" },
            null,
            null);

        Assert.Empty(routing.ProxyProcesses);
        Assert.Equal(new[] { "example.com" }, routing.ProxyDomains);
        Assert.Equal(new[] { "1.1.1.1" }, routing.ProxyIps);
        Assert.Empty(routing.DirectDomains);
        Assert.Empty(routing.ExcludeAddresses);
    }

    [Fact]
    public void BuildTunRouting_IsEmpty_ReflectsProxyRulesOnly()
    {
        var empty = SplitRouting.BuildTunRouting(
            Array.Empty<string>(),
            new[] { new ExtraApp { Process = "voice.exe", Mode = ExtraAppMode.Bypass, Enabled = true } },
            Array.Empty<string>(), Array.Empty<string>(), null, null);
        Assert.True(empty.IsEmpty);

        var proxy = SplitRouting.BuildTunRouting(
            new[] { "game.exe" },
            Array.Empty<ExtraApp>(),
            Array.Empty<string>(), Array.Empty<string>(), null, null);
        Assert.False(proxy.IsEmpty);
    }
}
