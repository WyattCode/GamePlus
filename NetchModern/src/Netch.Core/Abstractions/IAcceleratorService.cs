using System.ComponentModel;
using Netch.Core.Models.Games;
using Netch.Core.Models.Servers;
using Netch.Core.Services;

namespace Netch.Core.Abstractions;

/// <summary>
///     The game accelerator: brings up a node (xray), routes the selected game's traffic through it while
///     everything else stays direct, sets the system proxy, and exposes live latency/loss/jitter metrics.
/// </summary>
public interface IAcceleratorService : INotifyPropertyChanged
{
    AccelerationState State { get; }

    Game? CurrentGame { get; }

    Server? CurrentNode { get; }

    AccelerationStatus Status { get; }

    event EventHandler<AccelerationState>? StateChanged;

    Task StartAsync(Game game, Server node, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    /// <summary>Current acceleration-effect report, or null when no effect monitor is wired up.</summary>
    EffectReport? EffectReport();
}
