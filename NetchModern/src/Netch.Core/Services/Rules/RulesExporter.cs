using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.Rules;

/// <summary>
///     Serializes a set of <see cref="Game" /> objects into the remote game-rules file format
///     understood by <see cref="GameRulesFile.Parse" /> (see docs/game-rules.md). Custom games are
///     skipped and <c>IsCustom</c> / <c>IsFavorite</c> are omitted from the output so the produced
///     file can never inject a "custom" game back into a user's library. The caller's <see cref="Game" />
///     instances are never mutated.
/// </summary>
public static class RulesExporter
{
    /// <summary>
    ///     Options used for serializing individual <see cref="Game" /> objects. Property names stay
    ///     PascalCase (matching custom-games.json) and <c>IsCustom</c> / <c>IsFavorite</c> are removed
    ///     from the contract entirely.
    /// </summary>
    private static readonly JsonSerializerOptions GameOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { IgnorePerUserProperties }
        }
    };

    private static void IgnorePerUserProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(Game))
            return;

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var name = typeInfo.Properties[i].Name;
            if (string.Equals(name, nameof(Game.IsCustom), StringComparison.Ordinal) ||
                string.Equals(name, nameof(Game.IsFavorite), StringComparison.Ordinal))
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }

    /// <summary>
    ///     Builds the rules-file JSON for <paramref name="games" />. <paramref name="version" /> must be
    ///     a positive integer; <paramref name="updatedAt" /> is written as ISO-8601 UTC.
    /// </summary>
    public static string Export(IEnumerable<Game> games, int version, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "version must be a positive integer");

        var gamesArray = new JsonArray();
        foreach (var game in games)
        {
            if (game is null)
                continue;
            if (game.IsCustom)
                continue;

            // Serialize a copy so the caller's object is never mutated.
            var copy = CopyForExport(game);
            var node = JsonSerializer.SerializeToNode(copy, GameOptions);
            gamesArray.Add(node);
        }

        var file = new JsonObject
        {
            ["schema"] = GameRulesFile.SupportedSchema,
            ["version"] = version,
            ["updatedAt"] = updatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["games"] = gamesArray
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return file.ToJsonString(options);
    }

    /// <summary>
    ///     Copies every property of <see cref="Game" /> so the caller's instance is never mutated.
    ///     Only per-user / runtime state is intentionally left out:
    ///     <list type="bullet">
    ///         <item><see cref="Game.IsCustom" /> — user-added flag, must never leak into a rules file.</item>
    ///         <item><see cref="Game.IsFavorite" /> — per-user UI state.</item>
    ///     </list>
    ///     Computed read-only properties (<c>RuleCount</c>, <c>CategoryName</c>, <c>RegionText</c>,
    ///     <c>Accent</c>) are <see cref="JsonIgnoreAttribute" />-marked and therefore not serialized;
    ///     they are not copied either. Lists are non-null by contract, so they are copied directly.
    /// </summary>
    private static Game CopyForExport(Game source)
    {
        return new Game
        {
            Id = source.Id,
            Name = source.Name,
            EnglishName = source.EnglishName,
            Aliases = new List<string>(source.Aliases),
            Icon = source.Icon,
            AccentColor = source.AccentColor,
            Category = source.Category,
            Regions = new List<string>(source.Regions),
            Platforms = new List<string>(source.Platforms),
            ProxyDomains = new List<string>(source.ProxyDomains),
            ProxyIps = new List<string>(source.ProxyIps),
            ProxyProcesses = new List<string>(source.ProxyProcesses),
            IsCustom = false,
            IsFavorite = false
        };
    }
}
