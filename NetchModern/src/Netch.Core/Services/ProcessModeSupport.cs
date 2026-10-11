using System.IO;

namespace Netch.Core.Services;

/// <summary>
///     Process mode (netfilter2 / Redirector) needs NetFilter SDK files that Game+ does NOT ship
///     (commercial licence). Callers should check <see cref="IsAvailable()" /> before offering or
///     starting process mode and surface <see cref="UnavailableMessage" /> instead of a technical error.
/// </summary>
public static class ProcessModeSupport
{
    /// <summary>User-facing message explaining that process mode is not available in this build.</summary>
    public const string UnavailableMessage = "进程模式暂不可用：未包含 NetFilter 组件";

    /// <summary>Native NetFilter SDK components process mode depends on.</summary>
    public static readonly string[] RequiredFiles = { "Redirector.bin", "nfapi.dll", "nfdriver.sys" };

    /// <summary>
    ///     Files from <see cref="RequiredFiles" /> that exist neither in <paramref name="baseDirectory" />\bin
    ///     nor in <paramref name="baseDirectory" /> itself.
    /// </summary>
    public static IReadOnlyList<string> MissingFiles(string baseDirectory)
    {
        var missing = new List<string>();
        foreach (var file in RequiredFiles)
        {
            if (File.Exists(Path.Combine(baseDirectory, "bin", file)))
                continue;
            if (File.Exists(Path.Combine(baseDirectory, file)))
                continue;
            missing.Add(file);
        }

        return missing;
    }

    /// <summary>True when all required NetFilter files are present in <paramref name="baseDirectory" />.</summary>
    public static bool IsAvailable(string baseDirectory) => MissingFiles(baseDirectory).Count == 0;

    /// <summary>True when all required NetFilter files are present next to the application.</summary>
    public static bool IsAvailable() => IsAvailable(AppContext.BaseDirectory);
}
