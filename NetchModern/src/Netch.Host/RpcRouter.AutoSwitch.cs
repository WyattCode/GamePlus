using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Netch.Core.Services.Import;

namespace Netch.Host;

// Auto-switch: node switching used by AutoSwitchHost (split out of RpcRouter.cs).
internal sealed partial class RpcRouter
{
    /// <summary>
    ///     Switches acceleration to the next-ranked node for <paramref name="gameId" />, excluding the
    ///     node ids already present in <paramref name="tried" /> (current node + nodes already tried
    ///     this session). Ranks exactly like <see cref="PickBestNodeAsync" /> but never pings. The
    ///     chosen candidate's id is added to <paramref name="tried" /> BEFORE the switch is attempted,
    ///     so a failed node is not picked again. Returns the candidate's display name, or null when no
    ///     candidate is left. Throws <see cref="AutoSwitchAbortedException" /> when the user changed
    ///     the acceleration state meanwhile (the switch is silently abandoned).
    /// </summary>
    internal async Task<string?> SwitchToNextNodeAsync(string gameId, ISet<string> tried, CancellationToken ct)
    {
        // Snapshot the user's stop generation up front: accel.stop bumps it BEFORE waiting for
        // _accelLock, so re-checking it under the lock aborts a stale switch.
        var gen = Volatile.Read(ref _stopGeneration);

        var game = await WithStateValue(
            () => Games.Games.FirstOrDefault(x => x.Id == gameId)
                  ?? throw new InvalidOperationException("游戏不存在"),
            ct);

        // Snapshot the pool + its stable ids under the lock (no pinging — reuse the last measurements).
        var (pool, ids) = await WithStateValue(() =>
        {
            var list = Servers.Servers.ToList();
            return (list, ServerIds.Assign(list));
        }, ct);
        if (pool.Count == 0)
            return null;

        // Same penalty/choice reads as PickBestNodeAsync — no network I/O inside this block.
        var (penalty, choice) = await WithStateValue(() =>
        {
            var p = SettingsSvc.Settings.Advanced.RegionPenaltyMs;
            var c = SettingsSvc.Settings.GameServerChoice.GetValueOrDefault(game.Id);
            return (p, c);
        }, ct);

        var ranking = NodeRanker.Rank(game, pool, penalty, choice);

        // The ranking orders tested (Delay >= 0) nodes first, so the first pass over it already prefers
        // tested nodes; the second pass only falls back when nothing tested is left at all.
        Server? candidate = null;
        foreach (var c in ranking.Candidates)
        {
            if (c.Delay < 0)
                continue;
            if (ids.TryGetValue(c.Server, out var cid) && !tried.Contains(cid))
            {
                candidate = c.Server;
                break;
            }
        }

        if (candidate is null)
        {
            foreach (var c in ranking.Candidates)
            {
                if (ids.TryGetValue(c.Server, out var cid) && !tried.Contains(cid))
                {
                    candidate = c.Server;
                    break;
                }
            }
        }

        if (candidate is null)
            return null;

        var candidateId = ids[candidate];
        var name = string.IsNullOrWhiteSpace(candidate.Remark) ? candidate.Hostname : candidate.Remark;

        // Mark the candidate as tried before attempting the switch so a broken node is not picked again.
        tried.Add(candidateId);

        // ONE critical section under _accelLock for the whole stop+start: a user accel.stop bumps
        // _stopGeneration and then waits for this lock, so the generation re-check below aborts the
        // switch instead of restarting acceleration against the user's will. Taking _state (inside
        // WithStateValue) while holding _accelLock is fine — StartAccelAsync never does the reverse.
        await _accelLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _stopGeneration) != gen)
                throw new AutoSwitchAbortedException();

            var (state, currentGame, previous) = await WithStateValue(
                () => (Accel.State, Accel.CurrentGame, Accel.CurrentNode), ct);
            if (state != AccelerationState.Accelerating || currentGame?.Id != gameId)
                throw new AutoSwitchAbortedException();

            // Register a linked CTS exactly like StartAccelAsync does, so a user stop can cancel the
            // switch's stop/start pair.
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _startCts = startCts;
            try
            {
                await Accel.StopAsync(startCts.Token);
                try
                {
                    await Accel.StartAsync(game, candidate, startCts.Token);
                }
                catch (Exception e) when (Volatile.Read(ref _stopGeneration) == gen && !startCts.Token.IsCancellationRequested)
                {
                    // 启动失败且并非用户停止：尽力恢复原线路，然后把原始异常包装后抛出。
                    var restored = false;
                    if (previous is not null)
                    {
                        try
                        {
                            using var restoreCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            await Accel.StartAsync(game, previous, restoreCts.Token);
                            restored = true;
                        }
                        catch
                        {
                            // 恢复失败：忽略，按未恢复处理。
                        }
                    }

                    throw new InvalidOperationException(
                        $"切换到 {name} 失败：{e.Message}" + (restored ? "，已恢复原线路" : ""), e);
                }
                catch (OperationCanceledException) when (Volatile.Read(ref _stopGeneration) != gen)
                {
                    // 用户在切换期间停止了加速：按中止处理，不恢复。
                    throw new AutoSwitchAbortedException();
                }
            }
            finally
            {
                if (ReferenceEquals(_startCts, startCts))
                    _startCts = null;
            }

            return name;
        }
        finally
        {
            _accelLock.Release();
        }
    }
}

/// <summary>
///     Thrown by <see cref="RpcRouter.SwitchToNextNodeAsync" /> when the user changed the
///     acceleration state (stopped, or started another game) while a switch was in flight.
///     The auto-switch loop treats it as a silent no-op.
/// </summary>
internal sealed class AutoSwitchAbortedException : Exception { }
