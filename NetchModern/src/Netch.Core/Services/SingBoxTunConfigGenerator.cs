using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Everything the sing-box TUN config needs. All lists are already normalised/deduplicated
///     (see <see cref="SplitRouting.BuildTunRouting" />).
/// </summary>
public sealed record TunRouting(
    IReadOnlyList<string> ProxyProcesses,   // game process rules + enabled extra "proxy" apps (bare names, may lack ".exe")
    IReadOnlyList<string> BypassProcesses,  // enabled extra "bypass" apps (never one of the game's own processes)
    IReadOnlyList<string> ProxyDomains,     // domain suffixes routed to the proxy
    IReadOnlyList<string> ProxyIps,         // IPs / CIDRs routed to the proxy
    IReadOnlyList<string> DirectDomains,    // domain suffixes forced DIRECT (game CDN downloads), may be empty
    IReadOnlyList<string> ExcludeAddresses) // IPs / CIDRs removed from the TUN routes (the node server), may be empty
{
    public bool IsEmpty => ProxyProcesses.Count == 0 && ProxyDomains.Count == 0 && ProxyIps.Count == 0;
}

/// <summary>
///     Builds the sing-box (1.14.x) configuration for the second sing-box instance that owns the
///     WinTUN interface and routes game traffic to the local SOCKS5 endpoint of the node core.
/// </summary>
public static class SingBoxTunConfigGenerator
{
    public const string InterfaceName = "GamePlus";
    public const string TunAddress = "172.19.0.1/30";
    public const string InboundTag = "tun-in";
    public const string ProxyTag = "proxy";
    public const string DirectTag = "direct";

    /// <summary>
    ///     Proxy cores and the host itself: always DIRECT so the TUN never loops their traffic.
    /// </summary>
    public static readonly string[] CoreProcesses = { "xray.exe", "sing-box.exe", "gameplus-host.exe" };

    /// <summary>
    ///     Builds a Go RE2 <c>process_path_regex</c> pattern of the form
    ///     <c>(?i)(^|\)NAME(\.exe)?$</c> where NAME is the basename of the input (anything after
    ///     the last '\' or '/'), trimmed, with one trailing ".exe" removed case-insensitively.
    ///     Metacharacters are escaped by hand instead of <see cref="Regex.Escape" /> because
    ///     .NET escapes spaces/'#' which Go RE2 rejects or treats differently. The pattern is
    ///     case-insensitive and anchored to the file name, so it never matches e.g.
    ///     <c>NotSteam.exe</c>. A blank name throws <see cref="ArgumentException" />.
    /// </summary>
    public static string ProcessPathRegex(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name must not be blank.", nameof(processName));

        var name = processName.Trim();
        var lastSeparator = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (lastSeparator >= 0)
            name = name[(lastSeparator + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Length == 0)
            throw new ArgumentException("Process name must not be blank.", nameof(processName));

        var sb = new StringBuilder("(?i)(^|\\\\)");
        foreach (var c in name)
        {
            if (c is '\\' or '.' or '+' or '*' or '?' or '(' or ')' or '|' or '[' or ']' or '{' or '}' or '^' or '$')
                sb.Append('\\');
            sb.Append(c);
        }
        sb.Append(@"(\.exe)?$");
        return sb.ToString();
    }

    /// <summary>
    ///     Builds the full sing-box TUN configuration. The shape is fixed for sing-box 1.14.3:
    ///     a tun inbound (gvisor stack, no DNS hijack), a direct and a socks outbound, and an
    ///     ordered rule set. Rules whose source list is empty are omitted entirely.
    /// </summary>
    public static JsonObject Build(TunRouting routing, Socks5Server socks)
    {
        // dns_mode is disabled on purpose: Windows resolves DNS inside the DNS Client service
        // (svchost), so DNS traffic cannot be attributed to a game process. Hijacking DNS or
        // using fake-ip would push every application's lookups through sing-box and leave
        // stale fake IPs in the Windows resolver cache after shutdown. System DNS is untouched.
        var inbound = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = InboundTag,
            ["interface_name"] = InterfaceName,
            // A single IPv4 /30. No IPv6 address is configured on purpose: IPv6 is simply not
            // captured, so games either fall back to IPv4 or go direct — nothing breaks.
            ["address"] = new JsonArray(TunAddress),
            ["auto_route"] = true,
            ["strict_route"] = false,
            ["dns_mode"] = "disabled",
            // gvisor stack: sing-box handles the traffic itself without a system TCP listener,
            // so no Windows Firewall prompt appears.
            ["stack"] = "gvisor"
        };

        // The node server address is removed from the TUN routes so the proxy core's own
        // connection to the node never re-enters the TUN (loop protection, together with
        // the core-process rule below).
        if (routing.ExcludeAddresses.Count > 0)
        {
            var exclude = new JsonArray();
            foreach (var address in routing.ExcludeAddresses)
                exclude.Add(NormalizeCidr(address));
            inbound["route_exclude_address"] = exclude;
        }

        var outbounds = new JsonArray
        {
            new JsonObject { ["type"] = DirectTag, ["tag"] = DirectTag },
            new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = ProxyTag,
                ["server"] = socks.Hostname,
                ["server_port"] = (int)socks.Port,
                ["version"] = "5"
            }
        };

        if (socks.Auth())
        {
            outbounds[1]!["username"] = socks.Username;
            outbounds[1]!["password"] = socks.Password;
        }

        // Rule order matters: excluded > private > forced-direct domains (game CDN split) >
        // bypass apps > proxy processes > proxy domains > proxy IPs > final direct.
        var rules = new JsonArray
        {
            // Sniff only on web ports so server-first game protocols on other ports get no
            // sniff delay; domain rules still work for HTTP, TLS and QUIC traffic.
            new JsonObject
            {
                ["inbound"] = InboundTag,
                ["port"] = new JsonArray(80, 443),
                ["action"] = "sniff",
                ["sniffer"] = new JsonArray("http", "tls", "quic")
            },
            // The proxy cores (and the host) always go direct so the TUN never loops their
            // own traffic back into the proxy.
            new JsonObject
            {
                ["process_path_regex"] = ProcessRegexArray(CoreProcesses),
                ["action"] = "route",
                ["outbound"] = DirectTag
            },
            new JsonObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = DirectTag }
        };

        if (routing.DirectDomains.Count > 0)
            rules.Add(new JsonObject
            {
                ["domain_suffix"] = StringArray(routing.DirectDomains),
                ["action"] = "route",
                ["outbound"] = DirectTag
            });

        if (routing.BypassProcesses.Count > 0)
            rules.Add(new JsonObject
            {
                ["process_path_regex"] = ProcessRegexArray(routing.BypassProcesses),
                ["action"] = "route",
                ["outbound"] = DirectTag
            });

        if (routing.ProxyProcesses.Count > 0)
            rules.Add(new JsonObject
            {
                ["process_path_regex"] = ProcessRegexArray(routing.ProxyProcesses),
                ["action"] = "route",
                ["outbound"] = ProxyTag
            });

        if (routing.ProxyDomains.Count > 0)
            rules.Add(new JsonObject
            {
                ["domain_suffix"] = StringArray(routing.ProxyDomains),
                ["action"] = "route",
                ["outbound"] = ProxyTag
            });

        if (routing.ProxyIps.Count > 0)
            rules.Add(new JsonObject
            {
                ["ip_cidr"] = StringArray(routing.ProxyIps),
                ["action"] = "route",
                ["outbound"] = ProxyTag
            });

        var route = new JsonObject
        {
            // Required by the sing-box docs for TUN on Windows to avoid routing loops.
            ["auto_detect_interface"] = true,
            ["find_process"] = true,
            ["final"] = DirectTag,
            ["rules"] = rules
        };

        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["inbounds"] = new JsonArray(inbound),
            ["outbounds"] = outbounds,
            ["route"] = route
        };
    }

    /// <summary>Serialises <see cref="Build" /> as indented JSON.</summary>
    public static string Generate(TunRouting routing, Socks5Server socks) =>
        Build(routing, socks).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>A bare IPv4 gets "/32", a bare IPv6 gets "/128"; CIDRs are kept as-is.</summary>
    private static string NormalizeCidr(string address)
    {
        var trimmed = address.Trim();
        if (trimmed.Contains('/'))
            return trimmed;
        return trimmed.Contains(':') ? $"{trimmed}/128" : $"{trimmed}/32";
    }

    private static JsonArray ProcessRegexArray(IEnumerable<string> names)
    {
        var array = new JsonArray();
        foreach (var name in names)
            array.Add(ProcessPathRegex(name));
        return array;
    }

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
