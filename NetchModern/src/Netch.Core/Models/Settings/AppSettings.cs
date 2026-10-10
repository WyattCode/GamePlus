using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Settings;

/// <summary>
///     Root application configuration, persisted as JSON.
/// </summary>
public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private AppTheme _theme = AppTheme.System;

    [ObservableProperty]
    private string _language = "System";

    [ObservableProperty]
    private bool _minimizeToTray = true;

    /// <summary>Remote game-rules URL. Empty = the feature is disabled (no network traffic at all).</summary>
    [ObservableProperty]
    private string _rulesUrl = "";

    /// <summary>Global master switch for auto-acceleration on game launch.</summary>
    [ObservableProperty]
    private bool _autoAccelerate = true;

    /// <summary>
    ///     Automatic subscription refresh interval in hours. 0 = disabled; allowed values are
    ///     0, 6, 12, 24, 48. Defaults to 12.
    /// </summary>
    [ObservableProperty]
    private int _subscriptionAutoUpdateHours = 12;

    public DnsSettings Dns { get; set; } = new();

    public AdvancedSettings Advanced { get; set; } = new();

    /// <summary>Ids of games the user has favourited.</summary>
    public List<string> FavoriteGames { get; set; } = new();

    /// <summary>Ids of games with the per-game auto-acceleration toggle enabled.</summary>
    public List<string> AutoAccelerateGames { get; set; } = new();

    /// <summary>
    ///     Per-game server choice for games that have both 国服 and 国际服 (gameId → "cn" | "intl").
    ///     A missing entry means "intl" (the default).
    ///     Concurrent so the accelerator's history recorder can read it lock-free while the RPC path
    ///     writes it under the state lock.
    /// </summary>
    public ConcurrentDictionary<string, string> GameServerChoice { get; set; } = new();

    /// <summary>
    ///     Per-game acceleration (capture) mode override (gameId → "auto" | "process" | "tun" | "proxy").
    ///     A missing entry means "follow the global default" (<see cref="AdvancedSettings.DefaultCaptureMode" />).
    ///     Concurrent so the accelerator can read it lock-free while the RPC path writes it under the
    ///     state lock.
    /// </summary>
    public ConcurrentDictionary<string, string> GameCaptureMode { get; set; } = new();

    /// <summary>
    ///     Games whose CDN download domains should also be routed through the node (gameId → true).
    ///     A missing entry means downloads go DIRECT (the default) — personal VPS bandwidth is too
    ///     limited for multi-GB downloads. Concurrent so the accelerator can read it lock-free while
    ///     the RPC path writes it under the state lock.
    /// </summary>
    public ConcurrentDictionary<string, bool> GameDownloadViaProxy { get; set; } = new();

    /// <summary>
    ///     Per-game launch target for 「加速并启动」 (gameId → target). Only three forms are allowed
    ///     (validated by <c>LaunchTargetValidator.Normalize</c>): a <c>steam://rungameid/&lt;appid&gt;</c>
    ///     link, an Epic launcher link, or an absolute path to an existing <c>.exe</c> file.
    ///     Concurrent so the RPC path writes it while settings are being serialized.
    /// </summary>
    public ConcurrentDictionary<string, string> GameLaunchTargets { get; set; } = new();

    /// <summary>
    ///     Extra applications that are additionally proxied or explicitly bypassed in process-capture
    ///     mode. Only applies when acceleration runs in process mode (netfilter Redirector).
    /// </summary>
    public List<ExtraApp> ExtraApps { get; set; } = new();

    /// <summary>
    ///     Last measured direct baseline per game (gameId → baseline). Reused for the effect comparison
    ///     on later sessions so the ~3.5 s measurement only runs once per game by default.
    ///     Concurrent so the acceleration path can mutate it while settings are being serialized.
    /// </summary>
    public ConcurrentDictionary<string, StoredBaseline> EffectBaselines { get; set; } = new();

    /// <summary>
    ///     Games whose direct baseline should be re-measured on the next acceleration (one-shot flag,
    ///     set by the effect card's "重新测量" button). Keys are game ids; the value is always true.
    ///     Concurrent so the RPC path can mutate it while settings are being serialized.
    /// </summary>
    public ConcurrentDictionary<string, bool> RemeasureGames { get; set; } = new();
}

public enum AppTheme
{
    System,
    Light,
    Dark
}

public partial class DnsSettings : ObservableObject
{
    [ObservableProperty]
    private string _otherDns = "1.1.1.1";
}
