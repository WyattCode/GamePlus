using System.Text.Json;
using Netch.Core.Models.Games;
using Netch.Host;
using Xunit;

namespace Netch.Tests;

public class MappingTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Theory]
    [InlineData("Hot", GameCategory.Hot)]
    [InlineData("hot", GameCategory.Hot)]
    [InlineData("PC", GameCategory.Pc)]
    [InlineData("pc", GameCategory.Pc)]
    [InlineData("Console", GameCategory.Console)]
    [InlineData("console", GameCategory.Console)]
    [InlineData("Platform", GameCategory.Platform)]
    [InlineData("platform", GameCategory.Platform)]
    public void ToGame_ParsesCategoryCaseInsensitively(string value, GameCategory expected)
    {
        var game = Mapping.ToGame(Json($$"""{"name":"x","category":"{{value}}"}"""));
        Assert.Equal(expected, game.Category);
    }

    [Fact]
    public void ToGame_RejectsUnknownCategory()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Mapping.ToGame(Json("""{"name":"x","category":"Bogus"}""")));
        Assert.Contains("Bogus", ex.Message);
    }

    [Fact]
    public void ToGame_AbsentCategory_DefaultsToPcOnAdd()
    {
        var game = Mapping.ToGame(Json("""{"name":"x"}"""));
        Assert.Equal(GameCategory.Pc, game.Category);
    }

    [Fact]
    public void ToGame_AbsentCategory_KeepsExistingOnUpdate()
    {
        var existing = new Game { Id = "g1", Name = "old", Category = GameCategory.Console };
        var game = Mapping.ToGame(Json("""{"name":"new"}"""), "g1", existing);
        Assert.Equal(GameCategory.Console, game.Category);
        Assert.Equal("new", game.Name);
    }

    [Fact]
    public void ToGame_Merge_PreservesFieldsAbsentFromParams()
    {
        var existing = new Game
        {
            Id = "g1",
            Name = "old",
            Icon = "🎮",
            AccentColor = "#123456",
            EnglishName = "Old",
            Category = GameCategory.Hot,
            Aliases = new List<string> { "a", "b" },
            Regions = new List<string> { "亚服" },
            Platforms = new List<string> { "Steam" },
            ProxyDomains = new List<string> { "a.com" },
            ProxyIps = new List<string> { "1.2.3.4" },
            ProxyProcesses = new List<string> { "game.exe" }
        };

        // Only name + regions present.
        var merged = Mapping.ToGame(Json("""{"name":"new","regions":["美服"]}"""), "g1", existing);

        Assert.Equal("new", merged.Name);
        Assert.Equal("🎮", merged.Icon);
        Assert.Equal("#123456", merged.AccentColor);
        Assert.Equal("Old", merged.EnglishName);
        Assert.Equal(GameCategory.Hot, merged.Category);
        Assert.Equal(new[] { "a", "b" }, merged.Aliases);
        Assert.Equal(new[] { "美服" }, merged.Regions);
        Assert.Equal(new[] { "Steam" }, merged.Platforms);
        Assert.Equal(new[] { "a.com" }, merged.ProxyDomains);
        Assert.Equal(new[] { "1.2.3.4" }, merged.ProxyIps);
        Assert.Equal(new[] { "game.exe" }, merged.ProxyProcesses);
    }

    [Fact]
    public void ToGame_Merge_OverwritesPresentFields()
    {
        var existing = new Game { Id = "g1", Name = "old", Icon = "🎮", Category = GameCategory.Pc };
        var merged = Mapping.ToGame(
            Json("""{"name":"new","icon":"🕹️","category":"Console","aliases":["x"]}"""), "g1", existing);
        Assert.Equal("new", merged.Name);
        Assert.Equal("🕹️", merged.Icon);
        Assert.Equal(GameCategory.Console, merged.Category);
        Assert.Equal(new[] { "x" }, merged.Aliases);
    }
}
