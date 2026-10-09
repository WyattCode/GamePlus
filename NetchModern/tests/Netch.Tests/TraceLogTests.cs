using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class TraceLogTests
{
    [Fact]
    public void Append_WritesLine()
    {
        var dir = Directory.CreateTempSubdirectory("trace-");
        try
        {
            var path = Path.Combine(dir.FullName, "trace.log");
            TraceLog.Append(path, "hello");
            var text = File.ReadAllText(path);
            Assert.Contains("hello", text);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Append_RotatesWhenOverMaxBytes()
    {
        var dir = Directory.CreateTempSubdirectory("trace-");
        try
        {
            var path = Path.Combine(dir.FullName, "trace.log");
            // Write a file just over the cap.
            File.WriteAllText(path, new string('x', (int)TraceLog.MaxBytes + 1));

            TraceLog.Append(path, "after-rotate");

            var rotated = path + ".1";
            Assert.True(File.Exists(rotated), "rotated file should exist");
            Assert.True(new FileInfo(rotated).Length > TraceLog.MaxBytes);
            var fresh = File.ReadAllText(path);
            Assert.Contains("after-rotate", fresh);
            Assert.True(new FileInfo(path).Length < TraceLog.MaxBytes);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Append_OverwritesPreviousRotation()
    {
        var dir = Directory.CreateTempSubdirectory("trace-");
        try
        {
            var path = Path.Combine(dir.FullName, "trace.log");
            File.WriteAllText(path + ".1", "old-rotation");
            File.WriteAllText(path, new string('y', (int)TraceLog.MaxBytes + 1));

            TraceLog.Append(path, "second");

            var rotated = File.ReadAllText(path + ".1");
            Assert.DoesNotContain("old-rotation", rotated);
            Assert.True(new FileInfo(path + ".1").Length > TraceLog.MaxBytes);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
