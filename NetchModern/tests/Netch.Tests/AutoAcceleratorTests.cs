using Netch.Core.Models.Games;
using Netch.Core.Services.AutoAccel;

namespace Netch.Tests;

public class AutoAcceleratorTests
{
    private sealed class FakeProcessSource : IProcessSource
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> RunningProcessNames() => Names;
    }

    private sealed class Harness
    {
        public FakeProcessSource Source { get; } = new();
        public List<string> Started { get; } = new();
        public int StopCount { get; private set; }
        public AccelerationState State { get; set; } = AccelerationState.Idle;
        public string? CurrentGameId { get; set; }
        public Exception? StartError { get; set; }
        public List<AutoAccelEvent> Events { get; } = new();

        public AutoAccelerator Build()
        {
            var watcher = new GameProcessWatcher(Source);
            var accel = new AutoAccelerator(
                watcher,
                gameId =>
                {
                    if (StartError is not null)
                        throw StartError;
                    Started.Add(gameId);
                    State = AccelerationState.Accelerating;
                    CurrentGameId = gameId;
                    return Task.CompletedTask;
                },
                () =>
                {
                    StopCount++;
                    State = AccelerationState.Idle;
                    CurrentGameId = null;
                    return Task.CompletedTask;
                },
                () => State,
                () => CurrentGameId);
            accel.Event += e => Events.Add(e);
            return accel;
        }
    }

    private static Game GameWith(string id, params string[] processes) => new()
    {
        Id = id,
        Name = id,
        ProxyProcesses = processes.ToList()
    };

    [Fact]
    public async Task AutoStartsOnLaunch()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);

        Assert.Equal(new[] { "g1" }, h.Started);
        Assert.Contains(h.Events, e => e.Kind == "started" && e.GameId == "g1");
    }

    [Fact]
    public async Task AutoStopsOnExit()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);
        h.Source.Names.Clear();
        await accel.TickAsync(games, true);

        Assert.Equal(1, h.StopCount);
        Assert.Contains(h.Events, e => e.Kind == "stopped" && e.GameId == "g1");
    }

    [Fact]
    public async Task ManualStartIsNotStopped()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        // User manually started acceleration before the game launched.
        h.State = AccelerationState.Accelerating;
        h.CurrentGameId = "g1";

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);
        h.Source.Names.Clear();
        await accel.TickAsync(games, true);

        Assert.Empty(h.Started);
        Assert.Equal(0, h.StopCount);
    }

    [Fact]
    public async Task BusyRaisesSkipped()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        h.State = AccelerationState.Accelerating;
        h.CurrentGameId = "other";

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);

        Assert.Empty(h.Started);
        Assert.Contains(h.Events, e => e.Kind == "skipped" && e.GameId == "g1");
    }

    [Fact]
    public async Task FailureRaisesFailedAndDoesNotRetryUntilRelaunch()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };
        h.StartError = new InvalidOperationException("boom");

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);
        await accel.TickAsync(games, true);

        Assert.Contains(h.Events, e => e.Kind == "failed" && e.GameId == "g1" && e.Message == "boom");
        Assert.Empty(h.Started);

        // Relaunch: exit then launch again → retry.
        h.StartError = null;
        h.Source.Names.Clear();
        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);

        Assert.Equal(new[] { "g1" }, h.Started);
    }

    [Fact]
    public async Task ManualStopWhileRunningDoesNotRestart()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        await accel.TickAsync(games, true);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, true);
        Assert.Equal(new[] { "g1" }, h.Started);

        // User stops acceleration manually while the game is still running.
        h.State = AccelerationState.Idle;
        h.CurrentGameId = null;
        await accel.TickAsync(games, true);
        await accel.TickAsync(games, true);

        Assert.Equal(new[] { "g1" }, h.Started);
    }

    [Fact]
    public async Task DisabledMasterSwitchDoesNothing()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        await accel.TickAsync(games, false);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, false);

        Assert.Empty(h.Started);
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task ReEnablingDoesNotFireForAlreadyRunningGame()
    {
        var h = new Harness();
        var accel = h.Build();
        var games = new[] { GameWith("g1", "game.exe") };

        // Disabled while the game launches — the watcher still advances.
        await accel.TickAsync(games, false);
        h.Source.Names.Add("game.exe");
        await accel.TickAsync(games, false);

        // Re-enable: the game is already running, so no launch edge fires.
        await accel.TickAsync(games, true);

        Assert.Empty(h.Started);
    }
}
