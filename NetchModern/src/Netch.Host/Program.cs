using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core;
using Netch.Core.Abstractions;
using Netch.Core.Services;
using Netch.Host;

// Headless Game+ backend. Electron spawns this, reads the "GAMEPLUS_READY {port,pid}" line from stdout,
// then drives the engine over ws://127.0.0.1:<port>/ws?token=<token>.

// Native capture libraries (Redirector.bin, nfapi.dll, tun2socks.bin, RouteHelper.bin) live in ".\bin";
// add it to the DLL search path so their P/Invokes resolve — same as the WPF app did in App.OnStartup.
var nativeDir = Path.Combine(AppContext.BaseDirectory, "bin");
if (Directory.Exists(nativeDir))
    SetDllDirectory(nativeDir);

var services = Composition.Build();

// Local crash reporting: always on, never uploaded unless the user opts in (handled by Electron).
var crash = new CrashReporter(Path.Combine(AppPaths.DataDirectory, "crashes"));
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    crash.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception, $"terminating={e.IsTerminating}");
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    crash.Write("TaskScheduler.UnobservedTaskException", e.Exception);
    e.SetObserved();
};

// Load persisted settings + servers/subscriptions before serving.
await services.GetRequiredService<ISettingsService>().LoadAsync();
await services.GetRequiredService<IPersistenceService>().LoadAsync();

// Recover from a previous crashed run: restore the user's system proxy if we left it pointing at a dead
// local port, and kill any orphaned proxy cores still running from that run.
try
{
    services.GetRequiredService<ISystemProxyService>().RecoverFromCrash();
}
catch (Exception e)
{
    Console.Error.WriteLine("proxy recovery failed: " + e.Message);
}

try
{
    OrphanCleanup.KillStaleCores(AppContext.BaseDirectory);
}
catch (Exception e)
{
    Console.Error.WriteLine("orphan cleanup failed: " + e.Message);
}

// When elevated via UAC we lose the stdin/stdout pipe to the parent, so the handshake goes through a
// file (--conn-file) and lifecycle is bound to the parent's PID (--parent-pid) instead of stdin EOF.
string? connFile = null;
string? tokenSha256 = null;
var parentPid = 0;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--conn-file") connFile = args[i + 1];
    else if (args[i] == "--parent-pid") int.TryParse(args[i + 1], out parentPid);
    else if (args[i] == "--token-sha256") tokenSha256 = args[i + 1];
}

var router = new RpcRouter(services);

// Auto-acceleration: watch for game launches/exits every few seconds and start/stop acceleration.
var autoAccel = new AutoAccelHost(router, log: services.GetRequiredService<ILogService>());
using var autoAccelCts = new CancellationTokenSource();
_ = autoAccel.RunAsync(autoAccelCts.Token);

// Remote game rules: refresh in the background shortly after startup when a URL is configured. Failures
// are logged and swallowed — the bundled/cached rules stay active.
var rulesUrl = services.GetRequiredService<ISettingsService>().Settings.RulesUrl;
if (!string.IsNullOrWhiteSpace(rulesUrl))
{
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            await router.RefreshRulesAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            try
            {
                services.GetRequiredService<ILogService>()
                    .Log(Netch.Core.Enums.LogLevel.Warning, $"后台更新游戏规则失败：{e.Message}");
            }
            catch
            {
                // best effort
            }
        }
    });
}

// Security: the elevated host must not hand its bearer token to any same-user process via a world-readable
// temp file. When the launcher supplies --token-sha256 we never know the token at all — we only verify the
// SHA-256 of whatever the client presents. Fall back to a self-generated token (old launcher) for compat.
string? generatedToken = null;
byte[]? expectedTokenHash = null;
if (!string.IsNullOrWhiteSpace(tokenSha256))
{
    try
    {
        expectedTokenHash = Convert.FromHexString(tokenSha256.Trim());
    }
    catch
    {
        Console.Error.WriteLine("host: invalid --token-sha256 value");
        return;
    }
}
else
{
    generatedToken = Guid.NewGuid().ToString("N");
}

bool ValidateToken(string? presented)
{
    if (expectedTokenHash is not null)
    {
        if (string.IsNullOrEmpty(presented))
            return false;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(hash, expectedTokenHash);
    }

    return string.Equals(presented, generatedToken, StringComparison.Ordinal);
}

using var server = new LoopbackWebSocketServer(ValidateToken);
int port;
try
{
    port = server.Start();
}
catch (Exception e)
{
    Console.Error.WriteLine("host: could not bind a loopback port: " + e.Message);
    return;
}

// Include our PID so the (non-elevated) parent can poll our liveness — it has no child handle when we're
// launched elevated via UAC. The token is never written to disk: the launcher already knows it.
var conn = generatedToken is null
    ? JsonSerializer.Serialize(new { port, pid = Environment.ProcessId })
    : JsonSerializer.Serialize(new { port, token = generatedToken, pid = Environment.ProcessId });

// The line the parent waits for when we share a console (dev / non-elevated).
Console.WriteLine("GAMEPLUS_READY " + conn);
Console.Out.Flush();

// Elevated path: hand the connection info to the non-elevated parent via a file it can read.
if (connFile != null)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(connFile)!);
        var tmp = connFile + ".tmp";
        File.WriteAllText(tmp, conn);
        File.Move(tmp, connFile, true);
    }
    catch (Exception e)
    {
        Console.Error.WriteLine("conn-file write failed: " + e.Message);
    }
}

// Clean up the system proxy / running cores if we're told to exit or our parent (Electron) goes away.
// Elevated (UAC) launches have no piped stdin, so bind to the parent PID; direct/dev launches share a
// stdin pipe, so watch it for EOF. Using exactly one avoids the stdin-EOF watch killing an elevated host.
AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup(services);
if (parentPid > 0)
    _ = WatchParentPidAsync(parentPid, services);
else
    _ = WatchParentAsync(services);

using var acceptCts = new CancellationTokenSource();
try
{
    await server.AcceptLoopAsync(
        ws => new ClientSession(ws, router, services, autoAccel).RunAsync(),
        acceptCts.Token);
}
catch (Exception e)
{
    Console.Error.WriteLine("host fatal: " + e);
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetDllDirectory(string lpPathName);

// If stdin closes, Electron (our parent) has exited — tear down and quit.
static async Task WatchParentAsync(IServiceProvider sp)
{
    try
    {
        using var stdin = Console.OpenStandardInput();
        var buf = new byte[256];
        while (await stdin.ReadAsync(buf) > 0)
        {
            // drain; Electron doesn't send us anything over stdin
        }
    }
    catch
    {
        // ignore
    }

    Cleanup(sp);
    Environment.Exit(0);
}

// Elevated sidecar can't share stdin with a non-elevated parent, so bind our lifetime to the parent's PID.
static async Task WatchParentPidAsync(int pid, IServiceProvider sp)
{
    while (true)
    {
        await Task.Delay(1500);
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            if (p.HasExited)
                break;
        }
        catch (ArgumentException)
        {
            break; // parent process no longer exists
        }
        catch
        {
            // transient query failure — keep watching
        }
    }

    Cleanup(sp);
    Environment.Exit(0);
}

static void Cleanup(IServiceProvider sp)
{
    // Runs once. The watchers call Cleanup then Environment.Exit, which fires ProcessExit → Cleanup again;
    // without this guard StopAsync would run (and block up to 6s) twice on every shutdown.
    if (Interlocked.Exchange(ref ShutdownGuard.Done, 1) == 1)
        return;

    try
    {
        sp.GetRequiredService<IAcceleratorService>().StopAsync().Wait(TimeSpan.FromSeconds(6));
    }
    catch
    {
        // best effort
    }

    try
    {
        var proxy = sp.GetRequiredService<ISystemProxyService>();
        if (proxy.IsEnabled)
            proxy.Disable();
    }
    catch
    {
        // best effort
    }
}

// Ensures Cleanup runs exactly once across the watcher path and the ProcessExit handler.
internal static class ShutdownGuard
{
    public static int Done;
}
