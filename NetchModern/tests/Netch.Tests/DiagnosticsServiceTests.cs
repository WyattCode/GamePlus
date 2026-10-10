using System.Net;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Services.Diagnostics;

namespace Netch.Tests;

public class DiagnosticsServiceTests
{
    /// <summary>Configurable fake environment so the whole run can be exercised on Linux CI.</summary>
    private sealed class FakeEnv : IDiagnosticEnvironment
    {
        public bool Admin { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool DriverInstalled { get; set; }
        public DriverServiceState ServiceState { get; set; } = DriverServiceState.NotInstalled;
        public bool CanBind { get; set; } = true;
        public IPAddress[]? DnsResult { get; set; } = new[] { IPAddress.Parse("1.2.3.4") };
        public Func<Server, int> Probe { get; set; } = _ => 42;
        public string? ProxyServer { get; set; }
        public string? ProxyOverride { get; set; }
        public int ProxyEnable { get; set; }
        public HashSet<int> ListeningPorts { get; } = new();
        public bool DisableCalled { get; private set; }
        public Func<string, DiagnosticItem>? ThrowOn { get; set; }

        public string BaseDirectory => "/app";

        public bool IsAdministrator() => Admin;

        public bool FileExists(string path)
        {
            var name = Path.GetFileName(path);
            return Files.Contains(name);
        }

        public bool DriverFileInstalled() => DriverInstalled;

        public DriverServiceState NetfilterServiceState() => ServiceState;

        public bool CanBindLoopback() => CanBind;

        public Task<IPAddress[]?> ResolveAsync(string host, CancellationToken ct) =>
            Task.FromResult(DnsResult);

        public Task<int> ProbeNodeAsync(Server node, CancellationToken ct) =>
            Task.FromResult(Probe(node));

        public (string? Server, string? Override, int Enable) ReadSystemProxy() =>
            (ProxyServer, ProxyOverride, ProxyEnable);

        public bool IsLoopbackPortListening(int port) => ListeningPorts.Contains(port);

        public void DisableSystemProxy()
        {
            DisableCalled = true;
            ProxyEnable = 0;
        }
    }

    private static Server Node(string remark, int delay = -1) => new ShadowsocksServer
    {
        Remark = remark,
        Hostname = "example.com",
        Port = 8388,
        Delay = delay
    };

    private static DiagnosticContext Ctx(
        IReadOnlyList<Server>? servers = null,
        Server? node = null,
        bool accelerating = false,
        string? error = null,
        Game? game = null) =>
        new(game, node, servers ?? Array.Empty<Server>(), accelerating, error);

    private static DiagnosticItem Item(DiagnosticReport r, string id) =>
        r.Items.Single(i => i.Id == id);

    private static FakeEnv GoodEnv()
    {
        var env = new FakeEnv { Admin = true };
        foreach (var f in new[] { "xray.exe", "sing-box.exe", "geoip.dat", "geosite.dat",
                     "Redirector.bin", "nfapi.dll", "nfdriver.sys",
                     "wintun.dll", "tun2socks.bin", "RouteHelper.bin" })
            env.Files.Add(f);
        env.DriverInstalled = true;
        env.ServiceState = DriverServiceState.Running;
        return env;
    }

    [Fact]
    public async Task MissingCoreFiles_ReportsErrorListingThem()
    {
        var env = GoodEnv();
        env.Files.Remove("xray.exe");
        env.Files.Remove("geoip.dat");
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        var core = Item(report, "core");
        Assert.Equal(DiagnosticStatus.Error, core.Status);
        Assert.Contains("xray.exe", core.Detail);
        Assert.Contains("geoip.dat", core.Detail);
    }

    [Fact]
    public async Task NonAdmin_WarnsAndSkipsDrivers()
    {
        var env = GoodEnv();
        env.Admin = false;
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, Item(report, "admin").Status);
        Assert.Equal(DiagnosticStatus.Skipped, Item(report, "netfilter").Status);
        Assert.Equal(DiagnosticStatus.Skipped, Item(report, "wintun").Status);
    }

    [Fact]
    public async Task AllGood_SummaryIsNoProblems()
    {
        var env = GoodEnv();
        env.ProxyEnable = 0;
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(servers: new[] { Node("HK", 30) }), CancellationToken.None);

        Assert.Equal("未发现问题", report.Summary);
    }

    [Fact]
    public async Task DeadLoopbackProxy_ReportsErrorWithFix_AndFixDisablesIt()
    {
        var env = GoodEnv();
        env.ProxyEnable = 1;
        env.ProxyServer = "127.0.0.1:1080";
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        var proxy = Item(report, "proxy");
        Assert.Equal(DiagnosticStatus.Error, proxy.Status);
        Assert.Equal("fixProxy", proxy.FixAction);

        var fixedOk = await svc.FixProxyAsync();
        Assert.True(fixedOk);
        Assert.True(env.DisableCalled);
        Assert.Equal(0, env.ProxyEnable);
    }

    [Fact]
    public async Task LoopbackProxyListening_WhileAccelerating_IsOk()
    {
        var env = GoodEnv();
        env.ProxyEnable = 1;
        env.ProxyServer = "127.0.0.1:1080";
        env.ListeningPorts.Add(1080);
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(accelerating: true), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Ok, Item(report, "proxy").Status);
    }

    [Fact]
    public async Task ForeignProxy_Warns()
    {
        var env = GoodEnv();
        env.ProxyEnable = 1;
        env.ProxyServer = "corp-proxy:8080";
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Warning, Item(report, "proxy").Status);
    }

    [Fact]
    public async Task HttpHttpsLoopbackForm_IsParsed()
    {
        var env = GoodEnv();
        env.ProxyEnable = 1;
        env.ProxyServer = "http=127.0.0.1:1080;https=127.0.0.1:1080";
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        var proxy = Item(report, "proxy");
        Assert.Equal(DiagnosticStatus.Error, proxy.Status);
        Assert.Equal("fixProxy", proxy.FixAction);
    }

    [Fact]
    public async Task NoServers_NodeError()
    {
        var env = GoodEnv();
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Error, Item(report, "node").Status);
    }

    [Fact]
    public async Task AllNodesFail_Error_SomeFail_Warning()
    {
        var env = GoodEnv();
        env.Probe = _ => -4;
        var svc = new DiagnosticsService(env);

        var allFail = await svc.RunAsync(Ctx(servers: new[] { Node("A"), Node("B") }), CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Error, Item(allFail, "node").Status);

        env.Probe = s => s.Remark == "A" ? 30 : -4;
        var someFail = await svc.RunAsync(Ctx(servers: new[] { Node("A"), Node("B") }), CancellationToken.None);
        Assert.Equal(DiagnosticStatus.Warning, Item(someFail, "node").Status);
    }

    [Fact]
    public async Task DnsFailure_Error()
    {
        var env = GoodEnv();
        env.DnsResult = null;
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Error, Item(report, "dns").Status);
    }

    [Fact]
    public async Task ThrowingCheck_BecomesError_OthersStillPresent()
    {
        var env = GoodEnv();
        env.CanBind = false;
        var svc = new DiagnosticsService(env);

        var report = await svc.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Error, Item(report, "port").Status);
        Assert.Contains(report.Items, i => i.Id == "core");
        Assert.Contains(report.Items, i => i.Id == "admin");
    }

    [Theory]
    [InlineData("netfilter2 driver failed", "进程模式驱动")]
    [InlineData("xray did not start", "加速核心启动失败")]
    [InlineData("没有可用线路", "订阅")]
    [InlineData("线路不存在", "已被删除")]
    [InlineData("加速规则为空", "加速规则")]
    [InlineData("connect timeout", "超时")]
    [InlineData("Access is denied", "管理员")]
    [InlineData("something weird", "未能识别")]
    public void ErrorExplainer_MapsKnownSubstrings(string error, string expectedFragment)
    {
        var text = ErrorExplainer.Explain(error);
        Assert.Contains(expectedFragment, text);
    }
}
