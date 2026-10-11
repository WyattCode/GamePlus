using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Settings;

/// <summary>
///     LAN sharing for consoles (Switch / PlayStation / Xbox): exposes extra HTTP (+ optional SOCKS5)
///     proxy inbounds on one private LAN IPv4 address of this PC while acceleration is running, so the
///     console can point its "proxy server" network setting at this PC.
///     Only TCP that the console sends via the HTTP proxy is accelerated — game UDP traffic, P2P and
///     voice chat are NOT proxied, and NAT type is NOT improved.
///     Default OFF; enabling requires an explicit user action in the UI.
/// </summary>
public partial class LanShareSettings : ObservableObject
{
    /// <summary>Master switch. Default OFF; the UI must confirm before enabling.</summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>Private LAN IPv4 of this PC to bind the LAN inbounds to (e.g. 192.168.1.10).</summary>
    [ObservableProperty]
    private string _bindAddress = "";

    /// <summary>HTTP proxy port for the console (1024–65535).</summary>
    [ObservableProperty]
    private int _httpPort = 7899;

    /// <summary>Optional SOCKS5 port for PCs/phones on the LAN. 0 = off.</summary>
    [ObservableProperty]
    private int _socksPort = 0;

    /// <summary>
    ///     Non-empty allowlist of private IPv4 addresses / CIDRs that may use the LAN inbounds.
    ///     Traffic from other LAN sources is blocked by routing rules.
    /// </summary>
    [ObservableProperty]
    private List<string> _allowedClients = new();

    /// <summary>Optional HTTP basic / SOCKS username. Must be set together with <see cref="Password" />.</summary>
    [ObservableProperty]
    private string _username = "";

    /// <summary>Optional HTTP basic / SOCKS password. Must be set together with <see cref="Username" />.</summary>
    [ObservableProperty]
    private string _password = "";
}

/// <summary>
///     Immutable, validated snapshot of the LAN-share settings handed to the server controllers at
///     acceleration start. Built by <c>LanShareValidator.BuildOptions</c>.
/// </summary>
public sealed record LanShareOptions(
    string BindAddress,
    int HttpPort,
    int SocksPort,
    IReadOnlyList<string> AllowedClients,
    string? Username,
    string? Password);

/// <summary>
///     Result of resolving the LAN-share options at acceleration start: either a validated snapshot or
///     a Chinese problem message — the acceleration itself continues without LAN sharing.
/// </summary>
public sealed record LanShareOptionsResult(LanShareOptions? Options, string? Problem);
