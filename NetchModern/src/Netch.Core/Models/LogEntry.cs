using Netch.Core.Enums;

namespace Netch.Core.Models;

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    public string LevelText => Level.ToString().ToUpperInvariant();
}
