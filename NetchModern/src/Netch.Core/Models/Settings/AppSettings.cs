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
    private ushort _socks5LocalPort = 2801;

    [ObservableProperty]
    private ushort _httpLocalPort = 2802;

    [ObservableProperty]
    private bool _allowDevices;

    [ObservableProperty]
    private bool _tcping = true;

    [ObservableProperty]
    private bool _startWhenOpened;

    [ObservableProperty]
    private bool _stopWhenExited = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private string _stunServer = "stun.qq.com";

    public DnsSettings Dns { get; set; } = new();

    public string? LastServerRemark { get; set; }
    public string? LastModeRemark { get; set; }

    /// <summary>Ids of games the user has favourited.</summary>
    public List<string> FavoriteGames { get; set; } = new();
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
    private bool _useCustomDns;

    [ObservableProperty]
    private string _chinaDns = "223.5.5.5";

    [ObservableProperty]
    private string _otherDns = "1.1.1.1";
}
