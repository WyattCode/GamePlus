using Microsoft.Win32;
using Netch.Core.Abstractions;

namespace Netch.Core.Services;

/// <summary>
///     Production <see cref="IProxySettingsStore" /> backed by the per-user Internet Settings registry key
///     plus a WinINET settings-changed refresh.
/// </summary>
public sealed class WindowsProxySettingsStore : IProxySettingsStore
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public (string? Server, string? Override, int Enable) Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        if (key == null)
            return (null, null, 0);

        var server = key.GetValue("ProxyServer") as string;
        var @override = key.GetValue("ProxyOverride") as string;
        var enable = key.GetValue("ProxyEnable") as int? ?? 0;
        return (server, @override, enable);
    }

    public void Write(string? server, string? @override, int enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                        ?? throw new InvalidOperationException("Cannot open Internet Settings registry key.");

        if (server != null)
            key.SetValue("ProxyServer", server, RegistryValueKind.String);
        else
            key.DeleteValue("ProxyServer", throwOnMissingValue: false);

        if (@override != null)
            key.SetValue("ProxyOverride", @override, RegistryValueKind.String);
        else
            key.DeleteValue("ProxyOverride", throwOnMissingValue: false);

        key.SetValue("ProxyEnable", enable, RegistryValueKind.DWord);
    }

    public void Refresh() => WinInet.Refresh();
}
