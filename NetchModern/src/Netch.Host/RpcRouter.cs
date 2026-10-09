using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

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
    private IUpdateChecker UpdateChecker => _sp.GetRequiredService<IUpdateChecker>();
    private ILogService Logs => _sp.GetRequiredService<ILogService>();

    public Task<object?> DispatchAsync(string method, JsonElement @params, CancellationToken ct) =>
        DispatchCoreAsync(method, @params, ct);

    private async Task<object?> DispatchCoreAsync(string method, JsonElement @params, CancellationToken ct)
    {
        switch (method)
        {
            case "games.list":
                return await WithStateValue(() => Games.Games.Select(Mapping.ToDto).ToArray(), ct);

            case "servers.list":
                return await WithStateValue(ServerList, ct);

            case "subs.list":
                return await WithStateValue(SubList, ct);

            case "logs.recent":
                // LogService.Recent is thread-safe (ConcurrentQueue snapshot), so no state lock needed.
                return Logs.Recent.Select(Mapping.ToDto).ToArray();

            case "servers.ping":
            {
                // Snapshot under the lock, ping the snapshot outside it (network I/O), then build the
                // response under the lock again.
                var snapshot = await WithStateValue(() => Servers.Servers.ToList(), ct);
                await Servers.PingAsync(snapshot, ct);
                return await WithStateValue(ServerList, ct);
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
                    return Games.Games.Select(Mapping.ToDto).ToArray();
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
                    return Games.Games.Select(Mapping.ToDto).ToArray();
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
                    return Games.Games.Select(Mapping.ToDto).ToArray();
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
                    return Games.Games.Select(Mapping.ToDto).ToArray();
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

            default:
                throw new InvalidOperationException($"unknown method: {method}");
        }
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

    private void ApplySettings(JsonElement p)
    {
        var s = SettingsSvc.Settings;
        if (JsonParams.OptionalString(p, "theme") is { } theme && Enum.TryParse<Netch.Core.Models.Settings.AppTheme>(theme, true, out var appTheme))
            s.Theme = appTheme;
        if (JsonParams.OptionalString(p, "language") is { } language)
            s.Language = language;
        if (JsonParams.OptionalBool(p, "minimizeToTray") is { } minimizeToTray)
            s.MinimizeToTray = minimizeToTray;
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
            node = await PickBestNodeAsync(ct);
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

    /// <summary>Smart routing: lowest tested latency; if nothing is tested yet, ping first.</summary>
    private async Task<Server> PickBestNodeAsync(CancellationToken ct)
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

        return pool.Where(s => s.Delay >= 0).OrderBy(s => s.Delay).FirstOrDefault() ?? pool[0];
    }
}
