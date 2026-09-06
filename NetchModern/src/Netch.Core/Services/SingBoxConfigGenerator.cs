using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Builds a sing-box client config exposing a local SOCKS5 (+ HTTP) inbound for a Hysteria2 node.
///     Mirrors the routing behaviour of <see cref="V2rayConfigGenerator" /> (game mode vs global mode).
/// </summary>
public static class SingBoxConfigGenerator
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string GenerateHysteria2(Hysteria2Server s, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null)
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

        // Keep LAN / loopback direct so the local proxy stays reachable.
        var rules = new JsonArray
        {
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" }
        };

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

        return config.ToJsonString(Options);
    }
}
