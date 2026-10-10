using Netch.Core.Models.Games;
using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class GameMatcherTests
{
    private static Game MakeGame(string id, string name, string englishName = "",
        GameCategory category = GameCategory.Pc, params string[] processes)
    {
        return new Game
        {
            Id = id,
            Name = name,
            EnglishName = englishName,
            Category = category,
            ProxyProcesses = processes.ToList()
        };
    }

    private static InstalledGame Installed(string source, string sourceId, string name, params string[] exes) =>
        new(source, sourceId, name, null, exes);

    [Fact]
    public void MatchesByProcess()
    {
        var library = new[] { MakeGame("cs2", "反恐精英2", "Counter-Strike 2", GameCategory.Pc, "cs2.exe") };
        var installed = new[] { Installed("Steam", "steam:730", "Counter-Strike 2", "cs2.exe") };

        var result = GameMatcher.Match(installed, library);

        Assert.Single(result);
        Assert.Equal("cs2", result[0].GameId);
        Assert.Equal("进程", result[0].MatchedBy);
    }

    [Fact]
    public void MatchesByNormalizedName()
    {
        var library = new[] { MakeGame("cs2", "反恐精英2", "Counter-Strike 2") };
        var installed = new[] { Installed("Steam", "steam:730", "counter strike 2") };

        var result = GameMatcher.Match(installed, library);

        Assert.Single(result);
        Assert.Equal("cs2", result[0].GameId);
        Assert.Equal("名称", result[0].MatchedBy);
    }

    [Fact]
    public void DoesNotMatchPlatformByName()
    {
        var library = new[] { MakeGame("steam", "Steam", "Steam", GameCategory.Platform) };
        var installed = new[] { Installed("Steam", "steam:1", "Steam") };

        var result = GameMatcher.Match(installed, library);

        Assert.Single(result);
        Assert.Null(result[0].GameId);
    }

    [Fact]
    public void UnmatchedStaysUnmatched()
    {
        var library = new[] { MakeGame("cs2", "反恐精英2", "Counter-Strike 2", GameCategory.Pc, "cs2.exe") };
        var installed = new[] { Installed("Steam", "steam:999", "Some Random Game", "random.exe") };

        var result = GameMatcher.Match(installed, library);

        Assert.Single(result);
        Assert.Null(result[0].GameId);
        Assert.Null(result[0].MatchedBy);
    }

    [Fact]
    public void DedupesBySourceId()
    {
        var library = new[] { MakeGame("cs2", "反恐精英2", "Counter-Strike 2", GameCategory.Pc, "cs2.exe") };
        var installed = new[]
        {
            Installed("Steam", "steam:730", "Counter-Strike 2", "cs2.exe"),
            Installed("Steam", "steam:730", "Counter-Strike 2", "cs2.exe")
        };

        var result = GameMatcher.Match(installed, library);
        Assert.Single(result);
    }

    [Fact]
    public void DedupesMultipleInstalledForSameLibraryGame()
    {
        var library = new[] { MakeGame("cs2", "反恐精英2", "Counter-Strike 2", GameCategory.Pc, "cs2.exe") };
        var installed = new[]
        {
            Installed("其他", "uninstall:CS2", "Counter-Strike 2", "cs2.exe"),
            Installed("Steam", "steam:730", "Counter-Strike 2", "cs2.exe")
        };

        var result = GameMatcher.Match(installed, library);

        Assert.Single(result);
        Assert.Equal("steam:730", result[0].Installed.SourceId);
    }
}
