using Netch.Core.Services.AutoStart;
using Xunit;

namespace Netch.Tests;

public class InstallLocationPolicyTests
{
    private const string SystemSid = "S-1-5-18";
    private const string AdminsSid = "S-1-5-32-544";
    private const string UsersSid = "S-1-5-32-545";
    private const string AuthenticatedUsersSid = "S-1-5-11";

    private static readonly string[] Roots = { @"C:\Program Files" };

    /// <summary>Fake inspector: dictionary path -> info, case-insensitive.</summary>
    private sealed class FakeInspector : IPathSecurityInspector
    {
        private readonly Dictionary<string, PathSecurityInfo> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeInspector Set(string path, PathSecurityInfo info)
        {
            _map[path] = info;
            return this;
        }

        public PathSecurityInfo Inspect(string path) =>
            _map.TryGetValue(path, out var info)
                ? info
                : new PathSecurityInfo(false, false, null, Array.Empty<PathAce>());
    }

    private static PathSecurityInfo Dir(
        string owner = AdminsSid,
        bool reparse = false,
        params PathAce[] aces) =>
        new(true, reparse, owner, aces);

    private static PathAce Allow(string sid, FileSystemRightsLite rights, bool inheritOnly = false) =>
        new(sid, rights, true, inheritOnly);

    private static PathAce Deny(string sid, FileSystemRightsLite rights, bool inheritOnly = false) =>
        new(sid, rights, false, inheritOnly);

    /// <summary>
    ///     Typical Program Files layout: the install dir and exe are owned by Administrators with
    ///     Users read/execute (0x1200a9) and Authenticated Users only AppendData on the drive root.
    /// </summary>
    private static FakeInspector TypicalTrustedLayout(string exe = @"C:\Program Files\Game+\Game+.exe")
    {
        var installDir = @"C:\Program Files\Game+";
        var root = @"C:\Program Files";
        var drive = @"C:";
        return new FakeInspector()
            .Set(exe, Dir(aces: Allow(UsersSid, (FileSystemRightsLite)0x1200a9)))
            .Set(installDir, Dir(aces: Allow(UsersSid, (FileSystemRightsLite)0x1200a9)))
            .Set(root, Dir(aces: Allow(UsersSid, (FileSystemRightsLite)0x1200a9)))
            .Set(drive, Dir(aces: Allow(AuthenticatedUsersSid, FileSystemRightsLite.AppendData)));
    }

    [Fact]
    public void TrustedProgramFilesLayout_Passes()
    {
        var inspector = TypicalTrustedLayout();
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.Null(reason);
    }

    [Fact]
    public void ProgramFilesEvilPrefix_Rejected()
    {
        var inspector = TypicalTrustedLayout(@"C:\Program Files Evil\Game+.exe");
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files Evil\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
        Assert.Contains("Program Files", reason);
    }

    [Fact]
    public void DotDotSegment_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\..\Evil\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
    }

    [Fact]
    public void UserWritableInstallDir_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+",
            Dir(aces: Allow(UsersSid, FileSystemRightsLite.WriteData)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
        Assert.Contains("权限", reason);
    }

    [Fact]
    public void UsersModifyOnExe_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+\Game+.exe",
            Dir(aces: Allow(UsersSid, FileSystemRightsLite.WriteData | FileSystemRightsLite.Delete)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
    }

    [Fact]
    public void InheritOnlyAce_Ignored()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+\Game+.exe",
            Dir(aces: Allow(UsersSid, FileSystemRightsLite.WriteData, inheritOnly: true)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.Null(reason);
    }

    [Fact]
    public void DenyAce_Ignored()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+\Game+.exe",
            Dir(aces: Deny(UsersSid, FileSystemRightsLite.WriteData)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.Null(reason);
    }

    [Fact]
    public void ReparsePoint_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+", Dir(reparse: true));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
        Assert.Contains("符号链接", reason);
    }

    [Fact]
    public void UntrustedOwner_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files\Game+\Game+.exe", Dir(owner: UsersSid));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
    }

    [Fact]
    public void AncestorWithUsersDelete_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files",
            Dir(aces: Allow(UsersSid, FileSystemRightsLite.Delete)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
    }

    [Fact]
    public void AncestorWithUsersAppendData_Allowed()
    {
        var inspector = TypicalTrustedLayout();
        inspector.Set(@"C:\Program Files",
            Dir(aces: Allow(UsersSid, FileSystemRightsLite.AppendData)));
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.Null(reason);
    }

    [Fact]
    public void MissingPath_Rejected()
    {
        var inspector = new FakeInspector();
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Roots, inspector);
        Assert.NotNull(reason);
    }

    [Fact]
    public void NoTrustedRoots_Rejected()
    {
        var inspector = TypicalTrustedLayout();
        var reason = InstallLocationPolicy.GetUntrustedReason(
            @"C:\Program Files\Game+\Game+.exe", Array.Empty<string>(), inspector);
        Assert.NotNull(reason);
    }
}
