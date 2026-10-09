using System.Runtime.CompilerServices;
using System.Text.Json;
using Netch.Core.Models;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;

namespace Netch.Host;

// DTOs mirror src/shared/types.ts in the Electron app. Serialized camelCase.
internal record ServerDto(string Id, string Kind, string Type, string Remark, string Hostname,
    int Port, string Group, int Delay);

internal record GameDto(string Id, string Name, string Icon, string Accent, string RegionText,
    int RuleCount, string[] Categories, bool Favorite, bool Custom,
    string EnglishName, string[] Aliases, string Category, string[] Regions, string[] Platforms,
    string[] ProxyDomains, string[] ProxyIps, string[] ProxyProcesses);

internal record SubscriptionDto(string Id, string Remark, string Link, int ServerCount, string? LastUpdated);

internal record AccelStatusDto(string State, int Latency, double Loss, int Jitter, int DurationSec,
    long DownloadBps, long UploadBps, string? GameId, string? NodeId, string? CaptureMode);

internal record LogDto(string Ts, string Level, string Message);

internal record SettingsDto(string Theme, string Language, bool MinimizeToTray);

/// <summary>
///     Assigns stable ids to reference-typed domain objects that have no id of their own. Kept for
///     subscriptions (which now carry their own persisted <see cref="Subscription.Id" />, so this is only
///     used as a fallback for objects created before the id was assigned).
/// </summary>
internal sealed class IdRegistry<T> where T : class
{
    private readonly ConditionalWeakTable<T, string> _ids = new();

    public string IdOf(T item) => _ids.GetValue(item, static _ => Guid.NewGuid().ToString("N")[..12]);

    public T? Resolve(string id, IEnumerable<T> pool)
    {
        foreach (var item in pool)
            if (IdOf(item) == id)
                return item;
        return null;
    }
}

internal static class Mapping
{
    public static string KindOf(Server s) => s switch
    {
        VlessServer => "vless",
        VMessServer => "vmess",
        TrojanServer => "trojan",
        ShadowsocksServer => "ss",
        Socks5Server => "socks5",
        Hysteria2Server => "hysteria2",
        WireGuardServer => "wireguard",
        _ => s.Type.ToLowerInvariant()
    };

    // Short chip label; Hysteria2 is abbreviated to HY2 to match the UI palette.
    public static string ChipOf(Server s) => s is Hysteria2Server ? "HY2" : s.Type;

    public static ServerDto ToDto(Server s, string id) =>
        new(id, KindOf(s), ChipOf(s), s.Remark, s.Hostname, s.Port, s.Group, s.Delay);

    public static GameDto ToDto(Game g) =>
        new(g.Id, g.Name, g.Icon, g.Accent, g.RegionText, g.RuleCount,
            BuildCategories(g), g.IsFavorite, g.IsCustom,
            g.EnglishName, g.Aliases.ToArray(), g.Category.ToString(), g.Regions.ToArray(),
            g.Platforms.ToArray(), g.ProxyDomains.ToArray(), g.ProxyIps.ToArray(),
            g.ProxyProcesses.ToArray());

    /// <summary>Builds a <see cref="Game" /> from client-edit fields (used by games.add / games.update).</summary>
    public static Game ToGame(JsonElement p, string? id = null)
    {
        var game = new Game { Id = id ?? "" };
        if (JsonParams.OptionalString(p, "name") is { } name) game.Name = name;
        if (JsonParams.OptionalString(p, "englishName") is { } en) game.EnglishName = en;
        if (JsonParams.OptionalString(p, "icon") is { } icon) game.Icon = icon;
        if (JsonParams.OptionalString(p, "category") is { } cat && Enum.TryParse<GameCategory>(cat, true, out var category))
            game.Category = category;
        game.Aliases = JsonParams.StringList(p, "aliases");
        game.Regions = JsonParams.StringList(p, "regions");
        game.Platforms = JsonParams.StringList(p, "platforms");
        game.ProxyDomains = JsonParams.StringList(p, "proxyDomains");
        game.ProxyIps = JsonParams.StringList(p, "proxyIps");
        game.ProxyProcesses = JsonParams.StringList(p, "proxyProcesses");
        return game;
    }

    private static string[] BuildCategories(Game g)
    {
        var cats = new List<string> { g.CategoryName };
        foreach (var p in g.Platforms)
            if (!cats.Contains(p))
                cats.Add(p);
        return cats.ToArray();
    }

    public static SubscriptionDto ToDto(Subscription s) =>
        new(s.Id, s.Remark, s.Link, s.ServerCount, s.LastUpdated?.ToString("o"));

    public static string StateName(AccelerationState state) => state switch
    {
        AccelerationState.Connecting => "connecting",
        AccelerationState.Accelerating => "accelerating",
        AccelerationState.Stopping => "stopping",
        _ => "idle"
    };

    public static AccelStatusDto StatusDto(Netch.Core.Abstractions.IAcceleratorService a, IReadOnlyList<Server> pool)
    {
        var st = a.Status;
        string? nodeId = null;
        if (a.CurrentNode is { } n)
        {
            var map = ServerIds.Assign(pool);
            if (map.TryGetValue(n, out var id))
                nodeId = id;
            else
                nodeId = ServerIds.Fingerprint(n);
        }

        return new AccelStatusDto(
            StateName(a.State),
            st.Latency,
            Math.Round(st.Loss, 1),
            st.Jitter,
            (int)st.Duration.TotalSeconds,
            st.DownloadRate,
            st.UploadRate,
            a.CurrentGame?.Id,
            nodeId,
            st.CaptureMode);
    }

    public static LogDto ToDto(LogEntry e) =>
        new(e.Timestamp.ToString("o"), e.Level.ToString().ToLowerInvariant(), e.Message);

    public static SettingsDto ToDto(Netch.Core.Models.Settings.AppSettings s) =>
        new(s.Theme.ToString().ToLowerInvariant(), s.Language, s.MinimizeToTray);
}
