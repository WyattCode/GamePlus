using Netch.Core.Models.Settings;

namespace Netch.Core.Services;

/// <summary>
///     Persistence seam for per-game direct baselines. Implemented by the host over
///     <c>ISettingsService</c>; tests inject a fake.
/// </summary>
public interface IBaselineStore
{
    /// <summary>Returns the stored baseline for a game, or null when it was never measured.</summary>
    StoredBaseline? Get(string gameId);

    /// <summary>Stores (or replaces) the baseline for a game.</summary>
    void Save(string gameId, StoredBaseline baseline);

    /// <summary>
    ///     Returns true and clears the one-shot "re-measure next time" flag when it is set for the game.
    /// </summary>
    bool ConsumeRemeasure(string gameId);

    /// <summary>
    ///     Sets the one-shot "re-measure next time" flag for a game (idempotent).
    /// </summary>
    void MarkRemeasure(string gameId);
}
