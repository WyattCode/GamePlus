using Netch.Core.Services;
using Netch.Core.Enums;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;

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

    private static Game MakeGame(params string[] domains) =>
        new() { Id = "test-game", Name = "Test Game", ProxyDomains = domains.ToList() };

    private static ShadowsocksServer MakeServer() =>
        new() { Hostname = "example.com", Port = 8388, Remark = "test" };

    private static AcceleratorService MakeAccelerator(FakeServerController controller, FakeSystemProxy proxy,
        IModeControllerFactory? modeFactory = null)
    {
        var log = new LogService();
        return new AcceleratorService(log, _ => controller, proxy, modeFactory ?? new FakeModeControllerFactory());
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
}
