namespace Netch.Core.Services.Library;

/// <summary>
///     Scans the local machine for installed games from one source (Steam, Epic, the uninstall registry, …).
///     Implementations must never throw for missing/corrupt files — they skip and continue.
/// </summary>
public interface IGameScanner
{
    /// <summary>Human-readable scanner name (used in error reporting).</summary>
    string Name { get; }

    /// <summary>Enumerates the games this scanner can find. Never throws.</summary>
    IEnumerable<InstalledGame> Scan(CancellationToken ct);
}
