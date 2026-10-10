namespace Netch.Core.Services;

/// <summary>
///     Version of the bundled (compiled-in) game rule library. Bump this whenever
///     <see cref="SampleGames.Create" /> changes so a cached/remote rule file with a lower version is
///     superseded by the new bundled copy.
/// </summary>
public static partial class SampleGames
{
    public const int RulesVersion = 4;
}
