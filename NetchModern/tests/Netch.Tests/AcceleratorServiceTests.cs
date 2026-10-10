using Netch.Core.Services;
using Netch.Core.Enums;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;

namespace Netch.Tests;

public class AcceleratorServiceTests
{
    private sealed class FakeServerController : IServerController
    {
        public string Name => "fake";
        public int HttpPort => 2802;
        public int PreferredSocksPort { get; set; }
        public IReadOnlyCollection<string>? ProxyDomains { get; set; }
        public IReadOnlyCollection<string>? ProxyIps { get; set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public Task<Socks5Server> StartAsync(Server server, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new Socks5Server { Hostname = "127.0.0.1", Port = 2801 });
        }
        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
        public Task<(long Uplink, long Downlink)> QueryTrafficAsync(CancellationToken ct = default) =>
            Task.FromResult((0L, 0L));
    }

    private sealed class FakeSystemProxy : ISystemProxyService
    {
        public bool IsEnabled { get; private set; }
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public void Enable(string host, int port) { IsEnabled = true; Host = host; Port = port; }
        public void Disable() => IsEnabled = false;
        public void RecoverFromCrash() { /* no-op for tests */ }
    }

    /// <summary>Mode controller whose StartAsync can be made to fail (simulates a missing driver / no admin).</summary>
    private sealed class FakeModeController : IModeController
    {
        private readonly bool _fail;
        public FakeModeController(ModeType type, bool fail)
        {
            Type = type;
            _fail = fail;
        }

        public ModeType Type { get; }
        public string Name => $"fake-{Type}";
        public ModeFeature Features => ModeFeature.None;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task StartAsync(Socks5Server socks5, Mode mode, CancellationToken ct = default)
        {
            StartCalls++;
            if (_fail)
                throw new InvalidOperationException($"{Type} capture unavailable");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModeControllerFactory : IModeControllerFactory
    {
        private readonly bool _failProcess;
        private readonly bool _failTun;
        public FakeModeControllerFactory(bool failProcess = false, bool failTun = false)
        {
            _failProcess = failProcess;
            _failTun = failTun;
        }

        public FakeModeController? Last { get; private set; }

        public IModeController Create(ModeType type)
        {
            var fail = type == ModeType.ProcessMode ? _failProcess : _failTun;
            Last = new FakeModeController(type, fail);
            return Last;
        }
    }

    /// <summary>In-memory baseline store used to exercise the measurement policy.</summary>
    private sealed class FakeBaselineStore : IBaselineStore
    {
        public Dictionary<string, StoredBaseline> Baselines { get; } = new();
        public HashSet<string> Remeasure { get; } = new();
        public int SaveCalls { get; private set; }

        public StoredBaseline? Get(string gameId) =>
            Baselines.TryGetValue(gameId, out var b) ? b : null;

        public void Save(string gameId, StoredBaseline baseline)
        {
            SaveCalls++;
            Baselines[gameId] = baseline;
        }

        public bool ConsumeRemeasure(string gameId) => Remeasure.Remove(gameId);

        public void MarkRemeasure(string gameId) => Remeasure.Add(gameId);
    }

    private sealed class FakeEffectProbe : IEffectProbe
    {
        public int DirectValue { get; set; } = 120;
        public int SocksValue { get; set; } = 74;
        public int DirectCalls { get; private set; }

        public Task<int> DirectAsync(string host, int port, CancellationToken ct)
        {
            DirectCalls++;
            return Task.FromResult(DirectValue);
        }

        public Task<int> ViaSocksAsync(string socksHost, int socksPort, string host, int port, CancellationToken ct) =>
            Task.FromResult(SocksValue);
    }

    private static Game MakeGame(params string[] domains) =>
        new() { Id = "test-game", Name = "Test Game", ProxyDomains = domains.ToList() };

    private static ShadowsocksServer MakeServer() =>
        new() { Hostname = "example.com", Port = 8388, Remark = "test" };

    private static AcceleratorService MakeAccelerator(FakeServerController controller, FakeSystemProxy proxy,
        IModeControllerFactory? modeFactory = null, Func<Game, string>? captureModeFor = null)
    {
        var log = new LogService();
        return new AcceleratorService(log, _ => controller, proxy, modeFactory ?? new FakeModeControllerFactory(),
            null, null, null, null, captureModeFor);
    }

    private static AcceleratorService MakeAcceleratorWithEffect(FakeServerController controller, FakeSystemProxy proxy,
        EffectMonitor effect, IBaselineStore? baselines, Func<AdvancedSettings>? advanced)
    {
        var log = new LogService();
        return new AcceleratorService(log, _ => controller, proxy, new FakeModeControllerFactory(),
            effect, null, advanced, baselines);
    }

    [Fact]
    public async Task StartAsync_WithDomainRules_StartsProxyAndSetsSystemProxy()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var accel = MakeAccelerator(controller, proxy);
        var game = MakeGame("game.example.com");
        var server = MakeServer();

        await accel.StartAsync(game, server);

        // Domain-only game → system-proxy routing: proxy enabled at the controller's local HTTP port.
        Assert.Equal(new[] { "game.example.com" }, controller.ProxyDomains);
        Assert.True(proxy.IsEnabled);
        Assert.Equal("127.0.0.1", proxy.Host);
        Assert.Equal(controller.HttpPort, proxy.Port);
        Assert.Equal(AccelerationState.Accelerating, accel.State);
        Assert.Equal(1, controller.StartCalls);
        Assert.Equal("proxy", accel.Status.CaptureMode);
    }

    [Fact]
    public async Task StartAsync_WithNoRoutingRules_ThrowsAndCleansUp()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var accel = MakeAccelerator(controller, proxy);
        var game = new Game { Id = "g", Name = "G" }; // no domains/IPs/processes
        var server = MakeServer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => accel.StartAsync(game, server));

        // A failed start must leave the accelerator clean: idle and no stray system proxy.
        Assert.Equal(AccelerationState.Idle, accel.State);
        Assert.False(proxy.IsEnabled);
        Assert.Equal(AccelerationState.Idle, accel.State);
    }

    [Fact]
    public async Task StopAsync_ReturnsToIdleAndRestoresSystemProxy()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var accel = MakeAccelerator(controller, proxy);
        var game = MakeGame("game.example.com");
        var server = MakeServer();

        await accel.StartAsync(game, server);
        Assert.Equal(AccelerationState.Accelerating, accel.State);

        await accel.StopAsync();

        Assert.Equal(AccelerationState.Idle, accel.State);
        Assert.False(proxy.IsEnabled);
        Assert.Null(accel.CurrentNode);
        Assert.Null(accel.CurrentGame);
        Assert.Equal(1, controller.StopCalls);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent_WhenAlreadyAccelerating()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var accel = MakeAccelerator(controller, proxy);
        var game = MakeGame("game.example.com");
        var server = MakeServer();

        await accel.StartAsync(game, server);
        // Second start while accelerating is a no-op — must not spawn a second controller.
        await accel.StartAsync(game, server);

        Assert.Equal(1, controller.StartCalls);
        Assert.Equal(AccelerationState.Accelerating, accel.State);
    }

    [Fact]
    public async Task StartAsync_ProcessCaptureFails_WithDomains_FallsBackToSystemProxy()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory(failProcess: true);
        var accel = MakeAccelerator(controller, proxy, modeFactory);
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyProcesses = new List<string> { "game.exe" },
            ProxyDomains = new List<string> { "game.example.com" }
        };
        var server = MakeServer();

        await accel.StartAsync(game, server);

        Assert.Equal(AccelerationState.Accelerating, accel.State);
        Assert.True(proxy.IsEnabled);
        Assert.Equal("127.0.0.1", proxy.Host);
        Assert.Equal(controller.HttpPort, proxy.Port);
        // Controller restarted with domain rules (global start + restart = 2 calls).
        Assert.Equal(2, controller.StartCalls);
        Assert.Equal(new[] { "game.example.com" }, controller.ProxyDomains);
        Assert.Equal("proxy", accel.Status.CaptureMode);
    }

    [Fact]
    public async Task StartAsync_ProcessCaptureFails_WithIps_UsesTun()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory(failProcess: true);
        var accel = MakeAccelerator(controller, proxy, modeFactory);
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyProcesses = new List<string> { "game.exe" },
            ProxyIps = new List<string> { "1.2.3.4" }
        };
        var server = MakeServer();

        await accel.StartAsync(game, server);

        Assert.Equal(AccelerationState.Accelerating, accel.State);
        Assert.Equal("tun", accel.Status.CaptureMode);
        Assert.False(proxy.IsEnabled);
        Assert.Equal(ModeType.TunMode, modeFactory.Last!.Type);
    }

    [Fact]
    public async Task StartAsync_ProcessCaptureFails_NoIpsNoDomains_ThrowsAndCleansUp()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory(failProcess: true);
        var accel = MakeAccelerator(controller, proxy, modeFactory);
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyProcesses = new List<string> { "game.exe" }
        };
        var server = MakeServer();

        await Assert.ThrowsAsync<InvalidOperationException>(() => accel.StartAsync(game, server));

        Assert.Equal(AccelerationState.Idle, accel.State);
        Assert.False(proxy.IsEnabled);
        Assert.Equal(1, controller.StopCalls);
        Assert.Null(accel.Status.CaptureMode);
    }

    [Fact]
    public async Task StartAsync_ProcessCaptureSucceeds_UsesProcessMode()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory();
        var accel = MakeAccelerator(controller, proxy, modeFactory);
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyProcesses = new List<string> { "game.exe" }
        };
        var server = MakeServer();

        await accel.StartAsync(game, server);

        Assert.Equal(AccelerationState.Accelerating, accel.State);
        Assert.Equal("process", accel.Status.CaptureMode);
        Assert.False(proxy.IsEnabled);
        Assert.Equal(ModeType.ProcessMode, modeFactory.Last!.Type);
    }

    [Fact]
    public async Task StartAsync_ForcedProcess_UsesOnlyProcessMode_NoFallback()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory(failProcess: true);
        var accel = MakeAccelerator(controller, proxy, modeFactory, _ => "process");
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyProcesses = new List<string> { "game.exe" },
            ProxyIps = new List<string> { "1.2.3.4" },
            ProxyDomains = new List<string> { "game.example.com" }
        };
        var server = MakeServer();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => accel.StartAsync(game, server));

        Assert.Contains("进程模式", ex.Message);
        Assert.Equal(AccelerationState.Idle, accel.State);
        Assert.False(proxy.IsEnabled);
        // No fallback to TUN or system proxy.
        Assert.Equal("", accel.Status.CaptureMode ?? "");
    }

    [Fact]
    public async Task StartAsync_ForcedTun_NoIps_ThrowsAvailabilityError_NeverStartsController()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory();
        var accel = MakeAccelerator(controller, proxy, modeFactory, _ => "tun");
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyDomains = new List<string> { "game.example.com" }
        };
        var server = MakeServer();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => accel.StartAsync(game, server));

        Assert.Contains("TUN", ex.Message);
        Assert.Equal(0, controller.StartCalls);
        Assert.Equal(AccelerationState.Idle, accel.State);
    }

    [Fact]
    public async Task StartAsync_ForcedProxy_EnablesSystemProxyWithProxyCaptureMode()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var modeFactory = new FakeModeControllerFactory();
        var accel = MakeAccelerator(controller, proxy, modeFactory, _ => "proxy");
        var game = new Game
        {
            Id = "g",
            Name = "G",
            ProxyDomains = new List<string> { "game.example.com" }
        };
        var server = MakeServer();

        await accel.StartAsync(game, server);

        Assert.Equal(AccelerationState.Accelerating, accel.State);
        Assert.Equal("proxy", accel.Status.CaptureMode);
        Assert.True(proxy.IsEnabled);
        Assert.Equal("127.0.0.1", proxy.Host);
        Assert.Equal(controller.HttpPort, proxy.Port);
        Assert.Equal(new[] { "game.example.com" }, controller.ProxyDomains);
    }

    [Fact]
    public async Task StartAsync_FirstTime_MeasuresAndSavesBaseline()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var probe = new FakeEffectProbe();
        var effect = new EffectMonitor(probe, sampleInterval: Timeout.InfiniteTimeSpan);
        var store = new FakeBaselineStore();
        var accel = MakeAcceleratorWithEffect(controller, proxy, effect, store, null);
        var game = MakeGame("game.example.com");

        await accel.StartAsync(game, MakeServer());

        Assert.Equal(5, probe.DirectCalls);
        Assert.Equal(1, store.SaveCalls);
        Assert.True(store.Baselines.ContainsKey("test-game"));
        Assert.Equal(120, store.Baselines["test-game"].Latency);
        Assert.False(effect.Report().BaselineFromCache);
        await accel.StopAsync();
    }

    [Fact]
    public async Task StartAsync_SecondTime_ReusesStoredBaseline()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var probe = new FakeEffectProbe();
        var effect = new EffectMonitor(probe, sampleInterval: Timeout.InfiniteTimeSpan);
        var store = new FakeBaselineStore();
        store.Baselines["test-game"] = new StoredBaseline
        {
            Latency = 130, Loss = 0, Jitter = 0, Samples = 5,
            Target = "game.example.com:443", MeasuredUtc = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var accel = MakeAcceleratorWithEffect(controller, proxy, effect, store, null);
        var game = MakeGame("game.example.com");

        await accel.StartAsync(game, MakeServer());

        Assert.Equal(0, probe.DirectCalls);
        var report = effect.Report();
        Assert.True(report.BaselineFromCache);
        Assert.NotNull(report.Baseline);
        Assert.Equal(130, report.Baseline!.Latency);
        await accel.StopAsync();
    }

    [Fact]
    public async Task StartAsync_MeasureEffectTrue_AlwaysMeasures()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var probe = new FakeEffectProbe();
        var effect = new EffectMonitor(probe, sampleInterval: Timeout.InfiniteTimeSpan);
        var store = new FakeBaselineStore();
        store.Baselines["test-game"] = new StoredBaseline
        {
            Latency = 130, Loss = 0, Jitter = 0, Samples = 5,
            Target = "game.example.com:443", MeasuredUtc = DateTime.UtcNow
        };
        var adv = new AdvancedSettings { MeasureEffect = true };
        var accel = MakeAcceleratorWithEffect(controller, proxy, effect, store, () => adv);
        var game = MakeGame("game.example.com");

        await accel.StartAsync(game, MakeServer());

        Assert.Equal(5, probe.DirectCalls);
        Assert.False(effect.Report().BaselineFromCache);
        await accel.StopAsync();
    }

    [Fact]
    public async Task StartAsync_RemeasureFlag_MeasuresOnceAndIsConsumed()
    {
        var controller = new FakeServerController();
        var proxy = new FakeSystemProxy();
        var probe = new FakeEffectProbe();
        var effect = new EffectMonitor(probe, sampleInterval: Timeout.InfiniteTimeSpan);
        var store = new FakeBaselineStore();
        store.Baselines["test-game"] = new StoredBaseline
        {
            Latency = 130, Loss = 0, Jitter = 0, Samples = 5,
            Target = "game.example.com:443", MeasuredUtc = DateTime.UtcNow
        };
        store.Remeasure.Add("test-game");
        var accel = MakeAcceleratorWithEffect(controller, proxy, effect, store, null);
        var game = MakeGame("game.example.com");

        await accel.StartAsync(game, MakeServer());
        Assert.Equal(5, probe.DirectCalls);
        Assert.False(store.Remeasure.Contains("test-game"));
        await accel.StopAsync();

        // Second start: flag consumed, stored baseline reused.
        await accel.StartAsync(game, MakeServer());
        Assert.Equal(5, probe.DirectCalls);
        await accel.StopAsync();
    }
}
