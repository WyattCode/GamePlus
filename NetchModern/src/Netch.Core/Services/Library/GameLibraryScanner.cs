using Netch.Core.Models.Games;

namespace Netch.Core.Services.Library;

/// <summary>Result of a full library scan.</summary>
public record ScanResult(
    IReadOnlyList<DetectedGame> Games,
    IReadOnlyList<string> Errors,
    DateTime ScannedAtUtc);

/// <summary>
///     Runs every registered <see cref="IGameScanner" /> (each isolated so one failure can't abort the
///     scan) and matches the results against the rule library.
/// </summary>
public sealed class GameLibraryScanner
{
    private readonly IReadOnlyList<IGameScanner> _scanners;

    public GameLibraryScanner(IEnumerable<IGameScanner> scanners) =>
        _scanners = scanners.ToList();

    public ScanResult ScanAll(IReadOnlyList<Game> library, CancellationToken ct)
    {
        var installed = new List<InstalledGame>();
        var errors = new List<string>();

        foreach (var scanner in _scanners)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var game in scanner.Scan(ct))
                    installed.Add(game);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{scanner.Name}: {ex.Message}");
            }
        }

        var matched = GameMatcher.Match(installed, library);
        return new ScanResult(matched, errors, DateTime.UtcNow);
    }
}
