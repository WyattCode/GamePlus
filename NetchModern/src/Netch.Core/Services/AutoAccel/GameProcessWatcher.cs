namespace Netch.Core.Services.AutoAccel;

public enum WatcherEventKind
{
    Launched,
    Exited
}

/// <summary>A launch/exit edge detected for a watched game.</summary>
public sealed record WatcherEvent(WatcherEventKind Kind, string GameId);

/// <summary>
///     Pure, timer-free decision core: compares the running-process set between ticks and reports
///     launch/exit edges for watched games. Games already running on the very first tick are recorded
///     as running but never reported as launched.
/// </summary>
public sealed class GameProcessWatcher
{
    private readonly IProcessSource _source;

    /// <summary>Game id → lower-cased process names, for games that have at least one process rule.</summary>
    private Dictionary<string, HashSet<string>> _watched = new(StringComparer.Ordinal);

    /// <summary>Game ids whose processes were running at the previous tick.</summary>
    private HashSet<string> _running = new(StringComparer.Ordinal);

    private bool _firstTick = true;

    public GameProcessWatcher(IProcessSource source) => _source = source;

    public IReadOnlyList<WatcherEvent> Tick(IReadOnlyList<Models.Games.Game> watchedGames)
    {
        var events = new List<WatcherEvent>();

        var watched = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var game in watchedGames)
        {
            if (game.ProxyProcesses.Count == 0)
                continue;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in game.ProxyProcesses)
            {
                var name = Normalize(raw);
                if (name.Length > 0)
                    names.Add(name);
            }

            if (names.Count > 0)
                watched[game.Id] = names;
        }

        IReadOnlySet<string> running;
        try
        {
            running = _source.RunningProcessNames();
        }
        catch
        {
            running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var nowRunning = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (gameId, names) in watched)
        {
            foreach (var name in names)
            {
                if (running.Contains(name))
                {
                    nowRunning.Add(gameId);
                    break;
                }
            }
        }

        if (_firstTick)
        {
            // Games already running when Game+ starts must not trigger auto-acceleration.
            _firstTick = false;
        }
        else
        {
            foreach (var gameId in nowRunning)
            {
                if (!_running.Contains(gameId))
                    events.Add(new WatcherEvent(WatcherEventKind.Launched, gameId));
            }

            foreach (var gameId in _running)
            {
                if (!nowRunning.Contains(gameId))
                    events.Add(new WatcherEvent(WatcherEventKind.Exited, gameId));
            }
        }

        _watched = watched;
        _running = nowRunning;
        return events;
    }

    private static string Normalize(string raw)
    {
        var name = (raw ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0)
            return "";
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }
}
