using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

public class GameRepositoryTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameplus-gametest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AddCustomGamePersistsToDisk()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "custom-games.json");
            var repo = new GameRepository(new FakeSettings(), path);
            repo.AddCustomGame(new Game { Name = "My Game" });

            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            Assert.Contains("My Game", json);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void AddCustomGameRollsBackWhenSaveFails()
    {
        var dir = NewTempDir();
        try
        {
            // Point the repository at a path whose parent is a FILE, so the write fails.
            var blocker = Path.Combine(dir, "blocker");
            File.WriteAllText(blocker, "x");
            var path = Path.Combine(blocker, "custom-games.json");

            var repo = new GameRepository(new FakeSettings(), path);
            var before = repo.Games.Count;

            var game = new Game { Name = "Will Fail" };
            var ex = Assert.Throws<InvalidOperationException>(() => repo.AddCustomGame(game));
            Assert.Contains("保存自定义游戏失败", ex.Message);
            Assert.Equal(before, repo.Games.Count);
            Assert.DoesNotContain(game, repo.Games);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void RemoveGameRollsBackWhenSaveFails()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "custom-games.json");
            var repo = new GameRepository(new FakeSettings(), path);
            var game = new Game { Name = "Keep Me" };
            repo.AddCustomGame(game);

            // Replace the file with a directory so the next write fails.
            File.Delete(path);
            Directory.CreateDirectory(path);

            var ex = Assert.Throws<InvalidOperationException>(() => repo.RemoveGame(game));
            Assert.Contains("保存自定义游戏失败", ex.Message);
            Assert.Contains(game, repo.Games);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void CorruptCustomGamesFileIsRenamedAside()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "custom-games.json");
            File.WriteAllText(path, "{ this is not valid json");

            var repo = new GameRepository(new FakeSettings(), path);

            // The corrupt file should have been renamed, not silently overwritten.
            Assert.False(File.Exists(path));
            Assert.NotEmpty(Directory.GetFiles(dir, "custom-games.json.corrupt-*"));

            // And the repository should still be usable.
            repo.AddCustomGame(new Game { Name = "After Corrupt" });
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void ToggleFavoriteDoesNotSaveSettings()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "custom-games.json");
            var settings = new CountingSettings();
            var repo = new GameRepository(settings, path);
            var game = repo.Games.First();

            repo.ToggleFavorite(game);

            Assert.Equal(0, settings.SaveCount);
            Assert.True(game.IsFavorite);
            Assert.Contains(game.Id, settings.Settings.FavoriteGames);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task JsonSettingsServiceConcurrentSavesDoNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var svc = new JsonSettingsService(path);
            await svc.LoadAsync();

            var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() => svc.SaveAsync())).ToArray();
            await Task.WhenAll(tasks);

            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            // Must be valid JSON (no interleaved writes).
            using var doc = JsonDocument.Parse(json);
            Assert.NotNull(doc);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public async Task JsonPersistenceServiceConcurrentSavesDoNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var servers = new ServerRepository();
            var modes = new ModeRepository();
            var subs = new SubscriptionRepository(servers, new HttpClient());
            var svc = new JsonPersistenceService(servers, modes, subs, dir);

            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() => svc.SaveAsync())).ToArray();
            await Task.WhenAll(tasks);

            Assert.True(File.Exists(Path.Combine(dir, "servers.json")));
            Assert.True(File.Exists(Path.Combine(dir, "modes.json")));
            Assert.True(File.Exists(Path.Combine(dir, "subscriptions.json")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignored */ }
        }
    }

    private sealed class CountingSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int SaveCount { get; private set; }
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
