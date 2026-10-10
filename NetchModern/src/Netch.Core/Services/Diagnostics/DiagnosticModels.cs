namespace Netch.Core.Services.Diagnostics;

/// <summary>Outcome of a single diagnostic check.</summary>
public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error,
    Skipped
}

/// <summary>
///     One diagnostic result. <paramref name="FixAction" /> is a machine id for an optional one-click
///     fix (currently only <c>"fixProxy"</c>); null when no automatic fix exists.
/// </summary>
public sealed record DiagnosticItem(
    string Id,
    string Title,
    DiagnosticStatus Status,
    string Detail,
    string? Suggestion,
    string? FixAction);

/// <summary>Inputs for a diagnostics run (all optional — a bare run checks the environment only).</summary>
public sealed record DiagnosticContext(
    Netch.Core.Models.Games.Game? Game,
    Netch.Core.Models.Servers.Server? Node,
    IReadOnlyList<Netch.Core.Models.Servers.Server> Servers,
    bool Accelerating,
    string? LastError);

/// <summary>Result of a diagnostics run.</summary>
public sealed record DiagnosticReport(
    IReadOnlyList<DiagnosticItem> Items,
    string Summary,
    DateTime RanAtUtc,
    string? ErrorExplanation);
