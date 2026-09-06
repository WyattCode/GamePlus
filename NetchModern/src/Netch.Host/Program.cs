using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core.Abstractions;
using Netch.Host;

// Headless Game+ backend. Electron spawns this, reads the "GAMEPLUS_READY {port,token}" line from
// stdout, then drives the engine over ws://127.0.0.1:<port>/ws?token=<token>.

// Native capture libraries (Redirector.bin, nfapi.dll, tun2socks.bin, RouteHelper.bin) live in ".\bin";
// add it to the DLL search path so their P/Invokes resolve — same as the WPF app did in App.OnStartup.
var nativeDir = Path.Combine(AppContext.BaseDirectory, "bin");
if (Directory.Exists(nativeDir))
    SetDllDirectory(nativeDir);

var services = Composition.Build();

// Load persisted settings + servers/subscriptions before serving.
await services.GetRequiredService<ISettingsService>().LoadAsync();
await services.GetRequiredService<IPersistenceService>().LoadAsync();

// When elevated via UAC we lose the stdin/stdout pipe to the parent, so the handshake goes through a
// file (--conn-file) and lifecycle is bound to the parent's PID (--parent-pid) instead of stdin EOF.
string? connFile = null;
var parentPid = 0;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--conn-file") connFile = args[i + 1];
    else if (args[i] == "--parent-pid") int.TryParse(args[i + 1], out parentPid);
}

var router = new RpcRouter(services);
var token = Guid.NewGuid().ToString("N");

// Pick a free port and bind, retrying if another process grabs it in the gap between choosing and binding.
HttpListener? listener = null;
var port = 0;
for (var attempt = 0; attempt < 12 && listener is null; attempt++)
{
    port = FreeTcpPort();
    var candidate = new HttpListener();
    candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
    try
    {
        candidate.Start();
        listener = candidate;
    }
    catch
    {
        candidate.Close();
    }
}

if (listener is null)
{
    Console.Error.WriteLine("host: could not bind a loopback port");
    return;
}

// Include our PID so the (non-elevated) parent can poll our liveness — it has no child handle when we're
// launched elevated via UAC.
var conn = JsonSerializer.Serialize(new { port, token, pid = Environment.ProcessId });

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

try
{
    while (true)
    {
        var ctx = await listener.GetContextAsync();

        if (!ctx.Request.IsWebSocketRequest || ctx.Request.Url?.AbsolutePath != "/ws")
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            continue;
        }

        // Localhost-only already, but require the one-time token as defence in depth.
        if (ctx.Request.QueryString["token"] != token)
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.Close();
            continue;
        }

        HttpListenerWebSocketContext wsCtx;
        try
        {
            wsCtx = await ctx.AcceptWebSocketAsync(null);
        }
        catch
        {
            continue;
        }

        _ = new ClientSession(wsCtx.WebSocket, router, services).RunAsync();
    }
}
catch (Exception e)
{
    Console.Error.WriteLine("host fatal: " + e);
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetDllDirectory(string lpPathName);

static int FreeTcpPort()
{
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    var p = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return p;
}

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
