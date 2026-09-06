using System.Runtime.CompilerServices;
using Netch.Core.Models;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;

namespace Netch.Host;

// DTOs mirror src/shared/types.ts in the Electron app. Serialized camelCase.
internal record ServerDto(string Id, string Kind, string Type, string Remark, string Hostname,
    int Port, string Group, int Delay);

internal record GameDto(string Id, string Name, string Icon, string Accent, string RegionText,
    int RuleCount, string[] Categories, bool Favorite, bool Custom);

internal record SubscriptionDto(string Id, string Remark, string Link, int ServerCount, string? LastUpdated);

internal record AccelStatusDto(string State, int Latency, double Loss, int Jitter, int DurationSec,
    long DownloadBps, long UploadBps, string? GameId, string? NodeId);

internal record LogDto(string Ts, string Level, string Message);

internal record SettingsDto(string Theme, string Language, bool MinimizeToTray);

/// <summary>Assigns stable ids to reference-typed domain objects that have no id of their own (servers, subs).</summary>
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

    public static ServerDto ToDto(Server s, IdRegistry<Server> ids) =>
        new(ids.IdOf(s), KindOf(s), ChipOf(s), s.Remark, s.Hostname, s.Port, s.Group, s.Delay);

    public static GameDto ToDto(Game g) =>
        new(g.Id, g.Name, g.Icon, g.Accent, g.RegionText, g.RuleCount,
            BuildCategories(g), g.IsFavorite, g.IsCustom);

    private static string[] BuildCategories(Game g)
    {
        var cats = new List<string> { g.CategoryName };
        foreach (var p in g.Platforms)
            if (!cats.Contains(p))
                cats.Add(p);
        return cats.ToArray();
    }

    public static SubscriptionDto ToDto(Subscription s, IdRegistry<Subscription> ids) =>
        new(ids.IdOf(s), s.Remark, s.Link, s.ServerCount, s.LastUpdated?.ToString("o"));

    public static string StateName(AccelerationState state) => state switch
    {
        AccelerationState.Connecting => "connecting",
        AccelerationState.Accelerating => "accelerating",
        AccelerationState.Stopping => "stopping",
        _ => "idle"
    };

    public static AccelStatusDto StatusDto(Netch.Core.Abstractions.IAcceleratorService a, IdRegistry<Server> ids)
    {
        var st = a.Status;
        return new AccelStatusDto(
            StateName(a.State),
            st.Latency,
            Math.Round(st.Loss, 1),
            st.Jitter,
            (int)st.Duration.TotalSeconds,
            st.DownloadRate,
            st.UploadRate,
            a.CurrentGame?.Id,
            a.CurrentNode is { } n ? ids.IdOf(n) : null);
    }

    public static LogDto ToDto(LogEntry e) =>
        new(e.Timestamp.ToString("o"), e.Level.ToString().ToLowerInvariant(), e.Message);

    public static SettingsDto ToDto(Netch.Core.Models.Settings.AppSettings s) =>
        new(s.Theme.ToString().ToLowerInvariant(), s.Language, s.MinimizeToTray);
}
