using System.Text.RegularExpressions;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services.Import;

/// <summary>Result of parsing a pasted import payload.</summary>
public sealed record ImportParseResult(IReadOnlyList<Server> Valid, int Invalid, string Format);

/// <summary>
///     Unified entry point for the "import nodes" feature. Accepts Clash YAML, share links,
///     Base64 subscription blobs and (already-decoded) QR text, and produces validated servers.
/// </summary>
public static partial class NodeImporter
{
    /// <summary>
    ///     Detects the payload format and parses it into validated servers.
    ///     Format is one of "clash" | "links" | "base64" | "empty".
    /// </summary>
    public static ImportParseResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ImportParseResult(Array.Empty<Server>(), 0, "empty");

        var trimmed = text.Trim();

        if (LooksLikeClash(trimmed))
        {
            var clash = ClashConfigParser.Parse(trimmed);
            var (valid, invalid) = ValidateAll(clash);
            return new ImportParseResult(valid, invalid, "clash");
        }

        var format = LooksLikeBase64(trimmed) ? "base64" : "links";
        var parsed = ShareLinkParser.ParseText(trimmed);
        var (validLinks, invalidLinks) = ValidateAll(parsed);
        return new ImportParseResult(validLinks, invalidLinks, format);
    }

    /// <summary>Returns null when the server is usable, otherwise a Chinese reason string.</summary>
    public static string? Validate(Server s)
    {
        if (s == null)
            return "服务器为空";

        if (string.IsNullOrWhiteSpace(s.Hostname))
            return "服务器地址为空";
        if (s.Hostname.Any(char.IsWhiteSpace))
            return "服务器地址包含空白字符";
        if (s.Port is < 1 or > 65535)
            return "端口超出范围 (1-65535)";

        switch (s)
        {
            case ShadowsocksServer ss:
                if (string.IsNullOrWhiteSpace(ss.EncryptMethod))
                    return "Shadowsocks 缺少加密方式";
                if (string.IsNullOrEmpty(ss.Password))
                    return "Shadowsocks 缺少密码";
                break;
            case VMessServer vm:
                if (!Guid.TryParse(vm.UserId, out _))
                    return "VMess 的 UUID 无效";
                break;
            case VlessServer vl:
                if (!Guid.TryParse(vl.UserId, out _))
                    return "VLESS 的 UUID 无效";
                break;
            case TrojanServer tr:
                if (string.IsNullOrEmpty(tr.Password))
                    return "Trojan 缺少密码";
                break;
            case Hysteria2Server hy:
                if (string.IsNullOrEmpty(hy.Password))
                    return "Hysteria2 缺少密码";
                break;
        }

        return null;
    }

    /// <summary>
    ///     Stable identity for duplicate detection: type | host | port | credential | transport path | sni.
    ///     The remark is deliberately ignored.
    /// </summary>
    public static string IdentityKey(Server s)
    {
        var type = s.GetType().Name.ToLowerInvariant();
        var host = (s.Hostname ?? string.Empty).Trim().ToLowerInvariant();
        var port = s.Port;
        var credential = Credential(s);
        var path = TransportPath(s);
        var sni = Sni(s);

        return string.Join('|', type, host, port.ToString(), credential, path, sni);
    }

    /// <summary>
    ///     Drops incoming entries whose identity key already exists in <paramref name="existing" />
    ///     or earlier in <paramref name="incoming" />.
    /// </summary>
    public static (IReadOnlyList<Server> Added, int Duplicates) Dedupe(
        IEnumerable<Server> existing, IEnumerable<Server> incoming)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (existing != null)
        {
            foreach (var server in existing)
            {
                if (server == null)
                    continue;
                seen.Add(IdentityKey(server));
            }
        }

        var added = new List<Server>();
        var duplicates = 0;

        if (incoming != null)
        {
            foreach (var server in incoming)
            {
                if (server == null)
                    continue;
                if (!seen.Add(IdentityKey(server)))
                {
                    duplicates++;
                    continue;
                }
                added.Add(server);
            }
        }

        return (added, duplicates);
    }

    private static (IReadOnlyList<Server> Valid, int Invalid) ValidateAll(IReadOnlyList<Server> servers)
    {
        var valid = new List<Server>();
        var invalid = 0;
        foreach (var server in servers)
        {
            if (Validate(server) == null)
                valid.Add(server);
            else
                invalid++;
        }
        return (valid, invalid);
    }

    private static string Credential(Server s) => s switch
    {
        ShadowsocksServer ss => ss.Password ?? string.Empty,
        VMessServer vm => vm.UserId ?? string.Empty,
        VlessServer vl => vl.UserId ?? string.Empty,
        TrojanServer tr => tr.Password ?? string.Empty,
        Hysteria2Server hy => hy.Password ?? string.Empty,
        Socks5Server sk => (sk.Username ?? string.Empty) + ":" + (sk.Password ?? string.Empty),
        _ => string.Empty
    };

    private static string TransportPath(Server s) => s switch
    {
        VMessServer vm => vm.Path ?? vm.ServiceName ?? string.Empty,
        VlessServer vl => vl.Path ?? vl.ServiceName ?? string.Empty,
        TrojanServer tr => tr.Path ?? tr.ServiceName ?? string.Empty,
        _ => string.Empty
    };

    private static string Sni(Server s) => s switch
    {
        VMessServer vm => vm.Sni ?? string.Empty,
        VlessServer vl => vl.Sni ?? string.Empty,
        TrojanServer tr => tr.Sni ?? string.Empty,
        Hysteria2Server hy => hy.Sni ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>True when the text contains a line starting with <c>proxies:</c>.</summary>
    private static bool LooksLikeClash(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("proxies:", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    ///     Heuristic: a Base64 subscription blob has no "://" and consists only of Base64 characters.
    /// </summary>
    private static bool LooksLikeBase64(string text)
    {
        if (text.Contains("://"))
            return false;
        if (text.Length < 8)
            return false;
        return Base64Regex().IsMatch(text);
    }

    [GeneratedRegex(@"^[A-Za-z0-9+/=_\-\s]+$")]
    private static partial Regex Base64Regex();
}
