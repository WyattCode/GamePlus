using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Brings a node up as a local SOCKS5 + HTTP proxy by generating a sing-box client config and launching
///     the bundled <c>sing-box.exe</c>. Used for protocols xray can't do: Hysteria2 and Shadowsocks nodes
///     that carry a SIP003 plugin (obfs-local / v2ray-plugin).
/// </summary>
public sealed class SingBoxServerController : ProcessGuard, IServerController
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private string? _configPath;

    protected override string ExecutablePath { get; } = ResolveSingBox();

    public string Name => "sing-box";

    public int HttpPort { get; private set; }

    public int SocksPort { get; private set; }

    /// <summary>Local Clash API port (for traffic stats). Valid after <see cref="StartAsync" />.</summary>
    public int ApiPort { get; private set; }

    /// <summary>Fixed local SOCKS5 port (0 = automatic). Set before <see cref="StartAsync" />.</summary>
    public int PreferredSocksPort { get; set; }

    public IReadOnlyCollection<string>? ProxyDomains { get; set; }

    public IReadOnlyCollection<string>? ProxyIps { get; set; }

    public async Task<Socks5Server> StartAsync(Server server, CancellationToken ct = default)
    {
        if (server is not (Hysteria2Server or ShadowsocksServer))
            throw new InvalidOperationException("SingBoxServerController only handles Hysteria2 and Shadowsocks servers.");

        SocksPort = ResolveSocksPort();
        HttpPort = NextDistinctPort(SocksPort);
        ApiPort = NextDistinctPort(SocksPort, HttpPort);

        var dir = Path.Combine(AppPaths.DataDirectory, "data");
        Directory.CreateDirectory(dir);
        // Unique per-instance name so two concurrent sing-box controllers never clobber each other's config.
        _configPath = Path.Combine(dir, $"singbox-{SocksPort}.json");

        var config = SingBoxConfigGenerator.Generate(server, SocksPort, HttpPort, ApiPort, ProxyDomains, ProxyIps);
        await File.WriteAllTextAsync(_configPath, config, ct);

        StartProcess($"run -c \"{_configPath}\"");

        if (!await WaitForPortAsync(SocksPort, TimeSpan.FromSeconds(12), ct))
        {
            var tail = string.Join("\n", RecentOutput.TakeLast(6));
            await StopProcessAsync(ct);
            throw new InvalidOperationException($"sing-box did not start listening on 127.0.0.1:{SocksPort}.\n{tail}");
        }

        return new Socks5Server
        {
            Hostname = "127.0.0.1",
            Port = (ushort)SocksPort,
            Remark = $"local-{server.Type}"
        };
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await StopProcessAsync(ct);

        // Remove the per-instance config so stale files don't pile up in the data folder.
        try
        {
            if (_configPath != null && File.Exists(_configPath))
                File.Delete(_configPath);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    /// <summary>Reads cumulative bytes from sing-box's Clash API (<c>/connections</c> → uploadTotal/downloadTotal).</summary>
    public async Task<(long Uplink, long Downlink)> QueryTrafficAsync(CancellationToken ct = default)
    {
        if (ApiPort == 0 || HasExited)
            return (0, 0);

        try
        {
            var json = await Http.GetStringAsync($"http://127.0.0.1:{ApiPort}/connections", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var up = root.TryGetProperty("uploadTotal", out var u) ? u.GetInt64() : 0;
            var down = root.TryGetProperty("downloadTotal", out var d) ? d.GetInt64() : 0;
            return (up, down);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>Uses the fixed port when configured (and free), otherwise picks a free one.</summary>
    private int ResolveSocksPort()
    {
        if (PreferredSocksPort <= 0)
            return GetFreeTcpPort();

        if (!AdvancedSettingsValidator.IsPortFree(PreferredSocksPort))
            throw new InvalidOperationException(
                $"本地端口 {PreferredSocksPort} 已被占用，请在高级设置中更换端口或改为自动");

        return PreferredSocksPort;
    }

    private static int NextDistinctPort(params int[] taken)
    {
        int port;
        do
        {
            port = GetFreeTcpPort();
        } while (taken.Contains(port));

        return port;
    }

    private static string ResolveSingBox()
    {
        var inBin = Path.Combine(AppContext.BaseDirectory, "bin", "sing-box.exe");
        return File.Exists(inBin) ? inBin : Path.Combine(AppContext.BaseDirectory, "sing-box.exe");
    }
}
