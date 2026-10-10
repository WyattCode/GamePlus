using Netch.Core.Services;

namespace Netch.Tests;

public class CrashReporterTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameplus-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Write_CreatesFileWithContent()
    {
        var dir = NewDir();
        try
        {
            var reporter = new CrashReporter(dir);
            var path = reporter.Write("AppDomain.UnhandledException", new InvalidOperationException("boom"), "terminating=True");

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            Assert.StartsWith("crash-host-", Path.GetFileName(path));
            Assert.EndsWith(".txt", path);

            var text = File.ReadAllText(path!);
            Assert.Contains("source: AppDomain.UnhandledException", text);
            Assert.Contains("terminating=True", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("boom", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Write_PrunesToNewestN()
    {
        var dir = NewDir();
        try
        {
            var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var reporter = new CrashReporter(dir, () => t, keep: 3);
            for (var i = 0; i < 6; i++)
            {
                reporter.Write("test", new Exception("e" + i));
                t = t.AddSeconds(1);
            }

            var files = Directory.GetFiles(dir, "crash-host-*.txt");
            Assert.Equal(3, files.Length);
            // Newest three remain (names sort lexicographically by timestamp).
            Array.Sort(files, StringComparer.Ordinal);
            Assert.Contains("crash-host-20240101-000003", files[0]);
            Assert.Contains("crash-host-20240101-000005", files[2]);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Redact_ReplacesProfileAndUser_CaseInsensitive()
    {
        var text = @"C:\Users\Alice\AppData\Roaming\Game+ and c:\users\alice\file and ALICE";
        var result = CrashReporter.Redact(text, @"C:\Users\Alice", "Alice");

        Assert.DoesNotContain(@"C:\Users\Alice", result);
        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", result);
        Assert.Contains("<user>", result);
    }

    [Fact]
    public void Redact_IgnoresShortUserNames()
    {
        var text = "ab is a short name";
        var result = CrashReporter.Redact(text, null, "ab");
        Assert.Equal(text, result);
    }

    [Fact]
    public void Write_NeverThrows_ForUnwritableDirectory()
    {
        var dir = NewDir();
        try
        {
            // A path under a *file* can never be a directory — CreateDirectory throws.
            var filePath = Path.Combine(dir, "blocker");
            File.WriteAllText(filePath, "x");
            var reporter = new CrashReporter(Path.Combine(filePath, "crashes"));

            var path = reporter.Write("test", new Exception("boom"));
            Assert.Null(path);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
