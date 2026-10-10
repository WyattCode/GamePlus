using System.Text;

namespace Netch.Core.Utils;

/// <summary>
///     Redacts secrets from URLs and share links before they reach logs, error messages or the UI.
///     Subscription URLs frequently embed an access token in the path or query string, and share links
///     embed the node password / UUID in the userinfo — neither must ever be logged in full.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    ///     Keeps only the scheme and host of a URL: <c>https://example.com/…</c>.
    ///     Non-URL input (or unparseable input) collapses to <c>"…"</c>.
    /// </summary>
    public static string RedactUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "…";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "…";

        if (string.IsNullOrEmpty(uri.Scheme) || string.IsNullOrEmpty(uri.Host))
            return "…";

        var builder = new StringBuilder();
        builder.Append(uri.Scheme).Append("://").Append(uri.Host);
        if (!uri.IsDefaultPort && uri.Port > 0)
            builder.Append(':').Append(uri.Port);
        builder.Append("/…");
        return builder.ToString();
    }

    /// <summary>
    ///     Keeps only the scheme of a share link: <c>ss://…</c>, <c>vmess://…</c>.
    ///     Non-link input collapses to <c>"…"</c>.
    /// </summary>
    public static string RedactShareLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return "…";

        var trimmed = link.Trim();
        var index = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (index <= 0)
            return "…";

        var scheme = trimmed[..index];
        // A scheme is [a-zA-Z][a-zA-Z0-9+.-]* — reject anything else so we don't echo arbitrary text.
        if (!char.IsLetter(scheme[0]))
            return "…";
        foreach (var c in scheme)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return "…";
        }

        return scheme + "://…";
    }
}
