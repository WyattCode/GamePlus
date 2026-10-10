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

    public DnsSettings Dns { get; set; } = new();

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
    private string _otherDns = "1.1.1.1";
}
