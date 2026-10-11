using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Netch.Core.Services;

/// <summary>
///     Writes local crash reports to a directory and prunes old ones. Reports never leave the machine
///     unless the user opts in to telemetry uploads (handled by the Electron client). All operations are
///     best-effort: <see cref="Write" /> never throws and returns <c>null</c> on failure.
/// </summary>
public sealed class CrashReporter
{
    private readonly string _directory;
    private readonly Func<DateTime> _clock;
    private readonly int _keep;

    /// <summary>Creates a reporter writing into <paramref name="directory" />, keeping the newest <paramref name="keep" /> files.</summary>
    public CrashReporter(string directory, Func<DateTime>? clock = null, int keep = 20)
    {
        _directory = directory;
        _clock = clock ?? (() => DateTime.UtcNow);
        _keep = keep < 1 ? 1 : keep;
    }

    /// <summary>Directory the crash reports are written to.</summary>
    public string Directory => _directory;

    /// <summary>
    ///     Writes a crash report file and prunes old ones. Returns the file path, or <c>null</c> when the
    ///     report could not be written (never throws).
    /// </summary>
    public string? Write(string source, Exception? ex, string? extra = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);

            var now = _clock();
            var name = $"crash-host-{now:yyyyMMdd-HHmmss-fff}.txt";
            var path = Path.Combine(_directory, name);

            var sb = new StringBuilder();
            sb.Append("time: ").Append(now.ToString("o")).Append('\n');
            sb.Append("source: ").Append(source).Append('\n');
            sb.Append("version: ").Append(AppVersion()).Append('\n');
            sb.Append("os: ").Append(RuntimeInformation.OSDescription).Append('\n');
            sb.Append("arch: ").Append(RuntimeInformation.ProcessArchitecture).Append('\n');
            if (!string.IsNullOrEmpty(extra))
                sb.Append("extra: ").Append(extra).Append('\n');
            sb.Append('\n');
            if (ex is not null)
                sb.Append(ex).Append('\n');

            var text = Redact(
                sb.ToString(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.UserName);

            File.WriteAllText(path, text, new UTF8Encoding(false));
            Prune();
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Replaces the user profile path and user name with placeholders. Matching is case-insensitive;
    ///     user names shorter than 3 characters are ignored (too likely to match unrelated text).
    /// </summary>
    public static string Redact(string text, string? profile, string? user)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var result = text;
        if (!string.IsNullOrEmpty(profile))
        {
            result = result.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            // Also cover the forward-slash variant so paths copied from other tools are redacted too.
            var alt = profile.Replace('\\', '/');
            if (!string.Equals(alt, profile, StringComparison.Ordinal))
                result = result.Replace(alt, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(user) && user.Length >= 3)
            result = result.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private void Prune()
    {
        try
        {
            var files = System.IO.Directory.GetFiles(_directory, "crash-host-*.txt");
            if (files.Length <= _keep)
                return;

            Array.Sort(files, StringComparer.Ordinal);
            var remove = files.Length - _keep;
            for (var i = 0; i < remove; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                    // best effort
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string AppVersion()
    {
        try
        {
            var asm = typeof(CrashReporter).Assembly;
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString()
                   ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
