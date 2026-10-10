using System.Text.Json;
using Netch.Core.Models;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Netch.Core.Services.Diagnostics;
using Netch.Core.Services.Library;

namespace Netch.Host;

// DTOs mirror src/shared/types.ts in the Electron app. Serialized camelCase.
internal record ServerDto(string Id, string Kind, string Type, string Remark, string Hostname,
    int Port, string Group, int Delay);

internal record GameDto(string Id, string Name, string Icon, string Accent, string RegionText,
    int RuleCount, string[] Categories, bool Favorite, bool Custom,
    string EnglishName, string[] Aliases, string Category, string[] Regions, string[] Platforms,
    string[] ProxyDomains, string[] ProxyIps, string[] ProxyProcesses, bool AutoAccelerate,
    bool HasServerChoice, string? ServerChoice);

internal record SubscriptionDto(string Id, string Remark, string Link, int ServerCount, string? LastUpdated);

internal record AccelStatusDto(string State, int Latency, double Loss, int Jitter, int DurationSec,
    long DownloadBps, long UploadBps, string? GameId, string? NodeId, string? CaptureMode);

internal record LogDto(string Ts, string Level, string Message);

internal record SettingsDto(string Theme, string Language, bool MinimizeToTray, string RulesUrl,
    bool AutoAccelerate, ExtraAppDto[] ExtraApps, AdvancedSettingsDto Advanced);

internal record ExtraAppDto(string Name, string Process, string Mode, bool Enabled);

internal record AdvancedSettingsDto(string TunDns, bool MeasureEffect, int RegionPenaltyMs, int LocalSocksPort);

internal record EffectSnapshotDto(int Latency, double Loss, int Jitter, int Samples);

internal record EffectPointDto(long Ts, int Accel);

internal record EffectReportDto(bool HasTarget, string? Target, EffectSnapshotDto? Baseline,
    EffectSnapshotDto? Current, int? LatencyPct, string Summary, EffectPointDto[] Series, bool Measuring,
    string? BaselineMeasuredAt, bool BaselineFromCache);

internal record RankedNodeDto(string Id, string Remark, string Type, int Delay, string? Region,
    string? RegionName, bool RegionMatch, int Score, string Note);

internal record NodeRankingDto(string? RecommendedId, string Reason, string? PreferenceLabel,
    string[] PreferredRegions, RankedNodeDto[] Candidates, string? ServerChoice);

internal record DiagnosticItemDto(string Id, string Title, string Status, string Detail,
    string? Suggestion, string? FixAction);

internal record DiagnosticReportDto(DiagnosticItemDto[] Items, string Summary, string RanAt,
    string? ErrorExplanation);

internal record DetectedGameDto(string Source, string SourceId, string Name, string? InstallDir,
    string[] Executables, string? GameId, string? GameName, string? MatchedBy);

internal record LibraryScanDto(DetectedGameDto[] Games, string[] Errors, string ScannedAt);

internal record RulesStatusDto(int Version, string Source, int GameCount, string? LastCheck,
    string? LastError, string? LastResult, string Url);

internal record RulesUpdateDto(string Status, string Message, int Version, GameDto[] Games);

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

    public static GameDto ToDto(Game g) => ToDto(g, null, null);

    /// <summary>
    ///     Builds the game DTO. <paramref name="autoAccelerateIds" /> is the set of game ids with the
    ///     per-game auto-acceleration toggle enabled (null → false for every game).
    ///     <paramref name="serverChoices" /> maps game ids to their 国服/国际服 choice (null → default).
    /// </summary>
    public static GameDto ToDto(Game g, IReadOnlyCollection<string>? autoAccelerateIds,
        IReadOnlyDictionary<string, string>? serverChoices)
    {
        var hasChoice = GameRegionPreference.HasServerChoice(g);
        string? choice = null;
        if (hasChoice)
        {
            var raw = serverChoices is not null && serverChoices.TryGetValue(g.Id, out var v) ? v : null;
            choice = string.Equals(raw, "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "intl";
        }

        return new GameDto(g.Id, g.Name, g.Icon, g.Accent, g.RegionText, g.RuleCount,
            BuildCategories(g), g.IsFavorite, g.IsCustom,
            g.EnglishName, g.Aliases.ToArray(), g.Category.ToString(), g.Regions.ToArray(),
            g.Platforms.ToArray(), g.ProxyDomains.ToArray(), g.ProxyIps.ToArray(),
            g.ProxyProcesses.ToArray(),
            autoAccelerateIds is not null && autoAccelerateIds.Contains(g.Id),
            hasChoice, choice);
    }

    /// <summary>
    ///     Builds a <see cref="Game" /> from client-edit fields (used by games.add / games.update).
    ///     When <paramref name="existing" /> is supplied the fields absent from <paramref name="p" /> are
    ///     carried over from it (merge semantics), so an editor that doesn't send every field can't wipe
    ///     data such as <c>Icon</c>, <c>Aliases</c> or <c>AccentColor</c>.
    /// </summary>
    public static Game ToGame(JsonElement p, string? id = null, Game? existing = null)
    {
        var game = new Game { Id = id ?? existing?.Id ?? "" };
        if (existing is not null)
        {
            game.Name = existing.Name;
            game.EnglishName = existing.EnglishName;
            game.Icon = existing.Icon;
            game.AccentColor = existing.AccentColor;
            game.Category = existing.Category;
            game.Aliases = new List<string>(existing.Aliases);
            game.Regions = new List<string>(existing.Regions);
            game.Platforms = new List<string>(existing.Platforms);
            game.ProxyDomains = new List<string>(existing.ProxyDomains);
            game.ProxyIps = new List<string>(existing.ProxyIps);
            game.ProxyProcesses = new List<string>(existing.ProxyProcesses);
            game.IsCustom = existing.IsCustom;
            game.IsFavorite = existing.IsFavorite;
        }

        if (JsonParams.OptionalString(p, "name") is { } name) game.Name = name;
        if (JsonParams.OptionalString(p, "englishName") is { } en) game.EnglishName = en;
        if (JsonParams.OptionalString(p, "icon") is { } icon) game.Icon = icon;
        if (JsonParams.OptionalString(p, "accent") is { } accent) game.AccentColor = accent;
        if (JsonParams.HasProperty(p, "category"))
        {
            var cat = JsonParams.OptionalString(p, "category");
            if (string.IsNullOrWhiteSpace(cat))
            {
                // Absent/blank → keep existing value on update, default Pc on add.
                if (existing is null) game.Category = GameCategory.Pc;
            }
            else if (Enum.TryParse<GameCategory>(cat, true, out var category))
            {
                game.Category = category;
            }
            else
            {
                throw new InvalidOperationException(
                    $"未知的游戏分类：{cat}（可选：Hot / Pc / Console / Platform）");
            }
        }
        else if (existing is null)
        {
            game.Category = GameCategory.Pc;
        }

        if (JsonParams.HasProperty(p, "aliases")) game.Aliases = JsonParams.StringList(p, "aliases");
        if (JsonParams.HasProperty(p, "regions")) game.Regions = JsonParams.StringList(p, "regions");
        if (JsonParams.HasProperty(p, "platforms")) game.Platforms = JsonParams.StringList(p, "platforms");
        if (JsonParams.HasProperty(p, "proxyDomains")) game.ProxyDomains = JsonParams.StringList(p, "proxyDomains");
        if (JsonParams.HasProperty(p, "proxyIps")) game.ProxyIps = JsonParams.StringList(p, "proxyIps");
        if (JsonParams.HasProperty(p, "proxyProcesses")) game.ProxyProcesses = JsonParams.StringList(p, "proxyProcesses");
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
        new(s.Theme.ToString().ToLowerInvariant(), s.Language, s.MinimizeToTray, s.RulesUrl,
            s.AutoAccelerate, s.ExtraApps.Select(ToDto).ToArray(), ToAdvancedDto(s));

    /// <summary>
    ///     Builds the advanced-settings DTO. <c>TunDns</c> lives on <see cref="AppSettings.Dns" />, not on
    ///     <see cref="AdvancedSettings" />, so the whole settings object is needed here.
    /// </summary>
    public static AdvancedSettingsDto ToAdvancedDto(Netch.Core.Models.Settings.AppSettings s) =>
        new(s.Dns.OtherDns, s.Advanced.MeasureEffect, s.Advanced.RegionPenaltyMs, s.Advanced.LocalSocksPort);

    public static ExtraAppDto ToDto(ExtraApp a) =>
        new(a.Name, a.Process, a.Mode == ExtraAppMode.Bypass ? "bypass" : "proxy", a.Enabled);

    public static EffectSnapshotDto? ToDto(EffectSnapshot? s) =>
        s is null ? null : new EffectSnapshotDto(s.Latency, s.Loss, s.Jitter, s.Samples);

    public static EffectReportDto ToDto(EffectReport r) =>
        new(r.HasTarget, r.Target, ToDto(r.Baseline), ToDto(r.Current), r.LatencyPct, r.Summary,
            r.Series.Select(p => new EffectPointDto(p.TsMs, p.Accel)).ToArray(), r.Measuring,
            r.BaselineMeasuredAt, r.BaselineFromCache);

    /// <summary>Builds the ranking DTO, assigning stable ids from the same pool as servers.list.</summary>
    public static NodeRankingDto ToDto(NodeRanking ranking, IReadOnlyList<Server> pool)
    {
        var ids = ServerIds.Assign(pool);
        var candidates = ranking.Candidates
            .Select(c => new RankedNodeDto(
                ids.TryGetValue(c.Server, out var id) ? id : ServerIds.Fingerprint(c.Server),
                c.Server.Remark,
                ChipOf(c.Server),
                c.Delay,
                c.Region,
                c.RegionName,
                c.RegionMatch,
                c.Score,
                c.Note))
            .ToArray();

        string? recommendedId = null;
        if (ranking.Recommended is { } rec)
            recommendedId = ids.TryGetValue(rec.Server, out var rid) ? rid : ServerIds.Fingerprint(rec.Server);

        return new NodeRankingDto(
            recommendedId,
            ranking.Reason,
            ranking.PreferenceLabel,
            ranking.PreferredRegions.ToArray(),
            candidates,
            ranking.ServerChoice);
    }

    public static DiagnosticReportDto ToDto(DiagnosticReport r) =>
        new(r.Items.Select(ToDto).ToArray(), r.Summary, r.RanAtUtc.ToString("o"), r.ErrorExplanation);

    public static DiagnosticItemDto ToDto(DiagnosticItem i) =>
        new(i.Id, i.Title, StatusName(i.Status), i.Detail, i.Suggestion, i.FixAction);

    public static DetectedGameDto ToDto(DetectedGame d) =>
        new(d.Installed.Source, d.Installed.SourceId, d.Installed.Name, d.Installed.InstallDir,
            d.Installed.Executables.ToArray(), d.GameId, d.GameName, d.MatchedBy);

    public static LibraryScanDto ToDto(ScanResult r) =>
        new(r.Games.Select(ToDto).ToArray(), r.Errors.ToArray(), r.ScannedAtUtc.ToString("o"));

    private static string StatusName(DiagnosticStatus s) => s switch
    {
        DiagnosticStatus.Ok => "ok",
        DiagnosticStatus.Warning => "warning",
        DiagnosticStatus.Error => "error",
        _ => "skipped"
    };
}
