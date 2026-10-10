using Netch.Core.Models.Servers;
using Netch.Core.Services.Import;
using Xunit;

namespace Netch.Tests.Import;

public class ClashConfigParserTests
{
    [Fact]
    public void ParsesBlockStyleAllTypes()
    {
        const string yaml = """
proxies:
  - name: ss-node
    type: ss
    server: 1.2.3.4
    port: 8388
    cipher: aes-256-gcm
    password: sspass
  - name: vmess-node
    type: vmess
    server: 5.6.7.8
    port: 443
    uuid: 11111111-1111-1111-1111-111111111111
    alterId: 4
    cipher: auto
    tls: true
    servername: example.com
    network: ws
    ws-opts:
      path: /ws
      headers:
        Host: ws.example.com
  - name: trojan-node
    type: trojan
    server: 9.9.9.9
    port: 8443
    password: trpass
    sni: tr.example.com
    skip-cert-verify: true
  - name: vless-node
    type: vless
    server: 10.0.0.1
    port: 443
    uuid: 22222222-2222-2222-2222-222222222222
    flow: xtls-rprx-vision
    network: grpc
    grpc-opts:
      grpc-service-name: gs
  - name: hy2-node
    type: hysteria2
    server: 11.0.0.1
    port: 443
    password: hypass
    sni: hy.example.com
  - name: socks-node
    type: socks5
    server: 12.0.0.1
    port: 1080
    username: u
    password: p
""";

        var servers = ClashConfigParser.Parse(yaml);

        Assert.Equal(6, servers.Count);

        var ss = Assert.IsType<ShadowsocksServer>(servers[0]);
        Assert.Equal("ss-node", ss.Remark);
        Assert.Equal("1.2.3.4", ss.Hostname);
        Assert.Equal(8388, ss.Port);
        Assert.Equal("aes-256-gcm", ss.EncryptMethod);
        Assert.Equal("sspass", ss.Password);

        var vm = Assert.IsType<VMessServer>(servers[1]);
        Assert.Equal("11111111-1111-1111-1111-111111111111", vm.UserId);
        Assert.Equal(4, vm.AlterId);
        Assert.True(vm.Tls);
        Assert.Equal("example.com", vm.Sni);
        Assert.Equal("ws", vm.Network);
        Assert.Equal("/ws", vm.Path);
        Assert.Equal("ws.example.com", vm.Host);

        var tr = Assert.IsType<TrojanServer>(servers[2]);
        Assert.Equal("trpass", tr.Password);
        Assert.Equal("tr.example.com", tr.Sni);
        Assert.True(tr.AllowInsecure);

        var vl = Assert.IsType<VlessServer>(servers[3]);
        Assert.Equal("22222222-2222-2222-2222-222222222222", vl.UserId);
        Assert.Equal("xtls-rprx-vision", vl.Flow);
        Assert.Equal("grpc", vl.Network);
        Assert.Equal("gs", vl.ServiceName);

        var hy = Assert.IsType<Hysteria2Server>(servers[4]);
        Assert.Equal("hypass", hy.Password);
        Assert.Equal("hy.example.com", hy.Sni);

        var sk = Assert.IsType<Socks5Server>(servers[5]);
        Assert.Equal("u", sk.Username);
        Assert.Equal("p", sk.Password);
    }

    [Fact]
    public void ParsesFlowStyle()
    {
        const string yaml = "proxies: [{name: a, type: ss, server: 1.1.1.1, port: 1080, cipher: aes-128-gcm, password: p}]";

        var servers = ClashConfigParser.Parse(yaml);

        var ss = Assert.IsType<ShadowsocksServer>(Assert.Single(servers));
        Assert.Equal("1.1.1.1", ss.Hostname);
        Assert.Equal(1080, ss.Port);
    }

    [Fact]
    public void SkipsUnknownType()
    {
        const string yaml = """
proxies:
  - name: unknown
    type: wireguard
    server: 1.2.3.4
    port: 51820
  - name: ss
    type: ss
    server: 1.2.3.4
    port: 8388
    cipher: aes-128-gcm
    password: p
""";

        var servers = ClashConfigParser.Parse(yaml);

        Assert.Single(servers);
        Assert.IsType<ShadowsocksServer>(servers[0]);
    }

    [Fact]
    public void GarbageInputReturnsEmpty()
    {
        Assert.Empty(ClashConfigParser.Parse("this is not: [valid yaml"));
        Assert.Empty(ClashConfigParser.Parse(""));
        Assert.Empty(ClashConfigParser.Parse("proxies: not-a-sequence"));
    }

    [Fact]
    public void NeverThrowsOnMalformedEntries()
    {
        const string yaml = """
proxies:
  - name: missing-server
    type: ss
    port: 8388
  - name: bad-port
    type: ss
    server: 1.2.3.4
    port: not-a-number
  - name: ok
    type: ss
    server: 1.2.3.4
    port: 8388
    cipher: aes-128-gcm
    password: p
""";

        var servers = ClashConfigParser.Parse(yaml);

        Assert.Single(servers);
    }
}
