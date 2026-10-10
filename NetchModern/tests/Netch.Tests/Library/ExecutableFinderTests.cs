using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class ExecutableFinderTests : IDisposable
{
    private readonly string _dir;

    public ExecutableFinderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netch-exe-" + Guid.NewGuid().ToString("N"));
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
    public void ExcludesInstallersAndCrashHandlers()
    {
        File.WriteAllText(Path.Combine(_dir, "game.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "unins000.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "UnityCrashHandler64.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "setup.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "vc_redist.x64.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "dxsetup.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "GameLauncher.exe"), "");

        var found = ExecutableFinder.Find(_dir);

        Assert.Contains("game.exe", found);
        Assert.Contains("GameLauncher.exe", found);
        Assert.DoesNotContain("unins000.exe", found);
        Assert.DoesNotContain("UnityCrashHandler64.exe", found);
        Assert.DoesNotContain("setup.exe", found);
        Assert.DoesNotContain("vc_redist.x64.exe", found);
        Assert.DoesNotContain("dxsetup.exe", found);
    }

    [Fact]
    public void RespectsDepthLimit()
    {
        var deep = Path.Combine(_dir, "a", "b", "c");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "deep.exe"), "");

        var found = ExecutableFinder.Find(_dir, maxDepth: 2);
        Assert.DoesNotContain("deep.exe", found);
    }

    [Fact]
    public void MissingDirReturnsEmpty()
    {
        Assert.Empty(ExecutableFinder.Find(Path.Combine(Path.GetTempPath(), "netch-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ReturnsDistinctNames()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_dir, "game.exe"), "");
        File.WriteAllText(Path.Combine(sub, "game.exe"), "");

        var found = ExecutableFinder.Find(_dir);
        Assert.Single(found.Where(f => f == "game.exe"));
    }
}
