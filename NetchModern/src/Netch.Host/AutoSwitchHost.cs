using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Services.AutoSwitch;

namespace Netch.Host;

/// <summary>
///     One auto-switch event pushed to the UI over the websocket (event name "autoSwitch").
///     <paramref name="FromNode" /> / <paramref name="ToNode" /> are node display names (remark or
///     hostname), not ids.
/// </summary>
public sealed record AutoSwitchEvent(
    string Kind, string GameId, string? FromNode, string? ToNode, string Reason, string? Message);

/// <summary>
///     Owns the automatic node-switching loop: every second it reads a snapshot of the current
///     acceleration quality from the router, feeds it to the <see cref="AutoSwitchPolicy" />, and on a
///     sustained-bad-quality verdict switches to the next-ranked node via the router's accel
///     stop/start paths.
/// </summary>
internal sealed class AutoSwitchHost
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly RpcRouter _router;
    private readonly ILogService _log;
    private readonly AutoSwitchPolicy _policy = new();

    // Nodes already tried in this acceleration session (current node + every switch target), by id.
    private readonly HashSet<string> _tried = new(StringComparer.Ordinal);

    private string? _gameId;
    private volatile bool _switching;
    private bool _exhaustedReported;

    public AutoSwitchHost(RpcRouter router, ILogService? log = null)
    {
        _router = router;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public event Action<AutoSwitchEvent>? Event;

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await TickAsync(DateTime.Now, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _log.Log(LogLevel.Warning, "自动切换检测失败：" + e.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        // Never evaluate while a stop/start switch is in flight — the snapshot would be inconsistent.
        if (_switching)
            return;

        var snapshot = await _router.AutoSwitchSnapshotAsync(ct);
        if (snapshot is null)
        {
            // Not accelerating (or acceleration stopped normally) → fresh session next time.
            ResetSession();
            return;
        }

        if (!string.Equals(snapshot.GameId, _gameId, StringComparison.Ordinal))
        {
            // The game changed → fresh session (our own switch restarts keep the same game id, so they
            // do NOT reset here).
            ResetSession();
            _gameId = snapshot.GameId;
        }

        _tried.Add(snapshot.NodeId);

        // Once exhausted we stop evaluating entirely for this session.
        if (_exhaustedReported)
            return;

        if (!snapshot.Options.Enabled)
            return;

        var decision = _policy.Observe(now, snapshot.Latency, snapshot.Loss, snapshot.Options);
        switch (decision.Action)
        {
            case AutoSwitchAction.None:
                break;

            case AutoSwitchAction.Exhausted:
                ReportExhausted(snapshot, decision.Reason);
                break;

            case AutoSwitchAction.Switch:
                await SwitchAsync(snapshot, decision.Reason, now, ct);
                break;
        }
    }

    private async Task SwitchAsync(AutoSwitchSnapshot snapshot, string reason, DateTime now, CancellationToken ct)
    {
        _switching = true;
        try
        {
            var to = await _router.SwitchToNextNodeAsync(snapshot.GameId, _tried, ct);
            if (to is null)
            {
                // No candidate left → exhausted for this session.
                ReportExhausted(snapshot, reason);
                return;
            }

            _policy.RecordSwitch(now);
            _log.Log(LogLevel.Info, $"线路质量下降（{reason}），自动切换：{snapshot.NodeName} → {to}");
            Raise(new AutoSwitchEvent("switched", snapshot.GameId, snapshot.NodeName, to, reason, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Count the failed attempt against the switch budget so a permanently broken environment
            // can't retry forever.
            _policy.RecordSwitch(now);
            _log.Log(LogLevel.Warning, $"自动切换失败：{e.Message}");
            Raise(new AutoSwitchEvent("failed", snapshot.GameId, snapshot.NodeName, null, reason, e.Message));
        }
        finally
        {
            _switching = false;
        }
    }

    private void ReportExhausted(AutoSwitchSnapshot snapshot, string reason)
    {
        if (_exhaustedReported)
            return;
        _exhaustedReported = true;
        _log.Log(LogLevel.Info, $"线路质量下降（{reason}），自动切换：已无可切换线路");
        Raise(new AutoSwitchEvent("exhausted", snapshot.GameId, snapshot.NodeName, null, reason, null));
    }

    private void ResetSession()
    {
        _policy.Reset();
        _tried.Clear();
        _gameId = null;
        _exhaustedReported = false;
    }

    private void Raise(AutoSwitchEvent evt)
    {
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
