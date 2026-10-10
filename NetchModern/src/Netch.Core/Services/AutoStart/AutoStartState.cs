namespace Netch.Core.Services.AutoStart;

/// <summary>
///     State of the "launch at login" scheduled task. <see cref="Supported" /> is false when the app
///     is not running from an installed layout (no Game+.exe next to the host), in which case
///     <see cref="Reason" /> explains why.
/// </summary>
public sealed record AutoStartState(bool Enabled, bool Supported, string? Reason);
