using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class WireGuardServer : Server
{
    [ObservableProperty]
    private string _publicKey = string.Empty;

    [ObservableProperty]
    private string _privateKey = string.Empty;

    [ObservableProperty]
    private string _endpointAllowedIPs = "0.0.0.0/0, ::/0";

    /// <summary>Local interface addresses (comma-separated CIDRs), e.g. "10.0.0.2/32, fd00::2/128". Required.</summary>
    [ObservableProperty]
    private string _localAddresses = string.Empty;

    /// <summary>Optional pre-shared key.</summary>
    [ObservableProperty]
    private string? _preSharedKey;

    /// <summary>Interface MTU. Defaults to 1420.</summary>
    [ObservableProperty]
    private int _mtu = 1420;

    /// <summary>Optional reserved bytes for the WireGuard handshake (comma-separated ints), e.g. "1,2,3".</summary>
    [ObservableProperty]
    private string? _reserved;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "WG";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "WireGuard";

    public override string MaskedData() => $"{Hostname}:{Port} (wireguard)";
}
