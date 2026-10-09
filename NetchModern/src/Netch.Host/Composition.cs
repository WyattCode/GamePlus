using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core.Abstractions;
using Netch.Core.Controllers;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

namespace Netch.Host;

/// <summary>
///     Builds the Netch.Core service graph headlessly. This is the engine the Electron frontend drives
///     over the local WebSocket — no UI project references it directly anymore.
/// </summary>
internal static class Composition
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => new HttpClient(
                new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
            { Timeout = TimeSpan.FromSeconds(30) });

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IServerRepository, ServerRepository>();
        services.AddSingleton<IGameRepository, GameRepository>();
        services.AddSingleton<IModeRepository, ModeRepository>();
        services.AddSingleton<ISubscriptionRepository, SubscriptionRepository>();
        services.AddSingleton<IPersistenceService, JsonPersistenceService>();
        services.AddSingleton<ISystemProxyService, SystemProxyService>();
        services.AddSingleton<IModeControllerFactory, ModeControllerFactory>();

        services.AddTransient<V2rayServerController>();
        services.AddTransient<SingBoxServerController>();
        // Pick the proxy core by node protocol: Hysteria2 and Shadowsocks-with-plugin → sing-box,
        // everything else → xray (which cannot do Hysteria2 or SIP003 plugins).
        services.AddSingleton<Func<Server, IServerController>>(sp => server =>
            server is Hysteria2Server || (server is ShadowsocksServer { Plugin.Length: > 0 })
                ? sp.GetRequiredService<SingBoxServerController>()
                : sp.GetRequiredService<V2rayServerController>());

        services.AddSingleton<IAcceleratorService, AcceleratorService>();

        // Update check. Defaults to this project's repo; override per release (e.g. a fork/reorg) via
        // GAMEPLUS_UPDATE_OWNER/REPO env vars. Current version = the host assembly's.
        var current = typeof(Composition).Assembly.GetName().Version ?? new Version(3, 0, 0);
        var owner = Environment.GetEnvironmentVariable("GAMEPLUS_UPDATE_OWNER") ?? "WyattCode";
        var repo = Environment.GetEnvironmentVariable("GAMEPLUS_UPDATE_REPO") ?? "GamePlus";
        services.AddSingleton<IUpdateChecker>(sp =>
            new GitHubUpdateChecker(sp.GetRequiredService<HttpClient>(), current, owner, repo));

        return services.BuildServiceProvider();
    }
}
