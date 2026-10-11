using System.ComponentModel;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Netch.Core.Services.History;

namespace Netch.Tests;

public class AccelHistoryTests : IDisposable
{
    private readonly string _dir;

    public AccelHistoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "netch-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static AccelSession Session(string id, string gameId, DateTime startedAt, int durationSec = 60) =>
        new(id, gameId, "Game " + gameId, "香港 01", "process", null,
            startedAt, startedAt.AddSeconds(durationSec), durationSec, 120, 70, 42, "参考延迟降低 42%");

    [Fact]
    public void Store_RoundTrips()
    {
        var path = PathFor("history.json");
        var store = new JsonAccelHistoryStore(path);
        store.Add(Session("a", "g1", new DateTime(2025, 1, 1, 10, 0, 0)));
        store.Add(Session("b", "g2", new DateTime(2025, 1, 1, 11, 0, 0)));

        var reloaded = new JsonAccelHistoryStore(path);
        var list = reloaded.List(null, 50);
        Assert.Equal(2, list.Count);
        Assert.Equal("b", list[0].Id);
        Assert.Equal("a", list[1].Id);
        Assert.Equal("香港 01", list[0].NodeName);
        Assert.Equal(42, list[0].LatencyPct);
    }

    [Fact]
    public void Store_NewestFirst()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        store.Add(Session("old", "g1", new DateTime(2025, 1, 1, 8, 0, 0)));
        store.Add(Session("new", "g1", new DateTime(2025, 1, 1, 9, 0, 0)));
        var list = store.List(null, 50);
        Assert.Equal("new", list[0].Id);
        Assert.Equal("old", list[1].Id);
    }

    [Fact]
    public void Store_CapsAt500()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        for (var i = 0; i < 520; i++)
            store.Add(Session("s" + i, "g1", new DateTime(2025, 1, 1).AddSeconds(i)));

        var list = store.List(null, 1000);
        Assert.Equal(500, list.Count);
        Assert.Equal("s519", list[0].Id);
    }

    [Fact]
    public void Store_FiltersByGame()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        store.Add(Session("a", "g1", new DateTime(2025, 1, 1, 10, 0, 0)));
        store.Add(Session("b", "g2", new DateTime(2025, 1, 1, 11, 0, 0)));
        store.Add(Session("c", "g1", new DateTime(2025, 1, 1, 12, 0, 0)));

        var g1 = store.List("g1", 50);
        Assert.Equal(2, g1.Count);
        Assert.All(g1, s => Assert.Equal("g1", s.GameId));
    }

    [Fact]
    public void Store_ClearByGameAndAll()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        store.Add(Session("a", "g1", new DateTime(2025, 1, 1, 10, 0, 0)));
        store.Add(Session("b", "g2", new DateTime(2025, 1, 1, 11, 0, 0)));
        store.Add(Session("c", "g1", new DateTime(2025, 1, 1, 12, 0, 0)));

        store.Clear("g1");
        var afterGame = store.List(null, 50);
        Assert.Single(afterGame);
        Assert.Equal("g2", afterGame[0].GameId);

        store.Clear(null);
        Assert.Empty(store.List(null, 50));
    }

    [Fact]
    public void Store_CorruptFileIsEmpty()
    {
        var path = PathFor("history.json");
        File.WriteAllText(path, "{ this is not valid json ]");
        var store = new JsonAccelHistoryStore(path);
        Assert.Empty(store.List(null, 50));

        // And it recovers: adding after a corrupt load works.
        store.Add(Session("a", "g1", new DateTime(2025, 1, 1, 10, 0, 0)));
        Assert.Single(store.List(null, 50));
    }

    [Fact]
    public void Recorder_RecordsCompletedSession()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        var accel = new FakeAccelerator();
        var now = new DateTime(2025, 1, 1, 10, 0, 0);
        using var recorder = new AccelHistoryRecorder(accel, store, () => now, _ => "intl");

        accel.Raise(AccelerationState.Accelerating);
        now = now.AddSeconds(90);
        accel.Raise(AccelerationState.Stopping);

        var list = store.List(null, 50);
        Assert.Single(list);
        var s = list[0];
        Assert.Equal("g1", s.GameId);
        Assert.Equal("Game g1", s.GameName);
        Assert.Equal("香港 01", s.NodeName);
        Assert.Equal("process", s.Mode);
        Assert.Equal("intl", s.ServerChoice);
        Assert.Equal(90, s.DurationSec);
        Assert.Equal(120, s.BaselineMs);
        Assert.Equal(70, s.AcceleratedMs);
        Assert.Equal(42, s.LatencyPct);
        Assert.Equal("参考延迟降低 42%", s.Summary);
    }

    [Fact]
    public void Recorder_SkipsShortSessions()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        var accel = new FakeAccelerator();
        var now = new DateTime(2025, 1, 1, 10, 0, 0);
        using var recorder = new AccelHistoryRecorder(accel, store, () => now, _ => null);

        accel.Raise(AccelerationState.Accelerating);
        now = now.AddSeconds(3);
        accel.Raise(AccelerationState.Idle);

        Assert.Empty(store.List(null, 50));
    }

    [Fact]
    public void Recorder_SkipsFailedStart()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        var accel = new FakeAccelerator();
        var now = new DateTime(2025, 1, 1, 10, 0, 0);
        using var recorder = new AccelHistoryRecorder(accel, store, () => now, _ => null);

        // Connecting → Idle (failed start) must record nothing.
        accel.Raise(AccelerationState.Connecting);
        now = now.AddSeconds(30);
        accel.Raise(AccelerationState.Idle);

        Assert.Empty(store.List(null, 50));
    }

    [Fact]
    public void Recorder_RecordsServerChoiceFromLookup()
    {
        var store = new JsonAccelHistoryStore(PathFor("history.json"));
        var accel = new FakeAccelerator();
        var now = new DateTime(2025, 1, 1, 10, 0, 0);
        using var recorder = new AccelHistoryRecorder(accel, store, () => now, _ => "cn");

        accel.Raise(AccelerationState.Accelerating);
        now = now.AddSeconds(60);
        accel.Raise(AccelerationState.Stopping);

        var list = store.List(null, 50);
        Assert.Single(list);
        Assert.Equal("cn", list[0].ServerChoice);
    }

    [Fact]
    public void Recorder_StoreThrows_DoesNotThrowAndLogsWarning()
    {
        var accel = new FakeAccelerator();
        var now = new DateTime(2025, 1, 1, 10, 0, 0);
        var log = new FakeLogService();
        using var recorder = new AccelHistoryRecorder(
            accel, new ThrowingStore(), () => now, _ => "intl", log);

        accel.Raise(AccelerationState.Accelerating);
        now = now.AddSeconds(90);
        // Must not throw out of the StateChanged handler.
        accel.Raise(AccelerationState.Stopping);

        var entry = Assert.Single(log.Recent);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("failed to record session", entry.Message);
    }

    [Fact]
    public void AppSettings_GameServerChoice_RoundTripsThroughJson()
    {
        var settings = new AppSettings();
        settings.GameServerChoice["g1"] = "cn";
        settings.GameServerChoice["g2"] = "intl";

        var json = JsonSerializer.Serialize(settings);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.GameServerChoice.Count);
        Assert.Equal("cn", reloaded.GameServerChoice["g1"]);
        Assert.Equal("intl", reloaded.GameServerChoice["g2"]);
    }

    private sealed class ThrowingStore : IAccelHistoryStore
    {
        public IReadOnlyList<AccelSession> List(string? gameId, int limit) => Array.Empty<AccelSession>();
        public void Add(AccelSession s) => throw new InvalidOperationException("disk full");
        public void Clear(string? gameId) { }
    }

    private sealed class FakeLogService : ILogService
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Recent => _entries;

        public event EventHandler<LogEntry>? EntryAdded;

        public void Log(LogLevel level, string message)
        {
            var entry = new LogEntry(DateTime.Now, level, message);
            _entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
        }

        public void Clear() => _entries.Clear();
    }

    private sealed class FakeAccelerator : IAcceleratorService
    {
        public AccelerationState State { get; private set; } = AccelerationState.Idle;
        public Game? CurrentGame { get; } = new() { Id = "g1", Name = "Game g1" };
        public Server? CurrentNode { get; } = new Socks5Server { Remark = "香港 01" };
        public AccelerationStatus Status { get; } = new() { CaptureMode = "process" };

        public event EventHandler<AccelerationState>? StateChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(AccelerationState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }

        public Task StartAsync(Game game, Server node, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public EffectReport? EffectReport() => new(
            true, "game.example.com:443",
            new EffectSnapshot(120, 0, 4, 5),
            new EffectSnapshot(70, 0, 3, 5),
            42, "参考延迟降低 42%",
            Array.Empty<EffectPoint>(), false, null, false);
    }
}
