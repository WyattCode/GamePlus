using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class Socks5Server : Server
{
    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _username;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "S5";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "Socks5";

    public bool Auth() => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

    public override string MaskedData() => $"{Hostname}:{Port}" + (Auth() ? " (auth)" : string.Empty);
}
