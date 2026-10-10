using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.Rules;

/// <summary>
///     Root object of a remote game-rules file (see docs/game-rules.md). The file uses the same
///     System.Text.Json shape as custom-games.json (PascalCase properties, enum Category as string or
///     number), so it can be deserialized straight into <see cref="Game" />.
/// </summary>
public sealed class GameRulesFile
{
    /// <summary>Only schema version understood by this build.</summary>
    public const int SupportedSchema = 1;

    /// <summary>Upper bound on the number of games in one file (guards against absurd payloads).</summary>
    public const int MaxGames = 2000;

    /// <summary>Upper bound on the number of entries in any single list field.</summary>
    public const int MaxListEntries = 500;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);

    public int Schema { get; set; }

    public int Version { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public List<Game> Games { get; set; } = new();

    /// <summary>
    ///     Parses and validates <paramref name="json" />. Returns null and sets <paramref name="error" />
    ///     (Chinese, user-facing) when the file is malformed or fails validation.
    /// </summary>
    public static GameRulesFile? Parse(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "规则文件内容为空";
            return null;
        }

        GameRulesFile? file;
        try
        {
            file = JsonSerializer.Deserialize<GameRulesFile>(json, Options);
        }
        catch (JsonException e)
        {
            error = $"规则文件不是有效的 JSON：{e.Message}";
            return null;
        }

        if (file is null)
        {
            error = "规则文件内容为空";
            return null;
        }

        if (file.Schema != SupportedSchema)
        {
            error = $"不支持的规则文件版本（schema={file.Schema}，当前支持 {SupportedSchema}）";
            return null;
        }

        if (file.Version <= 0)
        {
            error = "规则文件缺少有效的 version（必须为正整数）";
            return null;
        }

        if (file.Games is null || file.Games.Count == 0)
        {
            error = "规则文件不包含任何游戏";
            return null;
        }

        if (file.Games.Count > MaxGames)
        {
            error = $"规则文件包含的游戏过多（{file.Games.Count} 个，最多 {MaxGames} 个）";
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < file.Games.Count; i++)
        {
            var game = file.Games[i];
            var index = i + 1;

            if (game is null)
            {
                error = $"第 {index} 个游戏为空";
                return null;
            }

            var id = game.Id ?? "";
            if (!IdPattern.IsMatch(id))
            {
                error = $"第 {index} 个游戏的 Id 无效：{id}（只能包含小写字母、数字、. _ -，且以字母或数字开头）";
                return null;
            }

            if (id.StartsWith("custom-", StringComparison.Ordinal))
            {
                error = $"第 {index} 个游戏的 Id 无效：{id}（不能以 custom- 开头）";
                return null;
            }

            if (!seen.Add(id))
            {
                error = $"第 {index} 个游戏的 Id 重复：{id}";
                return null;
            }

            var name = game.Name ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                error = $"第 {index} 个游戏缺少名称";
                return null;
            }

            if (name.Length > 64)
            {
                error = $"第 {index} 个游戏的名称过长（最多 64 个字符）：{name}";
                return null;
            }

            if (CheckList(game.Aliases, index, "Aliases", out error) is false) return null;
            if (CheckList(game.Regions, index, "Regions", out error) is false) return null;
            if (CheckList(game.Platforms, index, "Platforms", out error) is false) return null;
            if (CheckList(game.ProxyDomains, index, "ProxyDomains", out error) is false) return null;
            if (CheckList(game.ProxyIps, index, "ProxyIps", out error) is false) return null;
            if (CheckList(game.ProxyProcesses, index, "ProxyProcesses", out error) is false) return null;
            if (CheckList(game.DownloadDomains, index, "DownloadDomains", out error) is false) return null;

            foreach (var proc in game.ProxyProcesses ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(proc))
                {
                    error = $"第 {index} 个游戏的 ProxyProcesses 包含空项";
                    return null;
                }

                if (!proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"第 {index} 个游戏的进程名必须以 .exe 结尾：{proc}";
                    return null;
                }

                if (proc.Contains('\\') || proc.Contains('/'))
                {
                    error = $"第 {index} 个游戏的进程名不能包含路径分隔符：{proc}";
                    return null;
                }
            }

            foreach (var ip in game.ProxyIps ?? new List<string>())
            {
                if (!IsIpOrCidr(ip))
                {
                    error = $"第 {index} 个游戏的 IP 无效：{ip}";
                    return null;
                }
            }

            foreach (var domain in game.ProxyDomains ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    error = $"第 {index} 个游戏的 ProxyDomains 包含空项";
                    return null;
                }

                if (domain.Any(char.IsWhiteSpace))
                {
                    error = $"第 {index} 个游戏的域名不能包含空白字符：{domain}";
                    return null;
                }
            }

            foreach (var domain in game.DownloadDomains ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(domain))
                {
                    error = $"第 {index} 个游戏的 DownloadDomains 包含空项";
                    return null;
                }

                if (domain.Any(char.IsWhiteSpace))
                {
                    error = $"第 {index} 个游戏的域名不能包含空白字符：{domain}";
                    return null;
                }
            }
        }

        // IsCustom / IsFavorite in the file are ignored — force them off so a remote file can never
        // inject a "custom" game (which would be persisted back to custom-games.json).
        foreach (var game in file.Games)
        {
            game.IsCustom = false;
            game.IsFavorite = false;
        }

        return file;
    }

    private static bool CheckList(List<string>? list, int index, string field, out string? error)
    {
        error = null;
        if (list is null)
            return true;

        if (list.Count > MaxListEntries)
        {
            error = $"第 {index} 个游戏的 {field} 条目过多（{list.Count} 条，最多 {MaxListEntries} 条）";
            return false;
        }

        return true;
    }

    private static bool IsIpOrCidr(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var slash = value.IndexOf('/');
        if (slash < 0)
            return IPAddress.TryParse(value, out _);

        var address = value[..slash];
        var prefix = value[(slash + 1)..];
        if (!IPAddress.TryParse(address, out var parsed))
            return false;
        if (!int.TryParse(prefix, out var bits))
            return false;

        var max = parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return bits >= 0 && bits <= max;
    }
}
