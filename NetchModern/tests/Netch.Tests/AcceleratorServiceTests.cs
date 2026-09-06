using Netch.Core.Services;
using Netch.Core.Enums;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;

namespace Netch.Tests;

public class AcceleratorServiceTests
{
    private sealed class FakeServerController : IServerController
    {
        public string Name => "fake";
        public int HttpPort => 2802;
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
    }

    private static Game MakeGame(params string[] domains) =>
        new() { Id = "test-game", Name = "Test Game", ProxyDomains = domains.ToList() };

    private static ShadowsocksServer MakeServer() =>
        new() { Hostname = "example.com", Port = 8388, Remark = "test" };

    private static AcceleratorService MakeAccelerator(FakeServerController controller, FakeSystemProxy proxy)
    {
        var log = new LogService();
        return new AcceleratorService(log, _ => controller, proxy);
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
}
