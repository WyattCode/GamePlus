using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Translates a <see cref="Server" /> into a v2ray/xray client config exposing a local SOCKS5 inbound.
///     Ported in spirit from the original V2rayConfigUtils. Pure and unit-testable.
/// </summary>
public static class V2rayConfigGenerator
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Generate(Server server, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null)
        => GenerateNode(server, localSocksPort, localHttpPort, apiPort, proxyDomains, proxyIps).ToJsonString(Options);

    /// <param name="proxyDomains">
    ///     When non-empty (game mode), only these domains route through the node and everything else goes
    ///     direct. When null/empty (global mode), all traffic routes through the node except private IPs.
    /// </param>
    /// <param name="proxyIps">Game-mode IPs / CIDR ranges to route through the node (for games that use raw IPs).</param>
    public static JsonObject GenerateNode(Server server, int localSocksPort, int localHttpPort = 0, int apiPort = 0,
        IReadOnlyCollection<string>? proxyDomains = null, IReadOnlyCollection<string>? proxyIps = null)
    {
        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["tag"] = "socks-in",
                ["port"] = localSocksPort,
                ["listen"] = "127.0.0.1",
                ["protocol"] = "socks",
                ["settings"] = new JsonObject
                {
                    ["udp"] = true,
                    ["auth"] = "noauth"
                }
            }
        };

        // An HTTP inbound lets the Windows system proxy (WinINET) route every app through us.
        if (localHttpPort > 0)
            inbounds.Add(new JsonObject
            {
                ["tag"] = "http-in",
                ["port"] = localHttpPort,
                ["listen"] = "127.0.0.1",
                ["protocol"] = "http",
                ["settings"] = new JsonObject()
            });

        var rules = new JsonArray
        {
            // Keep LAN / loopback traffic direct so the local proxy itself stays reachable.
            new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["ip"] = new JsonArray { "geoip:private" }
            }
        };

        // Game mode: route only the game's domains/IPs through the node, send everything else direct.
        var hasDomains = proxyDomains is { Count: > 0 };
        var hasIps = proxyIps is { Count: > 0 };
        if (hasDomains || hasIps)
        {
            if (hasDomains)
            {
                var domains = new JsonArray();
                foreach (var d in proxyDomains!)
                    domains.Add($"domain:{d}");

                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "proxy",
                    ["domain"] = domains
                });
            }

            if (hasIps)
            {
                var ips = new JsonArray();
                foreach (var ip in proxyIps!)
                    ips.Add(ip);

                rules.Add(new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "proxy",
                    ["ip"] = ips
                });
            }

            // Everything not matched above stays direct.
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["network"] = "tcp,udp"
            });
        }

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = inbounds,
            ["outbounds"] = new JsonArray
            {
                BuildOutbound(server),
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" },
                new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" }
            },
            ["routing"] = new JsonObject
            {
                ["domainStrategy"] = "AsIs",
                ["rules"] = rules
            }
        };

        // Stats API: exposes outbound byte counters queryable via `xray api statsquery`.
        if (apiPort > 0)
        {
            inbounds.Add(new JsonObject
            {
                ["tag"] = "api-in",
                ["port"] = apiPort,
                ["listen"] = "127.0.0.1",
                ["protocol"] = "dokodemo-door",
                ["settings"] = new JsonObject { ["address"] = "127.0.0.1" }
            });

            config["stats"] = new JsonObject();
            config["api"] = new JsonObject
            {
                ["tag"] = "api",
                ["services"] = new JsonArray { "StatsService" }
            };
            config["policy"] = new JsonObject
            {
                ["system"] = new JsonObject
                {
                    ["statsOutboundUplink"] = true,
                    ["statsOutboundDownlink"] = true
                }
            };

            rules.Insert(0, new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "api-in" },
                ["outboundTag"] = "api"
            });
        }

        return config;
    }

    private static JsonObject BuildOutbound(Server server) => server switch
    {
        ShadowsocksServer ss => Shadowsocks(ss),
        TrojanServer tr => Trojan(tr),
        VMessServer vm => VMess(vm),
        VlessServer vl => Vless(vl),
        Socks5Server s5 => Socks(s5),
        WireGuardServer wg => WireGuard(wg),
        _ => throw new NotSupportedException($"Unsupported server type: {server.Type}")
    };

    private static JsonObject Shadowsocks(ShadowsocksServer s) => new()
    {
        ["tag"] = "proxy",
        ["protocol"] = "shadowsocks",
        ["settings"] = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = s.Hostname,
                    ["port"] = s.Port,
                    ["method"] = s.EncryptMethod,
                    ["password"] = s.Password
                }
            }
        }
    };

    private static JsonObject Trojan(TrojanServer s) => new()
    {
        ["tag"] = "proxy",
        ["protocol"] = "trojan",
        ["settings"] = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["address"] = s.Hostname,
                    ["port"] = s.Port,
                    ["password"] = s.Password
                }
            }
        },
        ["streamSettings"] = new JsonObject
        {
            ["network"] = "tcp",
            ["security"] = "tls",
            ["tlsSettings"] = new JsonObject
            {
                ["serverName"] = string.IsNullOrEmpty(s.Sni) ? s.Hostname : s.Sni,
                ["allowInsecure"] = s.AllowInsecure
            }
        }
    };

    private static JsonObject VMess(VMessServer s)
    {
        var outbound = new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vmess",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = s.Hostname,
                        ["port"] = s.Port,
                        ["users"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = s.UserId,
                                ["alterId"] = s.AlterId,
                                ["security"] = s.Security
                            }
                        }
                    }
                }
            }
        };

        outbound["streamSettings"] = new JsonObject
        {
            ["network"] = s.Network,
            ["security"] = s.Tls ? "tls" : "none"
        };

        return outbound;
    }

    private static JsonObject Vless(VlessServer s)
    {
        var user = new JsonObject
        {
            ["id"] = s.UserId,
            ["encryption"] = "none"
        };
        if (!string.IsNullOrEmpty(s.Flow))
            user["flow"] = s.Flow;

        var security = string.IsNullOrEmpty(s.Security) ? "none" : s.Security;
        var serverName = string.IsNullOrEmpty(s.Sni) ? s.Hostname : s.Sni;
        var fingerprint = string.IsNullOrEmpty(s.Fingerprint) ? "chrome" : s.Fingerprint;

        var streamSettings = new JsonObject
        {
            ["network"] = s.Network,
            ["security"] = security
        };

        if (security == "reality")
        {
            // REALITY: the public key + optional short id/spiderX; uTLS fingerprint is mandatory.
            streamSettings["realitySettings"] = new JsonObject
            {
                ["serverName"] = serverName,
                ["publicKey"] = s.PublicKey ?? string.Empty,
                ["shortId"] = s.ShortId ?? string.Empty,
                ["spiderX"] = s.SpiderX ?? string.Empty,
                ["fingerprint"] = fingerprint
            };
        }
        else if (security == "tls")
        {
            streamSettings["tlsSettings"] = new JsonObject
            {
                ["serverName"] = serverName,
                ["fingerprint"] = fingerprint,
                ["allowInsecure"] = false
            };
        }

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "vless",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["address"] = s.Hostname,
                        ["port"] = s.Port,
                        ["users"] = new JsonArray { user }
                    }
                }
            },
            ["streamSettings"] = streamSettings
        };
    }

    private static JsonObject Socks(Socks5Server s)
    {
        var serverObj = new JsonObject
        {
            ["address"] = s.Hostname,
            ["port"] = s.Port
        };
        if (s.Auth())
            serverObj["users"] = new JsonArray
            {
                new JsonObject { ["user"] = s.Username, ["pass"] = s.Password }
            };

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "socks",
            ["settings"] = new JsonObject { ["servers"] = new JsonArray { serverObj } }
        };
    }

    private static JsonObject WireGuard(WireGuardServer s) => new()
    {
        ["tag"] = "proxy",
        ["protocol"] = "wireguard",
        ["settings"] = new JsonObject
        {
            ["secretKey"] = s.PrivateKey,
            ["peers"] = new JsonArray
            {
                new JsonObject
                {
                    ["publicKey"] = s.PublicKey,
                    ["endpoint"] = $"{s.Hostname}:{s.Port}",
                    ["allowedIPs"] = new JsonArray { "0.0.0.0/0", "::/0" }
                }
            }
        }
    };
}
