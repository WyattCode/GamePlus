using System.Diagnostics;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Brings a proxy protocol up as local SOCKS5 + HTTP inbounds by generating an xray/v2ray config and
///     launching the bundled <c>xray.exe</c>. Also exposes a stats API for live traffic counters.
/// </summary>
public sealed class V2rayServerController : ProcessGuard, IServerController
{
    private string? _configPath;

    protected override string ExecutablePath { get; } = ResolveXray();

    public string Name => "Xray";

    /// <summary>Local HTTP inbound port (for the Windows system proxy). Valid after <see cref="StartAsync" />.</summary>
    public int HttpPort { get; private set; }

    /// <summary>Local SOCKS5 inbound port.</summary>
    public int SocksPort { get; private set; }

    /// <summary>Local xray stats API port.</summary>
    public int ApiPort { get; private set; }

    /// <summary>Fixed local SOCKS5 port (0 = automatic). Set before <see cref="StartAsync" />.</summary>
    public int PreferredSocksPort { get; set; }

    /// <summary>Game mode: only these domains route through the node; set before <see cref="StartAsync" />.</summary>
    public IReadOnlyCollection<string>? ProxyDomains { get; set; }

    /// <summary>Game mode: IPs / CIDR ranges to route through the node; set before <see cref="StartAsync" />.</summary>
    public IReadOnlyCollection<string>? ProxyIps { get; set; }

    public async Task<Socks5Server> StartAsync(Server server, CancellationToken ct = default)
    {
        SocksPort = ResolveSocksPort();
        HttpPort = NextDistinctPort(SocksPort);
        ApiPort = NextDistinctPort(SocksPort, HttpPort);

        var dir = Path.Combine(AppPaths.DataDirectory, "data");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "xray-config.json");

        var config = V2rayConfigGenerator.Generate(server, SocksPort, HttpPort, ApiPort, ProxyDomains, ProxyIps);
        await File.WriteAllTextAsync(_configPath, config, ct);

        StartProcess($"run -c \"{_configPath}\"");

        if (!await WaitForPortAsync(SocksPort, TimeSpan.FromSeconds(12), ct))
        {
            var tail = string.Join("\n", RecentOutput.TakeLast(6));
            await StopProcessAsync(ct);
            throw new InvalidOperationException($"xray did not start listening on 127.0.0.1:{SocksPort}.\n{tail}");
        }

        return new Socks5Server
        {
            Hostname = "127.0.0.1",
            Port = (ushort)SocksPort,
            Remark = $"local-{server.Type}"
        };
    }

    public Task StopAsync(CancellationToken ct = default) => StopProcessAsync(ct);

    /// <summary>Queries cumulative proxy-outbound bytes via <c>xray api statsquery</c>.</summary>
    public async Task<(long Uplink, long Downlink)> QueryTrafficAsync(CancellationToken ct = default)
    {
        if (ApiPort == 0 || HasExited)
            return (0, 0);

        try
        {
            var psi = new ProcessStartInfo(ExecutablePath, $"api statsquery --server=127.0.0.1:{ApiPort}")
            {
                RedirectStandardOutput = true,
                // Don't redirect stderr: we only consume stdout, and an undrained stderr pipe can fill and
                // deadlock the child until the 3s timeout kills it — turning every poll into a 3s stall.
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var proc = Process.Start(psi);
            if (proc == null)
                return (0, 0);

            // Bind the short-lived helper to the host's job object too, so a host crash doesn't leave it behind.
            AssignToJobObject(proc);

            try
            {
                var json = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);
                return ParseTraffic(json);
            }
            catch
            {
                // Never let a hung statsquery linger — it would accumulate one zombie xray per second.
                try { if (!proc.HasExited) proc.Kill(true); } catch { /* ignored */ }
                return (0, 0);
            }
        }
        catch
        {
            return (0, 0);
        }
    }

    private static (long Uplink, long Downlink) ParseTraffic(string json)
    {
        long up = 0, down = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("stat", out var stats))
                return (0, 0);

            foreach (var s in stats.EnumerateArray())
            {
                if (!s.TryGetProperty("name", out var nameEl))
                    continue;

                var name = nameEl.GetString();

                // xray omits "value" entirely when the counter is 0.
                long value = 0;
                if (s.TryGetProperty("value", out var valueEl))
                    value = valueEl.ValueKind == JsonValueKind.String
                        ? long.Parse(valueEl.GetString()!)
                        : valueEl.GetInt64();

                if (name == "outbound>>>proxy>>>traffic>>>uplink")
                    up = value;
                else if (name == "outbound>>>proxy>>>traffic>>>downlink")
                    down = value;
            }
        }
        catch
        {
            // Malformed / empty output — treat as no traffic yet.
        }

        return (up, down);
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

    private static string ResolveXray()
    {
        var inBin = Path.Combine(AppContext.BaseDirectory, "bin", "xray.exe");
        return File.Exists(inBin) ? inBin : Path.Combine(AppContext.BaseDirectory, "xray.exe");
    }
}
