using Netch.Core.Models.Servers;
using Netch.Host;
using Xunit;

namespace Netch.Tests;

public class ServerIdTests
{
    private static ShadowsocksServer MakeSs(string host = "a.example.com", string remark = "A", int port = 8388)
        => new()
        {
            Group = "NONE",
            Hostname = host,
            Port = (ushort)port,
            Remark = remark,
            Password = "pw",
            EncryptMethod = "aes-256-gcm"
        };

    [Fact]
    public void SameContentProducesSameIdAcrossFreshInstances()
    {
        var a = MakeSs();
        var b = MakeSs();

        Assert.Equal(ServerIds.Fingerprint(a), ServerIds.Fingerprint(b));
    }

    [Fact]
    public void DifferentContentProducesDifferentId()
    {
        var a = MakeSs(host: "a.example.com");
        var b = MakeSs(host: "b.example.com");

        Assert.NotEqual(ServerIds.Fingerprint(a), ServerIds.Fingerprint(b));
    }

    [Fact]
    public void DuplicatesGetDistinctIdsInListOrder()
    {
        var a = MakeSs();
        var b = MakeSs();
        var c = MakeSs();

        var map = ServerIds.Assign(new[] { a, b, c });

        Assert.Equal(3, map.Values.Distinct().Count());
        Assert.Equal(ServerIds.Fingerprint(a), map[a]);
        Assert.Equal(ServerIds.Fingerprint(a) + "-2", map[b]);
        Assert.Equal(ServerIds.Fingerprint(a) + "-3", map[c]);
    }

    [Fact]
    public void ChangingDelayDoesNotChangeId()
    {
        var a = MakeSs();
        var before = ServerIds.Fingerprint(a);
        a.Delay = 123;
        Assert.Equal(before, ServerIds.Fingerprint(a));
    }

    [Fact]
    public void ChangingCredentialChangesId()
    {
        var a = MakeSs();
        var b = MakeSs();
        b.Password = "other";
        Assert.NotEqual(ServerIds.Fingerprint(a), ServerIds.Fingerprint(b));
    }

    [Fact]
    public void ResolveFindsServerById()
    {
        var a = MakeSs(host: "a.example.com");
        var b = MakeSs(host: "b.example.com");
        var list = new[] { a, b };
        var map = ServerIds.Assign(list);

        Assert.Same(a, ServerIds.Resolve(map[a], list));
        Assert.Same(b, ServerIds.Resolve(map[b], list));
        Assert.Null(ServerIds.Resolve("does-not-exist", list));
    }

    [Fact]
    public void ResolveHandlesDuplicateSuffix()
    {
        var a = MakeSs();
        var b = MakeSs();
        var list = new[] { a, b };
        var map = ServerIds.Assign(list);

        Assert.Same(a, ServerIds.Resolve(map[a], list));
        Assert.Same(b, ServerIds.Resolve(map[b], list));
    }
}
