using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Netch.Core.Services.AutoStart;
using Netch.Core.Services.Diagnostics;
using Netch.Core.Services.History;
using Netch.Core.Services.Library;
using Netch.Core.Services.Rules;

namespace Netch.Host;

/// <summary>Maps JSON-RPC method names to Netch.Core operations. Stateless except for the shared id registries.</summary>
internal sealed class RpcRouter
{
    private readonly IServiceProvider _sp;

    public RpcRouter(IServiceProvider sp) => _sp = sp;

    // The repositories are single-threaded (built for the WPF dispatcher). This lock is held only for
    // in-memory reads/mutations + persistence saves — never across long network I/O — so a slow
    // subs.update can't block accel.stop. Two sessions (e.g. a renderer reload overlapping the old
    // socket) still serialise their state mutations through it.
    private readonly SemaphoreSlim _state = new(1, 1);

    // Dedicated accel lock: start is serialised, but stop cancels the in-flight start instead of queueing
    // behind it (see StopAccelAsync).
    private readonly SemaphoreSlim _accelLock = new(1, 1);
    private CancellationTokenSource? _startCts;

    // Bumped by every accel.stop. A start captures it before resolving the node (which may ping for
    // seconds) and aborts if a stop happened in the meantime — otherwise the stop would be a no-op
    // because _startCts isn't set yet.
    private int _stopGeneration;

    private IServerRepository Servers => _sp.GetRequiredService<IServerRepository>();
    private IGameRepository Games => _sp.GetRequiredService<IGameRepository>();
    private ISubscriptionRepository Subs => _sp.GetRequiredService<ISubscriptionRepository>();
    private IAcceleratorService Accel => _sp.GetRequiredService<IAcceleratorService>();
    private IPersistenceService Persistence => _sp.GetRequiredService<IPersistenceService>();
    private ISettingsService SettingsSvc => _sp.GetRequiredService<ISettingsService>();
    private IBaselineStore Baselines => _sp.GetRequiredService<IBaselineStore>();
    private IUpdateChecker UpdateChecker => _sp.GetRequiredService<IUpdateChecker>();
    private ILogService Logs => _sp.GetRequiredService<ILogService>();
    private DiagnosticsService Diagnostics => _sp.GetRequiredService<DiagnosticsService>();
    private GameLibraryScanner LibraryScanner => _sp.GetRequiredService<GameLibraryScanner>();
    private GameRulesService Rules => _sp.GetRequiredService<GameRulesService>();
    private AutoStartService AutoStart => _sp.GetRequiredService<AutoStartService>();
    private IAccelHistoryStore History => _sp.GetRequiredService<IAccelHistoryStore>();

    /// <summary>Current acceleration state (used by the auto-acceleration policy).</summary>
    internal AccelerationState CurrentAccelState => Accel.State;

    /// <summary>Id of the game currently being accelerated, or null.</summary>
    internal string? CurrentAccelGameId => Accel.CurrentGame?.Id;

    public Task<object?> DispatchAsync(string method, JsonElement @params, CancellationToken ct) =>
        DispatchCoreAsync(method, @params, ct);

    private async Task<object?> DispatchCoreAsync(string method, JsonElement @params, CancellationToken ct)
    {
        switch (method)
        {
            case "games.list":
                return await WithStateValue(() => GameList(), ct);

            case "servers.list":
                return await WithStateValue(ServerList, ct);

            case "subs.list":
                return await WithStateValue(SubList, ct);

            case "logs.recent":
                // LogService.Recent is thread-safe (ConcurrentQueue snapshot), so no state lock needed.
                return Logs.Recent.Select(Mapping.ToDto).ToArray();

            case "history.list":
            {
                // The store has its own lock; do not take the state lock.
                var gameId = JsonParams.OptionalString(@params, "gameId");
                var limit = JsonParams.OptionalInt(@params, "limit") ?? 50;
                if (limit <= 0) limit = 50;
                if (limit > JsonAccelHistoryStore.MaxSessions) limit = JsonAccelHistoryStore.MaxSessions;
                return History.List(gameId, limit).Select(Mapping.ToDto).ToArray();
            }

            case "history.clear":
            {
                var gameId = JsonParams.OptionalString(@params, "gameId");
                History.Clear(gameId);
                return new { ok = true };
            }

            case "servers.ping":
            {
                // Snapshot under the lock, ping the snapshot outside it (network I/O), then build the
                // response under the lock again.
                var snapshot = await WithStateValue(() => Servers.Servers.ToList(), ct);
                await Servers.PingAsync(snapshot, ct);
                return await WithStateValue(ServerList, ct);
            }

            case "nodes.rank":
            {
                var gameId = JsonParams.OptionalString(@params, "gameId");
                var ping = JsonParams.OptionalBool(@params, "ping") ?? false;

                // Snapshot under the lock; ping the snapshot outside it (network I/O).
                var pool = await WithStateValue(() => Servers.Servers.ToList(), ct);
                if (ping || pool.All(s => s.Delay < 0))
                {
                    await Servers.PingAsync(pool, ct);
                    pool = await WithStateValue(() => Servers.Servers.ToList(), ct);
                }

                // Unknown gameId → rank without preference (do not throw).
                var game = string.IsNullOrEmpty(gameId)
                    ? null
                    : await WithStateValue(() => Games.Games.FirstOrDefault(g => g.Id == gameId), ct);

                // Read the penalty + per-game server choice under the state lock. GameServerChoice is a
                // ConcurrentDictionary (written by games.setServer under the same lock, read lock-free by
                // the accelerator's history recorder), so reads here are safe either way. No network I/O
                // happens inside this block.
                var (penalty, choice) = await WithStateValue(() =>
                {
                    var p = SettingsSvc.Settings.Advanced.RegionPenaltyMs;
                    var c = game is null
                        ? null
                        : SettingsSvc.Settings.GameServerChoice.GetValueOrDefault(game.Id);
                    return (p, c);
                }, ct);

                var ranking = NodeRanker.Rank(game, pool, penalty, choice);
                return Mapping.ToDto(ranking, pool);
            }

            case "servers.export":
                // Every server as a share link string (skips protocols the exporter can't emit, e.g. WireGuard).
                return await WithStateValue(
                    () => Servers.Servers.ToList()
                        .Select(ShareLinkExporter.ToShareLink)
                        .Where(l => l is not null)
                        .Select(l => l!)
                        .ToArray(),
                    ct);

            case "accel.start":
            {
                var gameId = JsonParams.RequiredString(@params, "gameId");
                var nodeId = JsonParams.OptionalString(@params, "nodeId");
                await StartAccelAsync(gameId, nodeId, ct);
                return await WithStateValue(() => Mapping.StatusDto(Accel, Servers.Servers.ToList()), ct);
            }

            case "accel.stop":
                await StopAccelAsync(ct);
                return await WithStateValue(() => Mapping.StatusDto(Accel, Servers.Servers.ToList()), ct);

            case "accel.status":
                return await WithStateValue(() => Mapping.StatusDto(Accel, Servers.Servers.ToList()), ct);

            case "effect.get":
                return EffectDto();

            case "effect.remeasure":
            {
                var gameId = JsonParams.RequiredString(@params, "gameId");
                // The store owns the RemeasureGames dictionary (ConcurrentDictionary) and its own
                // persistence, so no state lock is needed here.
                Baselines.MarkRemeasure(gameId);
                return new { ok = true };
            }

            case "diag.run":
            {
                var gameId = JsonParams.OptionalString(@params, "gameId");
                var nodeId = JsonParams.OptionalString(@params, "nodeId");
                var error = JsonParams.OptionalString(@params, "error");

                // Snapshot game/node/servers + accelerating flag under the lock, then run the checks
                // OUTSIDE it (DNS + node probes are network I/O and can take seconds).
                var (game, node, servers, accelerating) = await WithStateValue(() =>
                {
                    var g = string.IsNullOrEmpty(gameId)
                        ? null
                        : Games.Games.FirstOrDefault(x => x.Id == gameId);
                    var pool = Servers.Servers.ToList();
                    var n = string.IsNullOrEmpty(nodeId)
                        ? null
                        : ServerIds.Resolve(nodeId!, pool);
                    var accel = Accel.State == AccelerationState.Accelerating
                                || Accel.State == AccelerationState.Connecting;
                    return (g, n, pool, accel);
                }, ct);

                var ctx = new DiagnosticContext(game, node, servers, accelerating, error);
                var report = await Diagnostics.RunAsync(ctx, ct);
                return Mapping.ToDto(report);
            }

            case "diag.fix":
            {
                var action = JsonParams.RequiredString(@params, "action");
                if (action == "fixProxy")
                {
                    var fixedOk = await Diagnostics.FixProxyAsync();
                    return new { @fixed = fixedOk };
                }
                throw new InvalidOperationException("未知的修复操作");
            }

            case "servers.add":
            {
                var link = JsonParams.RequiredString(@params, "link");
                var parsed = ShareLinkParser.ParseText(link);
                if (parsed.Count == 0)
                    throw new InvalidOperationException("无法识别的分享链接");
                return await WithStateValueAsync(async () =>
                {
                    foreach (var s in parsed)
                        Servers.Add(s);
                    await Persistence.SaveAsync();
                    return ServerList();
                }, ct);
            }

            case "servers.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValueAsync(async () =>
                {
                    var server = ServerIds.Resolve(id, Servers.Servers.ToList())
                                 ?? throw new InvalidOperationException("线路不存在");
                    Servers.Remove(server);
                    await Persistence.SaveAsync();
                    return ServerList();
                }, ct);
            }

            case "subs.add":
            {
                var remark = JsonParams.OptionalString(@params, "remark") ?? "";
                var link = JsonParams.RequiredString(@params, "link");
                if (string.IsNullOrWhiteSpace(link))
                    throw new InvalidOperationException("订阅链接不能为空");
                return await WithStateValueAsync(async () =>
                {
                    var sub = new Netch.Core.Models.Subscription
                    {
                        Remark = string.IsNullOrWhiteSpace(remark)
                            ? Netch.Core.Models.Subscription.DefaultRemarkFor(link)
                            : remark,
                        Link = link.Trim()
                    };
                    sub.EnsureId();
                    Subs.Add(sub);
                    await Persistence.SaveAsync();
                    return SubList();
                }, ct);
            }

            case "subs.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValueAsync(async () =>
                {
                    var sub = Subs.Subscriptions.FirstOrDefault(s => s.Id == id)
                              ?? throw new InvalidOperationException("订阅不存在");
                    Subs.Remove(sub); // also purges the servers this subscription imported
                    await Persistence.SaveAsync();
                    return new { subscriptions = SubList(), servers = ServerList() };
                }, ct);
            }

            case "subs.update":
            {
                var id = JsonParams.RequiredString(@params, "id");
                // Resolve under the lock, then fetch+parse OUTSIDE it (network I/O can take tens of seconds),
                // then apply the result under the lock again.
                var sub = await WithStateValue(
                    () => Subs.Subscriptions.FirstOrDefault(s => s.Id == id)
                          ?? throw new InvalidOperationException("订阅不存在"),
                    ct);

                var servers = await Subs.FetchAsync(sub, ct);

                return await WithStateValueAsync(async () =>
                {
                    // The subscription may have been removed while we were fetching — re-adding its servers
                    // now would leave orphans behind.
                    if (!Subs.Subscriptions.Contains(sub))
                        throw new InvalidOperationException("订阅不存在");

                    Subs.Apply(sub, servers);
                    await Persistence.SaveAsync();
                    return new { subscriptions = SubList(), servers = ServerList() };
                }, ct);
            }

            case "games.toggleFavorite":
            {
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValueAsync(async () =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    Games.ToggleFavorite(game);
                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.setAuto":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var enabled = JsonParams.OptionalBool(@params, "enabled") ?? false;
                return await WithStateValueAsync(async () =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    if (enabled && game.ProxyProcesses.Count == 0)
                        throw new InvalidOperationException("该游戏没有配置进程规则，无法自动加速");

                    var list = SettingsSvc.Settings.AutoAccelerateGames;
                    if (enabled)
                    {
                        if (!list.Contains(id))
                            list.Add(id);
                    }
                    else
                    {
                        list.RemoveAll(x => x == id);
                    }

                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.setServer":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var server = JsonParams.RequiredString(@params, "server");
                if (server is not ("cn" or "intl"))
                    throw new InvalidOperationException("服务器只能是 cn 或 intl");
                return await WithStateValueAsync(async () =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    if (!GameRegionPreference.HasServerChoice(game))
                        throw new InvalidOperationException("该游戏没有国服/国际服之分");

                    SettingsSvc.Settings.GameServerChoice[id] = server;
                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.add":
            {
                var game = Mapping.ToGame(@params);
                if (string.IsNullOrWhiteSpace(game.Name))
                    throw new InvalidOperationException("游戏名称不能为空");
                return await WithStateValue(() =>
                {
                    Games.AddCustomGame(game);
                    return GameList();
                }, ct);
            }

            case "games.update":
            {
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValue(() =>
                {
                    var existing = Games.Games.FirstOrDefault(g => g.Id == id)
                                   ?? throw new InvalidOperationException("游戏不存在");
                    if (!existing.IsCustom)
                        throw new InvalidOperationException("只能编辑自定义游戏");
                    // Merge: start from the existing game and only overwrite fields present in params, so
                    // fields the editor doesn't send (icon, aliases, accent, …) are preserved.
                    var updated = Mapping.ToGame(@params, id, existing);
                    updated.IsCustom = true;
                    updated.IsFavorite = existing.IsFavorite;
                    var index = Games.Games.IndexOf(existing);
                    if (index >= 0)
                    {
                        Games.Games[index] = updated;
                        try
                        {
                            Games.UpdateCustomGame(updated);
                        }
                        catch
                        {
                            // Roll back the in-memory swap so the list stays consistent with the store.
                            Games.Games[index] = existing;
                            throw;
                        }
                    }
                    return GameList();
                }, ct);
            }

            case "games.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValue(() =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    Games.RemoveGame(game);
                    return GameList();
                }, ct);
            }

            case "library.scan":
            {
                // Snapshot the library under the lock, then run the scan OUTSIDE it (file I/O can take
                // seconds) on a background thread.
                var library = await WithStateValue(() => Games.Games.ToList(), ct);
                var result = await Task.Run(() => LibraryScanner.ScanAll(library, ct), ct);
                return Mapping.ToDto(result);
            }

            case "library.addDetected":
            {
                var name = JsonParams.RequiredString(@params, "name").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("游戏名称不能为空");

                var executables = JsonParams.StringList(@params, "executables")
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0)
                    .ToList();
                if (executables.Count == 0)
                    throw new InvalidOperationException("请至少提供一个可执行文件");

                foreach (var exe in executables)
                {
                    if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"可执行文件必须以 .exe 结尾：{exe}");
                    if (exe.Contains('/') || exe.Contains('\\'))
                        throw new InvalidOperationException($"可执行文件不能包含路径分隔符：{exe}");
                }

                var source = JsonParams.OptionalString(@params, "source");

                return await WithStateValue(() =>
                {
                    var game = new Game
                    {
                        Name = name,
                        Icon = "🎮",
                        Category = GameCategory.Pc,
                        Platforms = new List<string> { "PC" },
                        ProxyProcesses = executables,
                        Regions = new List<string>(),
                        Aliases = new List<string>()
                    };
                    if (!string.IsNullOrWhiteSpace(source))
                        game.EnglishName = source!;
                    Games.AddCustomGame(game);
                    return GameList();
                }, ct);
            }

            case "settings.get":
                return await WithStateValue(() => Mapping.ToDto(SettingsSvc.Settings), ct);

            case "settings.set":
                return await WithStateValueAsync(async () =>
                {
                    ApplySettings(@params);
                    await SettingsSvc.SaveAsync();
                    return Mapping.ToDto(SettingsSvc.Settings);
                }, ct);

            case "settings.resetAdvanced":
                return await WithStateValueAsync(async () =>
                {
                    SettingsSvc.Settings.Advanced = new AdvancedSettings();
                    SettingsSvc.Settings.Dns.OtherDns = "1.1.1.1";
                    await SettingsSvc.SaveAsync();
                    return Mapping.ToDto(SettingsSvc.Settings);
                }, ct);

            case "rules.status":
                return await WithStateValue(() =>
                {
                    var st = Rules.Status;
                    return new RulesStatusDto(st.Version, st.Source, st.GameCount,
                        st.LastCheckUtc?.ToString("o"), st.LastError, st.LastResult,
                        SettingsSvc.Settings.RulesUrl ?? "");
                }, ct);

            case "rules.update":
                return await RefreshRulesAsync(ct);

            case "update.check":
            {
                // Pure network I/O — no shared state, so no lock.
                var result = await UpdateChecker.CheckAsync(ct);
                return new
                {
                    status = result.Status switch
                    {
                        UpdateCheckStatus.UpToDate => "upToDate",
                        UpdateCheckStatus.UpdateAvailable => "updateAvailable",
                        UpdateCheckStatus.NotConfigured => "notConfigured",
                        _ => "failed"
                    },
                    current = FormatVersion(result.Current),
                    latest = result.Latest is null ? null : FormatVersion(result.Latest),
                    tag = result.TagName,
                    url = result.Url,
                    error = result.Error
                };
            }

            case "autostart.get":
            {
                // No shared state — schtasks.exe is the source of truth, so no lock.
                var state = await AutoStart.GetAsync(ct);
                return new { enabled = state.Enabled, supported = state.Supported, reason = state.Reason };
            }

            case "autostart.set":
            {
                var enabled = JsonParams.OptionalBool(@params, "enabled") ?? false;
                var state = await AutoStart.SetAsync(enabled, ct);
                return new { enabled = state.Enabled, supported = state.Supported, reason = state.Reason };
            }

            default:
                throw new InvalidOperationException($"unknown method: {method}");
        }
    }

    /// <summary>
    ///     Downloads the configured rules file and, when it is newer, swaps the seed games. Network I/O
    ///     happens outside the state lock; only the swap is done under it. Also used by the startup
    ///     background refresh in Program.cs.
    /// </summary>
    internal async Task<RulesUpdateDto> RefreshRulesAsync(CancellationToken ct)
    {
        var url = SettingsSvc.Settings.RulesUrl ?? "";
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("未配置规则更新地址");

        var result = await Rules.UpdateAsync(url.Trim(), ct);

        if (result.Status == RulesUpdateStatus.Updated)
        {
            return await WithStateValue(() =>
            {
                Games.ReplaceSeedGames(Rules.CreateGames());
                return new RulesUpdateDto("updated", result.Message, result.Version, GameList());
            }, ct);
        }

        var status = result.Status == RulesUpdateStatus.UpToDate ? "upToDate" : "failed";
        return await WithStateValue(
            () => new RulesUpdateDto(status, result.Message, result.Version, GameList()),
            ct);
    }

    /// <summary>Builds the effect report DTO, or an empty "not started" report when no monitor is wired up.</summary>
    private EffectReportDto EffectDto()
    {
        var report = Accel.EffectReport();
        return report is null
            ? new EffectReportDto(false, null, null, null, null,
                "尚未开始测量", Array.Empty<EffectPointDto>(), false, null, false)
            : Mapping.ToDto(report);
    }

    // Distinct names (rather than overloads) so statement lambdas and value-returning lambdas bind
    // unambiguously — Func<T> vs Func<Task<T>> overloads are otherwise ambiguous for `async () => {...}`.

    /// <summary>Runs a synchronous, value-returning action under the state lock.</summary>
    private async Task<T> WithStateValue<T>(Func<T> action, CancellationToken ct)
    {
        await _state.WaitAsync(ct);
        try
        {
            return action();
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Runs an asynchronous, value-returning action under the state lock.</summary>
    private async Task<T> WithStateValueAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _state.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _state.Release();
        }
    }

    private ServerDto[] ServerList()
    {
        var list = Servers.Servers.ToList();
        var ids = ServerIds.Assign(list);
        return list.Select(s => Mapping.ToDto(s, ids[s])).ToArray();
    }

    private SubscriptionDto[] SubList() =>
        Subs.Subscriptions.ToList().Select(Mapping.ToDto).ToArray();

    /// <summary>Builds the games DTO list, marking the per-game auto-acceleration toggle + server choice.</summary>
    private GameDto[] GameList()
    {
        var auto = SettingsSvc.Settings.AutoAccelerateGames;
        var choices = SettingsSvc.Settings.GameServerChoice;
        return Games.Games.Select(g => Mapping.ToDto(g, auto, choices)).ToArray();
    }

    /// <summary>Game name for a given id, or null when unknown (used for auto-accel event payloads).</summary>
    internal string? GameNameOf(string gameId)
    {
        try
        {
            return Games.Games.FirstOrDefault(g => g.Id == gameId)?.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Games to watch for auto-acceleration plus the global master switch. Read under the state lock.
    /// </summary>
    internal async Task<(IReadOnlyList<Game> Games, bool Enabled)> AutoWatchedGamesAsync(CancellationToken ct)
    {
        return await WithStateValue(() =>
        {
            var settings = SettingsSvc.Settings;
            var ids = settings.AutoAccelerateGames;
            var watched = Games.Games
                .Where(g => ids.Contains(g.Id) && g.ProxyProcesses.Count > 0)
                .ToList();
            return ((IReadOnlyList<Game>)watched, settings.AutoAccelerate);
        }, ct);
    }

    /// <summary>
    ///     Starts acceleration for an auto-acceleration launch edge, using the same code path as
    ///     accel.start with a null nodeId (smart routing).
    /// </summary>
    internal Task StartAccelForAutoAsync(string gameId, CancellationToken ct) =>
        StartAccelAsync(gameId, null, ct);

    /// <summary>Stops acceleration for an auto-acceleration exit edge.</summary>
    internal Task StopAccelForAutoAsync(CancellationToken ct) =>
        StopAccelAsync(ct);

    private void ApplySettings(JsonElement p)
    {
        var s = SettingsSvc.Settings;
        if (JsonParams.OptionalString(p, "theme") is { } theme && Enum.TryParse<Netch.Core.Models.Settings.AppTheme>(theme, true, out var appTheme))
            s.Theme = appTheme;
        if (JsonParams.OptionalString(p, "language") is { } language)
            s.Language = language;
        if (JsonParams.OptionalBool(p, "minimizeToTray") is { } minimizeToTray)
            s.MinimizeToTray = minimizeToTray;
        if (JsonParams.OptionalBool(p, "autoAccelerate") is { } autoAccelerate)
            s.AutoAccelerate = autoAccelerate;
        if (JsonParams.OptionalString(p, "rulesUrl") is { } rulesUrl)
        {
            // Validate BEFORE mutating anything so a bad value can't leave settings half-applied.
            var trimmed = rulesUrl.Trim();
            if (trimmed.Length > 0 && !IsValidRulesUrl(trimmed))
                throw new InvalidOperationException("规则更新地址必须是 https:// 开头的网址");
            s.RulesUrl = trimmed;
        }
        if (JsonParams.HasProperty(p, "extraApps"))
        {
            // Validate the whole array BEFORE mutating anything (same style as rulesUrl).
            var parsed = ParseExtraApps(p);
            s.ExtraApps = parsed;
        }
        if (JsonParams.HasProperty(p, "advanced"))
            ApplyAdvanced(p, s);
    }

    /// <summary>
    ///     Applies the <c>advanced</c> object. Validates ALL provided fields first (throwing on the first
    ///     error without mutating anything), then applies them.
    /// </summary>
    private static void ApplyAdvanced(JsonElement p, AppSettings s)
    {
        if (!p.TryGetProperty("advanced", out var adv) || adv.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("advanced 必须是对象");

        string? tunDns = null;
        bool? measureEffect = null;
        int? regionPenalty = null;
        int? localPort = null;

        if (JsonParams.HasProperty(adv, "tunDns"))
            tunDns = AdvancedSettingsValidator.ValidateDns(JsonParams.OptionalString(adv, "tunDns"));
        if (JsonParams.HasProperty(adv, "measureEffect"))
            measureEffect = JsonParams.OptionalBool(adv, "measureEffect") ?? false;
        if (JsonParams.HasProperty(adv, "regionPenaltyMs"))
            regionPenalty = AdvancedSettingsValidator.ValidateRegionPenalty(
                JsonParams.OptionalInt(adv, "regionPenaltyMs") ?? 0);
        if (JsonParams.HasProperty(adv, "localSocksPort"))
            localPort = AdvancedSettingsValidator.ValidateLocalPort(
                JsonParams.OptionalInt(adv, "localSocksPort") ?? 0);

        // All fields validated — now apply.
        if (tunDns is not null)
            s.Dns.OtherDns = tunDns;
        if (measureEffect is { } me)
            s.Advanced.MeasureEffect = me;
        if (regionPenalty is { } rp)
            s.Advanced.RegionPenaltyMs = rp;
        if (localPort is { } lp)
            s.Advanced.LocalSocksPort = lp;
    }

    /// <summary>
    ///     Parses and validates the <c>extraApps</c> array. Throws before any mutation so a bad entry
    ///     can't leave settings half-applied.
    /// </summary>
    private static List<ExtraApp> ParseExtraApps(JsonElement p)
    {
        if (!p.TryGetProperty("extraApps", out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("extraApps 必须是数组");

        var result = new List<ExtraApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("extraApps 的每一项必须是对象");

            var process = (JsonParams.OptionalString(item, "process") ?? "").Trim();
            if (!SplitRouting.IsValidProcessName(process))
                throw new InvalidOperationException($"无效的进程名：{process}（应为 xxx.exe）");

            if (!seen.Add(process))
                throw new InvalidOperationException($"进程 {process} 重复");

            var modeRaw = (JsonParams.OptionalString(item, "mode") ?? "proxy").Trim().ToLowerInvariant();
            if (modeRaw is not ("proxy" or "bypass"))
                throw new InvalidOperationException($"无效的分流方式：{modeRaw}（可选：proxy / bypass）");

            var name = (JsonParams.OptionalString(item, "name") ?? "").Trim();
            if (name.Length == 0)
                name = process;
            if (name.Length > 40)
                name = name[..40];

            var enabled = JsonParams.OptionalBool(item, "enabled") ?? true;

            result.Add(new ExtraApp
            {
                Name = name,
                Process = process,
                Mode = modeRaw == "bypass" ? ExtraAppMode.Bypass : ExtraAppMode.Proxy,
                Enabled = enabled
            });

            if (result.Count > 50)
                throw new InvalidOperationException("分应用分流最多 50 条");
        }

        return result;
    }

    /// <summary>https:// anywhere; plain http:// only for loopback hosts (local testing).</summary>
    private static bool IsValidRulesUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    /// <summary>Formats a version as Major.Minor.Build (Build clamped to &gt;= 0), e.g. "3.0.0".</summary>
    private static string FormatVersion(Version v) =>
        $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    private async Task StartAccelAsync(string gameId, string? nodeId, CancellationToken ct)
    {
        // Capture the stop generation before any long-running work: if accel.stop arrives while we're
        // resolving/pinging the node, we must not proceed to start afterwards.
        var gen = Volatile.Read(ref _stopGeneration);

        // Resolve game under the lock (brief repository read), then resolve the node OUTSIDE it — picking a
        // node may ping (network I/O, seconds) and must not hold the state lock.
        var game = await WithStateValue(
            () => Games.Games.FirstOrDefault(x => x.Id == gameId)
                  ?? throw new InvalidOperationException("游戏不存在"),
            ct);

        Server node;
        if (string.IsNullOrEmpty(nodeId))
        {
            node = await PickBestNodeAsync(game, ct);
        }
        else
        {
            node = await WithStateValue(
                () => ServerIds.Resolve(nodeId, Servers.Servers.ToList())
                      ?? throw new InvalidOperationException("线路不存在"),
                ct);
        }

        await _accelLock.WaitAsync(ct);
        try
        {
            // A stop was issued while we were resolving the node — abort before touching the accelerator.
            if (Volatile.Read(ref _stopGeneration) != gen)
                throw new OperationCanceledException("加速已被取消");

            // Register a CTS so accel.stop can cancel this start instead of queueing behind it.
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _startCts = startCts;
            try
            {
                await Accel.StartAsync(game, node, startCts.Token);
            }
            finally
            {
                if (ReferenceEquals(_startCts, startCts))
                    _startCts = null;
            }
        }
        finally
        {
            _accelLock.Release();
        }
    }

    private async Task StopAccelAsync(CancellationToken ct)
    {
        // Bump the generation first so a start that is still resolving its node sees the stop.
        Interlocked.Increment(ref _stopGeneration);

        // Cancel an in-flight start first so it unwinds (AcceleratorService.StartAsync cleans up on
        // cancellation) instead of us queueing behind it on _accelLock for up to ~12s.
        try
        {
            _startCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        await _accelLock.WaitAsync(ct);
        try
        {
            await Accel.StopAsync(ct);
        }
        finally
        {
            _accelLock.Release();
        }
    }

    /// <summary>
    ///     Smart routing: region-aware lowest-latency pick. If nothing is tested yet, ping first.
    /// </summary>
    private async Task<Server> PickBestNodeAsync(Game game, CancellationToken ct)
    {
        // Snapshot under the lock; ping the snapshot outside it (network I/O).
        var pool = await WithStateValue(() => Servers.Servers.ToList(), ct);
        if (pool.Count == 0)
            throw new InvalidOperationException("没有可用线路，请先添加或导入线路");

        if (pool.All(s => s.Delay < 0))
        {
            await Servers.PingAsync(pool, ct);
            pool = await WithStateValue(() => Servers.Servers.ToList(), ct);
        }

        // Read the penalty + per-game server choice under the state lock. GameServerChoice is a
        // ConcurrentDictionary (written by games.setServer under the same lock, read lock-free by the
        // accelerator's history recorder), so reads here are safe either way. No network I/O inside
        // this block.
        var (penalty, choice) = await WithStateValue(() =>
        {
            var p = SettingsSvc.Settings.Advanced.RegionPenaltyMs;
            var c = SettingsSvc.Settings.GameServerChoice.GetValueOrDefault(game.Id);
            return (p, c);
        }, ct);

        var ranking = NodeRanker.Rank(game, pool, penalty, choice);
        var node = ranking.Recommended?.Server ?? pool[0];
        Logs.Log(Netch.Core.Enums.LogLevel.Info, $"智能选路：{node.Remark}（{ranking.Reason}）");
        return node;
    }
}
