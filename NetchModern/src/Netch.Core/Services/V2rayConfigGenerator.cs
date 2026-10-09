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

    private static JsonObject Shadowsocks(ShadowsocksServer s)
    {
        // Xray does not implement SIP003 plugins (obfs-local / v2ray-plugin). Such nodes must run on sing-box.
        if (!string.IsNullOrEmpty(s.Plugin))
            throw new NotSupportedException(
                $"Shadowsocks plugin '{s.Plugin}' is not supported by xray; route this node through sing-box.");

        return new JsonObject
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
    }

    private static JsonObject Trojan(TrojanServer s)
    {
        var streamSettings = new JsonObject
        {
            ["network"] = string.IsNullOrEmpty(s.Network) ? "tcp" : s.Network,
            ["security"] = "tls",
            ["tlsSettings"] = new JsonObject
            {
                ["serverName"] = string.IsNullOrEmpty(s.Sni) ? s.Hostname : s.Sni,
                ["allowInsecure"] = s.AllowInsecure
            }
        };

        ApplyTransport(streamSettings, s.Network, s.Host, s.Path, s.ServiceName, s.Mode, s.Alpn, s.Fingerprint);

        return new JsonObject
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
            ["streamSettings"] = streamSettings
        };
    }

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

        var streamSettings = new JsonObject
        {
            ["network"] = string.IsNullOrEmpty(s.Network) ? "tcp" : s.Network,
            ["security"] = s.Tls ? "tls" : "none"
        };

        if (s.Tls)
        {
            // SNI fallback: explicit sni → host header → hostname.
            var serverName = !string.IsNullOrEmpty(s.Sni) ? s.Sni
                : !string.IsNullOrEmpty(s.Host) ? s.Host
                : s.Hostname;

            var tlsSettings = new JsonObject
            {
                ["serverName"] = serverName,
                ["allowInsecure"] = s.AllowInsecure
            };
            AddAlpn(tlsSettings, s.Alpn);
            if (!string.IsNullOrEmpty(s.Fingerprint))
                tlsSettings["fingerprint"] = s.Fingerprint;

            streamSettings["tlsSettings"] = tlsSettings;
        }

        ApplyTransport(streamSettings, s.Network, s.Host, s.Path, s.ServiceName, s.Mode, s.Alpn, s.Fingerprint);

        outbound["streamSettings"] = streamSettings;
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
            ["network"] = string.IsNullOrEmpty(s.Network) ? "tcp" : s.Network,
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
            var tlsSettings = new JsonObject
            {
                ["serverName"] = serverName,
                ["fingerprint"] = fingerprint,
                ["allowInsecure"] = s.AllowInsecure
            };
            AddAlpn(tlsSettings, s.Alpn);
            streamSettings["tlsSettings"] = tlsSettings;
        }

        ApplyTransport(streamSettings, s.Network, s.Host, s.Path, s.ServiceName, s.Mode, s.Alpn, s.Fingerprint);

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

    /// <summary>
    ///     Emits the per-network transport settings block (ws / grpc / h2 / httpupgrade / xhttp) onto
    ///     <paramref name="streamSettings" />. "tcp" (and unknown values) emit nothing.
    /// </summary>
    private static void ApplyTransport(JsonObject streamSettings, string? network, string? host, string? path,
        string? serviceName, string? mode, string? alpn, string? fingerprint)
    {
        var net = string.IsNullOrEmpty(network) ? "tcp" : network.ToLowerInvariant();
        switch (net)
        {
            case "ws":
                streamSettings["wsSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrEmpty(path) ? "/" : path,
                    ["host"] = host ?? string.Empty
                };
                break;

            case "grpc":
                streamSettings["grpcSettings"] = new JsonObject
                {
                    ["serviceName"] = serviceName ?? string.Empty,
                    ["multiMode"] = string.Equals(mode, "multi", StringComparison.OrdinalIgnoreCase)
                };
                break;

            case "h2":
            case "http":
                var hosts = new JsonArray();
                if (!string.IsNullOrEmpty(host))
                    hosts.Add(host);
                streamSettings["httpSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrEmpty(path) ? "/" : path,
                    ["host"] = hosts
                };
                break;

            case "httpupgrade":
                streamSettings["httpupgradeSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrEmpty(path) ? "/" : path,
                    ["host"] = host ?? string.Empty
                };
                break;

            case "xhttp":
            case "splithttp":
                streamSettings["xhttpSettings"] = new JsonObject
                {
                    ["path"] = string.IsNullOrEmpty(path) ? "/" : path,
                    ["host"] = host ?? string.Empty,
                    ["mode"] = string.IsNullOrEmpty(mode) ? "auto" : mode
                };
                break;
        }
    }

    private static void AddAlpn(JsonObject tlsSettings, string? alpn)
    {
        if (string.IsNullOrEmpty(alpn))
            return;

        var arr = new JsonArray();
        foreach (var a in alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arr.Add(a);

        if (arr.Count > 0)
            tlsSettings["alpn"] = arr;
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

    private static JsonObject WireGuard(WireGuardServer s)
    {
        if (string.IsNullOrWhiteSpace(s.LocalAddresses))
            throw new InvalidOperationException(
                "WireGuard node is missing its local interface address (address=...); cannot build an xray outbound.");

        var addresses = new JsonArray();
        foreach (var a in s.LocalAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            addresses.Add(a);

        var peer = new JsonObject
        {
            ["publicKey"] = s.PublicKey,
            ["endpoint"] = $"{s.Hostname}:{s.Port}",
            ["allowedIPs"] = SplitCidrs(s.EndpointAllowedIPs)
        };
        if (!string.IsNullOrEmpty(s.PreSharedKey))
            peer["preSharedKey"] = s.PreSharedKey;

        var settings = new JsonObject
        {
            ["secretKey"] = s.PrivateKey,
            ["address"] = addresses,
            ["peers"] = new JsonArray { peer },
            ["mtu"] = s.Mtu
        };

        if (!string.IsNullOrEmpty(s.Reserved))
        {
            var reserved = new JsonArray();
            foreach (var r in s.Reserved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(r, out var v))
                    reserved.Add(v);
            }

            if (reserved.Count > 0)
                settings["reserved"] = reserved;
        }

        return new JsonObject
        {
            ["tag"] = "proxy",
            ["protocol"] = "wireguard",
            ["settings"] = settings
        };
    }

    private static JsonArray SplitCidrs(string? value)
    {
        var arr = new JsonArray();
        if (string.IsNullOrEmpty(value))
            return arr;

        foreach (var c in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arr.Add(c);

        return arr;
    }
}
