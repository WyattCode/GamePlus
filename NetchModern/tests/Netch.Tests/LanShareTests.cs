using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;

namespace Netch.Tests;

public class LanShareTests
{
    private static readonly IPAddress[] LocalAddresses =
    {
        IPAddress.Parse("192.168.1.10"),
        IPAddress.Parse("10.0.0.5")
    };

    private static LanShareSettings ValidSettings() => new()
    {
        Enabled = true,
        BindAddress = "192.168.1.10",
        HttpPort = 7899,
        SocksPort = 0,
        AllowedClients = new List<string> { "192.168.1.23" },
        Username = "",
        Password = ""
    };

    // ---------- Validator ----------

    [Fact]
    public void ValidateBindAddress_PublicIp_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateBindAddress("8.8.8.8", LocalAddresses));
        Assert.Contains("局域网", ex.Message);
    }

    [Fact]
    public void ValidateBindAddress_AllZeros_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateBindAddress("0.0.0.0", LocalAddresses));
    }

    [Fact]
    public void ValidateBindAddress_NotOnThisPc_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateBindAddress("192.168.1.99", LocalAddresses));
        Assert.Contains("局域网", ex.Message);
    }

    [Fact]
    public void ValidateBindAddress_PrivateLocalIp_IsAcceptedAndNormalized()
    {
        var result = LanShareValidator.ValidateBindAddress(" 192.168.1.10 ", LocalAddresses);
        Assert.Equal("192.168.1.10", result);
    }

    [Fact]
    public void ValidateBindAddress_NonCanonical_IsRejected()
    {
        // "010" would be parsed as octal 8 by IPAddress.TryParse, so leading zeros are ambiguous.
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateBindAddress("192.168.001.010", LocalAddresses));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateClients(new[] { "192.168.1.010" }));
    }

    [Fact]
    public void ValidateClients_Empty_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateClients(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateClients(null));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateClients(new[] { "" }));
    }

    [Fact]
    public void ValidateClients_BroadSlash8_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateClients(new[] { "10.0.0.0/8" }));
        Assert.Contains("过大", ex.Message);
    }

    [Fact]
    public void ValidateClients_SingleIp_IsAccepted()
    {
        var result = LanShareValidator.ValidateClients(new[] { "192.168.1.23" });
        Assert.Equal(new[] { "192.168.1.23/32" }, result);
    }

    [Fact]
    public void ValidateClients_Cidr_IsAcceptedAndNormalized()
    {
        var result = LanShareValidator.ValidateClients(new[] { "192.168.1.77/24" });
        Assert.Equal(new[] { "192.168.1.0/24" }, result);
    }

    [Fact]
    public void ValidateClients_PublicCidr_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateClients(new[] { "8.8.8.0/24" }));
    }

    [Fact]
    public void ValidateClients_MoreThan32_IsRejected()
    {
        var clients = Enumerable.Range(0, 33).Select(i => $"192.168.{i / 256}.{i % 256}").ToList();
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateClients(clients));
    }

    [Fact]
    public void ValidatePorts_ClashingPorts_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidatePorts(7899, 7899, 0));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidatePorts(7899, 0, 7899));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidatePorts(7899, 7898, 7898));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidatePorts(80, 0, 0));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidatePorts(7899, 80, 0));
    }

    [Fact]
    public void ValidatePorts_ValidPorts_Pass()
    {
        LanShareValidator.ValidatePorts(7899, 0, 2801);
        LanShareValidator.ValidatePorts(7899, 7898, 2801);
        LanShareValidator.ValidatePorts(7899, 0, 0);
    }

    [Fact]
    public void ValidateCredentials_OnlyOneSet_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateCredentials("user", ""));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateCredentials("", "pass"));
    }

    [Fact]
    public void ValidateCredentials_ColonInUsername_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateCredentials("us:er", "pass"));
    }

    [Fact]
    public void ValidateCredentials_Whitespace_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateCredentials("us er", "pass"));
        Assert.Throws<ArgumentException>(() => LanShareValidator.ValidateCredentials("user", "pa ss"));
    }

    [Fact]
    public void ValidateCredentials_TooLong_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LanShareValidator.ValidateCredentials(new string('u', 65), "pass"));
    }

    [Fact]
    public void ValidateCredentials_BothSet_AreAccepted()
    {
        LanShareValidator.ValidateCredentials("user", "pass");
        LanShareValidator.ValidateCredentials("", "");
    }

    [Fact]
    public void BuildOptions_Disabled_ReturnsNullWithoutProblem()
    {
        var settings = ValidSettings();
        settings.Enabled = false;

        var options = LanShareValidator.BuildOptions(settings, LocalAddresses, out var problem);

        Assert.Null(options);
        Assert.Null(problem);
    }

    [Fact]
    public void BuildOptions_Invalid_ReturnsNullWithChineseProblem()
    {
        var settings = ValidSettings();
        settings.BindAddress = "8.8.8.8";

        var options = LanShareValidator.BuildOptions(settings, LocalAddresses, out var problem);

        Assert.Null(options);
        Assert.NotNull(problem);
        Assert.Contains("局域网", problem);
    }

    [Fact]
    public void BuildOptions_Valid_ReturnsNormalizedOptions()
    {
        var settings = ValidSettings();
        settings.SocksPort = 7898;
        settings.Username = "user";
        settings.Password = "pass";

        var options = LanShareValidator.BuildOptions(settings, LocalAddresses, out var problem);

        Assert.NotNull(options);
        Assert.Null(problem);
        Assert.Equal("192.168.1.10", options!.BindAddress);
        Assert.Equal(7899, options.HttpPort);
        Assert.Equal(7898, options.SocksPort);
        Assert.Equal(new[] { "192.168.1.23/32" }, options.AllowedClients);
        Assert.Equal("user", options.Username);
        Assert.Equal("pass", options.Password);
    }

    // ---------- xray generator ----------

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static ShadowsocksServer MakeServer() =>
        new() { Hostname = "example.com", Port = 8388, EncryptMethod = "aes-256-gcm", Password = "pw" };

    private static JsonObject FindInbound(JsonObject config, string tag) =>
        ((JsonArray)config["inbounds"]!).Cast<JsonObject>().First(i => (string?)i["tag"] == tag);

    [Fact]
    public void Xray_NullLanShare_ConfigIdenticalToToday()
    {
        var without = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802);
        var withNull = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802, lanShare: null);

        Assert.Equal(without.ToJsonString(), withNull.ToJsonString());
        Assert.DoesNotContain(((JsonArray)withNull["inbounds"]!).Cast<JsonObject>(),
            i => ((string?)i["tag"])!.StartsWith("lan-"));
    }

    [Fact]
    public void Xray_LanShare_AddsHttpInboundBoundToBindAddress()
    {
        var config = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 0, new[] { "192.168.1.23/32" }, null, null));

        var lanHttp = FindInbound(config, "lan-http");
        Assert.Equal("192.168.1.10", (string?)lanHttp["listen"]);
        Assert.Equal(7899, (int?)lanHttp["port"]);
        Assert.Equal("http", (string?)lanHttp["protocol"]);
        Assert.False((bool?)lanHttp["settings"]!["allowTransparent"]);
    }

    [Fact]
    public void Xray_LanShare_FirstRulesAreAllowThenBlock()
    {
        var config = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 0, new[] { "192.168.1.23/32" }, null, null));

        var rules = (JsonArray)config["routing"]!["rules"]!;
        var allow = (JsonObject)rules[0]!;
        var block = (JsonObject)rules[1]!;

        Assert.Equal("proxy", (string?)allow["outboundTag"]);
        Assert.Equal(new[] { "lan-http" }, ((JsonArray)allow["inboundTag"]!).Select(t => (string)t!).ToArray());
        Assert.Equal(new[] { "192.168.1.23/32" }, ((JsonArray)allow["source"]!).Select(t => (string)t!).ToArray());

        Assert.Equal("block", (string?)block["outboundTag"]);
        Assert.Equal(new[] { "lan-http" }, ((JsonArray)block["inboundTag"]!).Select(t => (string)t!).ToArray());
    }

    [Fact]
    public void Xray_LanShare_WithSocks_AddsSocksInboundAndBothTagsInRules()
    {
        var config = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23/32" }, null, null));

        var lanSocks = FindInbound(config, "lan-socks");
        Assert.Equal("192.168.1.10", (string?)lanSocks["listen"]);
        Assert.Equal(7898, (int?)lanSocks["port"]);
        Assert.Equal("socks", (string?)lanSocks["protocol"]);
        Assert.True((bool?)lanSocks["settings"]!["udp"]);
        Assert.Equal("noauth", (string?)lanSocks["settings"]!["auth"]);

        var rules = (JsonArray)config["routing"]!["rules"]!;
        var allow = (JsonObject)rules[0]!;
        Assert.Equal(new[] { "lan-http", "lan-socks" }, ((JsonArray)allow["inboundTag"]!).Select(t => (string)t!).ToArray());
    }

    [Fact]
    public void Xray_LanShare_WithCredentials_AuthPresentOnlyWhenSet()
    {
        var config = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23/32" }, "user", "pass"));

        var lanHttp = FindInbound(config, "lan-http");
        var accounts = (JsonArray)lanHttp["settings"]!["accounts"]!;
        Assert.Equal("user", (string?)((JsonObject)accounts[0]!)["user"]);
        Assert.Equal("pass", (string?)((JsonObject)accounts[0]!)["pass"]);

        var lanSocks = FindInbound(config, "lan-socks");
        Assert.Equal("password", (string?)lanSocks["settings"]!["auth"]);
        var socksAccounts = (JsonArray)lanSocks["settings"]!["accounts"]!;
        Assert.Equal("user", (string?)((JsonObject)socksAccounts[0]!)["user"]);

        // Without credentials: no accounts anywhere.
        var noAuth = V2rayConfigGenerator.GenerateNode(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23/32" }, null, null));
        Assert.DoesNotContain(((JsonArray)noAuth["inbounds"]!).Cast<JsonObject>(),
            i => i["settings"] != null && i["settings"]!.AsObject().ContainsKey("accounts"));
    }

    // ---------- sing-box generator ----------

    /// <summary>sing-box Generate* returns the JSON text; parse it for structural assertions.</summary>
    private static JsonObject SingBoxSs(ShadowsocksServer server, int socks, int http,
        IReadOnlyCollection<string>? directDomains = null, LanShareOptions? lanShare = null) =>
        JsonNode.Parse(SingBoxConfigGenerator.GenerateShadowsocks(server, socks, http, directDomains: directDomains, lanShare: lanShare))!.AsObject();

    private static JsonObject FindSingBoxInbound(JsonObject config, string tag) =>
        ((JsonArray)config["inbounds"]!).Cast<JsonObject>().First(i => (string?)i["tag"] == tag);

    [Fact]
    public void SingBox_NullLanShare_ConfigIdenticalToToday()
    {
        var without = SingBoxConfigGenerator.GenerateShadowsocks(MakeServer(), 2801, 2802);
        var withNullJson = SingBoxConfigGenerator.GenerateShadowsocks(MakeServer(), 2801, 2802, lanShare: null);

        Assert.Equal(without, withNullJson);
        var withNull = JsonNode.Parse(withNullJson)!.AsObject();
        Assert.DoesNotContain(((JsonArray)withNull["inbounds"]!).Cast<JsonObject>(),
            i => ((string?)i["tag"])!.StartsWith("lan-"));
    }

    [Fact]
    public void SingBox_LanShare_AddsHttpInboundBoundToBindAddress()
    {
        var config = SingBoxSs(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 0, new[] { "192.168.1.23" }, null, null));

        var lanHttp = FindSingBoxInbound(config, "lan-http");
        Assert.Equal("192.168.1.10", (string?)lanHttp["listen"]);
        Assert.Equal(7899, (int?)lanHttp["listen_port"]);
        Assert.Equal("http", (string?)lanHttp["type"]);
        Assert.Null(lanHttp["users"]);
    }

    [Fact]
    public void SingBox_LanShare_FirstRulesAreAllowThenReject()
    {
        var config = SingBoxSs(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 0, new[] { "192.168.1.23" }, null, null));

        var rules = (JsonArray)config["route"]!["rules"]!;
        var allow = (JsonObject)rules[0]!;
        var reject = (JsonObject)rules[1]!;

        Assert.Equal("proxy", (string?)allow["outbound"]);
        Assert.Equal(new[] { "lan-http" }, ((JsonArray)allow["inbound"]!).Select(t => (string)t!).ToArray());
        Assert.Equal(new[] { "192.168.1.23/32" }, ((JsonArray)allow["source_ip_cidr"]!).Select(t => (string)t!).ToArray());

        Assert.Equal("reject", (string?)reject["action"]);
        Assert.Equal(new[] { "lan-http" }, ((JsonArray)reject["inbound"]!).Select(t => (string)t!).ToArray());
    }

    [Fact]
    public void SingBox_LanShare_WithSocks_AddsSocksInbound()
    {
        var config = SingBoxSs(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23" }, null, null));

        var lanSocks = FindSingBoxInbound(config, "lan-socks");
        Assert.Equal("192.168.1.10", (string?)lanSocks["listen"]);
        Assert.Equal(7898, (int?)lanSocks["listen_port"]);
        Assert.Equal("socks", (string?)lanSocks["type"]);
        Assert.Null(lanSocks["users"]);
    }

    [Fact]
    public void SingBox_LanShare_WithCredentials_UsersPresentOnlyWhenSet()
    {
        var config = SingBoxSs(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23" }, "user", "pass"));

        var lanHttp = FindSingBoxInbound(config, "lan-http");
        var users = (JsonArray)lanHttp["users"]!;
        Assert.Equal("user", (string?)((JsonObject)users[0]!)["username"]);
        Assert.Equal("pass", (string?)((JsonObject)users[0]!)["password"]);

        var noAuth = SingBoxSs(MakeServer(), 2801, 2802,
            lanShare: new LanShareOptions("192.168.1.10", 7899, 7898, new[] { "192.168.1.23" }, null, null));
        Assert.DoesNotContain(((JsonArray)noAuth["inbounds"]!).Cast<JsonObject>(),
            i => i["users"] != null);
    }

    [Fact]
    public void SingBox_LanShare_SniffRuleStaysFirst()
    {
        var config = SingBoxSs(MakeServer(), 2801, 2802,
            directDomains: new[] { "steamcontent.com" },
            lanShare: new LanShareOptions("192.168.1.10", 7899, 0, new[] { "192.168.1.23" }, null, null));

        var rules = (JsonArray)config["route"]!["rules"]!;
        Assert.Equal("sniff", (string?)((JsonObject)rules[0]!)["action"]);
        Assert.Equal("proxy", (string?)((JsonObject)rules[1]!)["outbound"]);
        Assert.Equal("reject", (string?)((JsonObject)rules[2]!)["action"]);
    }
}
