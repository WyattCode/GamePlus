using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Netch.Core.Services.AutoStart;

/// <summary>
///     Pure helpers for the "launch at login" Windows scheduled task: XML generation and locating the
///     installed Game+.exe. No I/O, no Windows APIs — everything is testable on any platform.
/// </summary>
public static class AutoStartTask
{
    /// <summary>Task Scheduler path of the auto-start task (leading backslash = root folder).</summary>
    public const string TaskName = @"\Game+\AutoStart";

    /// <summary>Default arguments passed to Game+.exe (start minimized to tray).</summary>
    public const string DefaultArguments = "--hidden";

    /// <summary>
    ///     Builds the Task Scheduler 1.2 XML for a logon trigger that runs Game+.exe with the highest
    ///     available privileges (no UAC prompt at logon). All values are XML-escaped.
    /// </summary>
    public static string BuildXml(string exePath, string userId, string arguments = DefaultArguments)
    {
        var command = SecurityElement.Escape(exePath) ?? "";
        var args = SecurityElement.Escape(arguments) ?? "";
        var user = SecurityElement.Escape(userId) ?? "";
        var workingDir = SecurityElement.Escape(DirectoryOf(exePath)) ?? "";

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
        sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
        sb.Append("  <RegistrationInfo>\r\n");
        sb.Append("    <Description>Game+ 开机自动启动</Description>\r\n");
        sb.Append("  </RegistrationInfo>\r\n");
        sb.Append("  <Triggers>\r\n");
        sb.Append("    <LogonTrigger>\r\n");
        sb.Append("      <Enabled>true</Enabled>\r\n");
        sb.Append("      <UserId>").Append(user).Append("</UserId>\r\n");
        sb.Append("      <Delay>PT10S</Delay>\r\n");
        sb.Append("    </LogonTrigger>\r\n");
        sb.Append("  </Triggers>\r\n");
        sb.Append("  <Principals>\r\n");
        sb.Append("    <Principal id=\"Author\">\r\n");
        sb.Append("      <UserId>").Append(user).Append("</UserId>\r\n");
        sb.Append("      <LogonType>InteractiveToken</LogonType>\r\n");
        sb.Append("      <RunLevel>HighestAvailable</RunLevel>\r\n");
        sb.Append("    </Principal>\r\n");
        sb.Append("  </Principals>\r\n");
        sb.Append("  <Settings>\r\n");
        sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n");
        sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n");
        sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
        sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\r\n");
        sb.Append("    <StartWhenAvailable>false</StartWhenAvailable>\r\n");
        sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n");
        sb.Append("    <IdleSettings>\r\n");
        sb.Append("      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n");
        sb.Append("      <RestartOnIdle>false</RestartOnIdle>\r\n");
        sb.Append("    </IdleSettings>\r\n");
        sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n");
        sb.Append("    <Enabled>true</Enabled>\r\n");
        sb.Append("    <Hidden>false</Hidden>\r\n");
        sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n");
        sb.Append("    <WakeToRun>false</WakeToRun>\r\n");
        sb.Append("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n");
        sb.Append("    <Priority>7</Priority>\r\n");
        sb.Append("  </Settings>\r\n");
        sb.Append("  <Actions Context=\"Author\">\r\n");
        sb.Append("    <Exec>\r\n");
        sb.Append("      <Command>").Append(command).Append("</Command>\r\n");
        sb.Append("      <Arguments>").Append(args).Append("</Arguments>\r\n");
        sb.Append("      <WorkingDirectory>").Append(workingDir).Append("</WorkingDirectory>\r\n");
        sb.Append("    </Exec>\r\n");
        sb.Append("  </Actions>\r\n");
        sb.Append("</Task>\r\n");
        return sb.ToString();
    }

    /// <summary>
    ///     Extracts the action command path from a Task Scheduler XML document. Namespace-agnostic:
    ///     returns the value of the first element whose local name is "Command". Returns null when the
    ///     XML cannot be parsed or contains no command.
    /// </summary>
    public static string? ParseCommand(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            var doc = XDocument.Parse(xml);
            var command = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Command");
            return command?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Resolves the installed Game+.exe from the host's base directory. The installed layout is
    ///     <c>&lt;install&gt;\resources\backend\gameplus-host.exe</c> with the app at
    ///     <c>&lt;install&gt;\Game+.exe</c>. Returns null when the file does not exist.
    /// </summary>
    public static string? ResolveAppExe(string hostBaseDirectory, Func<string, bool> fileExists)
    {
        var candidate = Path.GetFullPath(Path.Combine(hostBaseDirectory, "..", "..", "Game+.exe"));
        return fileExists(candidate) ? candidate : null;
    }

    /// <summary>
    ///     Directory part of a path, splitting on the last '\' or '/' so Windows-style paths resolve
    ///     correctly even when the code runs on Linux (tests). Returns "" when there is no separator.
    /// </summary>
    private static string DirectoryOf(string path)
    {
        var index = path.LastIndexOfAny(new[] { '\\', '/' });
        return index <= 0 ? "" : path[..index];
    }
}
