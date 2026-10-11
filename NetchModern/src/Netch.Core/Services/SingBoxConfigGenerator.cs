using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;

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
        IReadOnlyCollection<string>? directDomains = null, LanShareOptions? lanShare = null)
        => Build(ProxyHysteria2(s), localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains, lanShare).ToJsonString(Options);

    public static string GenerateShadowsocks(ShadowsocksServer s, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null,
        IReadOnlyCollection<string>? directDomains = null, LanShareOptions? lanShare = null)
        => Build(ProxyShadowsocks(s), localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains, lanShare).ToJsonString(Options);

    /// <summary>Dispatches to the right sing-box outbound builder for the given node.</summary>
    public static string Generate(Server server, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null,
        IReadOnlyCollection<string>? directDomains = null, LanShareOptions? lanShare = null)
        => server switch
        {
            Hysteria2Server hy2 => GenerateHysteria2(hy2, localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains, lanShare),
            ShadowsocksServer ss => GenerateShadowsocks(ss, localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps, directDomains, lanShare),
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
        IReadOnlyCollection<string>? directDomains = null, LanShareOptions? lanShare = null)
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

        ApplyLanShare(config, lanShare);

        return config;
    }

    /// <summary>
    ///     Adds the LAN-share inbounds ("lan-http", optional "lan-socks") and the allow/reject route rules
    ///     for the allowlisted clients. The rules are inserted at the start (after a sniff action if one
    ///     is present) so they win over the direct/proxy rules.
    /// </summary>
    private static void ApplyLanShare(JsonObject config, LanShareOptions? lanShare)
    {
        if (lanShare == null)
            return;

        var inbounds = (JsonArray)config["inbounds"]!;
        var rules = (JsonArray)config["route"]!["rules"]!;

        var httpIn = new JsonObject
        {
            ["type"] = "http",
            ["tag"] = "lan-http",
            ["listen"] = lanShare.BindAddress,
            ["listen_port"] = lanShare.HttpPort
        };
        if (!string.IsNullOrEmpty(lanShare.Username))
        {
            httpIn["users"] = new JsonArray
            {
                new JsonObject { ["username"] = lanShare.Username, ["password"] = lanShare.Password }
            };
        }

        inbounds.Add(httpIn);

        var lanTags = new JsonArray { "lan-http" };
        if (lanShare.SocksPort > 0)
        {
            var socksIn = new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = "lan-socks",
                ["listen"] = lanShare.BindAddress,
                ["listen_port"] = lanShare.SocksPort
            };
            if (!string.IsNullOrEmpty(lanShare.Username))
            {
                socksIn["users"] = new JsonArray
                {
                    new JsonObject { ["username"] = lanShare.Username, ["password"] = lanShare.Password }
                };
            }

            inbounds.Add(socksIn);
            lanTags.Add("lan-socks");
        }

        var inbound = new JsonArray();
        foreach (var t in lanTags)
            inbound.Add(t!.DeepClone());

        var sourceIpCidr = new JsonArray();
        foreach (var c in lanShare.AllowedClients)
            sourceIpCidr.Add(NormalizeCidr(c));

        var allow = new JsonObject
        {
            ["inbound"] = inbound,
            ["source_ip_cidr"] = sourceIpCidr,
            ["outbound"] = "proxy"
        };

        var reject = new JsonObject
        {
            ["inbound"] = inbound.DeepClone(),
            ["action"] = "reject"
        };

        // Insert after a sniff action if one is present (keep any existing first rule order otherwise).
        var insertAt = rules.Count > 0 && rules[0] is JsonObject first && first.ContainsKey("action")
            ? 1
            : 0;
        rules.Insert(insertAt, allow);
        rules.Insert(insertAt + 1, reject);
    }

    /// <summary>Normalizes a client entry to CIDR form: a single IP becomes a /32.</summary>
    private static string NormalizeCidr(string entry)
    {
        if (!entry.Contains('/'))
            return $"{entry}/32";

        // Strip any host bits from the network part (e.g. 192.168.1.77/24 → 192.168.1.0/24).
        var parts = entry.Split('/');
        if (IPAddress.TryParse(parts[0], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            && int.TryParse(parts[1], out var prefix) && prefix is >= 0 and <= 32)
        {
            var bytes = ip.GetAddressBytes();
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            var network = BitConverter.GetBytes(BitConverter.ToUInt32(bytes.Reverse().ToArray(), 0) & mask);
            Array.Reverse(network);
            return $"{new IPAddress(network)}/{prefix}";
        }

        return entry;
    }
}
