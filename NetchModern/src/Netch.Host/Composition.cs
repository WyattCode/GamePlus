using System.Net.Http;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core;
using Netch.Core.Abstractions;
using Netch.Core.Controllers;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Netch.Core.Services.AutoStart;
using Netch.Core.Services.Diagnostics;
using Netch.Core.Services.Library;
using Netch.Core.Services.Rules;

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

        // Remote-updatable game rules: the cache lives next to the other data files. LoadInitial() runs
        // once when the singleton is first resolved (before the repository reads its seeds).
        services.AddSingleton(sp =>
        {
            var svc = new GameRulesService(
                sp.GetRequiredService<HttpClient>(),
                Path.Combine(AppPaths.DataDirectory, "game-rules.json"),
                sp.GetRequiredService<ILogService>());
            svc.LoadInitial();
            return svc;
        });

        services.AddSingleton<IServerRepository, ServerRepository>();
        services.AddSingleton<IGameRepository>(sp =>
            new GameRepository(sp.GetRequiredService<ISettingsService>(),
                Path.Combine(AppPaths.DataDirectory, "custom-games.json"),
                sp.GetRequiredService<ILogService>(),
                sp.GetRequiredService<GameRulesService>().CreateGames));
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

        // Acceleration-effect measurement: a real TLS-handshake probe against the game's own target.
        services.AddSingleton<IEffectProbe, TlsHandshakeProbe>();
        services.AddSingleton<EffectMonitor>();
        services.AddSingleton<IBaselineStore>(sp => new SettingsBaselineStore(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogService>()));

        services.AddSingleton<IAcceleratorService>(sp => new AcceleratorService(
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<Func<Server, IServerController>>(),
            sp.GetRequiredService<ISystemProxyService>(),
            sp.GetRequiredService<IModeControllerFactory>(),
            sp.GetRequiredService<EffectMonitor>(),
            () => sp.GetRequiredService<ISettingsService>().Settings.ExtraApps.ToList(),
            () => sp.GetRequiredService<ISettingsService>().Settings.Advanced,
            sp.GetRequiredService<IBaselineStore>()));

        // One-click diagnostics: environment checks + plain-Chinese explanations.
        services.AddSingleton<IDiagnosticEnvironment>(_ =>
            new WindowsDiagnosticEnvironment(new WindowsProxySettingsStore()));
        services.AddSingleton<DiagnosticsService>();

        // Installed-game auto-detection: Steam / Epic / uninstall-registry scanners + matcher.
        services.AddSingleton(_ => new GameLibraryScanner(new IGameScanner[]
        {
            new SteamScanner(null),
            new EpicScanner(null),
            new UninstallRegistryScanner(new RegistryUninstallSource())
        }));

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

        // Launch-at-login scheduled task. The host runs elevated, so it can create a
        // highest-privileges task without a UAC prompt at each logon. Windows-only APIs are guarded
        // so the host still builds/runs on other platforms (where the feature reports unsupported).
        services.AddSingleton(_ => new AutoStartService(
            new SystemProcessRunner(),
            IsElevated,
            () => AutoStartTask.ResolveAppExe(AppContext.BaseDirectory, File.Exists),
            CurrentUserName,
            Path.GetTempPath()));

        return services.BuildServiceProvider();
    }

    /// <summary>True when the current process runs with administrator rights (false on non-Windows).</summary>
    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Current user name in DOMAIN\user form on Windows, Environment.UserName elsewhere.</summary>
    private static string CurrentUserName()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.UserName;
        try
        {
            return WindowsIdentity.GetCurrent().Name;
        }
        catch
        {
            return Environment.UserName;
        }
    }
}
