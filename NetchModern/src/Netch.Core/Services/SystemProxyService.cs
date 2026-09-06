using System.Runtime.InteropServices;
using Microsoft.Win32;
using Netch.Core.Abstractions;

namespace Netch.Core.Services;

/// <summary>
///     Windows system proxy (WinINET) via the per-user Internet Settings registry key + a settings-changed
///     refresh so running apps pick it up immediately. Remembers and restores the previous state.
/// </summary>
public sealed partial class SystemProxyService : ISystemProxyService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string BypassList = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private int _prevEnable;
    private string? _prevOverride;
    private string? _prevServer;

    public bool IsEnabled { get; private set; }

    public void Enable(string host, int port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                        ?? throw new InvalidOperationException("Cannot open Internet Settings registry key.");

        // Remember the previous state to restore on disable — but only on the first Enable. Re-capturing while
        // we're already enabled would snapshot our own proxy values and strand the system proxy on Disable.
        if (!IsEnabled)
        {
            _prevServer = key.GetValue("ProxyServer") as string;
            _prevOverride = key.GetValue("ProxyOverride") as string;
            _prevEnable = key.GetValue("ProxyEnable") as int? ?? 0;
        }

        key.SetValue("ProxyServer", $"{host}:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", BypassList, RegistryValueKind.String);
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);

        // Mark enabled before the refresh so a refresh failure still lets Disable() restore state.
        IsEnabled = true;
        Refresh();
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key == null)
            return;

        // Restore the previous proxy configuration.
        if (_prevServer != null)
            key.SetValue("ProxyServer", _prevServer, RegistryValueKind.String);
        if (_prevOverride != null)
            key.SetValue("ProxyOverride", _prevOverride, RegistryValueKind.String);
        key.SetValue("ProxyEnable", _prevEnable, RegistryValueKind.DWord);

        Refresh();
        IsEnabled = false;
    }

    private static void Refresh()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
