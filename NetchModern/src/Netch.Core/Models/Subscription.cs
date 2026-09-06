using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models;

public partial class Subscription : ObservableObject
{
    [ObservableProperty]
    private string _remark = string.Empty;

    [ObservableProperty]
    private string _link = string.Empty;

    [ObservableProperty]
    private int _serverCount;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    [JsonIgnore]
    public string LastUpdatedText => LastUpdated is null ? "never" : LastUpdated.Value.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    ///     The tag stamped on every server this subscription imports. Servers are matched back to their
    ///     subscription by this key when refreshing or deleting, so it must be computed identically everywhere.
    /// </summary>
    [JsonIgnore]
    public string GroupKey => string.IsNullOrWhiteSpace(Remark) ? Link : Remark;
}
