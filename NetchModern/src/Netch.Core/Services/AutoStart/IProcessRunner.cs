using System.Diagnostics;

namespace Netch.Core.Services.AutoStart;

/// <summary>
///     Seam for running external processes (schtasks.exe). Tests inject a fake so no real process is
///     spawned. Arguments are always passed as a list — never concatenated into a command line.
/// </summary>
public interface IProcessRunner
{
    Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken ct);
}

/// <summary>
///     Runs a process with <see cref="ProcessStartInfo.ArgumentList" /> (no shell, no quoting games),
///     captures stdout+stderr, and kills it after 15 seconds.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return (-1, "");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
            return (-1, "");
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch
        {
            stdout = "";
            stderr = "";
        }

        var output = string.IsNullOrEmpty(stderr) ? stdout : stdout + stderr;
        return (process.ExitCode, output);
    }
}
