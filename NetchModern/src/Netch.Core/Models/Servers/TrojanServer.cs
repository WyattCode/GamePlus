using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class TrojanServer : Server
{
    [ObservableProperty]
    private bool _allowInsecure;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string? _sni;

    /// <summary>Transport network: "tcp" (default), "ws", "grpc", "h2"/"http", "httpupgrade", "xhttp".</summary>
    [ObservableProperty]
    private string _network = "tcp";

    /// <summary>Transport host header / WS host.</summary>
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

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "Trojan";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "Trojan";

    public override string MaskedData() => $"{Hostname}:{Port}" + (string.IsNullOrEmpty(Sni) ? string.Empty : $" (sni {Sni})");
}
