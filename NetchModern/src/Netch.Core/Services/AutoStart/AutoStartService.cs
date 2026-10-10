using System.Text;

namespace Netch.Core.Services.AutoStart;

/// <summary>
///     Creates / removes the "launch at login" scheduled task via schtasks.exe. The host runs elevated
///     (UAC at app start), so creating a highest-privileges task needs no further prompt.
/// </summary>
public sealed class AutoStartService
{
    private readonly IProcessRunner _runner;
    private readonly Func<bool> _isElevated;
    private readonly Func<string?> _resolveExe;
    private readonly Func<string, string?> _untrustedReason;
    private readonly Func<string> _currentUser;
    private readonly string _tempDirectory;
    private readonly Func<string, FileMode, FileAccess, FileShare, Stream> _openTempFile;

    public AutoStartService(
        IProcessRunner runner,
        Func<bool> isElevated,
        Func<string?> resolveExe,
        Func<string, string?> untrustedReason,
        Func<string> currentUser,
        string tempDirectory,
        Func<string, FileMode, FileAccess, FileShare, Stream>? openTempFile = null)
    {
        _runner = runner;
        _isElevated = isElevated;
        _resolveExe = resolveExe;
        _untrustedReason = untrustedReason;
        _currentUser = currentUser;
        _tempDirectory = tempDirectory;
        _openTempFile = openTempFile ?? DefaultOpenTempFile;
    }

    /// <summary>
    ///     Opens the temp XML file with the requested mode/access/share. The write phase uses
    ///     <see cref="FileMode.CreateNew" /> (fails if a file/symlink/junction with that name already
    ///     exists, so a pre-planted file cannot be used) with <see cref="FileShare.None" />. The read
    ///     phase reopens with <see cref="FileMode.Open" />, <see cref="FileAccess.Read" /> and
    ///     <see cref="FileShare.Read" />: no write/delete sharing, so nobody can modify or replace the
    ///     file while we hold the handle, while schtasks.exe's read-only open stays compatible.
    /// </summary>
    private static Stream DefaultOpenTempFile(string path, FileMode mode, FileAccess access, FileShare share) =>
        new FileStream(path, mode, access, share);

    /// <summary>Queries the current state of the auto-start task.</summary>
    public async Task<AutoStartState> GetAsync(CancellationToken ct)
    {
        var exe = _resolveExe();
        if (exe is null)
            return new AutoStartState(false, false, "仅安装版支持开机启动");

        var reason = _untrustedReason(exe);

        var (exitCode, output) = await _runner.RunAsync(
            "schtasks.exe",
            new[] { "/Query", "/TN", AutoStartTask.TaskName, "/XML" },
            ct);

        var enabled = false;
        if (exitCode == 0)
        {
            var command = AutoStartTask.ParseCommand(output);
            enabled = CommandMatches(command, exe);
        }

        return reason is not null
            ? new AutoStartState(enabled, false, reason)
            : new AutoStartState(enabled, true, null);
    }

    /// <summary>Enables or disables the auto-start task. Requires elevation.</summary>
    public async Task<AutoStartState> SetAsync(bool enabled, CancellationToken ct)
    {
        if (!_isElevated())
            throw new InvalidOperationException("设置开机启动需要管理员权限，请允许 Game+ 以管理员身份运行后再试");

        if (enabled)
        {
            var exe = _resolveExe()
                      ?? throw new InvalidOperationException("未找到 Game+ 主程序（仅安装版支持开机启动）");

            // Refuse before writing anything: a highest-privileges task must only point at an exe that
            // a normal user cannot replace.
            var reason = _untrustedReason(exe);
            if (reason is not null)
                throw new InvalidOperationException(reason);

            var xml = AutoStartTask.BuildXml(exe, _currentUser());
            var tempFile = Path.Combine(_tempDirectory, $"gameplus-autostart-{Guid.NewGuid():N}.xml");
            try
            {
                // Task Scheduler requires UTF-16 LE with BOM for /XML imports.
                var encoding = new UnicodeEncoding(false, true);
                var preamble = encoding.GetPreamble();
                var body = encoding.GetBytes(xml);
                var expected = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, expected, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, expected, preamble.Length, body.Length);

                // Phase 1: write the file exclusively (CreateNew + FileShare.None), then close it.
                using (var writeStream = _openTempFile(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    writeStream.Write(expected, 0, expected.Length);
                    writeStream.Flush();
                    if (writeStream is FileStream fs)
                        fs.Flush(flushToDisk: true);
                }

                // Phase 2: reopen read-only with FileShare.Read (no write/delete sharing) and verify the
                // bytes we just wrote. This closes the gap between close and reopen: any tampering in
                // that window is detected here, and afterwards nobody can modify/replace the file while
                // we hold the handle. schtasks.exe's read-only open is compatible with FileShare.Read.
                using (var readStream = _openTempFile(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var actual = new byte[expected.Length];
                    var read = 0;
                    while (read < actual.Length)
                    {
                        var n = readStream.Read(actual, read, actual.Length - read);
                        if (n <= 0)
                            break;
                        read += n;
                    }

                    if (read != expected.Length || !actual.AsSpan(0, read).SequenceEqual(expected))
                        throw new InvalidOperationException("创建计划任务失败：临时文件被修改");

                    // The read handle stays open for the whole schtasks call.
                    var (exitCode, output) = await _runner.RunAsync(
                        "schtasks.exe",
                        new[] { "/Create", "/TN", AutoStartTask.TaskName, "/XML", tempFile, "/F" },
                        ct);

                    if (exitCode != 0)
                        throw new InvalidOperationException($"创建计划任务失败：{output.Trim()}");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch
                {
                    // ignore
                }
            }
        }
        else
        {
            var (exitCode, output) = await _runner.RunAsync(
                "schtasks.exe",
                new[] { "/Delete", "/TN", AutoStartTask.TaskName, "/F" },
                ct);

            if (exitCode != 0)
            {
                // "not found" is success: the task is already gone.
                var (queryExit, _) = await _runner.RunAsync(
                    "schtasks.exe",
                    new[] { "/Query", "/TN", AutoStartTask.TaskName },
                    ct);
                if (queryExit == 0)
                    throw new InvalidOperationException($"删除计划任务失败：{output.Trim()}");
            }
        }

        return await GetAsync(ct);
    }

    /// <summary>
    ///     Removes a stale auto-start task: one that points at a different exe, or at an exe that is no
    ///     longer in an admin-only-writable location. Returns a short English log message when a task
    ///     was removed (or when the cleanup itself failed), otherwise null. Never throws for schtasks
    ///     failures; cancellation is honoured.
    /// </summary>
    public async Task<string?> ReconcileAsync(CancellationToken ct)
    {
        if (!_isElevated())
            return null;

        ct.ThrowIfCancellationRequested();

        int queryExit;
        string queryOutput;
        try
        {
            (queryExit, queryOutput) = await _runner.RunAsync(
                "schtasks.exe",
                new[] { "/Query", "/TN", AutoStartTask.TaskName, "/XML" },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return "autostart reconcile: query failed: " + e.Message;
        }

        if (queryExit != 0)
            return null; // task does not exist

        var exe = _resolveExe();
        string? reason;
        if (exe is null)
        {
            reason = "installed exe not found";
        }
        else
        {
            reason = _untrustedReason(exe);
            if (reason is null)
            {
                var command = AutoStartTask.ParseCommand(queryOutput);
                if (!CommandMatches(command, exe))
                    reason = "task points at a different executable";
            }
        }

        if (reason is null)
            return null;

        try
        {
            var (deleteExit, deleteOutput) = await _runner.RunAsync(
                "schtasks.exe",
                new[] { "/Delete", "/TN", AutoStartTask.TaskName, "/F" },
                ct);
            if (deleteExit != 0)
                return "autostart reconcile: delete failed: " + deleteOutput.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return "autostart reconcile: delete failed: " + e.Message;
        }

        return "removed stale autostart task: " + reason;
    }

    /// <summary>
    ///     True when a task's action command equals the expected exe path, ignoring surrounding quotes
    ///     and whitespace and comparing case-insensitively.
    /// </summary>
    private static bool CommandMatches(string? command, string exe)
    {
        if (command is null)
            return false;
        var trimmed = command.Trim().Trim('"').Trim();
        return string.Equals(trimmed, exe.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
