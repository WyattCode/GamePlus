namespace Netch.Core.Models.Games;

/// <summary>
///     Per-game compatibility notes (Game+). <see cref="RecommendedMode" /> is an optional hint —
///     "auto", "process", "tun" or "proxy"; null means no recommendation. <see cref="Note" /> is a
///     user-facing Chinese note; <see cref="Verified" /> is false until confirmed on real hardware.
/// </summary>
public sealed class GameCompat
{
    public string? RecommendedMode { get; set; }

    public string Note { get; set; } = "";

    public bool Verified { get; set; }
}
