using CommunityToolkit.Mvvm.ComponentModel;

namespace Netch.Core.Models.Games;

public enum AccelerationState
{
    Idle,
    Connecting,
    Accelerating,
    Stopping
}

/// <summary>Live acceleration metrics shown on the accelerate screen.</summary>
public sealed partial class AccelerationStatus : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatencyText))]
    private int _latency = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LossText))]
    private double _loss;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JitterText))]
    private int _jitter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private TimeSpan _duration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadText))]
    private long _downloadRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadText))]
    private long _uploadRate;

    public string LatencyText => Latency < 0 ? "—" : $"{Latency} ms";
    public string LossText => $"{Loss:0.#}%";
    public string JitterText => $"{Jitter} ms";
    public string DurationText => Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    public string DownloadText => TrafficStats.FormatBytes(DownloadRate) + "/s";
    public string UploadText => TrafficStats.FormatBytes(UploadRate) + "/s";

    public void Reset()
    {
        Latency = -1;
        Loss = 0;
        Jitter = 0;
        Duration = TimeSpan.Zero;
        DownloadRate = 0;
        UploadRate = 0;
    }
}
