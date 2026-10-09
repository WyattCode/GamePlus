using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

namespace Netch.Tests;

public class ConfigGeneratorTests
{
    private static JsonObject Outbound(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        return root["outbounds"]!.AsArray()[0]!.AsObject();
    }

    [Fact]
    public void Vmess_WsTls_EmitsWsSettingsAndSni()
    {
        var server = new VMessServer
        {
            Hostname = "example.com",
            Port = 443,
            UserId = "11111111-1111-1111-1111-111111111111",
            Network = "ws",
            Tls = true,
            Host = "cdn.example.com",
            Path = "/ws",
            Sni = "sni.example.com"
        };

        var outbound = Outbound(V2rayConfigGenerator.Generate(server, 1080));
        var stream = outbound["streamSettings"]!.AsObject();

        Assert.Equal("ws", stream["network"]!.GetValue<string>());
        Assert.Equal("/ws", stream["wsSettings"]!["path"]!.GetValue<string>());
        Assert.Equal("cdn.example.com", stream["wsSettings"]!["host"]!.GetValue<string>());
        Assert.Equal("tls", stream["security"]!.GetValue<string>());
        Assert.Equal("sni.example.com", stream["tlsSettings"]!["serverName"]!.GetValue<string>());
    }

    [Fact]
    public void Vless_GrpcReality_EmitsGrpcSettings()
    {
        var server = new VlessServer
        {
            Hostname = "example.com",
            Port = 443,
            UserId = "22222222-2222-2222-2222-222222222222",
            Network = "grpc",
            Security = "reality",
            PublicKey = "PBK",
            ShortId = "ab",
            ServiceName = "grpcsvc",
            Mode = "multi",
            Sni = "www.example.com"
        };

        var outbound = Outbound(V2rayConfigGenerator.Generate(server, 1080));
        var stream = outbound["streamSettings"]!.AsObject();

        Assert.Equal("grpc", stream["network"]!.GetValue<string>());
        Assert.Equal("grpcsvc", stream["grpcSettings"]!["serviceName"]!.GetValue<string>());
        Assert.True(stream["grpcSettings"]!["multiMode"]!.GetValue<bool>());
        Assert.Equal("reality", stream["security"]!.GetValue<string>());
        Assert.Equal("PBK", stream["realitySettings"]!["publicKey"]!.GetValue<string>());
    }

    [Fact]
    public void Vless_AllowInsecure_EmitsTrue()
    {
        var server = new VlessServer
        {
            Hostname = "example.com",
            Port = 443,
            UserId = "33333333-3333-3333-3333-333333333333",
            Network = "tcp",
            Security = "tls",
            AllowInsecure = true
        };

        var outbound = Outbound(V2rayConfigGenerator.Generate(server, 1080));
        var stream = outbound["streamSettings"]!.AsObject();

        Assert.True(stream["tlsSettings"]!["allowInsecure"]!.GetValue<bool>());
    }

    [Fact]
    public void Trojan_Ws_EmitsWsSettings()
    {
        var server = new TrojanServer
        {
            Hostname = "example.com",
            Port = 443,
            Password = "pass",
            Network = "ws",
            Host = "cdn.example.com",
            Path = "/tj",
            Sni = "sni.example.com"
        };

        var outbound = Outbound(V2rayConfigGenerator.Generate(server, 1080));
        var stream = outbound["streamSettings"]!.AsObject();

        Assert.Equal("ws", stream["network"]!.GetValue<string>());
        Assert.Equal("/tj", stream["wsSettings"]!["path"]!.GetValue<string>());
        Assert.Equal("cdn.example.com", stream["wsSettings"]!["host"]!.GetValue<string>());
        Assert.Equal("sni.example.com", stream["tlsSettings"]!["serverName"]!.GetValue<string>());
    }

    [Fact]
    public void Shadowsocks_WithPlugin_XrayThrows()
    {
        var server = new ShadowsocksServer
        {
            Hostname = "example.com",
            Port = 8388,
            EncryptMethod = "aes-128-gcm",
            Password = "secret",
            Plugin = "obfs-local",
            PluginOption = "obfs=http;obfs-host=example.com"
        };

        Assert.Throws<NotSupportedException>(() => V2rayConfigGenerator.Generate(server, 1080));
    }

    [Fact]
    public void Shadowsocks_WithPlugin_SingBoxEmitsPlugin()
    {
        var server = new ShadowsocksServer
        {
            Hostname = "example.com",
            Port = 8388,
            EncryptMethod = "aes-128-gcm",
            Password = "secret",
            Plugin = "obfs-local",
            PluginOption = "obfs=http;obfs-host=example.com"
        };

        var outbound = Outbound(SingBoxConfigGenerator.Generate(server, 1080));

        Assert.Equal("shadowsocks", outbound["type"]!.GetValue<string>());
        Assert.Equal("obfs-local", outbound["plugin"]!.GetValue<string>());
        Assert.Equal("obfs=http;obfs-host=example.com", outbound["plugin_opts"]!.GetValue<string>());
    }

    [Fact]
    public void Shadowsocks_SimpleObfsAlias_NormalisedToObfsLocal()
    {
        var server = new ShadowsocksServer
        {
            Hostname = "example.com",
            Port = 8388,
            EncryptMethod = "aes-128-gcm",
            Password = "secret",
            Plugin = "simple-obfs",
            PluginOption = "obfs=http"
        };

        var outbound = Outbound(SingBoxConfigGenerator.Generate(server, 1080));
        Assert.Equal("obfs-local", outbound["plugin"]!.GetValue<string>());
    }

    [Fact]
    public void Shadowsocks_UnknownPlugin_SingBoxThrows()
    {
        var server = new ShadowsocksServer
        {
            Hostname = "example.com",
            Port = 8388,
            EncryptMethod = "aes-128-gcm",
            Password = "secret",
            Plugin = "unknown-plugin"
        };

        Assert.Throws<NotSupportedException>(() => SingBoxConfigGenerator.Generate(server, 1080));
    }

    [Fact]
    public void WireGuard_EmitsAddressAndPeers()
    {
        var server = new WireGuardServer
        {
            Hostname = "example.com",
            Port = 51820,
            PrivateKey = "privkey",
            PublicKey = "pubkey",
            LocalAddresses = "10.0.0.2/32,fd00::2/128",
            Mtu = 1420,
            Reserved = "1,2,3",
            PreSharedKey = "psk"
        };

        var outbound = Outbound(V2rayConfigGenerator.Generate(server, 1080));
        var settings = outbound["settings"]!.AsObject();

        Assert.Equal("wireguard", outbound["protocol"]!.GetValue<string>());
        Assert.Equal("privkey", settings["secretKey"]!.GetValue<string>());
        Assert.Equal(2, settings["address"]!.AsArray().Count);
        Assert.Equal("10.0.0.2/32", settings["address"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(1420, settings["mtu"]!.GetValue<int>());

        var peer = settings["peers"]!.AsArray()[0]!.AsObject();
        Assert.Equal("pubkey", peer["publicKey"]!.GetValue<string>());
        Assert.Equal("example.com:51820", peer["endpoint"]!.GetValue<string>());
        Assert.Equal("psk", peer["preSharedKey"]!.GetValue<string>());
        Assert.Equal(3, settings["reserved"]!.AsArray().Count);
    }

    [Fact]
    public void WireGuard_MissingAddress_Throws()
    {
        var server = new WireGuardServer
        {
            Hostname = "example.com",
            Port = 51820,
            PrivateKey = "privkey",
            PublicKey = "pubkey"
        };

        Assert.Throws<InvalidOperationException>(() => V2rayConfigGenerator.Generate(server, 1080));
    }
}
