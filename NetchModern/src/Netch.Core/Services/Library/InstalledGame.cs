namespace Netch.Core.Services.Library;

/// <summary>
///     A game discovered on this machine by one of the <see cref="IGameScanner" /> implementations.
/// </summary>
/// <param name="Source">Launcher / source name, e.g. "Steam", "Epic", "Battle.net", "EA", "Ubisoft", "Riot", "其他".</param>
/// <param name="SourceId">Stable id within the source, e.g. "steam:730", "epic:Fortnite", "uninstall:{key}".</param>
/// <param name="Name">Display name as reported by the launcher / uninstall entry.</param>
/// <param name="InstallDir">Install directory, if known.</param>
/// <param name="Executables">Executable file names only (no paths), e.g. "cs2.exe".</param>
public record InstalledGame(
    string Source,
    string SourceId,
    string Name,
    string? InstallDir,
    IReadOnlyList<string> Executables);
