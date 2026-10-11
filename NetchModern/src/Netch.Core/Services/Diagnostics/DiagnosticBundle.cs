using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Netch.Core.Utils;

namespace Netch.Core.Services.Diagnostics;

/// <summary>
///     Deep-copies a JSON tree and replaces anything that looks like a credential with "***".
///     Used by the one-click diagnostic bundle so a shared zip never leaks passwords, UUIDs,
///     tokens or subscription links.
/// </summary>
public static class ConfigSanitizer
{
    private static readonly string[] SensitiveFragments =
    {
        "password", "passwd", "pwd", "secret", "token", "key", "auth",
        "uuid", "userid", "id_token", "psk", "privatekey", "publickey",
        "shortid", "cookie"
    };

    private static readonly string[] LinkFragments = { "link", "url", "subscription" };

    private static readonly string[] ShareSchemes =
    {
        "ss", "vmess", "vless", "trojan", "hysteria2", "socks", "tg"
    };

    /// <summary>Returns a sanitized deep copy of <paramref name="node" /> (never the same instance).</summary>
    public static JsonNode? Sanitize(JsonNode? node)
    {
        return Sanitize(node, null);
    }

    /// <summary>
    ///     Returns a sanitized deep copy of <paramref name="node" /> (never the same instance).
    ///     When <paramref name="sensitiveHosts" /> is supplied, every occurrence of those
    ///     hostnames/IPs is replaced with <c>&lt;node&gt;</c> in string values.
    /// </summary>
    public static JsonNode? Sanitize(JsonNode? node, IReadOnlyCollection<string>? sensitiveHosts)
    {
        if (node is null)
            return null;

        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var kv in obj)
                {
                    var name = kv.Key;
                    var value = kv.Value;

                    if (IsSensitiveName(name))
                    {
                        result[name] = RedactValue(value);
                        continue;
                    }

                    if (IsLinkName(name))
                    {
                        result[name] = RedactLinkValue(value, sensitiveHosts);
                        continue;
                    }

                    result[name] = Sanitize(value, sensitiveHosts);
                }
                return result;
            }
            case JsonArray arr:
            {
                var result = new JsonArray();
                foreach (var item in arr)
                    result.Add(Sanitize(item, sensitiveHosts));
                return result;
            }
            case JsonValue val:
            {
                if (val.TryGetValue<string>(out var s))
                {
                    if (LooksLikeUrl(s))
                        return JsonValue.Create(RedactString(s));
                    return JsonValue.Create(LogSanitizer.Sanitize(s, sensitiveHosts));
                }
                return JsonValue.Create(val.GetValue<object?>());
            }
            default:
                return null;
        }
    }

    private static bool IsSensitiveName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        var lower = name.ToLowerInvariant();
        foreach (var frag in SensitiveFragments)
        {
            if (lower.Contains(frag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsLinkName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        var lower = name.ToLowerInvariant();
        foreach (var frag in LinkFragments)
        {
            if (lower.Contains(frag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static JsonNode? RedactValue(JsonNode? value)
    {
        if (value is null)
            return null;
        if (value is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s))
                return string.IsNullOrEmpty(s) ? JsonValue.Create(s) : JsonValue.Create("***");
            // Non-string sensitive values (numbers/bools) are still masked to avoid leaking ids.
            return JsonValue.Create("***");
        }
        return JsonValue.Create("***");
    }

    private static JsonNode? RedactLinkValue(JsonNode? value, IReadOnlyCollection<string>? sensitiveHosts)
    {
        if (value is null)
            return null;
        if (value is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (string.IsNullOrEmpty(s))
                return JsonValue.Create(s);
            return JsonValue.Create(LooksLikeUrl(s) ? RedactString(s) : LogSanitizer.Sanitize(s, sensitiveHosts));
        }
        return Sanitize(value, sensitiveHosts);
    }

    private static bool LooksLikeUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var idx = s.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0)
            return false;
        var scheme = s[..idx].ToLowerInvariant();
        if (scheme is "http" or "https")
            return true;
        foreach (var share in ShareSchemes)
        {
            if (scheme == share)
                return true;
        }
        return false;
    }

    private static string RedactString(string s)
    {
        var idx = s.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0)
            return "…";
        var scheme = s[..idx].ToLowerInvariant();
        if (scheme is "http" or "https")
            return SecretRedactor.RedactUrl(s);
        return SecretRedactor.RedactShareLink(s);
    }
}

/// <summary>
///     Scrubs URLs and query-like credential pairs out of a single log line before it is written
///     into a diagnostic bundle.
/// </summary>
public static class LogSanitizer
{
    private static readonly Regex UrlRegex =
        new(@"\b[a-z][a-z0-9+.-]*://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PairRegex =
        new(@"(?<key>\b(?:password|passwd|pwd|token|key|secret|auth)\b\s*=\s*)(?<val>[^\s&""']+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns <paramref name="line" /> with URLs and credential pairs redacted.</summary>
    public static string Sanitize(string line)
    {
        return Sanitize(line, null);
    }

    /// <summary>
    ///     Returns <paramref name="line" /> with URLs and credential pairs redacted. When
    ///     <paramref name="sensitiveHosts" /> is supplied, every occurrence (case-insensitive) of
    ///     those hostnames/IPs is replaced with <c>&lt;node&gt;</c>; longer hosts are replaced
    ///     first so suffixes do not shadow them.
    /// </summary>
    public static string Sanitize(string line, IReadOnlyCollection<string>? sensitiveHosts)
    {
        if (string.IsNullOrEmpty(line))
            return line ?? string.Empty;

        var result = UrlRegex.Replace(line, m => RedactMatch(m.Value));
        result = PairRegex.Replace(result, m => m.Groups["key"].Value + "***");

        if (sensitiveHosts is { Count: > 0 })
        {
            var hosts = sensitiveHosts
                .Where(h => !string.IsNullOrWhiteSpace(h) && h.Trim().Length >= 3)
                .Select(h => h.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(h => h.Length);
            foreach (var host in hosts)
                result = result.Replace(host, "<node>", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string RedactMatch(string url)
    {
        var idx = url.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0)
            return "…";
        var scheme = url[..idx].ToLowerInvariant();
        if (scheme is "http" or "https")
            return SecretRedactor.RedactUrl(url);
        return SecretRedactor.RedactShareLink(url);
    }
}

/// <summary>Everything the diagnostic bundle builder needs; assembled by the caller under its state lock.</summary>
public sealed record BundleInput(
    string AppVersion,
    string HostVersion,
    IReadOnlyDictionary<string, string> Environment,
    JsonNode? Settings,
    IReadOnlyList<object> Servers,
    IReadOnlyList<object> Subscriptions,
    IReadOnlyList<string> LogLines,
    IReadOnlyList<(string Name, string Content)> CrashFiles,
    object? Diagnostics,
    JsonNode? Client,
    IReadOnlyCollection<string>? SensitiveHosts = null);

/// <summary>
///     Builds the one-click diagnostic zip entirely in memory. The caller (Electron main) is
///     responsible for writing the returned bytes to a user-chosen path.
/// </summary>
public static class DiagnosticBundleBuilder
{
    private const int MaxLogLines = 5000;
    private const int MaxCrashFiles = 10;
    private const int MaxCrashFileBytes = 512 * 1024;
    private const long MaxTotalUncompressed = 20L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Builds the zip and returns its bytes.</summary>
    public static byte[] Build(BundleInput input, DateTime now)
    {
        var hosts = input.SensitiveHosts;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var budget = MaxTotalUncompressed;

            // README first so it is always present.
            AddText(zip, "README.txt", Readme(now), ref budget);

            var info = new JsonObject
            {
                ["generatedAt"] = now.ToString("o", CultureInfo.InvariantCulture),
                ["appVersion"] = input.AppVersion,
                ["hostVersion"] = input.HostVersion
            };
            var envObj = new JsonObject();
            foreach (var kv in input.Environment)
                envObj[kv.Key] = kv.Value;
            info["environment"] = envObj;
            AddText(zip, "info.json", info.ToJsonString(JsonOptions), ref budget);

            AddText(zip, "config/settings.json",
                ConfigSanitizer.Sanitize(input.Settings, hosts)?.ToJsonString(JsonOptions) ?? "null", ref budget);

            AddText(zip, "config/servers.json",
                JsonSerializer.Serialize(input.Servers, JsonOptions), ref budget);

            AddText(zip, "config/subscriptions.json",
                JsonSerializer.Serialize(input.Subscriptions, JsonOptions), ref budget);

            // Logs: newest last, capped at MaxLogLines. Truncate first when the budget is tight.
            var logs = input.LogLines;
            if (logs.Count > MaxLogLines)
                logs = logs.Skip(logs.Count - MaxLogLines).ToList();

            var logText = BuildLogText(logs, hosts);
            if (Encoding.UTF8.GetByteCount(logText) > budget)
            {
                // Drop the oldest lines until it fits (or nothing is left).
                var kept = logs.ToList();
                while (kept.Count > 0 && Encoding.UTF8.GetByteCount(BuildLogText(kept, hosts)) > budget)
                    kept.RemoveAt(0);
                logText = BuildLogText(kept, hosts);
            }
            AddText(zip, "logs/backend.log", logText, ref budget);

            var crashCount = 0;
            foreach (var crash in input.CrashFiles)
            {
                if (crashCount >= MaxCrashFiles)
                    break;
                var content = BuildLogText((crash.Content ?? string.Empty).Split('\n'), hosts);
                if (Encoding.UTF8.GetByteCount(content) > MaxCrashFileBytes)
                    content = TruncateUtf8(content, MaxCrashFileBytes);
                var name = SafeName(crash.Name);
                if (name.Length == 0)
                    continue;
                AddText(zip, "crash/" + name, content, ref budget);
                crashCount++;
            }

            AddText(zip, "diagnostics.json", BuildDiagnosticsText(input.Diagnostics, hosts), ref budget);

            AddText(zip, "client/client.json",
                ConfigSanitizer.Sanitize(input.Client, hosts)?.ToJsonString(JsonOptions) ?? "null", ref budget);
        }

        return ms.ToArray();
    }

    private static string BuildDiagnosticsText(object? diagnostics, IReadOnlyCollection<string>? sensitiveHosts)
    {
        if (diagnostics is null)
            return "null";

        var raw = JsonSerializer.Serialize(diagnostics, JsonOptions);
        try
        {
            var node = JsonNode.Parse(raw);
            return ConfigSanitizer.Sanitize(node, sensitiveHosts)?.ToJsonString(JsonOptions) ?? "null";
        }
        catch (JsonException)
        {
            return LogSanitizer.Sanitize(raw, sensitiveHosts);
        }
    }

    private static string BuildLogText(IReadOnlyList<string> lines, IReadOnlyCollection<string>? sensitiveHosts)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.Append(LogSanitizer.Sanitize(line, sensitiveHosts)).Append('\n');
        return sb.ToString();
    }

    private static void AddText(ZipArchive zip, string entryName, string content, ref long budget)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        if (bytes.LongLength > budget)
        {
            if (budget <= 0)
                return;
            bytes = Encoding.UTF8.GetBytes(TruncateUtf8(content ?? string.Empty, (int)Math.Min(budget, int.MaxValue)));
        }

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
        budget -= bytes.LongLength;
    }

    private static string TruncateUtf8(string text, int maxBytes)
    {
        if (maxBytes <= 0)
            return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
            return text;
        // Trim to a valid UTF-8 boundary.
        var len = maxBytes;
        while (len > 0 && (bytes[len] & 0xC0) == 0x80)
            len--;
        return Encoding.UTF8.GetString(bytes, 0, len);
    }

    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var trimmed = name.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Readme(DateTime now) =>
        "Game+ 诊断包 / Diagnostic bundle\n" +
        "================================\n\n" +
        $"生成时间 / Generated: {now:yyyy-MM-dd HH:mm:ss}\n\n" +
        "包含内容 / Contents:\n" +
        "  - README.txt            本说明 / this file\n" +
        "  - info.json             版本与环境信息 / versions + environment\n" +
        "  - config/settings.json  脱敏后的设置 / sanitized settings\n" +
        "  - config/servers.json   线路摘要（不含地址与凭据）/ server summary (no host/credentials)\n" +
        "  - config/subscriptions.json 订阅摘要（链接已脱敏）/ subscription summary (links redacted)\n" +
        "  - logs/backend.log      脱敏后的日志（最近 5000 行，节点地址已替换为 <node>）/ sanitized logs (last 5000 lines, node addresses replaced with <node>)\n" +
        "  - crash/                崩溃报告（最多 10 个）/ crash reports (max 10)\n" +
        "  - diagnostics.json      诊断结果 / diagnostics result\n" +
        "  - client/client.json    客户端信息 / client info\n\n" +
        "隐私说明 / Privacy:\n" +
        "  密码、密钥、令牌、UUID、订阅链接等敏感信息已被移除或脱敏。\n" +
        "  Passwords, keys, tokens, UUIDs and subscription links have been removed or redacted.\n\n" +
        "分享前请自行检查本压缩包内容。\n" +
        "Please review the contents before sharing this archive.\n";
}
