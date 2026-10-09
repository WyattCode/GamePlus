using Netch.Core.Abstractions;
using Netch.Core.Services;

namespace Netch.Tests;

public class SystemProxyServiceTests : IDisposable
{
    private readonly string _dir;

    public SystemProxyServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gameplus-proxy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignored */ }
    }

    private string BackupPath => Path.Combine(_dir, "proxy-backup.json");

    /// <summary>In-memory stand-in for the Windows registry store.</summary>
    private sealed class FakeStore : IProxySettingsStore
    {
        public string? Server { get; set; }
        public string? Override { get; set; }
        public int Enable { get; set; }
        public int RefreshCount { get; private set; }

        public (string? Server, string? Override, int Enable) Read() => (Server, Override, Enable);

        public void Write(string? server, string? @override, int enable)
        {
            Server = server;
            Override = @override;
            Enable = enable;
        }

        public void Refresh() => RefreshCount++;
    }

    [Fact]
    public void Enable_WritesBackup_DisableRestoresOriginalsAndDeletesBackup()
    {
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var svc = new SystemProxyService(store, BackupPath);

        svc.Enable("127.0.0.1", 12345);

        Assert.True(svc.IsEnabled);
        Assert.Equal("127.0.0.1:12345", store.Server);
        Assert.Equal(1, store.Enable);
        Assert.True(File.Exists(BackupPath));

        svc.Disable();

        Assert.False(svc.IsEnabled);
        Assert.Equal("corp-proxy:8080", store.Server);
        Assert.Equal("corp.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void RecoverFromCrash_RestoresOriginals_AndDeletesBackup()
    {
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var a = new SystemProxyService(store, BackupPath);
        a.Enable("127.0.0.1", 12345);

        // Simulate a crash: no Disable() call, registry still points at our dead port.
        Assert.Equal("127.0.0.1:12345", store.Server);

        var b = new SystemProxyService(store, BackupPath);
        b.RecoverFromCrash();

        Assert.Equal("corp-proxy:8080", store.Server);
        Assert.Equal("corp.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void RecoverFromCrash_UserChangedProxy_LeavesUserValue_AndDeletesBackup()
    {
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var a = new SystemProxyService(store, BackupPath);
        a.Enable("127.0.0.1", 12345);

        // User (or another tool) changed the proxy after our crash.
        store.Server = "user-proxy:9999";
        store.Override = "user.local";
        store.Enable = 1;

        var b = new SystemProxyService(store, BackupPath);
        b.RecoverFromCrash();

        Assert.Equal("user-proxy:9999", store.Server);
        Assert.Equal("user.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void StaleBackup_EnableThenDisable_RestoresBackupOriginals_NotStaleOurs()
    {
        // Simulate a crash that RecoverFromCrash couldn't handle: backup exists, registry still holds
        // our dead 127.0.0.1:<port>.
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var a = new SystemProxyService(store, BackupPath);
        a.Enable("127.0.0.1", 12345);
        // (no Disable — crash)

        // Fresh instance, still sees the stale backup + our stale registry value.
        var b = new SystemProxyService(store, BackupPath);
        b.Enable("127.0.0.1", 54321);
        b.Disable();

        // Must restore the true originals from the backup, not the stale 127.0.0.1:12345.
        Assert.Equal("corp-proxy:8080", store.Server);
        Assert.Equal("corp.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void EnableTwice_ThenDisable_RestoresTrueOriginals()
    {
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var svc = new SystemProxyService(store, BackupPath);

        svc.Enable("127.0.0.1", 12345);
        svc.Enable("127.0.0.1", 12345);
        svc.Disable();

        Assert.Equal("corp-proxy:8080", store.Server);
        Assert.Equal("corp.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void Disable_WithoutEnable_LeavesStoreUntouched()
    {
        var store = new FakeStore { Server = "corp-proxy:8080", Override = "corp.local", Enable = 1 };
        var svc = new SystemProxyService(store, BackupPath);

        svc.Disable();

        // A stray Disable on a fresh instance must not clobber the user's proxy settings.
        Assert.False(svc.IsEnabled);
        Assert.Equal("corp-proxy:8080", store.Server);
        Assert.Equal("corp.local", store.Override);
        Assert.Equal(1, store.Enable);
        Assert.Equal(0, store.RefreshCount);
        Assert.False(File.Exists(BackupPath));
    }
}
