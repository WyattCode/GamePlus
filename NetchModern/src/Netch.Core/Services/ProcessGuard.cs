using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Netch.Core.Services;

/// <summary>
///     Base class for controllers that shepherd a native child process. Readiness is detected by polling
///     the local listen port (robust regardless of the child's log output).
/// </summary>
public abstract class ProcessGuard : IAsyncDisposable
{
    private readonly List<string> _output = new();
    private readonly object _outputLock = new();
    private Process? _process;

    /// <summary>Full path (or PATH-resolvable name) of the executable to launch.</summary>
    protected abstract string ExecutablePath { get; }

    public IReadOnlyList<string> RecentOutput
    {
        get
        {
            lock (_outputLock)
                return _output.ToArray();
        }
    }

    protected bool HasExited => _process?.HasExited ?? true;

    public async ValueTask DisposeAsync()
    {
        await StopProcessAsync();
        _process?.Dispose();
    }

    protected void StartProcess(string arguments, string? workingDirectory = null)
    {
        lock (_outputLock)
            _output.Clear();
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(ExecutablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnOutput;

        _process.Start(); // throws Win32Exception if the executable is missing
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>Waits until <paramref name="port" /> on loopback accepts a connection, or the process dies.</summary>
    protected async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (HasExited)
                return false;

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return true;
            }
            catch
            {
                await Task.Delay(200, ct);
            }
        }

        return false;
    }

    protected async Task StopProcessAsync(CancellationToken ct = default)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(true);

                // Wait without blocking the caller thread; cap at 3s so a wedged process can't hang shutdown.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                try { await _process.WaitForExitAsync(timeoutCts.Token); }
                catch (OperationCanceledException) { }
            }
        }
        catch
        {
            // ignored
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null)
            return;

        lock (_outputLock)
        {
            _output.Add(e.Data);
            if (_output.Count > 200)
                _output.RemoveAt(0);
        }
    }

    /// <summary>Reserves an ephemeral loopback TCP port.</summary>
    protected static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
