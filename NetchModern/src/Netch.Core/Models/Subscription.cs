using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models;

public partial class Subscription : ObservableObject
{
    /// <summary>
    ///     Stable, persisted identity. Assigned once when the subscription is created (or migrated on load)
    ///     and never changes, so servers imported by this subscription can be matched back to it even when
    ///     the remark is blank or duplicated.
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

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
    ///     Derived from the immutable <see cref="Id" /> — never from the (mutable, possibly duplicated) remark.
    /// </summary>
    [JsonIgnore]
    public string GroupKey => "sub:" + Id;

    /// <summary>Assigns an id if the subscription does not have one yet. Returns the (possibly new) id.</summary>
    public string EnsureId()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N")[..12];
        return Id;
    }

    /// <summary>
    ///     Legacy group key used by builds before <see cref="Id" /> existed: the remark when set, otherwise
    ///     the link. Only used by the one-time migration in <c>JsonPersistenceService</c>.
    /// </summary>
    [JsonIgnore]
    public string LegacyGroupKey => string.IsNullOrWhiteSpace(Remark) ? Link : Remark;

    /// <summary>
    ///     Readable default remark derived from the link host, e.g. "未命名订阅 (example.com)".
    ///     Uniqueness is guaranteed by <see cref="Id" />, not by the remark.
    /// </summary>
    public static string DefaultRemarkFor(string link)
    {
        var host = TryGetHost(link);
        return string.IsNullOrWhiteSpace(host) ? "未命名订阅" : $"未命名订阅 ({host})";
    }

    private static string? TryGetHost(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        if (Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;
        return null;
    }
}
