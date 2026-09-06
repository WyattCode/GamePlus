using Netch.Core.Models.Servers;

namespace Netch.Core.Abstractions;

/// <summary>
///     Brings a proxy protocol up as a local SOCKS5 (+ HTTP) outbound — e.g. by launching xray or sing-box —
///     so the rest of the app can route through a single local endpoint regardless of the node's protocol.
/// </summary>
public interface IServerController
{
    string Name { get; }

    /// <summary>Local HTTP inbound port (for the Windows system proxy). Valid after <see cref="StartAsync" />.</summary>
    int HttpPort { get; }

    /// <summary>Game mode: only these domains route through the node; set before <see cref="StartAsync" />.</summary>
    IReadOnlyCollection<string>? ProxyDomains { get; set; }

    /// <summary>Game mode: IPs / CIDR ranges to route through the node; set before <see cref="StartAsync" />.</summary>
    IReadOnlyCollection<string>? ProxyIps { get; set; }

    Task<Socks5Server> StartAsync(Server server, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    /// <summary>Cumulative proxy-outbound bytes (uplink, downlink). Returns (0,0) if the core exposes no stats.</summary>
    Task<(long Uplink, long Downlink)> QueryTrafficAsync(CancellationToken ct = default);
}
