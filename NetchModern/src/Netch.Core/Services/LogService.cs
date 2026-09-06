using System.Collections.Concurrent;
using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models;

namespace Netch.Core.Services;

/// <summary>
///     Simple in-memory log sink with a bounded ring buffer. Raises <see cref="EntryAdded" /> so the UI
///     can append (marshalling to the dispatcher on its side).
/// </summary>
public sealed class LogService : ILogService
{
    private const int Capacity = 2000;
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Recent => _entries.ToArray();

    public event EventHandler<LogEntry>? EntryAdded;

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
