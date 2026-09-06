using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Servers;

/// <summary>
///     Base type for every proxy server. Observable so the UI reflects live edits and ping results.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ShadowsocksServer), "shadowsocks")]
[JsonDerivedType(typeof(TrojanServer), "trojan")]
[JsonDerivedType(typeof(VMessServer), "vmess")]
[JsonDerivedType(typeof(VlessServer), "vless")]
[JsonDerivedType(typeof(WireGuardServer), "wireguard")]
[JsonDerivedType(typeof(Socks5Server), "socks5")]
[JsonDerivedType(typeof(Hysteria2Server), "hysteria2")]
public abstract partial class Server : ObservableObject, ICloneable
{
    /// <summary>Latency in ms. Negative values encode failure states (-1 untested, -2 DNS fail, -4 timeout).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayText))]
    [property: JsonIgnore]
    private int _delay = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInGroup))]
    private string _group = "NONE";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _hostname = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private ushort _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _remark = string.Empty;

    /// <summary>Short protocol tag, e.g. "SS", "Trojan". Used for the type chip in the UI.</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>Full protocol name shown in editors.</summary>
    [JsonIgnore]
    public abstract string TypeName { get; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Remark) ? $"{Hostname}:{Port}" : Remark;

    [JsonIgnore]
    public bool IsInGroup => Group is not "NONE";

    /// <summary>Human-readable latency for the UI.</summary>
    [JsonIgnore]
    public string DelayText => Delay switch
    {
        -1 => "—",
        -2 => "DNS",
        < 0 => "timeout",
        _ => $"{Delay} ms"
    };

    public object Clone() => MemberwiseClone();

    /// <summary>Returns a copy of the server with secrets masked, for logging.</summary>
    public abstract string MaskedData();

    public override string ToString() => $"[{Type}][{Group}] {DisplayName}";
}
