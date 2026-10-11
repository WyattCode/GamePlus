using System.Text;
using System.Text.Json.Nodes;
using Netch.Core.Models.Games;
using Netch.Core.Services.Rules;
using Xunit;

namespace Netch.Tests.Rules;

public class SingleGameRuleTests
{
    private static readonly DateTime NowUtc = new(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Game CustomGame() => new()
    {
        Id = "custom-my-game",
        Name = "我的游戏",
        EnglishName = "My Game",
        Aliases = new List<string> { "mg" },
        Icon = "🎮",
        AccentColor = "#FF4655",
        Category = GameCategory.Pc,
        Regions = new List<string> { "国服", "国际服" },
        Platforms = new List<string> { "PC" },
        ProxyDomains = new List<string> { "example.com" },
        ProxyIps = new List<string> { "1.2.3.0/24" },
        ProxyProcesses = new List<string> { "mygame.exe" },
        DownloadDomains = new List<string> { "cdn.example.com" },
        IsCustom = true,
        IsFavorite = true
    };

    private static Game BuiltinGame() => new()
    {
        Id = "valorant",
        Name = "Valorant",
        Category = GameCategory.Hot,
        ProxyProcesses = new List<string> { "valorant.exe" }
    };

    private static JsonObject NewGameNode(string id) => new()
    {
        ["Id"] = id,
        ["Name"] = "X"
    };

    private static JsonObject Wrapper(JsonNode games) => new()
    {
        ["format"] = SingleGameRule.Format,
        ["schema"] = 1,
        ["version"] = 1,
        ["games"] = games
    };

    private static string ToUrlBase64(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // ---- envelope shape ----

    [Fact]
    public void ToJson_WrapsSingleGameInGamePlusEnvelope()
    {
        var json = SingleGameRule.ToJson(CustomGame(), NowUtc);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("gameplus-game-rule", (string?)root["format"]);
        Assert.Equal(1, (int?)root["schema"]);
        Assert.Equal(1, (int?)root["version"]);
        Assert.Equal("2024-06-01T12:00:00.000Z", (string?)root["exportedAt"]);
        Assert.Single((JsonArray)root["games"]!);
    }

    [Fact]
    public void ToJson_WritesUserIdForCustomGame_AndOmitsPerUserFlags()
    {
        var json = SingleGameRule.ToJson(CustomGame(), NowUtc);
        var gameNode = (JsonObject)((JsonArray)JsonNode.Parse(json)!["games"]!)[0]!;

        Assert.Equal("user-my-game", (string?)gameNode["Id"]);
        Assert.Null(gameNode["IsCustom"]);
        Assert.Null(gameNode["IsFavorite"]);
        // Same field shape as the rules exporter (PascalCase, string Category).
        Assert.Equal("Pc", (string?)gameNode["Category"]);
        Assert.Equal("mygame.exe", (string?)((JsonArray)gameNode["ProxyProcesses"]!)[0]);
    }

    [Fact]
    public void ToJson_KeepsBuiltinIdUnchanged()
    {
        var json = SingleGameRule.ToJson(BuiltinGame(), NowUtc);
        var gameNode = (JsonObject)((JsonArray)JsonNode.Parse(json)!["games"]!)[0]!;
        Assert.Equal("valorant", (string?)gameNode["Id"]);
    }

    // ---- round trips ----

    [Fact]
    public void Parse_RoundTripsJsonWithAllFields()
    {
        var parsed = SingleGameRule.Parse(SingleGameRule.ToJson(CustomGame(), NowUtc));

        Assert.Equal("user-my-game", parsed.Id);
        Assert.Equal("我的游戏", parsed.Name);
        Assert.Equal("My Game", parsed.EnglishName);
        Assert.Equal("mg", Assert.Single(parsed.Aliases));
        Assert.Equal("🎮", parsed.Icon);
        Assert.Equal("#FF4655", parsed.AccentColor);
        Assert.Equal(GameCategory.Pc, parsed.Category);
        Assert.Equal(new[] { "国服", "国际服" }, parsed.Regions);
        Assert.Equal(new[] { "PC" }, parsed.Platforms);
        Assert.Equal(new[] { "example.com" }, parsed.ProxyDomains);
        Assert.Equal(new[] { "1.2.3.0/24" }, parsed.ProxyIps);
        Assert.Equal(new[] { "mygame.exe" }, parsed.ProxyProcesses);
        Assert.Equal(new[] { "cdn.example.com" }, parsed.DownloadDomains);
    }

    [Fact]
    public void Parse_RoundTripsBuiltinGameIdUnchanged()
    {
        var parsed = SingleGameRule.Parse(SingleGameRule.ToJson(BuiltinGame(), NowUtc));
        Assert.Equal("valorant", parsed.Id);
    }

    [Fact]
    public void Parse_RoundTripsShareString()
    {
        var share = SingleGameRule.ToShareString(CustomGame(), NowUtc);

        Assert.StartsWith("gameplus-rule:v1:", share);
        var parsed = SingleGameRule.Parse(share);

        Assert.Equal("user-my-game", parsed.Id);
        Assert.Equal("我的游戏", parsed.Name);
        Assert.Equal("mygame.exe", Assert.Single(parsed.ProxyProcesses));
    }

    [Fact]
    public void ToShareString_UsesCompactBase64UrlPayload()
    {
        var share = SingleGameRule.ToShareString(CustomGame(), NowUtc);
        var payload = share[SingleGameRule.SharePrefix.Length..];

        Assert.DoesNotContain('=', payload);
        Assert.DoesNotContain('+', payload);
        Assert.DoesNotContain('/', payload);

        var padded = payload.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

        // Compact (no line breaks) and still the Game+ envelope.
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("gameplus-game-rule", (string?)root["format"]);
        Assert.Single((JsonArray)root["games"]!);
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundPayload()
    {
        var game = CustomGame();

        var fromJson = SingleGameRule.Parse("  \n" + SingleGameRule.ToJson(game, NowUtc) + "\n  ");
        Assert.Equal("user-my-game", fromJson.Id);

        var fromShare = SingleGameRule.Parse("\t" + SingleGameRule.ToShareString(game, NowUtc) + "\n");
        Assert.Equal("user-my-game", fromShare.Id);
    }

    // ---- rejections ----

    [Fact]
    public void Parse_RejectsEmptyText()
    {
        Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse("   "));
    }

    [Fact]
    public void Parse_RejectsGarbage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse("这不是规则"));
        Assert.Contains("无法识别的规则内容", ex.Message);
    }

    [Fact]
    public void Parse_RejectsWrongFormat()
    {
        var root = new JsonObject
        {
            ["format"] = "something-else",
            ["schema"] = 1,
            ["version"] = 1,
            ["games"] = new JsonArray(NewGameNode("x"))
        };
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse(root.ToJsonString()));
        Assert.Contains("无法识别的规则内容", ex.Message);
    }

    [Fact]
    public void Parse_RejectsPlainRulesFileWithoutFormat()
    {
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["version"] = 1,
            ["games"] = new JsonArray(NewGameNode("x"))
        };
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse(root.ToJsonString()));
        Assert.Contains("无法识别的规则内容", ex.Message);
    }

    [Fact]
    public void Parse_RejectsTwoGames()
    {
        var root = Wrapper(new JsonArray(NewGameNode("a"), NewGameNode("b")));
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse(root.ToJsonString()));
        Assert.Contains("无法识别的规则内容", ex.Message);
    }

    [Fact]
    public void Parse_RejectsOversizedText()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SingleGameRule.Parse(new string('x', SingleGameRule.MaxBytes + 1)));
        Assert.Equal("规则内容过大", ex.Message);
    }

    [Fact]
    public void Parse_RejectsOversizedSharePayload()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', SingleGameRule.MaxBytes + 1));
        var share = SingleGameRule.SharePrefix +
                    Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse(share));
        Assert.Equal("规则内容过大", ex.Message);
    }

    [Fact]
    public void Parse_RejectsCorruptedBase64()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SingleGameRule.Parse(SingleGameRule.SharePrefix + "!!!not-base64!!!"));
        Assert.Equal("规则分享串已损坏", ex.Message);
    }

    [Fact]
    public void Parse_RejectsSharePayloadThatIsNotJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SingleGameRule.Parse(SingleGameRule.SharePrefix + ToUrlBase64("not json at all")));
        Assert.Equal("规则分享串已损坏", ex.Message);
    }

    // ---- validation errors propagate from GameRulesFile.Parse ----

    [Fact]
    public void Parse_PropagatesGameRulesFileValidationErrors()
    {
        var game = CustomGame();
        game.ProxyProcesses = new List<string> { "notanexe" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => SingleGameRule.Parse(SingleGameRule.ToJson(game, NowUtc)));
        Assert.Contains(".exe", ex.Message);
    }

    [Fact]
    public void Parse_RejectsCustomIdInPayload()
    {
        var root = Wrapper(new JsonArray(new JsonObject { ["Id"] = "custom-foo", ["Name"] = "F" }));
        var ex = Assert.Throws<InvalidOperationException>(() => SingleGameRule.Parse(root.ToJsonString()));
        Assert.Contains("custom-", ex.Message);
    }

    // ---- id mapping ----

    [Fact]
    public void ExportId_MapsCustomPrefixToUser()
    {
        Assert.Equal("user-my-game", SingleGameRule.ExportId(new Game { Id = "custom-my-game" }));
        Assert.Equal("valorant", SingleGameRule.ExportId(new Game { Id = "valorant" }));
        Assert.Equal("user-", SingleGameRule.ExportId(new Game { Id = "custom-" }));
    }

    [Fact]
    public void TargetCustomId_StripsUserPrefixAndTrims()
    {
        Assert.Equal("custom-foo", SingleGameRule.TargetCustomId("user-foo"));
        Assert.Equal("custom-foo", SingleGameRule.TargetCustomId("foo"));
        Assert.Equal("custom-foo", SingleGameRule.TargetCustomId("  user-foo  "));
    }

    [Fact]
    public void TargetCustomId_CutsTo64Chars()
    {
        var target = SingleGameRule.TargetCustomId("user-" + new string('a', 100));
        Assert.Equal(64, target.Length);
        Assert.StartsWith("custom-", target);
    }
}
