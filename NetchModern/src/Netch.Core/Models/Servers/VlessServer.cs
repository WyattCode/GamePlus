using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class VlessServer : Server
{
    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private string _flow = string.Empty;

    [ObservableProperty]
    private string _network = "tcp";

    /// <summary>TLS / "reality" / "none". Determines which stream security block is emitted.</summary>
    [ObservableProperty]
    private string _security = "none";

    [ObservableProperty]
    private string? _sni;

    /// <summary>REALITY server public key (the "pbk" share-link field). Required when <see cref="Security" /> is "reality".</summary>
    [ObservableProperty]
    private string? _publicKey;

    /// <summary>REALITY short id ("sid"); may be empty.</summary>
    [ObservableProperty]
    private string? _shortId;

    /// <summary>REALITY spiderX ("spx"); may be empty.</summary>
    [ObservableProperty]
    private string? _spiderX;

    /// <summary>uTLS fingerprint ("fp") for TLS/REALITY, e.g. "chrome". Defaults to chrome when empty.</summary>
    [ObservableProperty]
    private string? _fingerprint;

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

    /// <summary>Accept a self-signed / mismatched certificate.</summary>
    [ObservableProperty]
    private bool _allowInsecure;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "VLESS";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "VLESS";

    public override string MaskedData()
        => $"{Hostname}:{Port} [{Network}{(Security is "none" or "" ? string.Empty : "+" + Security)}]";
}
