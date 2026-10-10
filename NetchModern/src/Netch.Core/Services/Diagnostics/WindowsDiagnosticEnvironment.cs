using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using Netch.Core.Abstractions;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services.Diagnostics;

/// <summary>
///     Production <see cref="IDiagnosticEnvironment" />. Every Windows-specific call is wrapped in
///     try/catch and returns a safe default so a diagnostics run never throws.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiagnosticEnvironment : IDiagnosticEnvironment
{
    private const string NetfilterServiceKey = @"SYSTEM\CurrentControlSet\Services\netfilter2";
    private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private readonly IProxySettingsStore _proxy;

    public WindowsDiagnosticEnvironment(IProxySettingsStore proxy) => _proxy = proxy;

    public string BaseDirectory => AppContext.BaseDirectory;

    public bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public bool DriverFileInstalled()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "drivers", "netfilter2.sys"));
        }
        catch
        {
            return false;
        }
    }

    public DriverServiceState NetfilterServiceState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(NetfilterServiceKey, writable: false);
            return key == null ? DriverServiceState.NotInstalled : DriverServiceState.Unknown;
        }
        catch
        {
            return DriverServiceState.Unknown;
        }
    }

    public bool CanBindLoopback()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IPAddress[]?> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            return await Dns.GetHostAddressesAsync(host, timeout.Token);
        }
        catch
        {
            return null;
        }
    }

    public Task<int> ProbeNodeAsync(Server node, CancellationToken ct) =>
        LatencyProbe.MeasureAsync(node, ct);

    public (string? Server, string? Override, int Enable) ReadSystemProxy()
    {
        try
        {
            return _proxy.Read();
        }
        catch
        {
            return (null, null, 0);
        }
    }

    public bool IsLoopbackPortListening(int port)
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            return props.GetActiveTcpListeners().Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    public void DisableSystemProxy()
    {
        try
        {
            var (server, @override, _) = _proxy.Read();
            _proxy.Write(server, @override, 0);
            _proxy.Refresh();
        }
        catch
        {
            // ignore
        }
    }

    public string? ReadHostsFile()
    {
        try
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            return File.ReadAllText(Path.Combine(systemRoot, "System32", "drivers", "etc", "hosts"));
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<FirewallRuleInfo>? ReadFirewallBlockRules()
    {
        object? policy = null;
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null)
                return null;

            policy = Activator.CreateInstance(type);
            if (policy == null)
                return null;

            dynamic fwPolicy = policy;
            var rules = new List<FirewallRuleInfo>();
            foreach (var rule in fwPolicy.Rules)
            {
                try
                {
                    // Action: 0 = NET_FW_ACTION_BLOCK. Direction: 1 = in, 2 = out.
                    if (!(bool)rule.Enabled || (int)rule.Action != 0)
                        continue;

                    var direction = (int)rule.Direction == 1 ? "入站" : "出站";
                    var app = rule.ApplicationName as string;
                    rules.Add(new FirewallRuleInfo((string)rule.Name,
                        string.IsNullOrWhiteSpace(app) ? null : app, direction));
                }
                finally
                {
                    if (rule != null && Marshal.IsComObject(rule))
                        Marshal.FinalReleaseComObject(rule);
                }
            }

            return rules;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (policy != null && Marshal.IsComObject(policy))
                Marshal.FinalReleaseComObject(policy);
        }
    }

    public bool? IsMemoryIntegrityEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HvciKey, writable: false);
            return key?.GetValue("Enabled") is int enabled && enabled == 1;
        }
        catch
        {
            return null;
        }
    }
}
