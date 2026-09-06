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

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "Trojan";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "Trojan";

    public override string MaskedData() => $"{Hostname}:{Port}" + (string.IsNullOrEmpty(Sni) ? string.Empty : $" (sni {Sni})");
}
