using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Netch.Core.Services.AutoStart;

namespace Netch.Host;

/// <summary>
///     Reads the owner and access rules of a file-system path via <see cref="FileSystemAclExtensions" />.
///     Any failure (missing path, access denied, non-Windows) yields a result that makes
///     <see cref="InstallLocationPolicy" /> reject the location.
/// </summary>
internal sealed class WindowsPathSecurityInspector : IPathSecurityInspector
{
    private static readonly PathSecurityInfo Reject = new(false, false, null, Array.Empty<PathAce>());

    public PathSecurityInfo Inspect(string path)
    {
        if (!OperatingSystem.IsWindows())
            return Reject;

        try
        {
            return InspectWindows(path);
        }
        catch
        {
            return Reject;
        }
    }

    [SupportedOSPlatform("windows")]
    private static PathSecurityInfo InspectWindows(string path)
    {
        var attributes = File.GetAttributes(path);
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;

        FileSystemSecurity security;
        if ((attributes & FileAttributes.Directory) != 0)
        {
            var info = new DirectoryInfo(path);
            security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        else
        {
            var info = new FileInfo(path);
            security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var ownerSid = owner?.Value;

        var aces = new List<PathAce>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = rule.IdentityReference is SecurityIdentifier s ? s.Value : rule.IdentityReference.Value;
            var rights = (FileSystemRightsLite)(int)rule.FileSystemRights;
            var allow = rule.AccessControlType == AccessControlType.Allow;
            var inheritOnly = (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0;
            aces.Add(new PathAce(sid, rights, allow, inheritOnly));
        }

        return new PathSecurityInfo(true, isReparsePoint, ownerSid, aces);
    }
}
