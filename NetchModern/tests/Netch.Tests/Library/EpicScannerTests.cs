using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class EpicScannerTests : IDisposable
{
    private readonly string _dir;

    public EpicScannerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netch-epic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void FindsInstalledGamesAndSkipsIncomplete()
    {
        var install = Path.Combine(_dir, "Fortnite");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "FortniteLauncher.exe"), "");

        File.WriteAllText(Path.Combine(_dir, "fortnite.item"), $$"""
            {
                "DisplayName": "Fortnite",
                "AppName": "Fortnite",
                "InstallLocation": "{{install.Replace("\\", "\\\\")}}",
                "LaunchExecutable": "FortniteLauncher.exe",
                "bIsApplication": true,
                "bIsIncompleteInstall": false
            }
            """);

        File.WriteAllText(Path.Combine(_dir, "incomplete.item"), """
            {
                "DisplayName": "Half Installed",
                "AppName": "HalfInstalled",
                "InstallLocation": "C:\\Nope",
                "LaunchExecutable": "nope.exe",
                "bIsApplication": true,
                "bIsIncompleteInstall": true
            }
            """);

        var scanner = new EpicScanner(_dir);
        var games = scanner.Scan(CancellationToken.None).ToList();

        Assert.Single(games);
        Assert.Equal("epic:Fortnite", games[0].SourceId);
        Assert.Equal("Fortnite", games[0].Name);
        Assert.Contains("FortniteLauncher.exe", games[0].Executables);
    }

    [Fact]
    public void MissingDirYieldsNothing()
    {
        var scanner = new EpicScanner(Path.Combine(Path.GetTempPath(), "netch-missing-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(scanner.Scan(CancellationToken.None));
    }
}
