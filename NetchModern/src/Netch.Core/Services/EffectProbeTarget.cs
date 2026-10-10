using System.Net;
using System.Net.Sockets;
using Netch.Core.Models.Games;

namespace Netch.Core.Services;

/// <summary>
///     Resolves the single host:port a game actually connects to, so the effect probe can measure a real
///     round trip against the game's own target (rather than an arbitrary public endpoint).
/// </summary>
public static class EffectProbeTarget
{
    /// <summary>
    ///     Returns the first usable (host, port) for the game, or null when the game has no measurable
    ///     domain/IP rule. Domains win over IPs; the port is always 443 (TLS).
    /// </summary>
    public static (string Host, int Port)? Resolve(Game game)
    {
        foreach (var raw in game.ProxyDomains)
        {
            var host = NormalizeDomain(raw);
            if (host is not null)
                return (host, 443);
        }

        foreach (var raw in game.ProxyIps)
        {
            var host = NormalizeIp(raw);
            if (host is not null)
                return (host, 443);
        }

        return null;
    }

    /// <summary>
    ///     Strips routing prefixes (<c>domain:</c>, <c>full:</c>, <c>*.</c>, <c>.</c>) and rejects entries
    ///     that are not a plain hostname (regexp/keyword/geosite/ext rules, wildcards).
    /// </summary>
    private static string? NormalizeDomain(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();

        foreach (var prefix in new[] { "domain:", "full:" })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                s = s[prefix.Length..].Trim();

        while (s.StartsWith("*.", StringComparison.Ordinal) || s.StartsWith(".", StringComparison.Ordinal))
            s = s.StartsWith("*.", StringComparison.Ordinal) ? s[2..] : s[1..];

        if (s.Length == 0)
            return null;

        if (s.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (s.Contains('*'))
            return null;

        // A hostname must contain at least one dot and no whitespace/slash.
        if (!s.Contains('.') || s.Any(char.IsWhiteSpace) || s.Contains('/'))
            return null;

        return s;
    }

    /// <summary>
    ///     Strips a CIDR suffix. <c>1.2.3.0/24</c> → <c>1.2.3.1</c> (network address + 1); <c>/32</c> or no
    ///     suffix → the address itself. IPv6 is returned as-is (no suffix handling).
    /// </summary>
    private static string? NormalizeIp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        var slash = s.IndexOf('/');
        if (slash < 0)
            return s;

        var addrPart = s[..slash];
        var prefixPart = s[(slash + 1)..];

        if (!IPAddress.TryParse(addrPart, out var addr))
            return null;

        // IPv6: return as-is (no network+1 arithmetic).
        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            return addrPart;

        if (!int.TryParse(prefixPart, out var prefix))
            return null;

        if (prefix >= 32)
            return addrPart;

        var bytes = addr.GetAddressBytes();
        var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = prefix <= 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        var network = value & mask;
        var host = network + 1;
        return new IPAddress(new[]
        {
            (byte)(host >> 24), (byte)(host >> 16), (byte)(host >> 8), (byte)host
        }).ToString();
    }
}
