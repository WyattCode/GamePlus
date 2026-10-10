namespace Netch.Core.Services.Library;

/// <summary>
///     Best-effort discovery of game executables inside an install directory. Never throws — permission
///     errors and missing directories are skipped.
/// </summary>
public static class ExecutableFinder
{
    private static readonly string[] ExcludePatterns =
    {
        "unins*", "*crash*", "*setup*", "*install*", "vc_redist*", "dxsetup*", "UnityCrashHandler*"
    };

    /// <summary>
    ///     Returns distinct <c>*.exe</c> file names found at the top level of <paramref name="dir" /> and up
    ///     to <paramref name="maxDepth" /> levels below it, excluding installers / crash handlers / setups.
    /// </summary>
    public static IReadOnlyList<string> Find(string? dir, int maxDepth = 2, int max = 20)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Walk(dir, 0, maxDepth, max, result, seen);
        }
        catch
        {
            // ignore — return whatever we collected
        }

        return result;
    }

    private static void Walk(string dir, int depth, int maxDepth, int max,
        List<string> result, HashSet<string> seen)
    {
        if (result.Count >= max)
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            if (result.Count >= max)
                return;
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name) || IsExcluded(name))
                continue;
            if (seen.Add(name))
                result.Add(name);
        }

        if (depth >= maxDepth)
            return;

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(dir);
        }
        catch
        {
            return;
        }

        foreach (var sub in subdirs)
        {
            if (result.Count >= max)
                return;
            Walk(sub, depth + 1, maxDepth, max, result, seen);
        }
    }

    private static bool IsExcluded(string name)
    {
        foreach (var pattern in ExcludePatterns)
            if (Matches(name, pattern))
                return true;
        return false;
    }

    /// <summary>Case-insensitive wildcard match supporting a single leading/trailing '*'.</summary>
    private static bool Matches(string name, string pattern)
    {
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            var mid = pattern.Trim('*');
            return name.Contains(mid, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*'))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);

        if (pattern.EndsWith('*'))
            return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
