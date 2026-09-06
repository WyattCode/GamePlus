using System.Net;
using System.Text;

namespace Netch.Core.Utils;

/// <summary>
///     Encoding helpers shared by the share-link parsers. Uses BCL only (no System.Web dependency).
/// </summary>
public static class ShareLinkUtils
{
    /// <summary>URL-safe Base64 decode with padding tolerance.</summary>
    public static string Base64UrlDecode(string text)
    {
        // Strip ALL whitespace first: MIME-wrapped subscription blobs are line-broken, and padding must be
        // computed from the real payload length, not the length including embedded \r\n.
        text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }

    /// <summary>URL-safe Base64 encode (no padding).</summary>
    public static string Base64UrlEncode(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool TryBase64UrlDecode(string text, out string decoded)
    {
        try
        {
            decoded = Base64UrlDecode(text);
            return true;
        }
        catch
        {
            decoded = string.Empty;
            return false;
        }
    }

    /// <summary>Parses a <c>a=b&amp;c=d</c> query string (leading '?' optional) into a dictionary.</summary>
    public static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return dict;

        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
                continue;

            var key = WebUtility.UrlDecode(pair[..idx]);
            var value = WebUtility.UrlDecode(pair[(idx + 1)..]);
            dict[key] = value;
        }

        return dict;
    }

    public static IEnumerable<string> GetLines(this string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line.Trim();
        }
    }

    public static string UrlDecode(string text) => WebUtility.UrlDecode(text);
}
