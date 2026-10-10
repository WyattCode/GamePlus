using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models.Games;
using Netch.Core.Services.AutoAccel;

namespace Netch.Host;

/// <summary>
///     Owns the auto-acceleration loop: every few seconds it asks the router which games to watch and
///     drives an <see cref="AutoAccelerator" /> wired to the router's accel start/stop paths.
/// </summary>
internal sealed class AutoAccelHost
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly RpcRouter _router;
    private readonly ILogService _log;
    private readonly AutoAccelerator _accelerator;

    public AutoAccelHost(RpcRouter router, IProcessSource? source = null, ILogService? log = null)
    {
        _router = router;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _accelerator = new AutoAccelerator(
            new GameProcessWatcher(source ?? new SystemProcessSource()),
            gameId => _router.StartAccelForAutoAsync(gameId, CancellationToken.None),
            () => _router.StopAccelForAutoAsync(CancellationToken.None),
            () => _router.CurrentAccelState,
            () => _router.CurrentAccelGameId,
            _log);
        _accelerator.Event += OnAutoAccelEvent;
    }

    public event Action<AutoAccelEvent>? Event;

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    var (games, enabled) = await _router.AutoWatchedGamesAsync(ct);
                    await _accelerator.TickAsync(games, enabled);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _log.Log(LogLevel.Warning, "自动加速检测失败：" + e.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void OnAutoAccelEvent(AutoAccelEvent evt)
    {
        var name = _router.GameNameOf(evt.GameId) ?? evt.GameId;
        switch (evt.Kind)
        {
            case "started":
                _log.Log(LogLevel.Info, $"检测到 {name} 启动，已自动开始加速");
                break;
            case "stopped":
                _log.Log(LogLevel.Info, $"检测到 {name} 退出，已自动停止加速");
                break;
            case "failed":
                _log.Log(LogLevel.Warning, $"自动加速 {name} 失败：{evt.Message}");
                break;
        }

        try
        {
            Event?.Invoke(evt);
        }
        catch
        {
            // ignore subscriber failures
        }
    }
}
