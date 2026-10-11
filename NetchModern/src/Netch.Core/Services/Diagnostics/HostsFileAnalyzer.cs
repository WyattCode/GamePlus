using System.Net;

namespace Netch.Core.Services.Diagnostics;

/// <summary>A single hosts-file entry that rewrote a domain we care about.</summary>
public sealed record HostsEntry(int Line, string Ip, string Host, string MatchedDomain);

/// <summary>
///     Pure hosts-file analysis: finds entries that point a game/platform domain at a custom IP
///     (typically written by accelerator tools such as Steam++ / Watt Toolkit).
/// </summary>
public static class HostsFileAnalyzer
{
    /// <summary>
    ///     Parses the hosts file and returns every entry whose host equals a watched domain
    ///     (case-insensitive) or is a subdomain of it. Comments are stripped, non-IP lines ignored,
    ///     localhost entries ignored.
    /// </summary>
    public static IReadOnlyList<HostsEntry> FindOverrides(string content, IEnumerable<string> domains)
    {
        var result = new List<HostsEntry>();
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            // First token must be an IP (IPv4 or IPv6); anything else is not a hosts entry.
            if (!IPAddress.TryParse(tokens[0], out _))
                continue;

            var ip = tokens[0];
            for (var t = 1; t < tokens.Length; t++)
            {
                var host = tokens[t];
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var domain in domains)
                {
                    if (MatchesDomain(host, domain))
                    {
                        result.Add(new HostsEntry(i + 1, ip, host, domain));
                        break;
                    }
                }
            }
        }

        return result;
    }

    private static bool MatchesDomain(string host, string domain)
    {
        if (host.Equals(domain, StringComparison.OrdinalIgnoreCase))
            return true;

        return host.Length > domain.Length &&
               host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }
}
