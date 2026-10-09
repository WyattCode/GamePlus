using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core;
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
        services.AddSingleton<IGameRepository>(sp =>
            new GameRepository(sp.GetRequiredService<ISettingsService>(), Path.Combine(AppPaths.DataDirectory, "custom-games.json"), sp.GetRequiredService<ILogService>()));
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

        // Update check. Source is resolved by UpdateSourceOptions (env vars, then update.json next to
        // the exe, then the default repo). Current version = the host assembly's.
        var current = typeof(Composition).Assembly.GetName().Version ?? new Version(3, 0, 0);
        var options = UpdateSourceOptions.Resolve();
        services.AddSingleton<IUpdateChecker>(sp =>
            new GitHubUpdateChecker(
                sp.GetRequiredService<HttpClient>(),
                current,
                options.Owner,
                options.Repo,
                options.Url,
                options.Enabled));

        return services.BuildServiceProvider();
    }
}
