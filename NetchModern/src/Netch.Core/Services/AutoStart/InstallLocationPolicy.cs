namespace Netch.Core.Services.AutoStart;

/// <summary>
///     Subset of Win32 file-system access rights, using the raw bit values so the policy can be
///     evaluated without referencing Windows-only APIs (testable on Linux).
/// </summary>
[Flags]
public enum FileSystemRightsLite
{
    None = 0,
    WriteData = 0x2,
    AppendData = 0x4,
    WriteExtendedAttributes = 0x10,
    DeleteChild = 0x40,
    WriteAttributes = 0x100,
    Delete = 0x10000,
    ChangePermissions = 0x40000,
    TakeOwnership = 0x80000,
    GenericAll = 0x10000000,
    GenericWrite = 0x40000000
}

/// <summary>One access-control entry on a path, reduced to what the policy needs.</summary>
public sealed record PathAce(string Sid, FileSystemRightsLite Rights, bool Allow, bool InheritOnly);

/// <summary>Snapshot of a path's security-relevant state.</summary>
public sealed record PathSecurityInfo(
    bool Exists,
    bool IsReparsePoint,
    string? OwnerSid,
    IReadOnlyList<PathAce> Aces);

/// <summary>Reads the security info of a path. Implemented per-platform by the host.</summary>
public interface IPathSecurityInspector
{
    PathSecurityInfo Inspect(string path);
}

/// <summary>
///     Decides whether the installed Game+.exe lives in a location that only administrators can modify.
///     A highest-privileges logon task is only safe when a normal user cannot replace the exe (or any
///     directory on the way to it), otherwise any user-level process could gain admin at next logon.
/// </summary>
public static class InstallLocationPolicy
{
    private const string ReasonNotUnderProgramFiles =
        "开机启动仅支持安装在 Program Files 下的 Game+（当前安装位置普通用户可修改）";

    private const string ReasonReparseOrMissing =
        "安装目录包含符号链接或不存在，已拒绝开机启动";

    private const string ReasonWritable =
        "安装目录的权限允许普通用户修改，已拒绝开机启动";

    private static readonly HashSet<string> TrustedSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-18", // SYSTEM
        "S-1-5-32-544", // Administrators
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" // TrustedInstaller
    };

    // Rights that let a normal user replace the exe itself or the install directory.
    private const FileSystemRightsLite ExeAndDirDangerous =
        FileSystemRightsLite.WriteData |
        FileSystemRightsLite.AppendData |
        FileSystemRightsLite.WriteExtendedAttributes |
        FileSystemRightsLite.DeleteChild |
        FileSystemRightsLite.WriteAttributes |
        FileSystemRightsLite.Delete |
        FileSystemRightsLite.ChangePermissions |
        FileSystemRightsLite.TakeOwnership |
        FileSystemRightsLite.GenericAll |
        FileSystemRightsLite.GenericWrite;

    // Ancestor directories: AppendData ("create folders") is harmless there.
    private const FileSystemRightsLite AncestorDangerous =
        FileSystemRightsLite.Delete |
        FileSystemRightsLite.DeleteChild |
        FileSystemRightsLite.ChangePermissions |
        FileSystemRightsLite.TakeOwnership |
        FileSystemRightsLite.GenericAll |
        FileSystemRightsLite.GenericWrite |
        FileSystemRightsLite.WriteData;

    /// <summary>
    ///     Returns null when <paramref name="exePath" /> is safe to auto-start, otherwise a
    ///     Simplified-Chinese reason explaining why it is not.
    /// </summary>
    public static string? GetUntrustedReason(
        string exePath,
        IReadOnlyList<string> trustedRoots,
        IPathSecurityInspector inspector)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return ReasonNotUnderProgramFiles;

        var exe = Normalize(exePath);
        if (exe is null)
            return ReasonNotUnderProgramFiles;

        var root = FindTrustedRoot(exe, trustedRoots);
        if (root is null)
            return ReasonNotUnderProgramFiles;

        // Paths to check: exe, install dir, and every ancestor up to and including the trusted root.
        var installDir = ParentOf(exe);
        if (installDir is null)
            return ReasonNotUnderProgramFiles;

        var chain = new List<string> { exe, installDir };
        var current = installDir;
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            var parent = ParentOf(current);
            if (parent is null)
                return ReasonNotUnderProgramFiles;
            chain.Add(parent);
            current = parent;
        }

        // (b) every checked path must exist and not be a reparse point.
        var infos = new List<PathSecurityInfo>(chain.Count);
        foreach (var path in chain)
        {
            PathSecurityInfo info;
            try
            {
                info = inspector.Inspect(path);
            }
            catch
            {
                return ReasonReparseOrMissing;
            }

            if (!info.Exists || info.IsReparsePoint)
                return ReasonReparseOrMissing;
            infos.Add(info);
        }

        // (c) owner of each checked path must be trusted.
        foreach (var info in infos)
        {
            if (info.OwnerSid is null || !TrustedSids.Contains(info.OwnerSid))
                return ReasonWritable;
        }

        // (d) exe + install dir: any Allow, non-inherit-only ACE for an untrusted SID granting a
        // dangerous right makes the location user-writable.
        if (HasUntrustedWrite(infos[0], ExeAndDirDangerous))
            return ReasonWritable;
        if (HasUntrustedWrite(infos[1], ExeAndDirDangerous))
            return ReasonWritable;

        // (e) ancestors above the install dir.
        for (var i = 2; i < infos.Count; i++)
        {
            if (HasUntrustedWrite(infos[i], AncestorDangerous))
                return ReasonWritable;
        }

        return null;
    }

    private static bool HasUntrustedWrite(PathSecurityInfo info, FileSystemRightsLite dangerous)
    {
        foreach (var ace in info.Aces)
        {
            if (!ace.Allow || ace.InheritOnly)
                continue;
            if (TrustedSids.Contains(ace.Sid))
                continue;
            if ((ace.Rights & dangerous) != 0)
                return true;
        }

        return false;
    }

    private static string? FindTrustedRoot(string normalizedExe, IReadOnlyList<string> trustedRoots)
    {
        string? best = null;
        foreach (var raw in trustedRoots)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var root = Normalize(raw);
            if (root is null)
                continue;
            if (!IsUnder(normalizedExe, root))
                continue;
            if (best is null || root.Length > best.Length)
                best = root;
        }

        return best;
    }

    /// <summary>Segment-boundary containment: <paramref name="path" /> strictly under <paramref name="root" />.</summary>
    private static bool IsUnder(string path, string root)
    {
        if (path.Length <= root.Length)
            return false;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        var next = path[root.Length];
        return next == '\\' || next == '/';
    }

    private static string? ParentOf(string normalized)
    {
        var index = normalized.LastIndexOfAny(new[] { '\\', '/' });
        if (index <= 0)
            return null;
        var parent = normalized[..index];
        // Keep a drive root like "C:" intact (no trailing separator).
        return parent.Length == 0 ? null : parent;
    }

    /// <summary>
    ///     Normalizes a Windows-style path without relying on <see cref="Path.GetFullPath" /> (which is
    ///     unreliable for <c>C:\</c> paths on Linux). Treats '\' and '/' as separators, rejects "." and
    ///     ".." segments, and trims trailing separators.
    /// </summary>
    private static string? Normalize(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0)
            return null;

        var segments = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.None);
        var parts = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                continue;
            if (segment == "." || segment == "..")
                return null;
            parts.Add(segment);
        }

        if (parts.Count == 0)
            return null;

        return string.Join('\\', parts);
    }
}
