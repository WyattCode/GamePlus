using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.Rules;

/// <summary>
///     Single-game rule export / import (Game+). The export is a one-game wrapper in the same shape
///     as the bundled rules file (<see cref="GameRulesFile" /> / <see cref="RulesExporter" />), so
///     the wrapper itself is a valid GameRulesFile (schema 1, version 1) and import validation
///     reuses <see cref="GameRulesFile.Parse" />. Rules ids can't start with "custom-", so custom
///     games are exported with a "user-" id prefix (stripped again on import). Besides the pretty
///     JSON text, the export can be carried as a compact share string:
///     "gameplus-rule:v1:" + base64url (no padding) of the UTF-8 compact JSON.
/// </summary>
public static class SingleGameRule
{
    /// <summary>Value of the required "format" field.</summary>
    public const string Format = "gameplus-game-rule";

    /// <summary>Prefix of the share-string form.</summary>
    public const string SharePrefix = "gameplus-rule:v1:";

    public const int Schema = GameRulesFile.SupportedSchema;

    public const int Version = 1;

    /// <summary>Upper bound on the payload — enforced on the raw text and on decoded share bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    private const string Unrecognized = "无法识别的规则内容（需要 Game+ 规则 JSON 或 gameplus-rule: 分享串）";
    private const string Corrupted = "规则分享串已损坏";
    private const string TooLarge = "规则内容过大";

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     The id written into an export: rules ids can't start with "custom-", so a custom game's
    ///     "custom-…" id becomes "user-…". Built-in ids are written unchanged.
    /// </summary>
    public static string ExportId(Game g)
    {
        var id = g.Id ?? "";
        return id.StartsWith("custom-", StringComparison.Ordinal)
            ? "user-" + id["custom-".Length..]
            : id;
    }

    /// <summary>
    ///     The library id of an imported game: "custom-" + the incoming id with a leading "user-"
    ///     stripped, cut to 64 characters (the rules-file id length limit).
    /// </summary>
    public static string TargetCustomId(string incomingId)
    {
        var id = (incomingId ?? "").Trim();
        if (id.StartsWith("user-", StringComparison.Ordinal))
            id = id["user-".Length..];
        var target = "custom-" + id;
        return target.Length > 64 ? target[..64] : target;
    }

    /// <summary>Pretty-printed UTF-8 JSON of the single-game wrapper (camelCase envelope, PascalCase game).</summary>
    public static string ToJson(Game g, DateTime nowUtc) => BuildJson(g, nowUtc, PrettyOptions);

    /// <summary>"gameplus-rule:v1:" + base64url (no padding) of the UTF-8 compact JSON.</summary>
    public static string ToShareString(Game g, DateTime nowUtc) =>
        SharePrefix + ToUrlBase64(Encoding.UTF8.GetBytes(BuildJson(g, nowUtc, CompactOptions)));

    /// <summary>
    ///     Parses a Game+ rule payload — either the JSON text or the share string (whitespace
    ///     trimmed). Returns the single game. Throws <see cref="InvalidOperationException" /> with a
    ///     Chinese, user-facing message; game-level validation errors come from
    ///     <see cref="GameRulesFile.Parse" /> unchanged.
    /// </summary>
    public static Game Parse(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException(Unrecognized);
        if (Encoding.UTF8.GetByteCount(trimmed) > MaxBytes)
            throw new InvalidOperationException(TooLarge);

        var fromShare = trimmed.StartsWith(SharePrefix, StringComparison.Ordinal);
        var json = fromShare ? DecodeSharePayload(trimmed[SharePrefix.Length..]) : trimmed;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(fromShare ? Corrupted : Unrecognized);
        }

        if (root is null)
            throw new InvalidOperationException(Unrecognized);

        // The wrapper must declare the Game+ single-game format and carry exactly one game;
        // everything else (schema/version/game validation) is delegated to GameRulesFile.Parse.
        var formatOk = root["format"] is JsonValue format && format.TryGetValue<string>(out var f)
                       && string.Equals(f, Format, StringComparison.Ordinal);
        if (!formatOk || root["games"] is not JsonArray games || games.Count != 1)
            throw new InvalidOperationException(Unrecognized);

        var file = GameRulesFile.Parse(root.ToJsonString(CompactOptions), out var error);
        if (file is null)
            throw new InvalidOperationException(error ?? Unrecognized);

        return file.Games[0];
    }

    /// <summary>Builds the wrapper JSON. The game node matches RulesExporter's game shape exactly.</summary>
    private static string BuildJson(Game g, DateTime nowUtc, JsonSerializerOptions options)
    {
        var file = new JsonObject
        {
            ["format"] = Format,
            ["schema"] = Schema,
            ["version"] = Version,
            ["exportedAt"] = nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["games"] = new JsonArray(GameNode(g))
        };
        return file.ToJsonString(options);
    }

    /// <summary>
    ///     Serializes the game with the same field names/shape as the rules exporter (PascalCase,
    ///     Category as a string, no IsCustom/IsFavorite) — with the export id written into Id.
    /// </summary>
    private static JsonObject GameNode(Game g)
    {
        return new JsonObject
        {
            ["Id"] = ExportId(g),
            ["Name"] = g.Name,
            ["EnglishName"] = g.EnglishName,
            ["Aliases"] = StringList(g.Aliases),
            ["Icon"] = g.Icon,
            ["AccentColor"] = g.AccentColor,
            ["Category"] = g.Category.ToString(),
            ["Regions"] = StringList(g.Regions),
            ["Platforms"] = StringList(g.Platforms),
            ["ProxyDomains"] = StringList(g.ProxyDomains),
            ["ProxyIps"] = StringList(g.ProxyIps),
            ["ProxyProcesses"] = StringList(g.ProxyProcesses),
            ["DownloadDomains"] = StringList(g.DownloadDomains)
        };
    }

    private static JsonArray StringList(List<string> list) =>
        new(list.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());

    /// <summary>Decodes the base64url (no padding) payload of a share string.</summary>
    private static string DecodeSharePayload(string payload)
    {
        byte[] bytes;
        try
        {
            var std = payload.Replace('-', '+').Replace('_', '/');
            switch (std.Length % 4)
            {
                case 2: std += "=="; break;
                case 3: std += "="; break;
            }
            bytes = Convert.FromBase64String(std);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(Corrupted);
        }

        if (bytes.Length > MaxBytes)
            throw new InvalidOperationException(TooLarge);

        return Encoding.UTF8.GetString(bytes);
    }

    private static string ToUrlBase64(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
