using System.Net;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services.Diagnostics;

/// <summary>State of the netfilter2 driver service as far as we can tell without a service API.</summary>
public enum DriverServiceState
{
    NotInstalled,
    Stopped,
    Running,
    Unknown
}

/// <summary>A single Windows firewall rule relevant to diagnostics (enabled Block rules only).</summary>
public sealed record FirewallRuleInfo(string Name, string? ApplicationPath, string Direction);

/// <summary>
///     Seam over the machine-specific bits the diagnostics run needs (registry, admin check, sockets,
///     DNS, latency probes). Abstracted so the whole diagnostics run can be unit-tested on Linux CI
///     without admin rights, network access or real processes.
/// </summary>
public interface IDiagnosticEnvironment
{
    /// <summary>True when the current process is elevated.</summary>
    bool IsAdministrator();

    /// <summary>Directory of the host executable; native helper files live in <c>&lt;BaseDirectory&gt;/bin/</c>.</summary>
    string BaseDirectory { get; }

    /// <summary>True when the file exists (never throws).</summary>
    bool FileExists(string path);

    /// <summary>True when <c>System32\drivers\netfilter2.sys</c> is present.</summary>
    bool DriverFileInstalled();

    /// <summary>Best-effort state of the netfilter2 service (registry key existence only).</summary>
    DriverServiceState NetfilterServiceState();

    /// <summary>True when a TCP listener can be bound on 127.0.0.1:0.</summary>
    bool CanBindLoopback();

    /// <summary>Resolves a hostname; null on failure. Callers expect a ~3 s timeout.</summary>
    Task<IPAddress[]?> ResolveAsync(string host, CancellationToken ct);

    /// <summary>Measures latency to a node (ms; negative = failure).</summary>
    Task<int> ProbeNodeAsync(Server node, CancellationToken ct);

    /// <summary>Reads the current system proxy triple.</summary>
    (string? Server, string? Override, int Enable) ReadSystemProxy();

    /// <summary>True when something is listening on the given loopback port.</summary>
    bool IsLoopbackPortListening(int port);

    /// <summary>Disables the system proxy (Enable=0), keeping server/override, and refreshes WinINET.</summary>
    void DisableSystemProxy();

    /// <summary>Contents of <c>%SystemRoot%\System32\drivers\etc\hosts</c>; null on any error.</summary>
    string? ReadHostsFile();

    /// <summary>Enabled firewall rules with Action=Block; null when the rules cannot be read.</summary>
    IReadOnlyList<FirewallRuleInfo>? ReadFirewallBlockRules();

    /// <summary>Windows "Memory Integrity" (HVCI) state: true/false, null when it cannot be determined.</summary>
    bool? IsMemoryIntegrityEnabled();
}
