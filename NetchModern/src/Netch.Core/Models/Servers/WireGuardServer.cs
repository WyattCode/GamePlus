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

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "WG";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "WireGuard";

    public override string MaskedData() => $"{Hostname}:{Port} (wireguard)";
}
