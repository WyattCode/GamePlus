using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class SteamScannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _secondLib;

    public SteamScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "netch-steam-" + Guid.NewGuid().ToString("N"));
        _secondLib = Path.Combine(Path.GetTempPath(), "netch-steam2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(_secondLib, "steamapps"));
    }

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_secondLib);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void FindsGamesAcrossLibraries()
    {
        var libraryFolders = $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" "{{Escape(_root)}}"
                }
                "1"
                {
                    "path" "{{Escape(_secondLib)}}"
                }
            }
            """;
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"), libraryFolders);

        WriteManifest(_root, "730", "Counter-Strike 2", "Counter-Strike Global Offensive");
        WriteManifest(_secondLib, "570", "Dota 2", "dota 2 beta");
        WriteManifest(_root, "228980", "Steamworks Common Redistributables", "Steamworks Shared");

        var scanner = new SteamScanner(_root);
        var games = scanner.Scan(CancellationToken.None).ToList();

        Assert.Equal(2, games.Count);

        var cs2 = games.Single(g => g.SourceId == "steam:730");
        Assert.Equal("Counter-Strike 2", cs2.Name);
        Assert.Equal(Path.Combine(_root, "steamapps", "common", "Counter-Strike Global Offensive"), cs2.InstallDir);

        var dota = games.Single(g => g.SourceId == "steam:570");
        Assert.Equal(Path.Combine(_secondLib, "steamapps", "common", "dota 2 beta"), dota.InstallDir);
    }

    [Fact]
    public void SkipsCorruptManifest()
    {
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + Escape(_root) + "\" } }");
        WriteManifest(_root, "730", "Counter-Strike 2", "cs2");
        File.WriteAllText(Path.Combine(_root, "steamapps", "appmanifest_999.acf"), "this is not vdf {{{");

        var scanner = new SteamScanner(_root);
        var games = scanner.Scan(CancellationToken.None).ToList();

        Assert.Single(games);
        Assert.Equal("steam:730", games[0].SourceId);
    }

    [Fact]
    public void MissingRootYieldsNothing()
    {
        var scanner = new SteamScanner(Path.Combine(Path.GetTempPath(), "netch-missing-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(scanner.Scan(CancellationToken.None));
    }

    private static void WriteManifest(string lib, string appId, string name, string installDir)
    {
        var common = Path.Combine(lib, "steamapps", "common", installDir);
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(common, "game.exe"), "");

        var acf = $$"""
            "AppState"
            {
                "appid" "{{appId}}"
                "name" "{{name}}"
                "installdir" "{{installDir}}"
            }
            """;
        File.WriteAllText(Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf"), acf);
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
