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

    /// <summary>
    ///     When false, the accelerator skips the direct baseline + effect sampling (saves ~3.5 s on start);
    ///     the effect card then shows "已关闭加速效果测量".
    /// </summary>
    [ObservableProperty]
    private bool _measureEffect = true;

    /// <summary>Penalty (ms) applied to a node outside the game's preferred regions.</summary>
    [ObservableProperty]
    private int _regionPenaltyMs = DefaultRegionPenaltyMs;

    /// <summary>
    ///     Fixed local SOCKS5 inbound port of xray / sing-box so other apps can be pointed at it.
    ///     0 = automatic (a free port is picked at start).
    /// </summary>
    [ObservableProperty]
    private int _localSocksPort;
}
