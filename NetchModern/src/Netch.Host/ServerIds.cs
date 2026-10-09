using System.Security.Cryptography;
using System.Text;
using Netch.Core.Models.Servers;

namespace Netch.Host;

/// <summary>
///     Deterministic server ids. The id is a fingerprint of the server's identifying fields, so it stays
///     stable across host restarts and subscription refreshes (which create fresh <see cref="Server" />
///     instances). Duplicates in the same list get a "-2", "-3", … suffix in list order.
/// </summary>
internal static class ServerIds
{
    /// <summary>Computes the base fingerprint (no duplicate suffix) for a server.</summary>
    public static string Fingerprint(Server s)
    {
        var sb = new StringBuilder();
        sb.Append(s.Group).Append('\u001f');
        sb.Append(Mapping.KindOf(s)).Append('\u001f');
        sb.Append(s.Hostname).Append('\u001f');
        sb.Append(s.Port).Append('\u001f');
        sb.Append(s.Remark).Append('\u001f');
        sb.Append(CredentialOf(s));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    ///     Assigns ids to a list of servers, disambiguating duplicates with a numeric suffix in list order.
    ///     Returns a map from server instance to id.
    /// </summary>
    public static Dictionary<Server, string> Assign(IReadOnlyList<Server> servers)
    {
        var result = new Dictionary<Server, string>(ReferenceEqualityComparer.Instance);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var s in servers)
        {
            var fp = Fingerprint(s);
            if (seen.TryGetValue(fp, out var count))
            {
                seen[fp] = count + 1;
                result[s] = $"{fp}-{count + 1}";
            }
            else
            {
                seen[fp] = 1;
                result[s] = fp;
            }
        }

        return result;
    }

    /// <summary>Resolves a server by id against the current list, or null when not found.</summary>
    public static Server? Resolve(string id, IReadOnlyList<Server> pool)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var map = Assign(pool);
        foreach (var kv in map)
            if (kv.Value == id)
                return kv.Key;
        return null;
    }

    private static string CredentialOf(Server s) => s switch
    {
        VMessServer v => v.UserId ?? string.Empty,
        VlessServer v => v.UserId ?? string.Empty,
        TrojanServer t => t.Password ?? string.Empty,
        ShadowsocksServer ss => ss.Password ?? string.Empty,
        Hysteria2Server h => h.Password ?? string.Empty,
        Socks5Server s5 => s5.Username ?? string.Empty,
        WireGuardServer wg => wg.PublicKey ?? string.Empty,
        _ => string.Empty
    };
}
