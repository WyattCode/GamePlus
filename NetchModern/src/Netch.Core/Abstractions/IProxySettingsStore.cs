namespace Netch.Core.Abstractions;

/// <summary>
///     Reads/writes the Windows per-user Internet Settings (WinINET) proxy values. Abstracted so the
///     proxy service can be unit-tested on non-Windows CI without touching the registry.
/// </summary>
public interface IProxySettingsStore
{
    /// <summary>Reads the current (ProxyServer, ProxyOverride, ProxyEnable) triple.</summary>
    (string? Server, string? Override, int Enable) Read();

    /// <summary>Writes the proxy triple. <paramref name="server" />/<paramref name="override" /> may be null to clear.</summary>
    void Write(string? server, string? @override, int enable);

    /// <summary>Notifies WinINET that settings changed so running apps pick them up immediately.</summary>
    void Refresh();
}
