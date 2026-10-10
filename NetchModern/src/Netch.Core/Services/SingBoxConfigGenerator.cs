using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Builds a sing-box client config exposing a local SOCKS5 (+ HTTP) inbound. Used for protocols xray
///     cannot handle: Hysteria2 and Shadowsocks nodes that carry a SIP003 plugin (obfs-local / v2ray-plugin).
///     Mirrors the routing behaviour of <see cref="V2rayConfigGenerator" /> (game mode vs global mode).
/// </summary>
public static class SingBoxConfigGenerator
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string GenerateHysteria2(Hysteria2Server s, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null,
        IReadOnlyCollection<string>? directDomains = null)
        => Build(ProxyHysteria2(s), localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains).ToJsonString(Options);

    public static string GenerateShadowsocks(ShadowsocksServer s, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null,
        IReadOnlyCollection<string>? directDomains = null)
        => Build(ProxyShadowsocks(s), localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains).ToJsonString(Options);

    /// <summary>Dispatches to the right sing-box outbound builder for the given node.</summary>
    public static string Generate(Server server, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null,
        IReadOnlyCollection<string>? directDomains = null)
        => server switch
        {
            Hysteria2Server hy2 => GenerateHysteria2(hy2, localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains),
            ShadowsocksServer ss => GenerateShadowsocks(ss, localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains),
            _ => throw new NotSupportedException($"sing-box does not handle server type: {server.Type}")
        };

    private static JsonObject ProxyHysteria2(Hysteria2Server s)
    {
        var proxy = new JsonObject
        {
            ["type"] = "hysteria2",
            ["tag"] = "proxy",
            ["server"] = s.Hostname,
            ["server_port"] = s.Port,
            ["password"] = s.Password,
            ["tls"] = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = string.IsNullOrEmpty(s.Sni) ? s.Hostname : s.Sni,
                ["insecure"] = s.AllowInsecure
            }
        };

        if (!string.IsNullOrEmpty(s.ObfsPassword))
            proxy["obfs"] = new JsonObject
            {
                ["type"] = "salamander",
                ["password"] = s.ObfsPassword
            };

        // Setting up/down bandwidth switches Hysteria2 to the Brutal congestion controller (loss-resilient).
        if (s.UpMbps > 0)
            proxy["up_mbps"] = s.UpMbps;
        if (s.DownMbps > 0)
            proxy["down_mbps"] = s.DownMbps;

        return proxy;
    }

    private static JsonObject ProxyShadowsocks(ShadowsocksServer s)
    {
        var proxy = new JsonObject
        {
            ["type"] = "shadowsocks",
            ["tag"] = "proxy",
            ["server"] = s.Hostname,
            ["server_port"] = s.Port,
            ["method"] = s.EncryptMethod,
            ["password"] = s.Password
        };

        if (!string.IsNullOrEmpty(s.Plugin))
        {
            proxy["plugin"] = NormalisePlugin(s.Plugin);
            if (!string.IsNullOrEmpty(s.PluginOption))
                proxy["plugin_opts"] = s.PluginOption;
        }

        return proxy;
    }

    /// <summary>
    ///     Maps SIP003 plugin aliases onto the two names sing-box understands. Anything else is rejected
    ///     with a user-facing (Chinese) message.
    /// </summary>
    private static string NormalisePlugin(string plugin)
    {
        var name = plugin.Trim().ToLowerInvariant();
        return name switch
        {
            "obfs-local" or "simple-obfs" or "obfs" => "obfs-local",
            "v2ray-plugin" => "v2ray-plugin",
            _ => throw new NotSupportedException($"不支持的 Shadowsocks 插件：{plugin}")
        };
    }

    private static JsonObject Build(JsonObject proxy, int localSocksPort, int localHttpPort, int apiPort,
        IReadOnlyCollection<string>? proxyDomains, IReadOnlyCollection<string>? proxyIps,
        IReadOnlyCollection<string>? directDomains = null)
    {
        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = "socks-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = localSocksPort
            }
        };

        if (localHttpPort > 0)
            inbounds.Add(new JsonObject
            {
                ["type"] = "http",
                ["tag"] = "http-in",
                ["listen"] = "127.0.0.1",
                ["listen_port"] = localHttpPort
            });

        // Keep LAN / loopback direct so the local proxy stays reachable.
        var rules = new JsonArray
        {
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" }
        };

        // Always-direct domains (e.g. game CDN downloads): sniff first so the destination domain is
        // recoverable even when the capture layer rewrites it to the local proxy, then place the direct
        // rule BEFORE the proxy rules so it wins.
        if (directDomains is { Count: > 0 })
        {
            rules.Insert(0, new JsonObject { ["action"] = "sniff" });

            var direct = new JsonArray();
            foreach (var d in directDomains)
                direct.Add(d);

            rules.Add(new JsonObject { ["domain_suffix"] = direct, ["outbound"] = "direct" });
        }

        var hasDomains = proxyDomains is { Count: > 0 };
        var hasIps = proxyIps is { Count: > 0 };
        string final;

        if (hasDomains || hasIps)
        {
            // Game mode: only the game's domains/IPs go through the node, everything else stays direct.
            if (hasDomains)
            {
                var domains = new JsonArray();
                foreach (var d in proxyDomains!)
                    domains.Add(d);
                rules.Add(new JsonObject { ["domain_suffix"] = domains, ["outbound"] = "proxy" });
            }

            if (hasIps)
            {
                var ips = new JsonArray();
                foreach (var ip in proxyIps!)
                    ips.Add(ip);
                rules.Add(new JsonObject { ["ip_cidr"] = ips, ["outbound"] = "proxy" });
            }

            final = "direct";
        }
        else
        {
            // Global mode: everything (except private IPs above) goes through the node.
            final = "proxy";
        }

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = false },
            ["inbounds"] = inbounds,
            ["outbounds"] = new JsonArray
            {
                proxy,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new JsonObject
            {
                ["rules"] = rules,
                ["final"] = final
            }
        };

        // Clash API exposes cumulative uploadTotal/downloadTotal for the live speed display.
        if (apiPort > 0)
            config["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"127.0.0.1:{apiPort}"
                }
            };

        return config;
    }
}
