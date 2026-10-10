using Netch.Core.Models.Games;
using Netch.Core.Services.AutoAccel;

namespace Netch.Tests;

public class GameProcessWatcherTests
{
    private sealed class FakeProcessSource : IProcessSource
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> RunningProcessNames() => Names;
    }

    private static Game GameWith(string id, params string[] processes) => new()
    {
        Id = id,
        Name = id,
        ProxyProcesses = processes.ToList()
    };

    [Fact]
    public void FirstTickIsSilent()
    {
        var source = new FakeProcessSource();
        source.Names.Add("game.exe");
        var watcher = new GameProcessWatcher(source);

        var events = watcher.Tick(new[] { GameWith("g1", "game.exe") });

        Assert.Empty(events);
    }

    [Fact]
    public void LaunchEdgeIsReported()
    {
        var source = new FakeProcessSource();
        var watcher = new GameProcessWatcher(source);
        var games = new[] { GameWith("g1", "game.exe") };

        Assert.Empty(watcher.Tick(games));
        source.Names.Add("game.exe");
        var events = watcher.Tick(games);

        var evt = Assert.Single(events);
        Assert.Equal(WatcherEventKind.Launched, evt.Kind);
        Assert.Equal("g1", evt.GameId);
    }

    [Fact]
    public void ExitEdgeIsReported()
    {
        var source = new FakeProcessSource();
        var watcher = new GameProcessWatcher(source);
        var games = new[] { GameWith("g1", "game.exe") };

        watcher.Tick(games);
        source.Names.Add("game.exe");
        watcher.Tick(games);
        source.Names.Clear();
        var events = watcher.Tick(games);

        var evt = Assert.Single(events);
        Assert.Equal(WatcherEventKind.Exited, evt.Kind);
        Assert.Equal("g1", evt.GameId);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var source = new FakeProcessSource();
        var watcher = new GameProcessWatcher(source);
        var games = new[] { GameWith("g1", "Game.EXE") };

        watcher.Tick(games);
        source.Names.Add("game.exe");
        var events = watcher.Tick(games);

        var evt = Assert.Single(events);
        Assert.Equal(WatcherEventKind.Launched, evt.Kind);
    }

    [Fact]
    public void GamesWithoutProcessesAreIgnored()
    {
        var source = new FakeProcessSource();
        var watcher = new GameProcessWatcher(source);
        var games = new[] { GameWith("g1") };

        Assert.Empty(watcher.Tick(games));
        source.Names.Add("anything.exe");
        Assert.Empty(watcher.Tick(games));
    }

    [Fact]
    public void MultipleProcessesAnyOneTriggersLaunch()
    {
        var source = new FakeProcessSource();
        var watcher = new GameProcessWatcher(source);
        var games = new[] { GameWith("g1", "a.exe", "b.exe") };

        watcher.Tick(games);
        source.Names.Add("b.exe");
        var events = watcher.Tick(games);

        Assert.Single(events);
    }
}
