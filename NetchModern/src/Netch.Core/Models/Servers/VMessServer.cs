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

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "VMess";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "VMess";

    public override string MaskedData() => $"{Hostname}:{Port} [{Network}{(Tls ? "+tls" : string.Empty)}]";
}
