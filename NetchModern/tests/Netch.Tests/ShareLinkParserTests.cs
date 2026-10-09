using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Netch.Core.Utils;

namespace Netch.Tests;

public class ShareLinkParserTests
{
    [Fact]
    public void ParseVmess_WsTls_ParsesTransportAndTls()
    {
        var json = """
        {
          "v": "2",
          "ps": "ws-node",
          "add": "example.com",
          "port": "443",
          "id": "11111111-1111-1111-1111-111111111111",
          "aid": "0",
          "scy": "auto",
          "net": "ws",
          "host": "cdn.example.com",
          "path": "/ws",
          "tls": "tls",
          "sni": "sni.example.com",
          "alpn": "h2,http/1.1",
          "fp": "chrome"
        }
        """;
        var link = "vmess://" + ShareLinkUtils.Base64UrlEncode(json);

        var server = Assert.IsType<VMessServer>(ShareLinkParser.ParseUri(link));

        Assert.Equal("ws", server.Network);
        Assert.True(server.Tls);
        Assert.Equal("cdn.example.com", server.Host);
        Assert.Equal("/ws", server.Path);
        Assert.Equal("sni.example.com", server.Sni);
        Assert.Equal("h2,http/1.1", server.Alpn);
        Assert.Equal("chrome", server.Fingerprint);
    }

    [Fact]
    public void ParseVless_GrpcReality_ParsesTransport()
    {
        var link = "vless://22222222-2222-2222-2222-222222222222@example.com:443" +
                   "?type=grpc&security=reality&pbk=PBK&sid=ab&fp=chrome&sni=www.example.com" +
                   "&serviceName=grpcsvc&mode=multi&flow=xtls-rprx-vision#reality-node";

        var server = Assert.IsType<VlessServer>(ShareLinkParser.ParseUri(link));

        Assert.Equal("grpc", server.Network);
        Assert.Equal("reality", server.Security);
        Assert.Equal("grpcsvc", server.ServiceName);
        Assert.Equal("multi", server.Mode);
        Assert.Equal("PBK", server.PublicKey);
        Assert.Equal("ab", server.ShortId);
        Assert.Equal("reality-node", server.Remark);
    }

    [Fact]
    public void ParseVless_AllowInsecure_Parsed()
    {
        var link = "vless://33333333-3333-3333-3333-333333333333@example.com:443?type=tcp&security=tls&allowInsecure=1";
        var server = Assert.IsType<VlessServer>(ShareLinkParser.ParseUri(link));
        Assert.True(server.AllowInsecure);
    }

    [Fact]
    public void ParseTrojan_Ws_ParsesTransport()
    {
        var link = "trojan://pass@example.com:443?type=ws&host=cdn.example.com&path=/tj&sni=sni.example.com#tj";
        var server = Assert.IsType<TrojanServer>(ShareLinkParser.ParseUri(link));

        Assert.Equal("ws", server.Network);
        Assert.Equal("cdn.example.com", server.Host);
        Assert.Equal("/tj", server.Path);
        Assert.Equal("sni.example.com", server.Sni);
    }

    [Fact]
    public void ParseShadowsocks_WithObfsPlugin_ParsesPlugin()
    {
        var userInfo = ShareLinkUtils.Base64UrlEncode("aes-128-gcm:secret");
        var link = $"ss://{userInfo}@example.com:8388/?plugin=obfs-local%3Bobfs%3Dhttp%3Bobfs-host%3Dexample.com#ss";

        var server = Assert.IsType<ShadowsocksServer>(ShareLinkParser.ParseUri(link));

        Assert.Equal("obfs-local", server.Plugin);
        Assert.Equal("obfs=http;obfs-host=example.com", server.PluginOption);
    }

    [Fact]
    public void ParseWireGuard_ParsesKeysAndAddress()
    {
        var link = "wireguard://privkey@example.com:51820?publickey=pubkey&address=10.0.0.2/32,fd00::2/128" +
                   "&mtu=1420&reserved=1,2,3&presharedkey=psk#wg";

        var server = Assert.IsType<WireGuardServer>(ShareLinkParser.ParseUri(link));

        Assert.Equal("privkey", server.PrivateKey);
        Assert.Equal("pubkey", server.PublicKey);
        Assert.Equal("10.0.0.2/32,fd00::2/128", server.LocalAddresses);
        Assert.Equal(1420, server.Mtu);
        Assert.Equal("1,2,3", server.Reserved);
        Assert.Equal("psk", server.PreSharedKey);
        Assert.Equal("wg", server.Remark);
    }

    [Fact]
    public void RoundTrip_Vmess_PreservesKeyFields()
    {
        var original = new VMessServer
        {
            Remark = "rt",
            Hostname = "example.com",
            Port = 443,
            UserId = "44444444-4444-4444-4444-444444444444",
            Network = "ws",
            Tls = true,
            Host = "cdn.example.com",
            Path = "/ws",
            Sni = "sni.example.com",
            Alpn = "h2",
            Fingerprint = "chrome"
        };

        var link = ShareLinkExporter.ToShareLink(original);
        Assert.NotNull(link);
        var parsed = Assert.IsType<VMessServer>(ShareLinkParser.ParseUri(link!));

        Assert.Equal(original.Hostname, parsed.Hostname);
        Assert.Equal(original.Port, parsed.Port);
        Assert.Equal(original.UserId, parsed.UserId);
        Assert.Equal(original.Network, parsed.Network);
        Assert.Equal(original.Tls, parsed.Tls);
        Assert.Equal(original.Host, parsed.Host);
        Assert.Equal(original.Path, parsed.Path);
        Assert.Equal(original.Sni, parsed.Sni);
        Assert.Equal(original.Alpn, parsed.Alpn);
        Assert.Equal(original.Fingerprint, parsed.Fingerprint);
    }

    [Fact]
    public void RoundTrip_Vless_PreservesKeyFields()
    {
        var original = new VlessServer
        {
            Remark = "rt",
            Hostname = "example.com",
            Port = 443,
            UserId = "55555555-5555-5555-5555-555555555555",
            Network = "grpc",
            Security = "reality",
            PublicKey = "PBK",
            ShortId = "ab",
            SpiderX = "/",
            Fingerprint = "chrome",
            ServiceName = "grpcsvc",
            Mode = "multi",
            Sni = "www.example.com",
            AllowInsecure = true
        };

        var link = ShareLinkExporter.ToShareLink(original);
        Assert.NotNull(link);
        var parsed = Assert.IsType<VlessServer>(ShareLinkParser.ParseUri(link!));

        Assert.Equal(original.Network, parsed.Network);
        Assert.Equal(original.Security, parsed.Security);
        Assert.Equal(original.PublicKey, parsed.PublicKey);
        Assert.Equal(original.ShortId, parsed.ShortId);
        Assert.Equal(original.ServiceName, parsed.ServiceName);
        Assert.Equal(original.Mode, parsed.Mode);
        Assert.Equal(original.Sni, parsed.Sni);
        Assert.True(parsed.AllowInsecure);
    }

    [Fact]
    public void RoundTrip_Trojan_PreservesKeyFields()
    {
        var original = new TrojanServer
        {
            Remark = "rt",
            Hostname = "example.com",
            Port = 443,
            Password = "pass",
            Network = "ws",
            Host = "cdn.example.com",
            Path = "/tj",
            Sni = "sni.example.com",
            AllowInsecure = true
        };

        var link = ShareLinkExporter.ToShareLink(original);
        Assert.NotNull(link);
        var parsed = Assert.IsType<TrojanServer>(ShareLinkParser.ParseUri(link!));

        Assert.Equal(original.Password, parsed.Password);
        Assert.Equal(original.Network, parsed.Network);
        Assert.Equal(original.Host, parsed.Host);
        Assert.Equal(original.Path, parsed.Path);
        Assert.Equal(original.Sni, parsed.Sni);
        Assert.True(parsed.AllowInsecure);
    }

    [Fact]
    public void RoundTrip_Shadowsocks_Plugin_PreservesPlugin()
    {
        var original = new ShadowsocksServer
        {
            Remark = "rt",
            Hostname = "example.com",
            Port = 8388,
            EncryptMethod = "aes-128-gcm",
            Password = "secret",
            Plugin = "obfs-local",
            PluginOption = "obfs=http;obfs-host=example.com"
        };

        var link = ShareLinkExporter.ToShareLink(original);
        Assert.NotNull(link);
        var parsed = Assert.IsType<ShadowsocksServer>(ShareLinkParser.ParseUri(link!));

        Assert.Equal(original.Plugin, parsed.Plugin);
        Assert.Equal(original.PluginOption, parsed.PluginOption);
    }

    [Fact]
    public void RoundTrip_WireGuard_PreservesKeyFields()
    {
        var original = new WireGuardServer
        {
            Remark = "rt",
            Hostname = "example.com",
            Port = 51820,
            PrivateKey = "privkey",
            PublicKey = "pubkey",
            LocalAddresses = "10.0.0.2/32,fd00::2/128",
            Mtu = 1420,
            Reserved = "1,2,3",
            PreSharedKey = "psk"
        };

        var link = ShareLinkExporter.ToShareLink(original);
        Assert.NotNull(link);
        var parsed = Assert.IsType<WireGuardServer>(ShareLinkParser.ParseUri(link!));

        Assert.Equal(original.PrivateKey, parsed.PrivateKey);
        Assert.Equal(original.PublicKey, parsed.PublicKey);
        Assert.Equal(original.LocalAddresses, parsed.LocalAddresses);
        Assert.Equal(original.Mtu, parsed.Mtu);
        Assert.Equal(original.Reserved, parsed.Reserved);
        Assert.Equal(original.PreSharedKey, parsed.PreSharedKey);
    }
}
