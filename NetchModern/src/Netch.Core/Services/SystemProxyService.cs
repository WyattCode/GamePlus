using System.Text.Json;
using Netch.Core.Abstractions;

namespace Netch.Core.Services;

/// <summary>
///     Windows system proxy (WinINET) via the per-user Internet Settings registry key + a settings-changed
///     refresh so running apps pick it up immediately. Remembers and restores the previous state.
///     The original state is also persisted to a small JSON backup so a crash / force-kill can be
///     recovered on the next start (see <see cref="RecoverFromCrash" />).
/// </summary>
public sealed class SystemProxyService : ISystemProxyService
{
    private const string BypassList = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

    private readonly IProxySettingsStore _store;
    private readonly string _backupPath;

    private int _prevEnable;
    private string? _prevOverride;
    private string? _prevServer;

    /// <summary>Production constructor: Windows registry store + <c>%AppData%\Game+\proxy-backup.json</c>.</summary>
    public SystemProxyService()
        : this(new WindowsProxySettingsStore(), Path.Combine(AppPaths.DataDirectory, "proxy-backup.json"))
    {
    }

    /// <summary>Test/DI constructor.</summary>
    public SystemProxyService(IProxySettingsStore store, string backupPath)
    {
        _store = store;
        _backupPath = backupPath;
    }

    public bool IsEnabled { get; private set; }

    public void Enable(string host, int port)
    {
        var ours = $"{host}:{port}";

        if (!IsEnabled)
        {
            // Prefer a stale backup's originals over the current registry values: after a crash the
            // registry may still hold *our* dead 127.0.0.1:<port>, and snapshotting that would strand
            // the user's proxy on a dead port forever.
            var backup = TryReadBackup();
            if (backup != null)
            {
                _prevServer = backup.OriginalServer;
                _prevOverride = backup.OriginalOverride;
                _prevEnable = backup.OriginalEnable;
            }
            else
            {
                var (server, @override, enable) = _store.Read();
                _prevServer = server;
                _prevOverride = @override;
                _prevEnable = enable;
            }
        }

        // Persist the originals + what we're about to set, atomically, before touching the registry.
        WriteBackup(new ProxyBackup
        {
            OriginalServer = _prevServer,
            OriginalOverride = _prevOverride,
            OriginalEnable = _prevEnable,
            Ours = ours
        });

        _store.Write(ours, BypassList, 1);

        // Mark enabled before the refresh so a refresh failure still lets Disable() restore state.
        IsEnabled = true;
        _store.Refresh();
    }

    public void Disable()
    {
        // No-op when we never enabled: writing our (null, null, 0) defaults would delete the user's
        // ProxyServer/ProxyOverride values.
        if (!IsEnabled)
            return;

        // Restore the previous proxy configuration. If the write fails, keep the backup file so the next
        // start can still recover the user's original settings.
        _store.Write(_prevServer, _prevOverride, _prevEnable);
        _store.Refresh();
        IsEnabled = false;

        TryDeleteBackup();
    }

    public void RecoverFromCrash()
    {
        var backup = TryReadBackup();
        if (backup == null)
            return;

        try
        {
            var (server, _, _) = _store.Read();

            // Only restore if the registry still holds the value we set — otherwise the user changed
            // their proxy since the crash and we must leave it alone.
            if (string.Equals(server, backup.Ours, StringComparison.OrdinalIgnoreCase))
            {
                _store.Write(backup.OriginalServer, backup.OriginalOverride, backup.OriginalEnable);
                _store.Refresh();
            }
        }
        finally
        {
            TryDeleteBackup();
        }
    }

    private ProxyBackup? TryReadBackup()
    {
        try
        {
            if (!File.Exists(_backupPath))
                return null;

            var json = File.ReadAllText(_backupPath);
            return JsonSerializer.Deserialize<ProxyBackup>(json);
        }
        catch
        {
            return null;
        }
    }

    private void WriteBackup(ProxyBackup backup)
    {
        try
        {
            var dir = Path.GetDirectoryName(_backupPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = _backupPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(backup));
            File.Move(tmp, _backupPath, overwrite: true);
        }
        catch
        {
            // Best-effort: a failed backup write must not break acceleration.
        }
    }

    private void TryDeleteBackup()
    {
        try
        {
            if (File.Exists(_backupPath))
                File.Delete(_backupPath);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class ProxyBackup
    {
        public string? OriginalServer { get; set; }
        public string? OriginalOverride { get; set; }
        public int OriginalEnable { get; set; }
        public string? Ours { get; set; }
    }
}
