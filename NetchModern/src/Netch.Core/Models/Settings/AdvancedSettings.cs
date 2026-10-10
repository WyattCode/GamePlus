using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Settings;

/// <summary>
///     Advanced settings that are otherwise only reachable through settings.json or hard-coded values.
/// </summary>
public partial class AdvancedSettings : ObservableObject
{
    /// <summary>Default off-region penalty (ms) applied by <see cref="Services.NodeRanker" />.</summary>
    public const int DefaultRegionPenaltyMs = 60;

    /// <summary>Upper bound for <see cref="RegionPenaltyMs" />.</summary>
    public const int MaxRegionPenaltyMs = 300;

    /// <summary>All valid values for <see cref="DefaultCaptureMode" />.</summary>
    public static readonly string[] CaptureModes = { "auto", "process", "tun", "proxy" };

    /// <summary>
    ///     When true, the accelerator takes a fresh direct baseline (~3.5 s) before EVERY acceleration.
    ///     When false (default) the baseline is measured automatically only on the first acceleration of
    ///     each game and then reused; the user can force a re-measure from the effect card.
    /// </summary>
    [ObservableProperty]
    private bool _measureEffect;

    /// <summary>Penalty (ms) applied to a node outside the game's preferred regions.</summary>
    [ObservableProperty]
    private int _regionPenaltyMs = DefaultRegionPenaltyMs;

    /// <summary>
    ///     Fixed local SOCKS5 inbound port of xray / sing-box so other apps can be pointed at it.
    ///     0 = automatic (a free port is picked at start).
    /// </summary>
    [ObservableProperty]
    private int _localSocksPort;

    /// <summary>
    ///     Global default acceleration (capture) mode: "auto" | "process" | "tun" | "proxy".
    ///     "auto" keeps the historical behaviour (process capture → TUN → system proxy fallback).
    ///     A per-game override in <see cref="AppSettings.GameCaptureMode" /> takes precedence.
    /// </summary>
    [ObservableProperty]
    private string _defaultCaptureMode = "auto";
}
