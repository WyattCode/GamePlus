using System.Text.RegularExpressions;
using Netch.Core.Services.Library;

namespace Netch.Core.Services.Launch;

/// <summary>
///     Validates and normalizes per-game launch targets for 「加速并启动」. Only three forms are
///     accepted: a <c>steam://rungameid/&lt;appid&gt;</c> link, an Epic launcher link, or an absolute
///     path to an existing <c>.exe</c> file. Anything else is rejected with the user-facing message.
/// </summary>
public static class LaunchTargetValidator
{
    private const string InvalidMessage = "启动方式无效：只支持 Steam/Epic 启动链接或本机 .exe 路径";

    private const int MaxPathLength = 260;

    private static readonly Regex SteamLinkRegex =
        new(@"^steam://rungameid/(\d{1,10})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpicLinkRegex =
        new(@"^com\.epicgames\.launcher://apps/([^?]+)\?action=launch&silent=true$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EpicAppIdRegex =
        new(@"^[A-Za-z0-9%:._-]{1,200}$", RegexOptions.Compiled);

    private static readonly Regex DrivePathRegex = new(@"^[A-Za-z]:[\\/]", RegexOptions.Compiled);

    /// <summary>
    ///     Normalizes a raw launch target. <c>null</c>/empty/whitespace → <c>null</c> (clears the
    ///     target). Returns the trimmed target, or throws <see cref="InvalidOperationException" /> with
    ///     the Chinese error message. <paramref name="fileExists" /> is injectable for tests; the
    ///     existence of an <c>.exe</c> target is checked here (and again when launching).
    /// </summary>
    public static string? Normalize(string? raw, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        // Control characters (newlines etc.) are never valid, even at the ends; Trim() would hide them.
        if (raw!.Any(char.IsControl))
            throw new InvalidOperationException(InvalidMessage);

        if (SteamLinkRegex.IsMatch(trimmed))
            return trimmed;

        var epicMatch = EpicLinkRegex.Match(trimmed);
        if (epicMatch.Success && EpicAppIdRegex.IsMatch(epicMatch.Groups[1].Value))
            return trimmed;

        if (IsExecutablePath(trimmed))
        {
            if (!fileExists(trimmed))
                throw new InvalidOperationException(InvalidMessage);
            return trimmed;
        }

        throw new InvalidOperationException(InvalidMessage);
    }

    /// <summary>
    ///     Suggests launch targets for an installed game found by the library scan: Steam entries map
    ///     to a rungameid link, Epic entries to the launcher link, everything else to the existing
    ///     executables inside its install directory. Empty when nothing usable is known.
    /// </summary>
    public static IReadOnlyList<string> SuggestFrom(InstalledGame g, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var results = new List<string>();
        var sourceId = g.SourceId ?? "";

        if (sourceId.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            var appId = sourceId["steam:".Length..].Trim();
            if (Regex.IsMatch(appId, @"^\d{1,10}$"))
                results.Add($"steam://rungameid/{appId}");
            return results;
        }

        if (sourceId.StartsWith("epic:", StringComparison.OrdinalIgnoreCase))
        {
            var appName = sourceId["epic:".Length..].Trim();
            if (appName.Length > 0 && appName.Length <= 200 && EpicAppIdRegex.IsMatch(appName))
                results.Add($"com.epicgames.launcher://apps/{appName}?action=launch&silent=true");
            return results;
        }

        if (!string.IsNullOrWhiteSpace(g.InstallDir))
        {
            foreach (var exe in g.Executables)
            {
                if (string.IsNullOrWhiteSpace(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                var full = Path.Combine(g.InstallDir, exe);
                if (fileExists(full))
                    results.Add(full);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Shape checks for an absolute Windows <c>.exe</c> path (existence checked by the caller).</summary>
    private static bool IsExecutablePath(string value)
    {
        if (value.Length > MaxPathLength)
            return false;
        if (value.StartsWith(@"\\"))
            return false;
        if (!DrivePathRegex.IsMatch(value))
            return false;
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var ch in value)
        {
            if (char.IsControl(ch) || ch == '"' || ch == '<' || ch == '>' || ch == '|' || ch == '*' || ch == '?')
                return false;
        }

        return true;
    }
}
