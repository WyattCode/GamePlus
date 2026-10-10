using System.Diagnostics;

namespace Netch.Core.Services.AutoAccel;

/// <summary>
///     Supplies the set of currently running process file names (lower-cased, e.g. "game.exe").
///     Abstracted so the watcher can be unit-tested without touching the real process table.
/// </summary>
public interface IProcessSource
{
    /// <summary>Lower-cased "name.exe" strings for every running process.</summary>
    IReadOnlySet<string> RunningProcessNames();
}

/// <summary>Real implementation backed by <see cref="Process.GetProcesses()" />.</summary>
public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlySet<string> RunningProcessNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return set;
        }

        foreach (var p in processes)
        {
            try
            {
                var name = p.ProcessName;
                if (!string.IsNullOrEmpty(name))
                    set.Add((name + ".exe").ToLowerInvariant());
            }
            catch
            {
                // Process exited / access denied — skip it.
            }
            finally
            {
                try
                {
                    p.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        return set;
    }
}
