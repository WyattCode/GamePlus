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
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(root, FolderName);
        var legacy = Path.Combine(root, LegacyFolderName);

        try
        {
            // First run after the rename: move the whole old folder over (atomic on the same volume).
            if (!Directory.Exists(dir) && Directory.Exists(legacy))
                Directory.Move(legacy, dir);
        }
        catch
        {
            // Best-effort: if migration fails (folder locked, cross-volume, ...), fall through and start fresh.
        }

        Directory.CreateDirectory(dir);
        return dir;
    }
}
