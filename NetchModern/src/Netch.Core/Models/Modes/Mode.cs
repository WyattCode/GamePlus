using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Modes;

/// <summary>
///     A capture-mode profile: a named set of routing rules bound to a <see cref="ModeType" />.
/// </summary>
public partial class Mode : ObservableObject
{
    [ObservableProperty]
    private string _remark = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeName))]
    private ModeType _type = ModeType.ProcessMode;

    public Mode()
    {
        // Keep the "N rules" summary live as the user edits the lists.
        Handle.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RuleCount));
        Bypass.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RuleCount));
    }

    /// <summary>Absolute path of the backing mode file, if persisted.</summary>
    [JsonIgnore]
    public string? FullName { get; set; }

    /// <summary>Rules whose traffic should be proxied (process names / IP CIDRs depending on type).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<string> Handle { get; } = new();

    /// <summary>Rules whose traffic should bypass the proxy.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<string> Bypass { get; } = new();

    [JsonIgnore]
    public int RuleCount => Handle.Count + Bypass.Count;

    [JsonIgnore]
    public string TypeName => Type switch
    {
        ModeType.ProcessMode => "Process",
        ModeType.TunMode => "TUN",
        ModeType.ShareMode => "Share",
        ModeType.WebMode => "Web",
        _ => "Unknown"
    };

    public override string ToString() => $"[{TypeName}] {Remark}";
}
