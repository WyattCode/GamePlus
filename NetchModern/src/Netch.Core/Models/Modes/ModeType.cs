namespace Netch.Core.Models.Modes;

/// <summary>
///     Traffic-capture strategy. Mirrors the original Netch modes.
/// </summary>
public enum ModeType
{
    /// <summary>Intercept selected process traffic via the netfilter2 kernel driver.</summary>
    ProcessMode = 0,

    /// <summary>Create a WinTUN virtual adapter and route through tun2socks.</summary>
    TunMode = 1,

    /// <summary>Share the proxied network over LAN using Npcap / WinPcap.</summary>
    ShareMode = 2,

    /// <summary>Plain local web proxy.</summary>
    WebMode = 3
}
