using System.Text.Json;

namespace Netch.Core.Services.Library;

/// <summary>
///     Scans Epic Games Launcher manifests (<c>*.item</c> JSON files) for installed games.
/// </summary>
public sealed class EpicScanner : IGameScanner
{
    private readonly string? _manifestsDir;

    public EpicScanner(string? manifestsDir) => _manifestsDir = manifestsDir;

    public string Name => "Epic";

    public IEnumerable<InstalledGame> Scan(CancellationToken ct)
    {
        var dir = ResolveDir();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var game = ReadManifest(file);
            if (game is not null)
                yield return game;
        }
    }

    private string? ResolveDir()
    {
        if (!string.IsNullOrWhiteSpace(_manifestsDir))
            return _manifestsDir;

        var programData = Environment.GetEnvironmentVariable("ProgramData");
        if (string.IsNullOrWhiteSpace(programData))
            programData = @"C:\ProgramData";
        return Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    private static InstalledGame? ReadManifest(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (GetBool(root, "bIsIncompleteInstall") == true)
                return null;

            // Keep only application entries (skip DLC / engine / plugin entries).
            var isApp = GetBool(root, "bIsApplication");
            if (isApp == false)
                return null;

            var name = GetString(root, "DisplayName");
            var appName = GetString(root, "AppName");
            var installLocation = GetString(root, "InstallLocation");
            var launchExe = GetString(root, "LaunchExecutable");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appName))
                return null;

            var executables = new List<string>();
            if (!string.IsNullOrWhiteSpace(launchExe))
            {
                var fileName = Path.GetFileName(launchExe.Replace('\\', '/'));
                if (!string.IsNullOrWhiteSpace(fileName))
                    executables.Add(fileName);
            }

            if (executables.Count == 0)
                executables.AddRange(ExecutableFinder.Find(installLocation));

            return new InstalledGame("Epic", $"epic:{appName}", name, installLocation, executables);
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool? GetBool(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
