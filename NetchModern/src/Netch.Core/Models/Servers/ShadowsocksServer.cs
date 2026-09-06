using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

public partial class ShadowsocksServer : Server
{
    [ObservableProperty]
    private string _encryptMethod = "aes-128-gcm";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string? _plugin;

    [ObservableProperty]
    private string? _pluginOption;

    [System.Text.Json.Serialization.JsonIgnore]
    public override string Type => "SS";

    [System.Text.Json.Serialization.JsonIgnore]
    public override string TypeName => "Shadowsocks";

    public override string MaskedData() => $"{Hostname}:{Port} [{EncryptMethod}]";
}
