using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models;

/// <summary>
///     Live traffic counters and rates surfaced on the dashboard.
/// </summary>
public partial class TrafficStats : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadRateText))]
    private long _uploadRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadRateText))]
    private long _downloadRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalUploadText))]
    private long _totalUpload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDownloadText))]
    private long _totalDownload;

    public string UploadRateText => $"{FormatBytes(UploadRate)}/s";
    public string DownloadRateText => $"{FormatBytes(DownloadRate)}/s";
    public string TotalUploadText => FormatBytes(TotalUpload);
    public string TotalDownloadText => FormatBytes(TotalDownload);

    public void Reset()
    {
        UploadRate = DownloadRate = TotalUpload = TotalDownload = 0;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
