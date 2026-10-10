using System.Text;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.Library;

/// <summary>An installed game paired with the library game it matched (if any).</summary>
public record DetectedGame(
    InstalledGame Installed,
    string? GameId,
    string? GameName,
    string? MatchedBy);

/// <summary>
///     Matches installed games to the built-in rule library by process name (preferred) or normalized name.
/// </summary>
public static class GameMatcher
{
    /// <summary>
    ///     Minimum length of a normalized name key. Shorter keys (e.g. "ys", "瓦", "wf") are too generic and
    ///     cause false positives, so they are never indexed nor looked up.
    /// </summary>
    private const int MinNameKeyLength = 3;

    /// <summary>Source preference when deduping multiple installed entries for the same library game.</summary>
    private static int SourceRank(string source) => source switch
    {
        "Steam" => 0,
        "Epic" => 1,
        _ => 2
    };

    public static IReadOnlyList<DetectedGame> Match(
        IEnumerable<InstalledGame> installed,
        IReadOnlyList<Game> library)
    {
        var results = new List<DetectedGame>();
        var seenSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Process-name index: exe name → library game (first wins).
        var byProcess = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in library)
            foreach (var proc in game.ProxyProcesses)
                if (!string.IsNullOrWhiteSpace(proc) && !byProcess.ContainsKey(proc))
                    byProcess[proc] = game;

        // Normalized-name index (skip platform entries — Steam/Epic themselves).
        var byName = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in library)
        {
            if (game.Category == GameCategory.Platform)
                continue;
            foreach (var candidate in NameCandidates(game))
            {
                var key = Normalize(candidate);
                if (key.Length >= MinNameKeyLength && !byName.ContainsKey(key))
                    byName[key] = game;
            }
        }

        // Prefer Steam/Epic entries over uninstall entries when several match the same game.
        var ordered = installed
            .OrderBy(g => SourceRank(g.Source))
            .ToList();

        foreach (var item in ordered)
        {
            if (!seenSourceIds.Add(item.SourceId))
                continue;

            Game? matched = null;
            string? matchedBy = null;

            foreach (var exe in item.Executables)
            {
                if (byProcess.TryGetValue(exe, out var g))
                {
                    matched = g;
                    matchedBy = "进程";
                    break;
                }
            }

            if (matched is null)
            {
                var key = Normalize(item.Name);
                if (key.Length >= MinNameKeyLength && byName.TryGetValue(key, out var g))
                {
                    matched = g;
                    matchedBy = "名称";
                }
            }

            if (matched is not null)
            {
                if (!claimedGameIds.Add(matched.Id))
                    continue; // another installed entry already claimed this library game
            }

            results.Add(new DetectedGame(item, matched?.Id, matched?.Name, matchedBy));
        }

        return results;
    }

    private static IEnumerable<string> NameCandidates(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.Name))
            yield return game.Name;
        if (!string.IsNullOrWhiteSpace(game.EnglishName))
            yield return game.EnglishName;
        foreach (var alias in game.Aliases)
            if (!string.IsNullOrWhiteSpace(alias))
                yield return alias;
    }

    /// <summary>
    ///     Normalizes a name for comparison: lower-case, strips spaces, punctuation, ™®© and separators.
    /// </summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }
}
