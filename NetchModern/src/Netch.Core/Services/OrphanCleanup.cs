using System.Diagnostics;

namespace Netch.Core.Services;

/// <summary>
///     Best-effort cleanup of proxy cores left behind by a previous crashed host run. Only kills processes
///     whose executable lives inside this app's install directory, so unrelated xray/sing-box installs are
///     never touched.
/// </summary>
public static class OrphanCleanup
{
    private static readonly string[] CoreNames = { "xray", "sing-box" };

    /// <summary>
    ///     Kills <c>xray</c> / <c>sing-box</c> processes whose executable path is inside
    ///     <paramref name="baseDir" />. Windows-only; all failures are swallowed.
    /// </summary>
    public static void KillStaleCores(string baseDir)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (string.IsNullOrWhiteSpace(baseDir))
            return;

        string normalizedBase;
        try
        {
            normalizedBase = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
        catch
        {
            return;
        }

        foreach (var name in CoreNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var p in processes)
            {
                try
                {
                    string? path = null;
                    try
                    {
                        path = p.MainModule?.FileName;
                    }
                    catch
                    {
                        // Access denied / process already gone — skip.
                    }

                    if (path == null)
                        continue;

                    var full = Path.GetFullPath(path);
                    if (!full.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                        continue;

                    p.Kill(true);
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }
}
