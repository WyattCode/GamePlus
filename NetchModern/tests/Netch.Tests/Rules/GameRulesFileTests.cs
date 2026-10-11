using Netch.Core.Models.Games;
using Netch.Core.Services.Rules;
using Xunit;

namespace Netch.Tests.Rules;

public class GameRulesFileTests
{
    private const string Valid = """
    {
      "schema": 1,
      "version": 20261010,
      "updatedAt": "2026-10-10T00:00:00Z",
      "games": [
        {
          "Id": "valorant",
          "Name": "无畏契约",
          "EnglishName": "Valorant",
          "Category": "Hot",
          "Regions": ["国际服", "亚服"],
          "Platforms": ["PC"],
          "ProxyProcesses": ["VALORANT-Win64-Shipping.exe"],
          "ProxyDomains": ["riotgames.com"],
          "ProxyIps": ["1.2.3.0/24"],
          "Aliases": ["瓦"],
          "Icon": "🎯",
          "AccentColor": "#FF4655"
        }
      ]
    }
    """;

    [Fact]
    public void ParsesValidFile()
    {
        var file = GameRulesFile.Parse(Valid, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        Assert.Equal(1, file!.Schema);
        Assert.Equal(20261010, file.Version);
        Assert.Single(file.Games);
        Assert.Equal("valorant", file.Games[0].Id);
        Assert.Equal("无畏契约", file.Games[0].Name);
        Assert.Equal("Hot", file.Games[0].Category.ToString());
        Assert.False(file.Games[0].IsCustom);
        Assert.False(file.Games[0].IsFavorite);
    }

    [Fact]
    public void AcceptsNumericCategory()
    {
        var json = Valid.Replace("\"Category\": \"Hot\"", "\"Category\": 0");
        var file = GameRulesFile.Parse(json, out var error);
        Assert.Null(error);
        Assert.NotNull(file);
    }

    [Fact]
    public void RejectsEmpty()
    {
        Assert.Null(GameRulesFile.Parse("", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        Assert.Null(GameRulesFile.Parse("{ not json", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsWrongSchema()
    {
        var json = Valid.Replace("\"schema\": 1", "\"schema\": 2");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("schema", error);
    }

    [Fact]
    public void RejectsNonPositiveVersion()
    {
        var json = Valid.Replace("\"version\": 20261010", "\"version\": 0");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("version", error);
    }

    [Fact]
    public void RejectsEmptyGames()
    {
        var json = Valid.Replace("\"games\": [", "\"games\": [ ");
        var empty = """
        { "schema": 1, "version": 2, "games": [] }
        """;
        Assert.Null(GameRulesFile.Parse(empty, out var error));
        Assert.NotNull(error);
        Assert.NotNull(json);
    }

    [Fact]
    public void RejectsInvalidId()
    {
        var json = Valid.Replace("\"valorant\"", "\"Valorant!\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("Id", error);
    }

    [Fact]
    public void RejectsCustomPrefixedId()
    {
        var json = Valid.Replace("\"valorant\"", "\"custom-abc\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("custom-", error);
    }

    [Fact]
    public void RejectsDuplicateIds()
    {
        var json = """
        {
          "schema": 1,
          "version": 2,
          "games": [
            { "Id": "a1", "Name": "A" },
            { "Id": "a1", "Name": "B" }
          ]
        }
        """;
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("重复", error);
    }

    [Fact]
    public void RejectsEmptyName()
    {
        var json = Valid.Replace("\"Name\": \"无畏契约\"", "\"Name\": \"\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("名称", error);
    }

    [Fact]
    public void RejectsTooLongName()
    {
        var json = Valid.Replace("\"Name\": \"无畏契约\"", "\"Name\": \"" + new string('a', 65) + "\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("名称", error);
    }

    [Fact]
    public void RejectsProcessWithoutExeSuffix()
    {
        var json = Valid.Replace("VALORANT-Win64-Shipping.exe", "VALORANT-Win64-Shipping");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains(".exe", error);
    }

    [Fact]
    public void RejectsProcessWithPathSeparator()
    {
        var json = Valid.Replace("VALORANT-Win64-Shipping.exe", "sub\\\\VALORANT.exe");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("路径分隔符", error);
    }

    [Fact]
    public void RejectsInvalidIp()
    {
        var json = Valid.Replace("\"1.2.3.0/24\"", "\"not-an-ip\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("IP", error);
    }

    [Fact]
    public void RejectsDomainWithWhitespace()
    {
        var json = Valid.Replace("\"riotgames.com\"", "\"riot games.com\"");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("域名", error);
    }

    [Fact]
    public void DownloadDomains_MissingIsEmpty()
    {
        var file = GameRulesFile.Parse(Valid, out var error);
        Assert.Null(error);
        Assert.NotNull(file);
        Assert.Empty(file!.Games[0].DownloadDomains);
    }

    [Fact]
    public void DownloadDomains_RoundTrips()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"DownloadDomains\": [\"steamcontent.com\", \"steampipe.akamaized.net\"],");
        var file = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        Assert.Equal(new[] { "steamcontent.com", "steampipe.akamaized.net" }, file!.Games[0].DownloadDomains);
    }

    [Fact]
    public void RejectsDownloadDomainWithWhitespace()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"DownloadDomains\": [\"steam content.com\"],");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("域名", error);
    }

    [Fact]
    public void RejectsEmptyDownloadDomain()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"DownloadDomains\": [\"\"],");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("DownloadDomains", error);
    }

    [Fact]
    public void RejectsTooManyGames()
    {
        var games = string.Join(",", Enumerable.Range(0, 2001)
            .Select(i => $"{{ \"Id\": \"g{i}\", \"Name\": \"G{i}\" }}"));
        var json = $"{{ \"schema\": 1, \"version\": 2, \"games\": [{games}] }}";
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("过多", error);
    }

    [Fact]
    public void Compat_AbsentIsNull()
    {
        var file = GameRulesFile.Parse(Valid, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        Assert.Null(file!.Games[0].Compat);
    }

    [Fact]
    public void Compat_RoundTrips()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            """
            "ProxyDomains": ["riotgames.com"],
              "compat": { "recommendedMode": "tun", "note": "Riot Vanguard 为内核级反作弊。", "verified": true },
            """);
        var file = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        var compat = file!.Games[0].Compat;
        Assert.NotNull(compat);
        Assert.Equal("tun", compat!.RecommendedMode);
        Assert.Equal("Riot Vanguard 为内核级反作弊。", compat.Note);
        Assert.True(compat.Verified);
    }

    [Fact]
    public void Compat_VerifiedDefaultsToFalse()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"note\": \"未验证的说明\" },");
        var file = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        var compat = file!.Games[0].Compat;
        Assert.NotNull(compat);
        Assert.Null(compat!.RecommendedMode);
        Assert.Equal("未验证的说明", compat.Note);
        Assert.False(compat.Verified);
    }

    [Fact]
    public void Compat_AcceptsAllModes()
    {
        foreach (var mode in new[] { "auto", "process", "tun", "proxy" })
        {
            var json = Valid.Replace(
                "\"ProxyDomains\": [\"riotgames.com\"],",
                $"\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": {{ \"recommendedMode\": \"{mode}\", \"note\": \"n\" }},");
            var file = GameRulesFile.Parse(json, out var error);

            Assert.Null(error);
            Assert.NotNull(file);
            Assert.Equal(mode, file!.Games[0].Compat!.RecommendedMode);
        }
    }

    [Fact]
    public void Compat_AcceptsNullRecommendedMode()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"recommendedMode\": null, \"note\": \"n\" },");
        var file = GameRulesFile.Parse(json, out var error);

        Assert.Null(error);
        Assert.NotNull(file);
        Assert.Null(file!.Games[0].Compat!.RecommendedMode);
    }

    [Fact]
    public void RejectsCompatWithInvalidMode()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"recommendedMode\": \"magic\", \"note\": \"n\" },");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("recommendedMode", error);
    }

    [Fact]
    public void RejectsCompatWithEmptyNote()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"note\": \"\" },");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("note", error);
    }

    [Fact]
    public void RejectsCompatWithTooLongNote()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"note\": \"" + new string('a', 201) + "\" },");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("note", error);
    }

    [Fact]
    public void RejectsCompatWithoutNote()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": { \"recommendedMode\": \"auto\" },");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("note", error);
    }

    [Fact]
    public void RejectsCompatThatIsNotAnObject()
    {
        var json = Valid.Replace(
            "\"ProxyDomains\": [\"riotgames.com\"],",
            "\"ProxyDomains\": [\"riotgames.com\"],\n          \"compat\": \"auto\",");
        Assert.Null(GameRulesFile.Parse(json, out var error));
        Assert.Contains("compat", error);
    }
}
