namespace Netch.Core.Abstractions;

/// <summary>
///     Sets/clears the Windows system HTTP proxy (WinINET), so every app that honours the system proxy
///     routes through our local endpoint.
/// </summary>
public interface ISystemProxyService
{
    bool IsEnabled { get; }

    void Enable(string host, int port);

    void Disable();

    /// <summary>
    ///     Restores the user's proxy settings from a durable backup left behind by a previous run that
    ///     crashed while accelerating. Safe to call at startup; a no-op when no backup exists.
    /// </summary>
    void RecoverFromCrash();
}
