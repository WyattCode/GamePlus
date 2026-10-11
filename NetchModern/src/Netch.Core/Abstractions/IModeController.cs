using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Abstractions;

/// <summary>
///     Backend that captures traffic and forwards it to a local SOCKS5 endpoint
///     (Process / TUN / Share). Real implementations wrap the native drivers.
/// </summary>
public interface IModeController
{
    string Name { get; }

    ModeFeature Features { get; }

    Task StartAsync(Socks5Server socks5, Mode mode, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    /// <summary>Cumulative bytes sent through the tunnel. Default 0 for controllers without counters.</summary>
    long GetUploadBytes() => 0;

    /// <summary>Cumulative bytes received through the tunnel.</summary>
    long GetDownloadBytes() => 0;
}

public interface IModeControllerFactory
{
    IModeController Create(Netch.Core.Models.Modes.ModeType type);

    /// <summary>
    ///     Whether the given mode can actually be started in this build. Defaults to true;
    ///     controllers depending on components that are not shipped (e.g. NetFilter SDK files
    ///     for process mode) return false so callers can fail gracefully.
    /// </summary>
    bool IsAvailable(ModeType type) => true;
}
