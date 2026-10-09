namespace Netch.Core.Services;

/// <summary>
///     Tiny append-only trace logger with a size cap. When a log file grows past <see cref="MaxBytes" />
///     it is rotated to <c>&lt;name&gt;.1</c> (overwriting any previous rotation) and a fresh file is
///     started. Thread-safe: all writes are serialised through a static lock.
/// </summary>
public static class TraceLog
{
    /// <summary>Rotate once the file exceeds this size (1 MB).</summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Appends a timestamped line to <paramref name="path" />, rotating when it grows too large.</summary>
    public static void Append(string path, string message)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    var rotated = path + ".1";
                    try
                    {
                        if (File.Exists(rotated))
                            File.Delete(rotated);
                        File.Move(path, rotated);
                    }
                    catch
                    {
                        // If rotation fails (file locked), fall through and keep appending.
                    }
                }

                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
            }
        }
        catch
        {
            // Tracing must never break the caller.
        }
    }
}
