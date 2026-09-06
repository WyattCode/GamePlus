using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

/// <summary>
///     Hysteria2 node (QUIC-based, strong against packet loss). Runs via the bundled sing-box core rather
///     than xray, since xray does not implement the protocol.
/// </summary>
public partial class Hysteria2Server : Server
{
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>TLS SNI / server name. Defaults to the hostname when empty.</summary>
    [ObservableProperty]
    private string? _sni;

    /// <summary>Accept a self-signed / mismatched certificate.</summary>
    [ObservableProperty]
    private bool _allowInsecure;

    /// <summary>Salamander obfuscation password (optional; leave empty to disable obfs).</summary>
    [ObservableProperty]
    private string? _obfsPassword;

    /// <summary>Upload bandwidth (Mbps). When &gt;0 together with <see cref="DownMbps" />, enables the Brutal
    ///     congestion controller — a fixed-rate, loss-resilient mode that keeps latency stable on lossy links.
    ///     Set to your real link speed; too high over-sends and causes loss.</summary>
    [ObservableProperty]
    private int _upMbps;

    /// <summary>Download bandwidth (Mbps). See <see cref="UpMbps" />.</summary>
    [ObservableProperty]
    private int _downMbps;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "Hysteria2";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "Hysteria2";

    public override string MaskedData() => $"{Hostname}:{Port} [hy2{(AllowInsecure ? "+insecure" : string.Empty)}]";
}
