using System.Net;
using System.Net.Sockets;

namespace Netch.Core.Services;

/// <summary>
///     Pure validation helpers for the advanced settings. Each method returns the normalized value or
///     throws <see cref="InvalidOperationException" /> with a user-facing Chinese message.
/// </summary>
public static class AdvancedSettingsValidator
{
    /// <summary>
    ///     Validates a TUN DNS value: a single IPv4 / IPv6 address, optionally with a port
    ///     (<c>1.1.1.1</c>, <c>223.5.5.5:53</c>, <c>::1</c>, <c>[2606:4700:4700::1111]:53</c>).
    ///     Hostnames, empty strings and comma-separated lists are rejected.
    /// </summary>
    public static string ValidateDns(string? value)
    {
        const string message = "TUN DNS 必须是 IP 地址（可带端口），例如 1.1.1.1 或 223.5.5.5:53";

        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException(message);

        // A single value only — no comma-separated lists.
        if (trimmed.Contains(','))
            throw new InvalidOperationException(message);

        string host;
        string? portText = null;

        if (trimmed.StartsWith('['))
        {
            // [IPv6]:port or [IPv6]
            var close = trimmed.IndexOf(']');
            if (close < 0)
                throw new InvalidOperationException(message);

            host = trimmed[1..close];
            var rest = trimmed[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                    throw new InvalidOperationException(message);
                portText = rest[1..];
            }
        }
        else if (IPAddress.TryParse(trimmed, out var whole))
        {
            // Bare IPv4 or IPv6 (no port).
            if (whole.AddressFamily == AddressFamily.InterNetwork)
            {
                // IPAddress.TryParse accepts short forms like "8" or "1.2.3"; require four octets.
                if (!IsStrictIpv4(trimmed))
                    throw new InvalidOperationException(message);
                host = whole.ToString();
            }
            else
            {
                // Bare IPv6: canonicalize to the bracketed form so a port can be appended later.
                return $"[{whole}]";
            }
        }
        else
        {
            // IPv4:port — split on the last colon. A bare IPv6 with a port must use brackets.
            var colon = trimmed.LastIndexOf(':');
            if (colon <= 0 || trimmed.IndexOf(':') != colon)
                throw new InvalidOperationException(message);
            host = trimmed[..colon];
            portText = trimmed[(colon + 1)..];
        }

        if (!IPAddress.TryParse(host, out var address))
            throw new InvalidOperationException(message);

        // IPv4 must be exactly four dotted decimal octets 0..255.
        if (address.AddressFamily == AddressFamily.InterNetwork && !IsStrictIpv4(host))
            throw new InvalidOperationException(message);

        if (portText is not null)
        {
            if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException(message);
        }

        return trimmed;
    }

    /// <summary>
    ///     Returns true when <paramref name="value" /> is exactly four dotted decimal octets in the
    ///     range 0..255. Rejects the short forms that <see cref="IPAddress.TryParse(string, out IPAddress)" />
    ///     would otherwise accept (e.g. <c>8</c> → 0.0.0.8, <c>1.2.3</c> → 1.2.0.3).
    /// </summary>
    private static bool IsStrictIpv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 3)
                return false;

            foreach (var c in part)
            {
                if (c is < '0' or > '9')
                    return false;
            }

            if (!int.TryParse(part, out var octet) || octet is < 0 or > 255)
                return false;
        }

        return true;
    }

    /// <summary>Validates the off-region penalty (0..300 ms).</summary>
    public static int ValidateRegionPenalty(int value)
    {
        if (value is < 0 or > Models.Settings.AdvancedSettings.MaxRegionPenaltyMs)
            throw new InvalidOperationException("地区偏好惩罚必须在 0–300 ms 之间");
        return value;
    }

    /// <summary>Validates the fixed local SOCKS5 port (0 = automatic, else 1024..65535).</summary>
    public static int ValidateLocalPort(int value)
    {
        if (value != 0 && value is < 1024 or > 65535)
            throw new InvalidOperationException("本地端口必须为 0（自动）或 1024–65535");
        return value;
    }

    /// <summary>Checks whether a TCP port on 127.0.0.1 is currently free (bind + release).</summary>
    public static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
