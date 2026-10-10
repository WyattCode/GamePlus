using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.AutoAccel;

/// <summary>An auto-acceleration decision worth surfacing to the UI / log.</summary>
public sealed record AutoAccelEvent(string Kind, string GameId, string? Message);

/// <summary>
///     Policy layer on top of <see cref="GameProcessWatcher" />: decides when to start/stop acceleration
///     in response to launch/exit edges. Never throws from <see cref="TickAsync" />.
/// </summary>
public sealed class AutoAccelerator
{
    private readonly GameProcessWatcher _watcher;
    private readonly Func<string, Task> _startAsync;
    private readonly Func<Task> _stopAsync;
    private readonly Func<AccelerationState> _state;
    private readonly Func<string?> _currentGameId;
    private readonly ILogService? _log;

    /// <summary>The game id we auto-started; null when we didn't start anything.</summary>
    private string? _autoStartedGameId;

    public AutoAccelerator(
        GameProcessWatcher watcher,
        Func<string, Task> startAsync,
        Func<Task> stopAsync,
        Func<AccelerationState> state,
        Func<string?> currentGameId,
        ILogService? log = null)
    {
        _watcher = watcher;
        _startAsync = startAsync;
        _stopAsync = stopAsync;
        _state = state;
        _currentGameId = currentGameId;
        _log = log;
    }

    public event Action<AutoAccelEvent>? Event;

    public async Task TickAsync(IReadOnlyList<Game> watchedGames, bool enabled)
    {
        IReadOnlyList<WatcherEvent> events;
        try
        {
            events = _watcher.Tick(watchedGames);
        }
        catch (Exception e)
        {
            SafeLog(LogLevel.Warning, "自动加速进程检测失败：" + e.Message);
            return;
        }

        if (!enabled)
        {
            // Still advance the watcher so re-enabling doesn't fire for already-running games.
            return;
        }

        foreach (var evt in events)
        {
            try
            {
                if (evt.Kind == WatcherEventKind.Launched)
                    await OnLaunchedAsync(evt.GameId);
                else
                    await OnExitedAsync(evt.GameId);
            }
            catch (Exception e)
            {
                SafeLog(LogLevel.Warning, "自动加速处理失败：" + e.Message);
            }
        }
    }

    private async Task OnLaunchedAsync(string gameId)
    {
        var state = _state();
        if (state != AccelerationState.Idle)
        {
            var current = _currentGameId();
            if (current == gameId)
                return; // already accelerating this very game
            Raise(new AutoAccelEvent("skipped", gameId, "已在加速其他游戏，未自动切换"));
            return;
        }

        try
        {
            await _startAsync(gameId);
            _autoStartedGameId = gameId;
            Raise(new AutoAccelEvent("started", gameId, null));
        }
        catch (Exception e)
        {
            // No retry until the game relaunches (edge-triggered).
            Raise(new AutoAccelEvent("failed", gameId, e.Message));
        }
    }

    private async Task OnExitedAsync(string gameId)
    {
        if (_autoStartedGameId != gameId)
            return; // user-started (or someone else's) acceleration — leave it alone

        _autoStartedGameId = null;

        var state = _state();
        if (state != AccelerationState.Accelerating && state != AccelerationState.Connecting)
            return;

        if (_currentGameId() != gameId)
            return;

        try
        {
            await _stopAsync();
            Raise(new AutoAccelEvent("stopped", gameId, null));
        }
        catch (Exception e)
        {
            Raise(new AutoAccelEvent("failed", gameId, e.Message));
        }
    }

    private void Raise(AutoAccelEvent evt)
    {
        try
        {
            Event?.Invoke(evt);
        }
        catch
        {
            // A misbehaving subscriber must not break the tick loop.
        }
    }

    private void SafeLog(LogLevel level, string message)
    {
        try
        {
            _log?.Log(level, message);
        }
        catch
        {
            // ignore
        }
    }
}
