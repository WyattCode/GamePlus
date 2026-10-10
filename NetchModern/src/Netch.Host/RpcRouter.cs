using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;
using Netch.Core.Services;
using Netch.Core.Services.AutoStart;
using Netch.Core.Services.AutoSwitch;
using Netch.Core.Services.Diagnostics;
using Netch.Core.Services.History;
using Netch.Core.Services.Import;
using Netch.Core.Services.Launch;
using Netch.Core.Services.Library;
using Netch.Core.Services.Rules;
using Netch.Core.Utils;

namespace Netch.Host;

/// <summary>Maps JSON-RPC method names to Netch.Core operations. Stateless except for the shared id registries.</summary>
internal sealed class RpcRouter
{
    private readonly IServiceProvider _sp;
    private readonly DateTime _startedAt = DateTime.UtcNow;

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

            case "servers.import":
            {
                var text = JsonParams.RequiredString(@params, "text");
                return await ImportNodesAsync(text, ct);
            }

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

            case "effect.baseline":
            {
                var gameId = JsonParams.RequiredString(@params, "gameId");
                if (string.IsNullOrWhiteSpace(gameId))
                    throw new InvalidOperationException("游戏 id 不能为空");
                // The store owns its own lock (like effect.remeasure), so no state lock is needed here.
                var stored = Baselines.Get(gameId);
                return new BaselineInfoDto(
                    stored is null
                        ? null
                        : new EffectSnapshotDto(stored.Latency, stored.Loss, stored.Jitter, stored.Samples),
                    stored?.MeasuredUtc.ToString("o"));
            }

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

                var report = await RunDiagnosticsAsync(gameId, nodeId, error, ct);
                return Mapping.ToDto(report);
            }

            case "diag.bundle":
            {
                var client = @params.ValueKind == JsonValueKind.Object
                             && @params.TryGetProperty("client", out var clientEl)
                    ? clientEl
                    : (JsonElement?)null;

                return await BuildDiagnosticBundleAsync(client, ct);
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
                return await RefreshSubscriptionAsync(id, ct);
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

            case "games.setMode":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var mode = (JsonParams.RequiredString(@params, "mode") ?? "").Trim().ToLowerInvariant();
                if (mode is not ("default" or "auto" or "process" or "tun" or "proxy"))
                    throw new InvalidOperationException("加速模式只能是 default、auto、process、tun 或 proxy");
                return await WithStateValueAsync(async () =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");

                    // Availability checks (same Chinese errors as the accelerator) — validate before mutating.
                    if (mode == "process" && game.ProxyProcesses.Count == 0)
                        throw new InvalidOperationException("该游戏没有进程规则，无法使用进程模式");
                    if (mode == "tun" && game.ProxyIps.Count == 0)
                        throw new InvalidOperationException("该游戏没有 IP 规则，无法使用 TUN 模式");
                    if (mode == "proxy" && game.ProxyDomains.Count == 0)
                        throw new InvalidOperationException("该游戏没有域名规则，无法使用系统代理模式");

                    if (mode == "default")
                        SettingsSvc.Settings.GameCaptureMode.TryRemove(id, out _);
                    else
                        SettingsSvc.Settings.GameCaptureMode[id] = mode;

                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.setDownloadProxy":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var enabled = JsonParams.OptionalBool(@params, "enabled") ?? false;
                return await WithStateValueAsync(async () =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    if (game.DownloadDomains.Count == 0)
                        throw new InvalidOperationException("该游戏没有单独的下载域名");

                    if (enabled)
                        SettingsSvc.Settings.GameDownloadViaProxy[id] = true;
                    else
                        SettingsSvc.Settings.GameDownloadViaProxy.TryRemove(id, out _);

                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.setLaunch":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var raw = JsonParams.OptionalString(@params, "target");

                // Validate OUTSIDE the state lock (File.Exists I/O) and BEFORE mutating anything, so a
                // bad target can't leave settings half-applied. Empty/whitespace clears the target.
                var target = LaunchTargetValidator.Normalize(raw);

                return await WithStateValueAsync(async () =>
                {
                    _ = Games.Games.FirstOrDefault(g => g.Id == id)
                        ?? throw new InvalidOperationException("游戏不存在");

                    if (string.IsNullOrEmpty(target))
                        SettingsSvc.Settings.GameLaunchTargets.TryRemove(id, out _);
                    else
                        SettingsSvc.Settings.GameLaunchTargets[id] = target;

                    await SettingsSvc.SaveAsync();
                    return GameList();
                }, ct);
            }

            case "games.suggestLaunch":
            {
                var id = JsonParams.RequiredString(@params, "id");

                // Snapshot the game under the lock, then run the scan OUTSIDE it (file I/O can take
                // seconds) on a background thread — exactly like library.scan, but restricted to this
                // single game so the matcher only reports entries for it.
                var game = await WithStateValue(
                    () => Games.Games.FirstOrDefault(g => g.Id == id)
                          ?? throw new InvalidOperationException("游戏不存在"),
                    ct);

                var result = await Task.Run(
                    () => LibraryScanner.ScanAll(new List<Game> { game }, ct), ct);

                var targets = result.Games
                    .Where(d => d.GameId == id)
                    .SelectMany(d => LaunchTargetValidator.SuggestFrom(d.Installed))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new { targets };
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
                            Games.Games[index] = updated;
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

            case "games.exportRule":
            {
                // Snapshot the game under the lock; serialization is pure CPU so it stays inside.
                var id = JsonParams.RequiredString(@params, "id");
                return await WithStateValue(() =>
                {
                    var game = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                    var now = DateTime.UtcNow;
                    return new
                    {
                        json = SingleGameRule.ToJson(game, now),
                        share = SingleGameRule.ToShareString(game, now),
                        fileName = $"{SingleGameRule.ExportId(game)}.gameplus-rule.json"
                    };
                }, ct);
            }

            case "games.importRule":
            {
                var text = JsonParams.RequiredString(@params, "text");
                var onConflict = (JsonParams.OptionalString(@params, "onConflict") ?? "ask").Trim().ToLowerInvariant();
                if (onConflict is not ("ask" or "replace" or "copy"))
                    throw new InvalidOperationException("无效的冲突处理方式（可选：ask / replace / copy）");
                return await ImportGameRuleAsync(text, onConflict, ct);
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

            case "lan.addresses":
                // Pure local NIC enumeration — no shared state, so no state lock.
                return new
                {
                    addresses = LocalPrivateIPv4Addresses()
                        .Select(a => new { address = a.Address.ToString(), interfaceName = a.InterfaceName })
                        .ToArray()
                };

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
    ///     Runs the diagnostics checks for the given game/node/error. Snapshots game/node/servers +
    ///     accelerating flag under the state lock, then runs the checks OUTSIDE it (DNS + node probes
    ///     are network I/O and can take seconds). Shared by diag.run and diag.bundle.
    /// </summary>
    private async Task<DiagnosticReport> RunDiagnosticsAsync(
        string? gameId, string? nodeId, string? error, CancellationToken ct)
    {
        var (game, node, servers, accelerating, lastProcessCaptureError) = await WithStateValue(() =>
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
            var captureError = Accel.LastProcessCaptureError;
            return (g, n, pool, accel, captureError);
        }, ct);

        var ctx = new DiagnosticContext(game, node, servers, accelerating, error, lastProcessCaptureError);
        return await Diagnostics.RunAsync(ctx, ct);
    }

    /// <summary>
    ///     Builds the one-click diagnostic bundle: snapshots settings/servers/subscriptions under the
    ///     state lock, runs diagnostics OUTSIDE it, then zips everything in memory and returns base64.
    /// </summary>
    private async Task<object> BuildDiagnosticBundleAsync(JsonElement? client, CancellationToken ct)
    {
        var now = DateTime.Now;

        // Snapshot everything that touches shared state under the lock. Servers are reduced to a
        // non-identifying summary (no host, no credentials); subscription links are redacted.
        var (settingsNode, servers, subscriptions, logLines, crashFiles) = await WithStateValue(() =>
        {
            var settings = JsonSerializer.SerializeToNode(SettingsSvc.Settings);

            var serverList = Servers.Servers
                .Select(s => (object)new
                {
                    type = s.Type.ToString(),
                    remark = s.Remark,
                    port = s.Port,
                    group = s.Group
                })
                .ToList();

            var subList = Subs.Subscriptions
                .Select(s => (object)new
                {
                    remark = s.Remark,
                    serverCount = s.ServerCount,
                    lastUpdated = s.LastUpdated?.ToString("o"),
                    host = SecretRedactor.RedactUrl(s.Link ?? "")
                })
                .ToList();

            var logs = Logs.Recent
                .Select(l => $"{l.Timestamp:yyyy-MM-dd HH:mm:ss} {l.Level} {l.Message}")
                .ToList();

            var crashes = ReadCrashFiles();

            return (settings, (IReadOnlyList<object>)serverList, (IReadOnlyList<object>)subList,
                (IReadOnlyList<string>)logs, crashes);
        }, ct);

        // Diagnostics run OUTSIDE the lock (network I/O), exactly like diag.run with no game/node.
        var report = await RunDiagnosticsAsync(null, null, null, ct);
        var diagnostics = Mapping.ToDto(report);

        JsonNode? clientNode = null;
        if (client is { } el && el.ValueKind == JsonValueKind.Object)
            clientNode = JsonNode.Parse(el.GetRawText());

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["dotnet"] = Environment.Version.ToString(),
            ["elevated"] = IsElevated().ToString(),
            ["culture"] = CultureInfo.CurrentCulture.Name,
            ["timeZone"] = TimeZoneInfo.Local.Id,
            ["uptimeSeconds"] = ((long)(DateTime.UtcNow - _startedAt).TotalSeconds).ToString(CultureInfo.InvariantCulture)
        };

        var hostVersion = typeof(RpcRouter).Assembly.GetName().Version?.ToString() ?? "unknown";
        var appVersion = hostVersion;

        var input = new BundleInput(
            appVersion,
            hostVersion,
            env,
            settingsNode,
            servers,
            subscriptions,
            logLines,
            crashFiles,
            diagnostics,
            clientNode);

        var bytes = DiagnosticBundleBuilder.Build(input, now);
        var fileName = $"GamePlus-diagnostics-{now:yyyyMMdd-HHmmss}.zip";
        return new
        {
            fileName,
            base64 = Convert.ToBase64String(bytes),
            size = bytes.Length
        };
    }

    /// <summary>Reads the newest crash-host-*.txt files (max 10) from the crash directory.</summary>
    private IReadOnlyList<(string Name, string Content)> ReadCrashFiles()
    {
        var result = new List<(string Name, string Content)>();
        try
        {
            var dir = Path.Combine(AppPaths.DataDirectory, "crashes");
            if (!System.IO.Directory.Exists(dir))
                return result;

            var files = System.IO.Directory.GetFiles(dir, "crash-host-*.txt");
            Array.Sort(files, StringComparer.Ordinal);
            var take = Math.Min(files.Length, 10);
            for (var i = files.Length - take; i < files.Length; i++)
            {
                try
                {
                    var content = File.ReadAllText(files[i]);
                    result.Add((Path.GetFileName(files[i]), content));
                }
                catch
                {
                    // best effort
                }
            }
        }
        catch
        {
            // best effort
        }
        return result;
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Lists the private IPv4 addresses (10/8, 172.16/12, 192.168/16) of this PC's Up, non-loopback
    ///     network interfaces, deduplicated by address. Used by lan.addresses and lanShare validation.
    /// </summary>
    private static IReadOnlyList<(IPAddress Address, string InterfaceName)> LocalPrivateIPv4Addresses()
    {
        var result = new List<(IPAddress, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || !LanShareValidator.IsPrivateIPv4(ip))
                    continue;
                if (seen.Add(ip.ToString()))
                    result.Add((ip, nic.Name));
            }
        }

        return result;
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

    /// <summary>
    ///     Refreshes one subscription: resolves it under the state lock, fetches + parses OUTSIDE the lock
    ///     (network I/O can take tens of seconds), then applies the result under the lock again. Used by
    ///     subs.update and by the background SubscriptionScheduler.
    /// </summary>
    public async Task<object> RefreshSubscriptionAsync(string id, CancellationToken ct)
    {
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

    /// <summary>
    ///     Snapshot of (id, lastUpdated) for every subscription, read under the state lock. Used by the
    ///     background SubscriptionScheduler so it never touches shared state outside the lock.
    /// </summary>
    public async Task<IReadOnlyList<(string Id, DateTime? LastUpdated)>> SnapshotSubscriptionsAsync(CancellationToken ct)
    {
        return await WithStateValue<IReadOnlyList<(string Id, DateTime? LastUpdated)>>(
            () => Subs.Subscriptions
                .Select(s => (s.Id, s.LastUpdated))
                .ToList(),
            ct);
    }

    /// <summary>Reads the configured subscription auto-update interval (hours) under the state lock.</summary>
    public async Task<int> GetSubscriptionAutoUpdateHoursAsync(CancellationToken ct)
    {
        return await WithStateValue(() => SettingsSvc.Settings.SubscriptionAutoUpdateHours, ct);
    }

    /// <summary>
    ///     Implements servers.import: a single http(s) URL becomes a subscription (created like subs.add,
    ///     then refreshed); anything else is parsed by NodeImporter and deduped against the current pool.
    /// </summary>
    private async Task<object> ImportNodesAsync(string text, CancellationToken ct)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("无法识别的节点内容");

        // A single http(s) URL → treat as a subscription: create it exactly like subs.add, then refresh.
        if (IsSingleHttpUrl(trimmed))
        {
            var sub = await WithStateValueAsync(async () =>
            {
                var s = new Netch.Core.Models.Subscription
                {
                    Remark = Netch.Core.Models.Subscription.DefaultRemarkFor(trimmed),
                    Link = trimmed
                };
                s.EnsureId();
                Subs.Add(s);
                await Persistence.SaveAsync();
                return s;
            }, ct);

            await RefreshSubscriptionAsync(sub.Id, ct);

            return await WithStateValue(() =>
            {
                // Report only the servers this subscription added, not the whole pool. Look the
                // subscription up again by id: it may have been removed while we were fetching.
                var added = Subs.Subscriptions.FirstOrDefault(s => s.Id == sub.Id)?.ServerCount ?? 0;
                var list = ServerList();
                return (object)new ImportResultDto(added, 0, 0, "subscription", list);
            }, ct);
        }

        var parsed = NodeImporter.Parse(trimmed);

        return await WithStateValueAsync(async () =>
        {
            var (added, duplicates) = NodeImporter.Dedupe(Servers.Servers.ToList(), parsed.Valid);
            if (added.Count == 0 && duplicates == 0)
                throw new InvalidOperationException("无法识别的节点内容");

            foreach (var s in added)
                Servers.Add(s);
            if (added.Count > 0)
                await Persistence.SaveAsync();

            var list = ServerList();
            return (object)new ImportResultDto(added.Count, duplicates, parsed.Invalid, parsed.Format, list);
        }, ct);
    }

    /// <summary>True when the whole (trimmed) text is a single http:// or https:// URL.</summary>
    private static bool IsSingleHttpUrl(string text)
    {
        if (text.Contains('\n') || text.Contains('\r'))
            return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    ///     Implements games.importRule: parses the payload (Game+ rule JSON or gameplus-rule: share
    ///     string) OUTSIDE the state lock, then detects conflicts + applies the import under it. The
    ///     result is always a CUSTOM game (id "custom-" + incoming id) — built-in games are never
    ///     modified.
    /// </summary>
    private async Task<object> ImportGameRuleAsync(string text, string onConflict, CancellationToken ct)
    {
        var incoming = SingleGameRule.Parse(text);
        return await WithStateValueAsync(
            () => Task.FromResult(ImportGameRuleUnderLock(incoming, onConflict)), ct);
    }

    /// <summary>Conflict detection + apply for games.importRule. Runs under the state lock.</summary>
    private object ImportGameRuleUnderLock(Game incoming, string onConflict)
    {
        // Exported custom games carry a "user-" id (rules ids can't start with "custom-"); strip it
        // and re-prefix with "custom-" for the in-library id.
        var baseId = incoming.Id.StartsWith("user-", StringComparison.Ordinal)
            ? incoming.Id["user-".Length..]
            : incoming.Id;
        var targetId = SingleGameRule.TargetCustomId(incoming.Id);

        // Built-in conflict: same id or same display name (case-insensitive).
        var builtin = Games.Games.FirstOrDefault(g => !g.IsCustom &&
            (g.Id == baseId || string.Equals(g.Name, incoming.Name, StringComparison.OrdinalIgnoreCase)));
        if (builtin is not null)
        {
            if (onConflict == "replace")
                throw new InvalidOperationException("内置游戏不能被替换，请选择「另存为副本」");
            if (onConflict == "ask")
                return ConflictResult("builtin", builtin.Id, builtin.Name, incoming.Name);
            return ImportRuleCopy(FreeCustomId(targetId), $"{incoming.Name}（导入）", incoming);
        }

        // Custom conflict: an existing custom game with the target id or the same name.
        var custom = Games.Games.FirstOrDefault(g => g.IsCustom &&
            (g.Id == targetId || string.Equals(g.Name, incoming.Name, StringComparison.OrdinalIgnoreCase)));
        if (custom is not null)
        {
            switch (onConflict)
            {
                case "ask":
                    return ConflictResult("custom", custom.Id, custom.Name, incoming.Name);
                case "replace":
                    // Overwrite in place: keep the existing id + favorite flag, take the incoming rules.
                    CopyRuleFields(custom, incoming);
                    custom.IsCustom = true;
                    Games.UpdateCustomGame(custom);
                    return ImportedResult(custom.Id);
                default: // "copy"
                    return ImportRuleCopy(FreeCustomId(targetId), $"{incoming.Name}（导入）", incoming);
            }
        }

        // No conflict → add directly under the target id.
        return ImportRuleCopy(targetId, incoming.Name, incoming);
    }

    /// <summary>Adds the incoming rule set as a new custom game and returns the imported result.</summary>
    private object ImportRuleCopy(string id, string name, Game incoming)
    {
        var game = new Game
        {
            Id = id,
            Name = name,
            EnglishName = incoming.EnglishName,
            Aliases = new List<string>(incoming.Aliases),
            Icon = incoming.Icon,
            AccentColor = incoming.AccentColor,
            Category = incoming.Category,
            Regions = new List<string>(incoming.Regions),
            Platforms = new List<string>(incoming.Platforms),
            ProxyDomains = new List<string>(incoming.ProxyDomains),
            ProxyIps = new List<string>(incoming.ProxyIps),
            ProxyProcesses = new List<string>(incoming.ProxyProcesses),
            DownloadDomains = new List<string>(incoming.DownloadDomains),
            IsCustom = true,
            IsFavorite = false
        };
        Games.AddCustomGame(game);
        return ImportedResult(game.Id);
    }

    /// <summary>Copies the importable rule/name fields onto an existing custom game (id + favorite kept).</summary>
    private static void CopyRuleFields(Game target, Game incoming)
    {
        target.Name = incoming.Name;
        target.EnglishName = incoming.EnglishName;
        target.Aliases = new List<string>(incoming.Aliases);
        target.Icon = incoming.Icon;
        target.AccentColor = incoming.AccentColor;
        target.Category = incoming.Category;
        target.Regions = new List<string>(incoming.Regions);
        target.Platforms = new List<string>(incoming.Platforms);
        target.ProxyDomains = new List<string>(incoming.ProxyDomains);
        target.ProxyIps = new List<string>(incoming.ProxyIps);
        target.ProxyProcesses = new List<string>(incoming.ProxyProcesses);
        target.DownloadDomains = new List<string>(incoming.DownloadDomains);
    }

    /// <summary>Returns candidate, or the first free "candidate-2", "candidate-3" … variant.</summary>
    private string FreeCustomId(string candidate)
    {
        var taken = Games.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(candidate))
            return candidate;
        for (var n = 2; ; n++)
        {
            var next = $"{candidate}-{n}";
            if (!taken.Contains(next))
                return next;
        }
    }

    private static object ConflictResult(string kind, string existingId, string existingName, string incomingName) =>
        new { status = "conflict", conflict = new { kind, existingId, existingName, incomingName } };

    private object ImportedResult(string importedId) =>
        new { status = "imported", importedId, games = GameList() };

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
        var modes = SettingsSvc.Settings.GameCaptureMode;
        var downloads = SettingsSvc.Settings.GameDownloadViaProxy;
        var launches = SettingsSvc.Settings.GameLaunchTargets;
        return Games.Games.Select(g => Mapping.ToDto(g, auto, choices, modes, downloads, launches)).ToArray();
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
    ///     Per-tick snapshot for the auto-switch host: the current game/node (with stable ids, same
    ///     scheme as <see cref="Mapping.StatusDto" />), the latest measured quality and the current
    ///     auto-switch options. Null unless we are actively accelerating. Read under the state lock.
    /// </summary>
    internal async Task<AutoSwitchSnapshot?> AutoSwitchSnapshotAsync(CancellationToken ct)
    {
        return await WithStateValue<AutoSwitchSnapshot?>(() =>
        {
            if (Accel.State != AccelerationState.Accelerating)
                return null;
            var game = Accel.CurrentGame;
            var node = Accel.CurrentNode;
            if (game is null || node is null)
                return null;

            var pool = Servers.Servers.ToList();
            var map = ServerIds.Assign(pool);
            var nodeId = map.TryGetValue(node, out var id) ? id : ServerIds.Fingerprint(node);
            var st = Accel.Status;
            var options = AutoSwitchOptions.FromSettings(SettingsSvc.Settings.Advanced);
            return new AutoSwitchSnapshot(game.Id, nodeId,
                string.IsNullOrWhiteSpace(node.Remark) ? node.Hostname : node.Remark,
                st.Latency, st.Loss, options);
        }, ct);
    }

    /// <summary>
    ///     Switches acceleration to the next-ranked node for <paramref name="gameId" />, excluding the
    ///     given node ids (current node + nodes already tried this session). Ranks exactly like
    ///     <see cref="PickBestNodeAsync" /> but never pings. Returns the candidate's display name, or
    ///     null when no candidate is left.
    /// </summary>
    internal async Task<string?> SwitchToNextNodeAsync(
        string gameId, IReadOnlySet<string> excludeNodeIds, CancellationToken ct)
    {
        var game = await WithStateValue(
            () => Games.Games.FirstOrDefault(x => x.Id == gameId)
                  ?? throw new InvalidOperationException("游戏不存在"),
            ct);

        // Snapshot the pool + its stable ids under the lock (no pinging — reuse the last measurements).
        var (pool, ids) = await WithStateValue(() =>
        {
            var list = Servers.Servers.ToList();
            return (list, ServerIds.Assign(list));
        }, ct);
        if (pool.Count == 0)
            return null;

        // Same penalty/choice reads as PickBestNodeAsync — no network I/O inside this block.
        var (penalty, choice) = await WithStateValue(() =>
        {
            var p = SettingsSvc.Settings.Advanced.RegionPenaltyMs;
            var c = SettingsSvc.Settings.GameServerChoice.GetValueOrDefault(game.Id);
            return (p, c);
        }, ct);

        var ranking = NodeRanker.Rank(game, pool, penalty, choice);

        // The ranking orders tested (Delay >= 0) nodes first, so the first pass over it already prefers
        // tested nodes; the second pass only falls back when nothing tested is left at all.
        Server? candidate = null;
        foreach (var c in ranking.Candidates)
        {
            if (c.Delay < 0)
                continue;
            if (ids.TryGetValue(c.Server, out var cid) && !excludeNodeIds.Contains(cid))
            {
                candidate = c.Server;
                break;
            }
        }

        if (candidate is null)
        {
            foreach (var c in ranking.Candidates)
            {
                if (ids.TryGetValue(c.Server, out var cid) && !excludeNodeIds.Contains(cid))
                {
                    candidate = c.Server;
                    break;
                }
            }
        }

        if (candidate is null)
            return null;

        await StopAccelAsync(ct);
        await StartAccelAsync(gameId, ids[candidate], ct);
        return string.IsNullOrWhiteSpace(candidate.Remark) ? candidate.Hostname : candidate.Remark;
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
        if (JsonParams.HasProperty(p, "subscriptionAutoUpdateHours"))
        {
            // Validate BEFORE mutating anything (same style as rulesUrl).
            var hours = JsonParams.OptionalInt(p, "subscriptionAutoUpdateHours") ?? 0;
            if (hours is not (0 or 6 or 12 or 24 or 48))
                throw new InvalidOperationException("订阅自动更新间隔只能是 0、6、12、24 或 48 小时");
            s.SubscriptionAutoUpdateHours = hours;
        }
        if (JsonParams.HasProperty(p, "extraApps"))
        {
            // Validate the whole array BEFORE mutating anything (same style as rulesUrl).
            var parsed = ParseExtraApps(p);
            s.ExtraApps = parsed;
        }
        if (JsonParams.HasProperty(p, "advanced"))
            ApplyAdvanced(p, s);
        if (JsonParams.HasProperty(p, "lanShare"))
            ApplyLanShare(p, s);
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
        string? defaultCaptureMode = null;
        bool? autoSwitchEnabled = null;
        int? autoSwitchLossPct = null;
        int? autoSwitchLatencyMs = null;
        int? autoSwitchSustainSec = null;
        int? autoSwitchCooldownSec = null;
        int? autoSwitchMaxSwitches = null;

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
        if (JsonParams.HasProperty(adv, "defaultCaptureMode"))
            defaultCaptureMode = AdvancedSettingsValidator.ValidateCaptureMode(
                JsonParams.OptionalString(adv, "defaultCaptureMode"));
        if (JsonParams.HasProperty(adv, "autoSwitchEnabled"))
            autoSwitchEnabled = JsonParams.OptionalBool(adv, "autoSwitchEnabled") ?? false;
        if (JsonParams.HasProperty(adv, "autoSwitchLossPct"))
            autoSwitchLossPct = AdvancedSettingsValidator.ValidateAutoSwitchLossPct(
                JsonParams.OptionalInt(adv, "autoSwitchLossPct") ?? 0);
        if (JsonParams.HasProperty(adv, "autoSwitchLatencyMs"))
            autoSwitchLatencyMs = AdvancedSettingsValidator.ValidateAutoSwitchLatencyMs(
                JsonParams.OptionalInt(adv, "autoSwitchLatencyMs") ?? 0);
        if (JsonParams.HasProperty(adv, "autoSwitchSustainSec"))
            autoSwitchSustainSec = AdvancedSettingsValidator.ValidateAutoSwitchSustainSec(
                JsonParams.OptionalInt(adv, "autoSwitchSustainSec") ?? 0);
        if (JsonParams.HasProperty(adv, "autoSwitchCooldownSec"))
            autoSwitchCooldownSec = AdvancedSettingsValidator.ValidateAutoSwitchCooldownSec(
                JsonParams.OptionalInt(adv, "autoSwitchCooldownSec") ?? 0);
        if (JsonParams.HasProperty(adv, "autoSwitchMaxSwitches"))
            autoSwitchMaxSwitches = AdvancedSettingsValidator.ValidateAutoSwitchMaxSwitches(
                JsonParams.OptionalInt(adv, "autoSwitchMaxSwitches") ?? 0);

        // All fields validated — now apply.
        if (tunDns is not null)
            s.Dns.OtherDns = tunDns;
        if (measureEffect is { } me)
            s.Advanced.MeasureEffect = me;
        if (regionPenalty is { } rp)
            s.Advanced.RegionPenaltyMs = rp;
        if (localPort is { } lp)
            s.Advanced.LocalSocksPort = lp;
        if (defaultCaptureMode is not null)
            s.Advanced.DefaultCaptureMode = defaultCaptureMode;
        if (autoSwitchEnabled is { } enabled)
            s.Advanced.AutoSwitchEnabled = enabled;
        if (autoSwitchLossPct is { } lossPct)
            s.Advanced.AutoSwitchLossPct = lossPct;
        if (autoSwitchLatencyMs is { } latencyMs)
            s.Advanced.AutoSwitchLatencyMs = latencyMs;
        if (autoSwitchSustainSec is { } sustainSec)
            s.Advanced.AutoSwitchSustainSec = sustainSec;
        if (autoSwitchCooldownSec is { } cooldownSec)
            s.Advanced.AutoSwitchCooldownSec = cooldownSec;
        if (autoSwitchMaxSwitches is { } maxSwitches)
            s.Advanced.AutoSwitchMaxSwitches = maxSwitches;
    }

    /// <summary>
    ///     Applies the <c>lanShare</c> object (LAN sharing for consoles). Validates ALL provided fields
    ///     first (throwing on the first error without mutating anything), then applies them. An empty
    ///     password with an unchanged username keeps the stored password unless
    ///     <c>clearPassword: true</c> is sent, so the UI can save without re-entering it.
    /// </summary>
    private static void ApplyLanShare(JsonElement p, AppSettings s)
    {
        if (!p.TryGetProperty("lanShare", out var lan) || lan.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("lanShare 必须是对象");

        var cur = s.LanShare;

        // Read every field first (absent fields fall back to the current values).
        var enabled = JsonParams.OptionalBool(lan, "enabled") ?? cur.Enabled;
        var bindRaw = JsonParams.OptionalString(lan, "bindAddress");
        var httpPort = JsonParams.OptionalInt(lan, "httpPort") ?? cur.HttpPort;
        var socksPort = JsonParams.OptionalInt(lan, "socksPort") ?? cur.SocksPort;
        var username = (JsonParams.OptionalString(lan, "username") ?? cur.Username).Trim();
        var clearPassword = JsonParams.OptionalBool(lan, "clearPassword") ?? false;

        // Password resolution: an explicitly sent non-empty password wins; an empty/absent password
        // keeps the stored one when the username is unchanged (unless clearPassword).
        var passwordRaw = JsonParams.OptionalString(lan, "password");
        string password;
        if (clearPassword || username.Length == 0)
            password = "";
        else if (!string.IsNullOrEmpty(passwordRaw))
            password = passwordRaw;
        else if (username == cur.Username.Trim())
            password = cur.Password;
        else
            password = "";

        // --- validation (nothing has been mutated yet) ---

        // The bind address is checked against the current local addresses only when enabling or when
        // an explicit non-empty address was provided.
        string normalizedBind;
        if (enabled || (bindRaw != null && bindRaw.Trim().Length > 0))
        {
            var localAddresses = LocalPrivateIPv4Addresses().Select(a => a.Address).ToList();
            normalizedBind = LanShareValidator.ValidateBindAddress((bindRaw ?? cur.BindAddress).Trim(), localAddresses);
        }
        else
        {
            normalizedBind = (bindRaw ?? cur.BindAddress).Trim();
        }

        IReadOnlyList<string> clients;
        if (JsonParams.HasProperty(lan, "allowedClients"))
        {
            var raw = JsonParams.StringList(lan, "allowedClients");
            // Required non-empty when enabling; still normalized when provided non-empty while disabled.
            if (enabled || raw.Any(c => !string.IsNullOrWhiteSpace(c)))
                clients = LanShareValidator.ValidateClients(raw);
            else
                clients = new List<string>(raw);
        }
        else
        {
            clients = enabled
                ? LanShareValidator.ValidateClients(cur.AllowedClients)
                : new List<string>(cur.AllowedClients);
        }

        LanShareValidator.ValidatePorts(httpPort, socksPort, s.Advanced.LocalSocksPort);
        LanShareValidator.ValidateCredentials(username, password);

        // --- all fields validated — now apply ---
        cur.Enabled = enabled;
        cur.BindAddress = normalizedBind;
        cur.HttpPort = httpPort;
        cur.SocksPort = socksPort;
        cur.AllowedClients = new List<string>(clients);
        cur.Username = username;
        cur.Password = password;
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
        // ConcurrentDictionary (written by games.setServer under the same lock, read lock-free by
        // the accelerator's history recorder), so reads here are safe either way. No network I/O inside
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

/// <summary>Per-tick snapshot for the auto-switch host, read under the state lock.</summary>
internal sealed record AutoSwitchSnapshot(
    string GameId, string NodeId, string NodeName, int Latency, double Loss, AutoSwitchOptions Options);
