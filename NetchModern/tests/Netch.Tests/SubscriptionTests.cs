using System.Collections.ObjectModel;
using Netch.Core.Abstractions;
using Netch.Core.Models;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class SubscriptionTests
{
    private static SubscriptionRepository NewRepo(out ServerRepository servers)
    {
        servers = new ServerRepository();
        return new SubscriptionRepository(servers, new HttpClient());
    }

    private static Subscription NewSub(string remark, string link)
    {
        var sub = new Subscription { Remark = remark, Link = link };
        sub.EnsureId();
        return sub;
    }

    [Fact]
    public void TwoUnnamedSubscriptionsHaveDifferentGroupKeys()
    {
        var a = NewSub("", "https://a.example.com/sub");
        var b = NewSub("", "https://b.example.com/sub");

        Assert.NotEqual(a.GroupKey, b.GroupKey);
        Assert.StartsWith("sub:", a.GroupKey);
        Assert.StartsWith("sub:", b.GroupKey);
    }

    [Fact]
    public void TwoSubscriptionsWithSameRemarkHaveDifferentGroupKeys()
    {
        var a = NewSub("机场", "https://a.example.com/sub");
        var b = NewSub("机场", "https://b.example.com/sub");

        Assert.NotEqual(a.GroupKey, b.GroupKey);
    }

    [Fact]
    public void ApplyOnOneDoesNotRemoveTheOthersServers()
    {
        var repo = NewRepo(out var servers);
        var a = NewSub("", "https://a.example.com/sub");
        var b = NewSub("", "https://b.example.com/sub");
        repo.Add(a);
        repo.Add(b);

        repo.Apply(a, new[] { MakeServer("a1"), MakeServer("a2") });
        repo.Apply(b, new[] { MakeServer("b1") });

        Assert.Equal(3, servers.Servers.Count);
        Assert.Equal(2, servers.Servers.Count(s => s.Group == a.GroupKey));
        Assert.Equal(1, servers.Servers.Count(s => s.Group == b.GroupKey));

        // Re-applying a must not touch b's servers.
        repo.Apply(a, new[] { MakeServer("a3") });
        Assert.Equal(2, servers.Servers.Count);
        Assert.Single(servers.Servers, s => s.Group == b.GroupKey);
    }

    [Fact]
    public void RemoveOnOneKeepsTheOthersServers()
    {
        var repo = NewRepo(out var servers);
        var a = NewSub("", "https://a.example.com/sub");
        var b = NewSub("", "https://b.example.com/sub");
        repo.Add(a);
        repo.Add(b);

        repo.Apply(a, new[] { MakeServer("a1") });
        repo.Apply(b, new[] { MakeServer("b1") });

        repo.Remove(a);

        Assert.Single(servers.Servers);
        Assert.Equal(b.GroupKey, servers.Servers[0].Group);
    }

    [Fact]
    public void Apply_ServerCount_CountsOnlyThisSubscriptionsServers()
    {
        var repo = NewRepo(out var servers);
        var existing = NewSub("existing", "https://existing.example.com/sub");
        repo.Add(existing);

        // Pool already holds servers from another subscription...
        repo.Apply(existing, new[] { MakeServer("old1"), MakeServer("old2") });

        // ...plus one plain manually-added server with no group.
        servers.Servers.Add(MakeServer("manual"));

        var previousTotal = servers.Servers.Count;

        var fresh = NewSub("fresh", "https://fresh.example.com/sub");
        repo.Add(fresh);
        repo.Apply(fresh, new[] { MakeServer("n1"), MakeServer("n2"), MakeServer("n3") });

        Assert.Equal(3, fresh.ServerCount);
        Assert.Equal(previousTotal + 3, servers.Servers.Count);
    }

    [Fact]
    public async Task LegacyMigrationRetagsServers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameplus-subtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var servers = new ServerRepository();
            var subs = new SubscriptionRepository(servers, new HttpClient());

            // Legacy subscription: no Id, remark blank, link used as the group key.
            var legacy = new Subscription { Remark = "", Link = "https://legacy.example.com/sub" };
            subs.Subscriptions.Add(legacy);

            var legacyServer = MakeServer("legacy1");
            legacyServer.Group = legacy.Link;
            servers.Servers.Add(legacyServer);

            var persistence = new JsonPersistenceService(servers, subs, dir);
            await persistence.SaveAsync();

            // Reload from disk — migration should assign an Id and re-tag the server.
            var servers2 = new ServerRepository();
            var subs2 = new SubscriptionRepository(servers2, new HttpClient());
            var persistence2 = new JsonPersistenceService(servers2, subs2, dir);
            await persistence2.LoadAsync();

            Assert.Single(subs2.Subscriptions);
            var migrated = subs2.Subscriptions[0];
            Assert.False(string.IsNullOrWhiteSpace(migrated.Id));
            Assert.Equal("sub:" + migrated.Id, migrated.GroupKey);

            Assert.Single(servers2.Servers);
            Assert.Equal(migrated.GroupKey, servers2.Servers[0].Group);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task LegacyMigrationAssignsSharedKeyServersToFirstSubscriptionOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameplus-subtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var servers = new ServerRepository();
            var subs = new SubscriptionRepository(servers, new HttpClient());

            var a = new Subscription { Remark = "机场", Link = "https://a.example.com/sub" };
            var b = new Subscription { Remark = "机场", Link = "https://b.example.com/sub" };
            subs.Subscriptions.Add(a);
            subs.Subscriptions.Add(b);

            var s1 = MakeServer("shared1");
            s1.Group = "机场";
            servers.Servers.Add(s1);

            var persistence = new JsonPersistenceService(servers, subs, dir);
            await persistence.SaveAsync();

            var servers2 = new ServerRepository();
            var subs2 = new SubscriptionRepository(servers2, new HttpClient());
            var persistence2 = new JsonPersistenceService(servers2, subs2, dir);
            await persistence2.LoadAsync();

            Assert.Equal(2, subs2.Subscriptions.Count);
            Assert.Single(servers2.Servers);
            Assert.Equal(subs2.Subscriptions[0].GroupKey, servers2.Servers[0].Group);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void DefaultRemarkUsesLinkHost()
    {
        Assert.Equal("未命名订阅 (example.com)", Subscription.DefaultRemarkFor("https://example.com/sub?token=1"));
        Assert.Equal("未命名订阅", Subscription.DefaultRemarkFor(""));
    }

    private static Server MakeServer(string remark) => new ShadowsocksServer
    {
        Remark = remark,
        Hostname = remark + ".example.com",
        Port = 8388,
        Password = "pw-" + remark,
        EncryptMethod = "aes-256-gcm"
    };
}
