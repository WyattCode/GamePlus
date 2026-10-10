using System.Net;
using System.Text;
using Netch.Core.Models.Games;
using Netch.Core.Services.Rules;
using Xunit;

namespace Netch.Tests.Rules;

public class GameRulesServiceTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameplus-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string RulesJson(int version, params string[] ids)
    {
        var games = string.Join(",", ids.Select(id =>
            $"{{ \"Id\": \"{id}\", \"Name\": \"{id}\", \"ProxyDomains\": [\"{id}.com\"] }}"));
        return $"{{ \"schema\": 1, \"version\": {version}, \"games\": [{games}] }}";
    }

    private static IEnumerable<Game> Bundled() => new List<Game>
    {
        new() { Id = "bundled1", Name = "内置游戏" }
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static GameRulesService MakeService(HttpMessageHandler handler, string cachePath,
        int bundledVersion = 1)
    {
        return new GameRulesService(new HttpClient(handler), cachePath, null, Bundled, bundledVersion);
    }

    [Fact]
    public async Task UpdateWritesCacheAndSwitchesToRemote()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var handler = new FakeHandler(_ => Ok(RulesJson(5, "remote1", "remote2")));
            var svc = MakeService(handler, cache);
            svc.LoadInitial();

            var result = await svc.UpdateAsync("https://example.com/rules.json", CancellationToken.None);

            Assert.Equal(RulesUpdateStatus.Updated, result.Status);
            Assert.Equal(5, result.Version);
            Assert.True(File.Exists(cache));
            Assert.Equal("remote", svc.Status.Source);

            var games = svc.CreateGames().ToList();
            Assert.Equal(2, games.Count);
            Assert.Equal("remote1", games[0].Id);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task SameOrOlderVersionIsUpToDate()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var handler = new FakeHandler(_ => Ok(RulesJson(1, "same")));
            var svc = MakeService(handler, cache);
            svc.LoadInitial();

            var result = await svc.UpdateAsync("https://example.com/rules.json", CancellationToken.None);

            Assert.Equal(RulesUpdateStatus.UpToDate, result.Status);
            Assert.False(File.Exists(cache));
            Assert.Equal("bundled", svc.Status.Source);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task InvalidJsonFailsAndKeepsActiveRules()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var handler = new FakeHandler(_ => Ok("{ not json"));
            var svc = MakeService(handler, cache);
            svc.LoadInitial();

            var result = await svc.UpdateAsync("https://example.com/rules.json", CancellationToken.None);

            Assert.Equal(RulesUpdateStatus.Failed, result.Status);
            Assert.Equal("bundled", svc.Status.Source);
            Assert.Single(svc.CreateGames());
            Assert.NotNull(svc.Status.LastError);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task NonHttpsUrlFailsWithoutRequest()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var handler = new FakeHandler(_ => Ok(RulesJson(9, "x")));
            var svc = MakeService(handler, cache);
            svc.LoadInitial();

            var result = await svc.UpdateAsync("ftp://example.com/rules.json", CancellationToken.None);

            Assert.Equal(RulesUpdateStatus.Failed, result.Status);
            Assert.Contains("https", result.Message);
            Assert.Equal(0, handler.Requests);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task OversizedBodyFails()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var big = new string('a', 6 * 1024 * 1024);
            var handler = new FakeHandler(_ => Ok(big));
            var svc = MakeService(handler, cache);
            svc.LoadInitial();

            var result = await svc.UpdateAsync("https://example.com/rules.json", CancellationToken.None);

            Assert.Equal(RulesUpdateStatus.Failed, result.Status);
            Assert.Contains("过大", result.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void LoadInitialUsesBundledWhenCacheIsOlder()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            File.WriteAllText(cache, RulesJson(1, "cached1"));

            var svc = MakeService(new FakeHandler(_ => Ok("")), cache, bundledVersion: 2);
            svc.LoadInitial();

            Assert.Equal("bundled", svc.Status.Source);
            Assert.Equal(2, svc.Status.Version);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void LoadInitialUsesNewerCache()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            File.WriteAllText(cache, RulesJson(7, "cached1"));

            var svc = MakeService(new FakeHandler(_ => Ok("")), cache);
            svc.LoadInitial();

            Assert.Equal("cached", svc.Status.Source);
            Assert.Equal(7, svc.Status.Version);
            Assert.Equal("cached1", svc.CreateGames().First().Id);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void LoadInitialIgnoresCorruptCache()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            File.WriteAllText(cache, "{ corrupt");

            var svc = MakeService(new FakeHandler(_ => Ok("")), cache);
            svc.LoadInitial();

            Assert.Equal("bundled", svc.Status.Source);
            Assert.True(File.Exists(cache)); // never deleted
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void CreateGamesReturnsFreshInstances()
    {
        var dir = NewTempDir();
        try
        {
            var cache = Path.Combine(dir, "game-rules.json");
            var svc = MakeService(new FakeHandler(_ => Ok("")), cache);
            svc.LoadInitial();

            var first = svc.CreateGames().ToList();
            var second = svc.CreateGames().ToList();

            Assert.NotSame(first[0], second[0]);
            first[0].Name = "mutated";
            Assert.NotEqual("mutated", svc.CreateGames().First().Name);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }
}
