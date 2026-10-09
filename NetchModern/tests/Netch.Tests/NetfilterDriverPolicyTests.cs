using Netch.Core.Controllers;
using Xunit;

namespace Netch.Tests;

public class NetfilterDriverPolicyTests
{
    private const string BundledSha = "aaaa1111";
    private const string BundledVer = "1.2.3.4";

    [Fact]
    public void NotInstalled_Installs()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: false,
            installedSha256: null,
            installedVersion: "",
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: null);

        Assert.Equal(NetfilterDriverAction.Install, action);
    }

    [Fact]
    public void SameHash_UsesExisting()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: BundledSha,
            installedVersion: BundledVer,
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: null);

        Assert.Equal(NetfilterDriverAction.UseExisting, action);
    }

    [Fact]
    public void SameVersionDifferentHash_UsesExisting()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: "bbbb2222",
            installedVersion: BundledVer,
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: null);

        Assert.Equal(NetfilterDriverAction.UseExisting, action);
    }

    [Fact]
    public void EmptyVersionsDifferentHashNotOwned_UsesForeign()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: "bbbb2222",
            installedVersion: "",
            bundledSha256: BundledSha,
            bundledVersion: "",
            ownedSha256: null);

        Assert.Equal(NetfilterDriverAction.UseForeign, action);
    }

    [Fact]
    public void OwnedHashMatchesInstalled_Replaces()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: "cccc3333",
            installedVersion: "1.0.0.0",
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: "cccc3333");

        Assert.Equal(NetfilterDriverAction.Replace, action);
    }

    [Fact]
    public void OwnedMarkerNull_UsesForeign()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: "cccc3333",
            installedVersion: "1.0.0.0",
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: null);

        Assert.Equal(NetfilterDriverAction.UseForeign, action);
    }

    [Fact]
    public void DifferentVersionDifferentHashNotOwned_UsesForeign()
    {
        var action = NetfilterDriverPolicy.Decide(
            installedExists: true,
            installedSha256: "dddd4444",
            installedVersion: "9.9.9.9",
            bundledSha256: BundledSha,
            bundledVersion: BundledVer,
            ownedSha256: "eeee5555");

        Assert.Equal(NetfilterDriverAction.UseForeign, action);
    }
}
