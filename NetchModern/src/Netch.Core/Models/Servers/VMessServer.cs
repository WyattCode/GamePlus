using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class VMessServer : Server
{
    [ObservableProperty]
    private int _alterId;

    [ObservableProperty]
    private string _network = "tcp";

    [ObservableProperty]
    private string _security = "auto";

    [ObservableProperty]
    private bool _tls;

    [ObservableProperty]
    private string _userId = string.Empty;

    /// <summary>Transport host header / WS host (v2rayN "host").</summary>
    [ObservableProperty]
    private string? _host;

    /// <summary>Transport path (WS / h2 / httpupgrade / xhttp).</summary>
    [ObservableProperty]
    private string? _path;

    /// <summary>gRPC service name.</summary>
    [ObservableProperty]
    private string? _serviceName;

    /// <summary>gRPC multi-mode ("multi" enables multiMode).</summary>
    [ObservableProperty]
    private string? _mode;

    /// <summary>Comma-separated ALPN list, e.g. "h2,http/1.1".</summary>
    [ObservableProperty]
    private string? _alpn;

    /// <summary>uTLS fingerprint ("fp"), e.g. "chrome".</summary>
    [ObservableProperty]
    private string? _fingerprint;

    /// <summary>TLS SNI / server name. Falls back to Host then Hostname when empty.</summary>
    [ObservableProperty]
    private string? _sni;

    /// <summary>Accept a self-signed / mismatched certificate.</summary>
    [ObservableProperty]
    private bool _allowInsecure;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "VMess";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "VMess";

    public override string MaskedData() => $"{Hostname}:{Port} [{Network}{(Tls ? "+tls" : string.Empty)}]";
}
