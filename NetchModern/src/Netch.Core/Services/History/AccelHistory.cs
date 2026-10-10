using System.Text.Json;
using System.Text.Json.Serialization;
using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services.History;

/// <summary>
///     One completed acceleration session. Deliberately contains no secrets: only the node's display
///     name (Remark) is stored, never its host/port/password.
/// </summary>
public sealed record AccelSession(
    string Id,
    string GameId,
    string GameName,
    string? NodeName,
    string? Mode,
    string? ServerChoice,
    DateTime StartedAt,
    DateTime? EndedAt,
    int DurationSec,
    int? BaselineMs,
    int? AcceleratedMs,
    int? LatencyPct,
    string? Summary);

/// <summary>Persistent store of acceleration sessions, newest first.</summary>
public interface IAccelHistoryStore
{
    /// <summary>Sessions newest-first, optionally filtered to one game, capped at <paramref name="limit" />.</summary>
    IReadOnlyList<AccelSession> List(string? gameId, int limit);

    void Add(AccelSession s);

    /// <summary>Removes all sessions, or only those of one game when <paramref name="gameId" /> is set.</summary>
    void Clear(string? gameId);
}

/// <summary>
///     JSON-file backed history store. The file is small and never on a network share, so all I/O
///     happens inside the lock; writes are atomic (temp file + overwrite move). A missing or corrupt
///     file is treated as an empty history — loading never throws.
/// </summary>
public sealed class JsonAccelHistoryStore : IAccelHistoryStore
{
    /// <summary>Hard cap on the number of persisted sessions (newest kept).</summary>
    public const int MaxSessions = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly string _path;
    private List<AccelSession> _sessions;

    public JsonAccelHistoryStore(string path)
    {
        _path = path;
        _sessions = Load(path);
    }

    public IReadOnlyList<AccelSession> List(string? gameId, int limit)
    {
        if (limit <= 0)
            return Array.Empty<AccelSession>();

        lock (_lock)
        {
            IEnumerable<AccelSession> query = _sessions;
            if (!string.IsNullOrEmpty(gameId))
                query = query.Where(s => s.GameId == gameId);
            return query.Take(limit).ToList();
        }
    }

    public void Add(AccelSession s)
    {
        lock (_lock)
        {
            _sessions.Insert(0, s);
            if (_sessions.Count > MaxSessions)
                _sessions.RemoveRange(MaxSessions, _sessions.Count - MaxSessions);
            Save();
        }
    }

    public void Clear(string? gameId)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(gameId))
                _sessions.Clear();
            else
                _sessions.RemoveAll(s => s.GameId == gameId);
            Save();
        }
    }

    private static List<AccelSession> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new List<AccelSession>();
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new List<AccelSession>();
            var list = JsonSerializer.Deserialize<List<AccelSession>>(json, JsonOptions);
            if (list is null)
                return new List<AccelSession>();
            // Defensive: keep newest-first ordering and the cap even if the file was hand-edited.
            list.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
            if (list.Count > MaxSessions)
                list.RemoveRange(MaxSessions, list.Count - MaxSessions);
            return list;
        }
        catch
        {
            // Corrupt / unreadable file → start empty rather than breaking acceleration.
            return new List<AccelSession>();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_sessions, JsonOptions);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, true);
        }
        catch
        {
            // History persistence must never break acceleration.
        }
    }
}

/// <summary>
///     Watches an <see cref="IAcceleratorService" /> and records one <see cref="AccelSession" /> per
///     completed acceleration. Sessions shorter than <see cref="MinDurationSec" /> and failed starts
///     (Connecting → Idle) are not recorded. Any exception in the handler is logged (when a log service
///     is supplied) and swallowed — history must never break acceleration.
/// </summary>
public sealed class AccelHistoryRecorder : IDisposable
{
    /// <summary>Sessions shorter than this are not recorded.</summary>
    public const int MinDurationSec = 5;

    private readonly IAcceleratorService _accel;
    private readonly IAccelHistoryStore _store;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, string?> _serverChoiceFor;
    private readonly ILogService? _log;
    private readonly object _lock = new();

    private PendingSession? _pending;
    private bool _disposed;

    public AccelHistoryRecorder(
        IAcceleratorService accel,
        IAccelHistoryStore store,
        Func<DateTime> clock,
        Func<string, string?> serverChoiceFor,
        ILogService? log = null)
    {
        _accel = accel;
        _store = store;
        _clock = clock;
        _serverChoiceFor = serverChoiceFor;
        _log = log;
        _accel.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, AccelerationState state)
    {
        try
        {
            Handle(state);
        }
        catch (Exception e)
        {
            // History must never break acceleration — log and swallow.
            LogWarning("accel history: failed to record session: " + e.Message);
        }
    }

    private void Handle(AccelerationState state)
    {
        lock (_lock)
        {
            if (state == AccelerationState.Accelerating)
            {
                var game = _accel.CurrentGame;
                if (game is null)
                    return;
                _pending = new PendingSession(
                    game.Id,
                    game.Name,
                    _accel.CurrentNode?.DisplayName,
                    _accel.Status.CaptureMode,
                    SafeServerChoice(game.Id),
                    _clock());
                return;
            }

            // First transition away from Accelerating (Stopping or Idle) finishes the session.
            if (_pending is null)
                return;

            var pending = _pending;
            _pending = null;

            var endedAt = _clock();
            var durationSec = (int)Math.Round((endedAt - pending.StartedAt).TotalSeconds);
            if (durationSec < MinDurationSec)
                return;

            int? baselineMs = null;
            int? acceleratedMs = null;
            int? latencyPct = null;
            string? summary = null;

            var report = _accel.EffectReport();
            if (report is not null)
            {
                baselineMs = report.Baseline?.Latency;
                var current = report.Current?.Latency;
                if (current is > 0)
                    acceleratedMs = current;
                latencyPct = report.LatencyPct;
                summary = report.Summary;
            }

            _store.Add(new AccelSession(
                Guid.NewGuid().ToString("N"),
                pending.GameId,
                pending.GameName,
                pending.NodeName,
                pending.Mode,
                pending.ServerChoice,
                pending.StartedAt,
                endedAt,
                durationSec,
                baselineMs,
                acceleratedMs,
                latencyPct,
                summary));
        }
    }

    private string? SafeServerChoice(string gameId)
    {
        try
        {
            return _serverChoiceFor(gameId);
        }
        catch (Exception e)
        {
            LogWarning("accel history: region lookup failed: " + e.Message);
            return null;
        }
    }

    private void LogWarning(string message)
    {
        try
        {
            _log?.Log(LogLevel.Warning, message);
        }
        catch
        {
            // Logging must never break acceleration either.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _accel.StateChanged -= OnStateChanged;
    }

    private sealed record PendingSession(
        string GameId,
        string GameName,
        string? NodeName,
        string? Mode,
        string? ServerChoice,
        DateTime StartedAt);
}
