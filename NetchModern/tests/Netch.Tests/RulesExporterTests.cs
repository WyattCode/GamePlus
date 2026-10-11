using System.Text.Json;
using Netch.Core.Models.Games;
using Netch.Core.Services;
using Netch.Core.Services.Rules;
using Xunit;

namespace Netch.Tests;

public class RulesExporterTests
{
    [Fact]
    public void Export_RoundTripsThroughGameRulesFileParse()
    {
        var games = SampleGames.Create();
        var expectedCount = games.Count(g => !g.IsCustom);
        var updatedAt = DateTimeOffset.UtcNow;

        var json = RulesExporter.Export(games, SampleGames.RulesVersion, updatedAt);

        var parsed = GameRulesFile.Parse(json, out var error);
        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(GameRulesFile.SupportedSchema, parsed!.Schema);
        Assert.Equal(SampleGames.RulesVersion, parsed.Version);
        Assert.Equal(expectedCount, parsed.Games.Count);

        // updatedAt round-trips (ISO-8601 UTC, second precision is enough).
        Assert.NotNull(parsed.UpdatedAt);
        Assert.Equal(
            updatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            parsed.UpdatedAt!.Value.ToString("yyyy-MM-ddTHH:mm:ss"));

        // Ids are preserved and no custom game leaked in.
        var expectedIds = games.Where(g => !g.IsCustom).Select(g => g.Id).ToList();
        var actualIds = parsed.Games.Select(g => g.Id).ToList();
        Assert.Equal(expectedIds, actualIds);
        Assert.DoesNotContain(parsed.Games, g => g.IsCustom);
        Assert.DoesNotContain(parsed.Games, g => g.IsFavorite);
    }

    [Fact]
    public void Export_SkipsCustomGames()
    {
        var games = SampleGames.Create();
        var custom = new Game
        {
            Id = "custom-test",
            Name = "Custom Test",
            IsCustom = true
        };
        var withCustom = games.Concat(new[] { custom }).ToList();

        var json = RulesExporter.Export(withCustom, 1, DateTimeOffset.UtcNow);
        var parsed = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.DoesNotContain(parsed!.Games, g => g.Id == "custom-test");
    }

    [Fact]
    public void Export_RejectsNonPositiveVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RulesExporter.Export(SampleGames.Create(), 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Export_ProducesIndentedJson()
    {
        var json = RulesExporter.Export(SampleGames.Create(), 1, DateTimeOffset.UtcNow);
        Assert.Contains("\n", json);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("schema", out _));
        Assert.True(doc.RootElement.TryGetProperty("version", out _));
        Assert.True(doc.RootElement.TryGetProperty("updatedAt", out _));
        Assert.True(doc.RootElement.TryGetProperty("games", out _));
    }

    [Fact]
    public void Export_TopLevelKeysAreCamelCase()
    {
        var json = RulesExporter.Export(SampleGames.Create(), 1, DateTimeOffset.UtcNow);
        using var doc = JsonDocument.Parse(json);

        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "schema", "version", "updatedAt", "games" }, names);

        // No PascalCase top-level keys.
        Assert.False(doc.RootElement.TryGetProperty("Schema", out _));
        Assert.False(doc.RootElement.TryGetProperty("Version", out _));
        Assert.False(doc.RootElement.TryGetProperty("UpdatedAt", out _));
        Assert.False(doc.RootElement.TryGetProperty("Games", out _));
    }

    [Fact]
    public void Export_GameObjectsKeepPascalCaseProperties()
    {
        var json = RulesExporter.Export(SampleGames.Create(), 1, DateTimeOffset.UtcNow);
        using var doc = JsonDocument.Parse(json);

        var first = doc.RootElement.GetProperty("games")[0];
        Assert.True(first.TryGetProperty("Id", out _));
        Assert.True(first.TryGetProperty("Name", out _));
        Assert.True(first.TryGetProperty("Category", out var category));
        // Category is written as the enum name string.
        Assert.Equal(JsonValueKind.String, category.ValueKind);
    }

    [Fact]
    public void Export_WritesChineseAndEmojiLiterally()
    {
        var games = new List<Game>
        {
            new()
            {
                Id = "genshin",
                Name = "原神",
                Category = GameCategory.Hot,
                Aliases = new List<string> { "Genshin Impact 🎮" }
            }
        };

        var json = RulesExporter.Export(games, 1, DateTimeOffset.UtcNow);

        Assert.Contains("原神", json);
        // System.Text.Json always escapes astral-plane chars (emoji) as surrogate pairs, even with
        // UnsafeRelaxedJsonEscaping; that is still valid JSON. Only BMP text (Chinese) must be literal.
        Assert.DoesNotContain("\\u539F", json);

        // Still parseable.
        var parsed = GameRulesFile.Parse(json, out var error);
        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal("原神", parsed!.Games[0].Name);
    }

    [Fact]
    public void Export_DoesNotMutateCallerGames()
    {
        var game = new Game
        {
            Id = "genshin",
            Name = "原神",
            Category = GameCategory.Hot,
            IsFavorite = true,
            IsCustom = false
        };
        var games = new List<Game> { game };

        var json = RulesExporter.Export(games, 1, DateTimeOffset.UtcNow);

        // Original object untouched.
        Assert.True(game.IsFavorite);
        Assert.False(game.IsCustom);

        // Output omits per-user state entirely.
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("games")[0];
        Assert.False(first.TryGetProperty("IsFavorite", out _));
        Assert.False(first.TryGetProperty("IsCustom", out _));
    }

    [Fact]
    public void Export_UpdatedAtIsIso8601UtcWithZ()
    {
        var updatedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(8));
        var json = RulesExporter.Export(SampleGames.Create(), 1, updatedAt);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("updatedAt").GetString();
        Assert.NotNull(value);
        Assert.EndsWith("Z", value);
        Assert.StartsWith("2024-01-01T19:04:05", value);
    }

    [Fact]
    public void Export_PreservesEnglishNameIconAndAccentColor()
    {
        var games = SampleGames.Create().Where(g => !g.IsCustom).ToList();
        Assert.NotEmpty(games);

        var json = RulesExporter.Export(games, SampleGames.RulesVersion, DateTimeOffset.UtcNow);
        var parsed = GameRulesFile.Parse(json, out var error);
        Assert.Null(error);
        Assert.NotNull(parsed);

        var byId = parsed!.Games.ToDictionary(g => g.Id);
        foreach (var original in games)
        {
            Assert.True(byId.TryGetValue(original.Id, out var roundTripped),
                $"game '{original.Id}' missing from parsed output");

            Assert.Equal(original.EnglishName, roundTripped!.EnglishName);
            Assert.Equal(original.Icon, roundTripped.Icon);
            Assert.Equal(original.AccentColor, roundTripped.AccentColor);
        }
    }

    [Fact]
    public void Export_PreservesDownloadDomains()
    {
        var games = SampleGames.Create().Where(g => !g.IsCustom).ToList();
        Assert.NotEmpty(games);

        var json = RulesExporter.Export(games, SampleGames.RulesVersion, DateTimeOffset.UtcNow);
        var parsed = GameRulesFile.Parse(json, out var error);
        Assert.Null(error);
        Assert.NotNull(parsed);

        var byId = parsed!.Games.ToDictionary(g => g.Id);
        foreach (var original in games)
        {
            Assert.True(byId.TryGetValue(original.Id, out var roundTripped),
                $"game '{original.Id}' missing from parsed output");

            Assert.Equal(original.DownloadDomains, roundTripped!.DownloadDomains);
        }

        // Steam carries the CDN download domains through the round-trip.
        Assert.Equal(new[] { "steamcontent.com", "steampipe.akamaized.net" }, byId["steam"].DownloadDomains);
    }
}
