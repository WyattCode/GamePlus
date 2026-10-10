using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Netch.Core.Services.Library;

/// <summary>
///     Scans Steam libraries for installed games. Reads <c>libraryfolders.vdf</c> (new and old formats)
///     plus every <c>appmanifest_*.acf</c> in each library's <c>steamapps</c> folder.
/// </summary>
public sealed class SteamScanner : IGameScanner
{
    private const string DefaultSteamRoot = @"C:\Program Files (x86)\Steam";

    // Steamworks Common Redistributables — not a game.
    private const string RedistributablesAppId = "228980";

    private readonly string? _steamRoot;

    public SteamScanner(string? steamRoot) => _steamRoot = steamRoot;

    public string Name => "Steam";

    public IEnumerable<InstalledGame> Scan(CancellationToken ct)
    {
        var root = ResolveRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var libraries = new List<string>();
        var seenLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddLibrary(libraries, seenLibs, root);

        foreach (var lib in ReadLibraryFolders(root))
            AddLibrary(libraries, seenLibs, lib);

        foreach (var lib in libraries)
        {
            ct.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            IEnumerable<string> manifests;
            try
            {
                manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var manifest in manifests)
            {
                ct.ThrowIfCancellationRequested();
                var game = ReadManifest(manifest, steamApps);
                if (game is not null)
                    yield return game;
            }
        }
    }

    private string? ResolveRoot()
    {
        if (!string.IsNullOrWhiteSpace(_steamRoot))
            return _steamRoot;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path && !string.IsNullOrWhiteSpace(path))
                    return path;
            }
            catch
            {
                // ignore — fall through to the default
            }
        }

        return DefaultSteamRoot;
    }

    private static void AddLibrary(List<string> libraries, HashSet<string> seen, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var normalized = path.Trim().TrimEnd('\\', '/');
        if (normalized.Length == 0)
            return;
        if (seen.Add(normalized))
            libraries.Add(normalized);
    }

    private static IEnumerable<string> ReadLibraryFolders(string root)
    {
        var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file))
            yield break;

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            yield break;
        }

        VdfNode parsed;
        try
        {
            parsed = VdfParser.Parse(text);
        }
        catch
        {
            yield break;
        }

        // New format: "libraryfolders" { "0" { "path" "D:\\SteamLibrary" ... } ... }
        var newRoot = parsed.GetNode("libraryfolders");
        if (newRoot is not null)
        {
            foreach (var (_, node) in newRoot.NodeEntries())
            {
                var path = node.GetString("path");
                if (!string.IsNullOrWhiteSpace(path))
                    yield return path;
            }
        }

        // Old format: "LibraryFolders" { "1" "D:\\SteamLibrary" ... }
        var oldRoot = parsed.GetNode("LibraryFolders");
        if (oldRoot is not null)
        {
            foreach (var key in oldRoot.Keys)
            {
                var value = oldRoot.GetString(key);
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    private static InstalledGame? ReadManifest(string manifestPath, string steamApps)
    {
        string text;
        try
        {
            text = File.ReadAllText(manifestPath);
        }
        catch
        {
            return null;
        }

        VdfNode parsed;
        try
        {
            parsed = VdfParser.Parse(text);
        }
        catch
        {
            return null;
        }

        var state = parsed.GetNode("AppState");
        if (state is null)
            return null;

        var appId = state.GetString("appid")?.Trim();
        var name = state.GetString("name")?.Trim();
        var installDir = state.GetString("installdir")?.Trim();

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
            return null;
        if (appId == RedistributablesAppId)
            return null;
        if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Proton", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
            return null;

        string? fullDir = null;
        if (!string.IsNullOrWhiteSpace(installDir))
            fullDir = Path.Combine(steamApps, "common", installDir);

        var executables = ExecutableFinder.Find(fullDir);
        return new InstalledGame("Steam", $"steam:{appId}", name, fullDir, executables);
    }
}
