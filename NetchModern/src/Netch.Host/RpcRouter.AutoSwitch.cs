using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Netch.Core.Services.Import;

namespace Netch.Host;

// Auto-switch: node switching used by AutoSwitchHost (split out of RpcRouter.cs).
internal sealed partial class RpcRouter
{
    /// <summary>
    ///     Switches acceleration to the next-ranked node for <paramref name="gameId" />, excluding the
    ///     given node ids (current node + nodes already tried this session). Ranks exactly like
    ///     <see cref="PickBestNodeAsync" /> but never pings. Returns the candidate's display name, or
    ///     null when no candidate is left.
    /// </summary>
    internal async Task<string?> SwitchToNextNodeAsync(
        string gameId, IReadOnlySet<string> excludeNodeIds, CancellationToken ct)
    {
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
            if (ids.TryGetValue(c.Server, out var cid) && !excludeNodeIds.Contains(cid))
            {
                candidate = c.Server;
                break;
            }
        }

        if (candidate is null)
        {
            foreach (var c in ranking.Candidates)
            {
                if (ids.TryGetValue(c.Server, out var cid) && !excludeNodeIds.Contains(cid))
                {
                    candidate = c.Server;
                    break;
                }
            }
        }

        if (candidate is null)
            return null;

        await StopAccelAsync(ct);
        await StartAccelAsync(gameId, ids[candidate], ct);
        return string.IsNullOrWhiteSpace(candidate.Remark) ? candidate.Hostname : candidate.Remark;
    }
}
