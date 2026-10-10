using System.Net;
using System.Net.Sockets;
using Netch.Core.Models.Settings;

namespace Netch.Core.Services;

/// <summary>
///     Pure validation helpers for the LAN-share feature (console proxy sharing). All methods throw
///     <see cref="ArgumentException" /> / <see cref="InvalidOperationException" /> with Chinese,
///     user-facing messages; <see cref="BuildOptions" /> reports problems via an out parameter instead.
/// </summary>
public static class LanShareValidator
{
    /// <summary>Maximum number of allowlisted clients.</summary>
    private const int MaxClients = 32;

    /// <summary>Minimum prefix length for an allowlisted CIDR (a /8 is considered too broad).</summary>
    private const int MinPrefixLength = 16;

    /// <summary>Whether <paramref name="address" /> is a private IPv4 address (10/8, 172.16/12, 192.168/16).</summary>
    public static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsPrivateIPv4String(string value)
    {
        return IPAddress.TryParse(value, out var ip) && IsPrivateIPv4(ip);
    }

    /// <summary>
    ///     Validates that <paramref name="value" /> is a private IPv4 address that currently exists on
    ///     this PC. Returns the normalized address string, or throws with a Chinese message.
    /// </summary>
    public static string ValidateBindAddress(string? value, IReadOnlyCollection<IPAddress> localAddresses)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !IPAddress.TryParse(value.Trim(), out var ip)
            || !IsPrivateIPv4(ip))
        {
            throw new ArgumentException("共享地址必须是本机的局域网 IPv4 地址（如 192.168.x.x）");
        }

        // Canonical dotted-decimal only: IPAddress.TryParse accepts inet_aton forms where a leading zero
        // means octal ("192.168.001.010" -> 192.168.1.8), so a non-canonical spelling is ambiguous.
        var normalized = ip.ToString();
        if (normalized != value.Trim())
            throw new ArgumentException("共享地址必须是本机的局域网 IPv4 地址（如 192.168.x.x）");
        if (!localAddresses.Any(a => a.Equals(ip)))
            throw new ArgumentException("共享地址必须是本机的局域网 IPv4 地址（如 192.168.x.x）");

        return normalized;
    }

    /// <summary>
    ///     Validates the client allowlist: non-empty, at most 32 entries, each a private IPv4 address or
    ///     a CIDR within the private ranges, no overly broad prefixes (shorter than /16) and no 0.0.0.0/0.
    ///     Returns the normalized entries (single IPs as /32 CIDR is NOT applied here — single IPs stay
    ///     as plain addresses; CIDR hosts are normalized away).
    /// </summary>
    public static IReadOnlyList<string> ValidateClients(IEnumerable<string>? clients)
    {
        if (clients == null)
            throw new ArgumentException("至少需要添加一个允许的客户端（局域网 IPv4 或网段）");

        var list = new List<string>();
        foreach (var raw in clients)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var value = raw.Trim();
            var normalized = NormalizeClient(value);
            if (!list.Contains(normalized))
                list.Add(normalized);
        }

        if (list.Count == 0)
            throw new ArgumentException("至少需要添加一个允许的客户端（局域网 IPv4 或网段）");

        if (list.Count > MaxClients)
            throw new ArgumentException($"允许的客户端最多 {MaxClients} 个");

        return list;
    }

    private static string NormalizeClient(string value)
    {
        string ipPart;
        int prefix;

        var slash = value.IndexOf('/');
        if (slash >= 0)
        {
            ipPart = value[..slash];
            if (!int.TryParse(value[(slash + 1)..].Trim(), out prefix) || prefix is < 0 or > 32)
                throw new ArgumentException($"无效的客户端网段：{value}");

            if (prefix < MinPrefixLength)
                throw new ArgumentException($"客户端网段过大（前缀长度不能小于 /{MinPrefixLength}）：{value}");
        }
        else
        {
            ipPart = value;
            prefix = 32;
        }

        if (!IPAddress.TryParse(ipPart.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || ip.ToString() != ipPart.Trim()) // canonical dotted-decimal only (no octal/short forms)
            throw new ArgumentException($"客户端必须是局域网 IPv4 地址或网段：{value}");

        if (!IsPrivateIPv4(ip))
            throw new ArgumentException($"客户端必须是私有网段内的 IPv4 地址：{value}");

        var bytes = ip.GetAddressBytes();
        var hostBits = 32 - prefix;
        var mask = hostBits >= 32 ? 0u : uint.MaxValue << hostBits;

        // Normalize: strip host bits from the network part (e.g. 192.168.1.77/24 → 192.168.1.0/24).
        var network = BitConverter.GetBytes(BitConverter.ToUInt32(bytes.Reverse().ToArray(), 0) & mask);
        Array.Reverse(network);
        return $"{new IPAddress(network)}/{prefix}";
    }

    /// <summary>
    ///     Validates the LAN inbound ports: 1024–65535, distinct from each other and from the local
    ///     SOCKS port setting (0 = automatic, which cannot clash).
    /// </summary>
    public static void ValidatePorts(int http, int socks, int localSocksPort)
    {
        if (http is < 1024 or > 65535)
            throw new ArgumentException("HTTP 端口必须在 1024–65535 之间");

        if (socks is < 0 or > 65535 || (socks > 0 && socks < 1024))
            throw new ArgumentException("SOCKS 端口必须在 1024–65535 之间（0 表示关闭）");

        if (socks == http)
            throw new ArgumentException("HTTP 端口与 SOCKS 端口不能相同");

        if (localSocksPort > 0 && (http == localSocksPort || socks == localSocksPort))
            throw new ArgumentException("LAN 共享端口不能与本地 SOCKS 端口相同");
    }

    /// <summary>
    ///     Validates the optional credentials: both empty or both set; 1–64 chars; no ':' in the
    ///     username; no whitespace or control characters.
    /// </summary>
    public static void ValidateCredentials(string? user, string? pass)
    {
        var hasUser = !string.IsNullOrEmpty(user);
        var hasPass = !string.IsNullOrEmpty(pass);

        if (hasUser != hasPass)
            throw new ArgumentException("用户名和密码必须同时填写或同时留空");

        if (!hasUser)
            return;

        if (user!.Length > 64 || pass!.Length > 64)
            throw new ArgumentException("用户名和密码长度不能超过 64 个字符");

        if (user.Contains(':'))
            throw new ArgumentException("用户名不能包含冒号");

        if (user.Any(char.IsWhiteSpace) || user.Any(char.IsControl)
            || pass!.Any(char.IsWhiteSpace) || pass.Any(char.IsControl))
        {
            throw new ArgumentException("用户名和密码不能包含空格或控制字符");
        }
    }

    /// <summary>
    ///     Builds the immutable options snapshot for the controllers. Returns null with a Chinese
    ///     <paramref name="problem" /> when the feature is disabled or invalid at start time (e.g. the
    ///     bind address is no longer present on this PC).
    /// </summary>
    public static LanShareOptions? BuildOptions(LanShareSettings s, IReadOnlyCollection<IPAddress> localAddresses,
        out string? problem)
    {
        problem = null;

        if (!s.Enabled)
            return null;

        try
        {
            var bind = ValidateBindAddress(s.BindAddress, localAddresses);
            var clients = ValidateClients(s.AllowedClients);
            ValidatePorts(s.HttpPort, s.SocksPort, 0);
            ValidateCredentials(s.Username, s.Password);

            return new LanShareOptions(bind, s.HttpPort, s.SocksPort, clients,
                string.IsNullOrEmpty(s.Username) ? null : s.Username,
                string.IsNullOrEmpty(s.Password) ? null : s.Password);
        }
        catch (ArgumentException ex)
        {
            problem = ex.Message;
            return null;
        }
    }
}
