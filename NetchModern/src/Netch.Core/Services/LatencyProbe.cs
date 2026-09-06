using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Measures latency (ms) to a proxy node. TCP-connect for TCP protocols; ICMP for UDP-based ones
///     (Hysteria2/TUIC), whose port never answers a TCP handshake — so a TCP probe there would always
///     read as 100% loss. Returns ms on success, or a negative failure code (-2 bad host, -4 timeout).
/// </summary>
public static class LatencyProbe
{
    public static Task<int> MeasureAsync(Server server, CancellationToken ct = default)
        => server is Hysteria2Server
            ? IcmpAsync(server.Hostname)
            : TcpAsync(server.Hostname, server.Port, ct);

    private static async Task<int> TcpAsync(string host, int port, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host) || port == 0)
            return -2;

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, timeout.Token);
            sw.Stop();
            return Math.Max(1, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return -4;
        }
    }

    /// <summary>ICMP echo to the host — used for UDP protocols where the port won't answer a TCP connect.</summary>
    private static async Task<int> IcmpAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return -2;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            return reply.Status == IPStatus.Success ? Math.Max(1, (int)reply.RoundtripTime) : -4;
        }
        catch
        {
            return -4;
        }
    }
}
