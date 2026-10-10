using System.Text;
using System.Xml.Linq;
using Netch.Core.Services.AutoStart;
using Xunit;

namespace Netch.Tests;

public class AutoStartTests
{
    private const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // --- BuildXml ---

    [Fact]
    public void BuildXml_IsWellFormed()
    {
        var xml = AutoStartTask.BuildXml(@"C:\App\Game+.exe", @"DOMAIN\user");
        var doc = XDocument.Parse(xml);
        Assert.Equal("Task", doc.Root!.Name.LocalName);
        Assert.Equal(Ns, doc.Root.Name.NamespaceName);
    }

    [Fact]
    public void BuildXml_HasHighestAvailableRunLevel()
    {
        var doc = XDocument.Parse(AutoStartTask.BuildXml(@"C:\App\Game+.exe", @"DOMAIN\user"));
        var runLevel = doc.Descendants(XName.Get("RunLevel", Ns)).Single().Value;
        Assert.Equal("HighestAvailable", runLevel);
    }

    [Fact]
    public void BuildXml_HasLogonTriggerWithUserId()
    {
        var doc = XDocument.Parse(AutoStartTask.BuildXml(@"C:\App\Game+.exe", @"DOMAIN\user"));
        var trigger = doc.Descendants(XName.Get("LogonTrigger", Ns)).Single();
        Assert.Equal("true", trigger.Element(XName.Get("Enabled", Ns))!.Value);
        Assert.Equal(@"DOMAIN\user", trigger.Element(XName.Get("UserId", Ns))!.Value);
        Assert.Equal("PT10S", trigger.Element(XName.Get("Delay", Ns))!.Value);
    }

    [Fact]
    public void BuildXml_EscapesSpecialCharacters()
    {
        const string path = "C:\\Program Files\\A&B \"x\"\\Game+.exe";
        var doc = XDocument.Parse(AutoStartTask.BuildXml(path, @"DOMAIN\user"));
        var command = doc.Descendants(XName.Get("Command", Ns)).Single().Value;
        Assert.Equal(path, command);
    }

    [Fact]
    public void BuildXml_HasArgumentsAndWorkingDirectory()
    {
        const string path = @"C:\Program Files\Game+\Game+.exe";
        var doc = XDocument.Parse(AutoStartTask.BuildXml(path, @"DOMAIN\user"));
        var exec = doc.Descendants(XName.Get("Exec", Ns)).Single();
        Assert.Equal("--hidden", exec.Element(XName.Get("Arguments", Ns))!.Value);
        Assert.Equal(@"C:\Program Files\Game+", exec.Element(XName.Get("WorkingDirectory", Ns))!.Value);
    }

    [Fact]
    public void BuildXml_HasDescription()
    {
        var doc = XDocument.Parse(AutoStartTask.BuildXml(@"C:\App\Game+.exe", @"DOMAIN\user"));
        var desc = doc.Descendants(XName.Get("Description", Ns)).Single().Value;
        Assert.Equal("Game+ 开机自动启动", desc);
    }

    // --- ResolveAppExe ---

    [Fact]
    public void ResolveAppExe_ReturnsPathWhenExists()
    {
        var hostBase = Path.Combine(Path.GetTempPath(), "install", "resources", "backend");
        var expected = Path.GetFullPath(Path.Combine(hostBase, "..", "..", "Game+.exe"));
        var result = AutoStartTask.ResolveAppExe(hostBase, p => p == expected);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveAppExe_ReturnsNullWhenMissing()
    {
        var hostBase = Path.Combine(Path.GetTempPath(), "install", "resources", "backend");
        var result = AutoStartTask.ResolveAppExe(hostBase, _ => false);
        Assert.Null(result);
    }

    // --- AutoStartService ---

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Args)> Calls { get; } = new();
        public Func<string, IReadOnlyList<string>, (int, string)> Handler { get; set; } = (_, _) => (0, "");

        public Task<(int ExitCode, string Output)> RunAsync(
            string fileName, IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add((fileName, args));
            return Task.FromResult(Handler(fileName, args));
        }
    }

    private static AutoStartService MakeService(
        FakeRunner runner,
        bool elevated = true,
        string? exe = @"C:\App\Game+.exe",
        string user = @"DOMAIN\user",
        string? tempDir = null,
        Func<string, FileMode, FileAccess, FileShare, Stream>? openTempFile = null)
    {
        return new AutoStartService(
            runner,
            () => elevated,
            () => exe,
            () => user,
            tempDir ?? Path.GetTempPath(),
            openTempFile);
    }

    [Fact]
    public async Task Get_QueryExitZero_Enabled()
    {
        var runner = new FakeRunner { Handler = (_, _) => (0, "") };
        var svc = MakeService(runner);
        var state = await svc.GetAsync(CancellationToken.None);
        Assert.True(state.Enabled);
        Assert.True(state.Supported);
        Assert.Null(state.Reason);
        Assert.Single(runner.Calls);
        Assert.Equal("schtasks.exe", runner.Calls[0].FileName);
        Assert.Equal(new[] { "/Query", "/TN", AutoStartTask.TaskName }, runner.Calls[0].Args);
    }

    [Fact]
    public async Task Get_QueryExitNonZero_Disabled()
    {
        var runner = new FakeRunner { Handler = (_, _) => (1, "not found") };
        var svc = MakeService(runner);
        var state = await svc.GetAsync(CancellationToken.None);
        Assert.False(state.Enabled);
        Assert.True(state.Supported);
    }

    [Fact]
    public async Task Get_ExeMissing_Unsupported()
    {
        var runner = new FakeRunner();
        var svc = MakeService(runner, exe: null);
        var state = await svc.GetAsync(CancellationToken.None);
        Assert.False(state.Enabled);
        Assert.False(state.Supported);
        Assert.Equal("仅安装版支持开机启动", state.Reason);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Set_NotElevated_ThrowsAndRunsNothing()
    {
        var runner = new FakeRunner();
        var svc = MakeService(runner, elevated: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(true, CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Set_Enable_CreatesTaskWithXmlFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gameplus-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string? xmlPath = null;
            byte[]? xmlBytes = null;
            var runner = new FakeRunner();
            runner.Handler = (_, args) =>
            {
                if (args.Count > 0 && args[0] == "/Create")
                {
                    var idx = args.ToList().IndexOf("/XML");
                    xmlPath = args[idx + 1];
                    Assert.True(File.Exists(xmlPath), "temp XML file must exist during the call");
                    xmlBytes = File.ReadAllBytes(xmlPath);
                    return (0, "");
                }
                return (0, "");
            };

            var svc = MakeService(runner, tempDir: tempDir);
            var state = await svc.SetAsync(true, CancellationToken.None);

            Assert.True(state.Enabled);
            Assert.NotNull(xmlPath);
            Assert.False(File.Exists(xmlPath), "temp XML file must be deleted afterwards");

            // UTF-16 LE with BOM.
            Assert.NotNull(xmlBytes);
            Assert.True(xmlBytes!.Length >= 2);
            Assert.Equal(0xFF, xmlBytes[0]);
            Assert.Equal(0xFE, xmlBytes[1]);

            var create = runner.Calls.First(c => c.Args.Count > 0 && c.Args[0] == "/Create");
            Assert.Contains("/TN", create.Args);
            Assert.Contains(AutoStartTask.TaskName, create.Args);
            Assert.Contains("/F", create.Args);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Set_Enable_CreateFails_Throws()
    {
        var runner = new FakeRunner();
        runner.Handler = (_, args) =>
            args.Count > 0 && args[0] == "/Create" ? (1, "ERROR: access denied") : (0, "");
        var svc = MakeService(runner);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(true, CancellationToken.None));
        Assert.Contains("创建计划任务失败", ex.Message);
    }

    [Fact]
    public async Task Set_Enable_ExeMissing_Throws()
    {
        var runner = new FakeRunner();
        var svc = MakeService(runner, exe: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(true, CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Set_Disable_DeletesTask()
    {
        var runner = new FakeRunner();
        runner.Handler = (_, args) =>
            args.Count > 0 && args[0] == "/Delete" ? (0, "") : (1, "");
        var svc = MakeService(runner);
        var state = await svc.SetAsync(false, CancellationToken.None);
        Assert.False(state.Enabled);
        var del = runner.Calls.First(c => c.Args.Count > 0 && c.Args[0] == "/Delete");
        Assert.Contains("/TN", del.Args);
        Assert.Contains(AutoStartTask.TaskName, del.Args);
        Assert.Contains("/F", del.Args);
    }

    [Fact]
    public async Task Set_Disable_NotFound_TreatedAsSuccess()
    {
        var runner = new FakeRunner();
        runner.Handler = (_, args) =>
            args.Count > 0 && args[0] == "/Delete" ? (1, "not found") : (1, "not found");
        var svc = MakeService(runner);
        var state = await svc.SetAsync(false, CancellationToken.None);
        Assert.False(state.Enabled);
    }

    [Fact]
    public async Task Set_Disable_DeleteFailsButTaskExists_Throws()
    {
        var runner = new FakeRunner();
        runner.Handler = (_, args) =>
            args.Count > 0 && args[0] == "/Delete" ? (1, "access denied") : (0, "");
        var svc = MakeService(runner);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(false, CancellationToken.None));
    }

    [Fact]
    public void BuildXml_ArgumentsPassedAsSeparateListItems()
    {
        // Sanity: the runner receives args as a list, never a concatenated command line.
        var runner = new FakeRunner();
        runner.Handler = (_, _) => (0, "");
        var svc = MakeService(runner);
        _ = svc.GetAsync(CancellationToken.None).Result;
        Assert.All(runner.Calls, c => Assert.IsAssignableFrom<IReadOnlyList<string>>(c.Args));
    }

    // --- Security: temp file handle must stay open across the schtasks call ---

    /// <summary>
    ///     A <see cref="MemoryStream" /> that records whether it was disposed and, on dispose, captures
    ///     the bytes written through it. The fake temp-file factory uses the captured bytes to serve the
    ///     read-back open, so the service's tamper check sees the exact bytes it wrote.
    /// </summary>
    private sealed class RecordingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        /// <summary>Bytes written through this stream, captured when it is disposed.</summary>
        public byte[]? CapturedBytes { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                CapturedBytes = ToArray();
                Disposed = true;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Records the streams and open parameters observed by a fake <c>openTempFile</c> delegate.
    ///     The factory lambda mutates the fields, so tests can inspect them after the service ran.
    /// </summary>
    private sealed class RecordingFactory
    {
        public RecordingStream? WriteStream { get; set; }
        public RecordingStream? ReadStream { get; set; }
        public (FileMode Mode, FileAccess Access, FileShare Share)? WriteOpen { get; set; }
        public (FileMode Mode, FileAccess Access, FileShare Share)? ReadOpen { get; set; }

        /// <summary>
        ///     Builds an <c>openTempFile</c> delegate that records the requested mode/access/share for the
        ///     write and read phases, keeps the bytes written through the write stream, and returns a
        ///     stream over those same bytes for the read open.
        /// </summary>
        public Func<string, FileMode, FileAccess, FileShare, Stream> Create()
        {
            return (_, mode, access, share) =>
            {
                if (mode == FileMode.CreateNew)
                {
                    WriteOpen = (mode, access, share);
                    WriteStream = new RecordingStream();
                    return WriteStream;
                }

                ReadOpen = (mode, access, share);
                // Serve the bytes captured from the write stream so the tamper check passes.
                var bytes = WriteStream?.CapturedBytes ?? Array.Empty<byte>();
                var read = new RecordingStream();
                read.Write(bytes, 0, bytes.Length);
                read.Position = 0;
                ReadStream = read;
                return read;
            };
        }
    }

    [Fact]
    public async Task Set_Enable_ReadStreamStaysOpenDuringRunnerCall()
    {
        var runner = new FakeRunner();
        var recorder = new RecordingFactory();
        var readDisposedDuringCall = false;
        var writeDisposedBeforeRead = false;

        runner.Handler = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "/Create")
            {
                writeDisposedBeforeRead = recorder.WriteStream!.Disposed;
                readDisposedDuringCall = recorder.ReadStream!.Disposed;
            }
            return (0, "");
        };

        var svc = MakeService(runner, openTempFile: recorder.Create());

        await svc.SetAsync(true, CancellationToken.None);

        Assert.NotNull(recorder.WriteStream);
        Assert.NotNull(recorder.ReadStream);
        Assert.True(writeDisposedBeforeRead, "write stream must be closed before the read stream is opened");
        Assert.False(readDisposedDuringCall, "read stream must remain open while schtasks runs");
        Assert.True(recorder.ReadStream!.Disposed, "read stream must be disposed after the call");
    }

    [Fact]
    public async Task Set_Enable_ReadStreamDisposedWhenRunnerThrows()
    {
        var runner = new FakeRunner();
        RecordingStream? readStream = null;
        runner.Handler = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "/Create")
                throw new InvalidOperationException("boom");
            return (0, "");
        };

        var svc = MakeService(runner, openTempFile: (_, mode, _, _) =>
        {
            var s = new RecordingStream();
            if (mode != FileMode.CreateNew)
                readStream = s;
            return s;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(true, CancellationToken.None));

        Assert.NotNull(readStream);
        Assert.True(readStream!.Disposed, "read stream must be disposed even when the runner throws");
    }

    [Fact]
    public async Task Set_Enable_TamperedTempFile_Throws()
    {
        var runner = new FakeRunner();
        var svc = MakeService(runner, openTempFile: (_, mode, _, _) =>
        {
            if (mode == FileMode.CreateNew)
                return new MemoryStream();
            // Simulate tampering between the write and read phases: return different bytes.
            return new MemoryStream(Encoding.UTF8.GetBytes("tampered"));
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetAsync(true, CancellationToken.None));
        Assert.Equal("创建计划任务失败：临时文件被修改", ex.Message);
        Assert.DoesNotContain(runner.Calls, c => c.Args.Count > 0 && c.Args[0] == "/Create");
    }

    [Fact]
    public async Task Set_Enable_WriteUsesCreateNewAndNoSharing()
    {
        var runner = new FakeRunner();
        var recorder = new RecordingFactory();

        var svc = MakeService(runner, openTempFile: recorder.Create());

        await svc.SetAsync(true, CancellationToken.None);

        Assert.NotNull(recorder.WriteOpen);
        Assert.Equal(FileMode.CreateNew, recorder.WriteOpen!.Value.Mode);
        Assert.Equal(FileAccess.Write, recorder.WriteOpen.Value.Access);
        Assert.Equal(FileShare.None, recorder.WriteOpen.Value.Share);

        Assert.NotNull(recorder.ReadOpen);
        Assert.Equal(FileMode.Open, recorder.ReadOpen!.Value.Mode);
        Assert.Equal(FileAccess.Read, recorder.ReadOpen.Value.Access);
        Assert.Equal(FileShare.Read, recorder.ReadOpen.Value.Share);
    }
}
