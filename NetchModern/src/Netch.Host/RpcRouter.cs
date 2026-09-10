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

    public IdRegistry<Server> ServerIds { get; } = new();
    public IdRegistry<Netch.Core.Models.Subscription> SubIds { get; } = new();

    // The repositories/accelerator are single-threaded. Each session dispatches sequentially, but two
    // sessions (e.g. a renderer reload overlapping the old socket) could otherwise dispatch at once — this
    // one router instance is shared by all sessions, so this gate serialises dispatch globally.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IServerRepository Servers => _sp.GetRequiredService<IServerRepository>();
    private IGameRepository Games => _sp.GetRequiredService<IGameRepository>();
    private ISubscriptionRepository Subs => _sp.GetRequiredService<ISubscriptionRepository>();
    private IAcceleratorService Accel => _sp.GetRequiredService<IAcceleratorService>();
    private IPersistenceService Persistence => _sp.GetRequiredService<IPersistenceService>();
    private ISettingsService SettingsSvc => _sp.GetRequiredService<ISettingsService>();
    private IUpdateChecker UpdateChecker => _sp.GetRequiredService<IUpdateChecker>();

    public async Task<object?> DispatchAsync(string method, JsonElement @params, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await DispatchCoreAsync(method, @params, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<object?> DispatchCoreAsync(string method, JsonElement @params, CancellationToken ct)
    {
        switch (method)
        {
            case "games.list":
                return Games.Games.Select(Mapping.ToDto).ToArray();

            case "servers.list":
                return ServerList();

            case "subs.list":
                return Subs.Subscriptions.Select(s => Mapping.ToDto(s, SubIds)).ToArray();

            case "servers.ping":
                await Servers.PingAllAsync(ct);
                return ServerList();

            case "servers.export":
                // Every server as a share link string (skips protocols the exporter can't emit, e.g. WireGuard).
                return Servers.Servers.ToList()
                    .Select(ShareLinkExporter.ToShareLink)
                    .Where(l => l is not null)
                    .Select(l => l!)
                    .ToArray();

            case "accel.start":
            {
                var gameId = JsonParams.RequiredString(@params, "gameId");
                var nodeId = JsonParams.OptionalString(@params, "nodeId");
                await StartAsync(gameId, nodeId, ct);
                return Mapping.StatusDto(Accel, ServerIds);
            }

            case "accel.stop":
                await Accel.StopAsync(ct);
                return Mapping.StatusDto(Accel, ServerIds);

            case "accel.status":
                return Mapping.StatusDto(Accel, ServerIds);

            case "servers.add":
            {
                var link = JsonParams.RequiredString(@params, "link");
                var parsed = ShareLinkParser.ParseText(link);
                if (parsed.Count == 0)
                    throw new InvalidOperationException("无法识别的分享链接");
                foreach (var s in parsed)
                    Servers.Add(s);
                await Persistence.SaveAsync();
                return ServerList();
            }

            case "servers.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var server = ServerIds.Resolve(id, Servers.Servers.ToList())
                             ?? throw new InvalidOperationException("线路不存在");
                Servers.Remove(server);
                await Persistence.SaveAsync();
                return ServerList();
            }

            case "subs.add":
            {
                var remark = JsonParams.OptionalString(@params, "remark") ?? "";
                var link = JsonParams.RequiredString(@params, "link");
                if (string.IsNullOrWhiteSpace(link))
                    throw new InvalidOperationException("订阅链接不能为空");
                Subs.Add(new Netch.Core.Models.Subscription
                {
                    Remark = string.IsNullOrWhiteSpace(remark) ? "未命名订阅" : remark,
                    Link = link.Trim()
                });
                await Persistence.SaveAsync();
                return SubList();
            }

            case "subs.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var sub = SubIds.Resolve(id, Subs.Subscriptions.ToList())
                          ?? throw new InvalidOperationException("订阅不存在");
                Subs.Remove(sub); // also purges the servers this subscription imported
                await Persistence.SaveAsync();
                return new { subscriptions = SubList(), servers = ServerList() };
            }

            case "subs.update":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var sub = SubIds.Resolve(id, Subs.Subscriptions.ToList())
                          ?? throw new InvalidOperationException("订阅不存在");
                await Subs.UpdateAsync(sub, ct);
                await Persistence.SaveAsync();
                return new { subscriptions = SubList(), servers = ServerList() };
            }

            case "games.toggleFavorite":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var game = Games.Games.FirstOrDefault(g => g.Id == id)
                           ?? throw new InvalidOperationException("游戏不存在");
                Games.ToggleFavorite(game);
                await SettingsSvc.SaveAsync();
                return Games.Games.Select(Mapping.ToDto).ToArray();
            }

            case "games.add":
            {
                var game = Mapping.ToGame(@params);
                if (string.IsNullOrWhiteSpace(game.Name))
                    throw new InvalidOperationException("游戏名称不能为空");
                Games.AddCustomGame(game);
                return Games.Games.Select(Mapping.ToDto).ToArray();
            }

            case "games.update":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var existing = Games.Games.FirstOrDefault(g => g.Id == id)
                               ?? throw new InvalidOperationException("游戏不存在");
                if (!existing.IsCustom)
                    throw new InvalidOperationException("只能编辑自定义游戏");
                var updated = Mapping.ToGame(@params, id);
                // Preserve identity/read-only bits, replace editable fields.
                updated.IsCustom = true;
                updated.IsFavorite = existing.IsFavorite;
                var index = Games.Games.IndexOf(existing);
                if (index >= 0)
                {
                    Games.Games[index] = updated;
                    Games.UpdateCustomGame(updated);
                }
                return Games.Games.Select(Mapping.ToDto).ToArray();
            }

            case "games.remove":
            {
                var id = JsonParams.RequiredString(@params, "id");
                var game = Games.Games.FirstOrDefault(g => g.Id == id)
                           ?? throw new InvalidOperationException("游戏不存在");
                Games.RemoveGame(game);
                return Games.Games.Select(Mapping.ToDto).ToArray();
            }

            case "settings.get":
                return Mapping.ToDto(SettingsSvc.Settings);

            case "settings.set":
                ApplySettings(@params);
                await SettingsSvc.SaveAsync();
                return Mapping.ToDto(SettingsSvc.Settings);

            case "update.check":
            {
                var info = await UpdateChecker.CheckAsync(ct);
                return info == null
                    ? null
                    : new { hasUpdate = info.HasUpdate, latest = info.Latest.ToString(), tag = info.TagName, url = info.Url };
            }

            default:
                throw new InvalidOperationException($"unknown method: {method}");
        }
    }

    private ServerDto[] ServerList() =>
        Servers.Servers.ToList().Select(s => Mapping.ToDto(s, ServerIds)).ToArray();

    private SubscriptionDto[] SubList() =>
        Subs.Subscriptions.ToList().Select(s => Mapping.ToDto(s, SubIds)).ToArray();

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

    private async Task StartAsync(string gameId, string? nodeId, CancellationToken ct)
    {
        var game = Games.Games.FirstOrDefault(g => g.Id == gameId)
                   ?? throw new InvalidOperationException("游戏不存在");

        var node = string.IsNullOrEmpty(nodeId)
            ? await PickBestNodeAsync(ct)
            : ServerIds.Resolve(nodeId, Servers.Servers.ToList())
              ?? throw new InvalidOperationException("线路不存在");

        await Accel.StartAsync(game, node, ct);
    }

    /// <summary>Smart routing: lowest tested latency; if nothing is tested yet, ping first.</summary>
    private async Task<Server> PickBestNodeAsync(CancellationToken ct)
    {
        var pool = Servers.Servers.ToList();
        if (pool.Count == 0)
            throw new InvalidOperationException("没有可用线路，请先添加或导入线路");

        if (pool.All(s => s.Delay < 0))
        {
            await Servers.PingAllAsync(ct);
            pool = Servers.Servers.ToList();
        }

        return pool.Where(s => s.Delay >= 0).OrderBy(s => s.Delay).FirstOrDefault() ?? pool[0];
    }
}
