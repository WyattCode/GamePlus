namespace Netch.Core;

/// <summary>
///     Resolves the per-user application data directory (<c>%AppData%\Game+</c>) and, on first access,
///     migrates data from the previous <c>NetchModern</c> folder so a rename never loses the user's
///     servers / games / favourites / settings.
/// </summary>
public static class AppPaths
{
    private const string FolderName = "Game+";
    private const string LegacyFolderName = "NetchModern";

    /// <summary>The app data directory. Guaranteed to exist. Legacy data is migrated in on first use.</summary>
    public static string DataDirectory { get; } = ResolveAndMigrate();

    private static string ResolveAndMigrate()
    {
        var root = ResolveRoot();
        var dir = Path.Combine(root, FolderName);
        var legacy = Path.Combine(root, LegacyFolderName);

        try
        {
            // First run after the rename: move the whole old folder over (atomic on the same volume).
            // Only ever migrate absolute paths — a relative root would move an unrelated folder in the CWD.
            if (Path.IsPathRooted(dir) && Path.IsPathRooted(legacy) &&
                !Directory.Exists(dir) && Directory.Exists(legacy))
                Directory.Move(legacy, dir);
        }
        catch
        {
            // Best-effort: if migration fails (folder locked, cross-volume, ...), fall through and start fresh.
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    ///     Resolves the roaming app-data root. <see cref="Environment.GetFolderPath(Environment.SpecialFolder)" />
    ///     can return an empty string (e.g. on non-Windows when the folder does not exist), and
    ///     <c>Path.Combine("", "Game+")</c> would then be a path relative to the current directory. Fall back
    ///     to the app base directory in that case.
    /// </summary>
    private static string ResolveRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(AppContext.BaseDirectory, "data");

        return root;
    }
}
