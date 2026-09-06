using System.Collections.ObjectModel;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Persists servers, modes and subscriptions as JSON under %AppData%\Game+\.
///     On first run (no files) the seeded repository contents are written out as-is.
/// </summary>
public sealed class JsonPersistenceService : IPersistenceService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _dir;
    private readonly IModeRepository _modes;
    private readonly IServerRepository _servers;
    private readonly ISubscriptionRepository _subscriptions;

    public JsonPersistenceService(IServerRepository servers, IModeRepository modes, ISubscriptionRepository subscriptions)
    {
        _servers = servers;
        _modes = modes;
        _subscriptions = subscriptions;

        _dir = AppPaths.DataDirectory;
    }

    private string ServersPath => Path.Combine(_dir, "servers.json");
    private string ModesPath => Path.Combine(_dir, "modes.json");
    private string SubscriptionsPath => Path.Combine(_dir, "subscriptions.json");

    public async Task LoadAsync()
    {
        var loadedAny = false;
        loadedAny |= await LoadInto(ServersPath, _servers.Servers);
        loadedAny |= await LoadInto(ModesPath, _modes.Modes);
        loadedAny |= await LoadInto(SubscriptionsPath, _subscriptions.Subscriptions);

        ReconcileSubscriptionServers();

        // First run: no files yet — persist the seeded data so it exists next time.
        if (!loadedAny)
            await SaveAsync();
    }

    /// <summary>
    ///     Self-heal orphans: drop any server still carrying a subscription group tag whose subscription no
    ///     longer exists (e.g. deleted by an older build that removed the subscription but not its nodes).
    ///     Manual nodes keep the "NONE" sentinel (<see cref="Server.IsInGroup" /> == false) and are untouched.
    /// </summary>
    private void ReconcileSubscriptionServers()
    {
        var groups = _subscriptions.Subscriptions.Select(s => s.GroupKey).ToHashSet();
        var orphans = _servers.Servers
            .Where(s => s.IsInGroup && !groups.Contains(s.Group))
            .ToList();
        foreach (var orphan in orphans)
            _servers.Remove(orphan);
    }

    public async Task SaveAsync()
    {
        // Snapshot on the calling thread before any await: the source ObservableCollections can be mutated
        // (subscription import / ping) on the UI thread, and enumerating one mid-write would throw.
        var servers = _servers.Servers.ToList();
        var modes = _modes.Modes.ToList();
        var subscriptions = _subscriptions.Subscriptions.ToList();

        await WriteList(ServersPath, servers);
        await WriteList(ModesPath, modes);
        await WriteList(SubscriptionsPath, subscriptions);
    }

    private static async Task<bool> LoadInto<T>(string path, ObservableCollection<T> target)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            await using var stream = File.OpenRead(path);
            var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, Options);
            if (items == null)
                return false;

            target.Clear();
            foreach (var item in items)
                target.Add(item);

            return true;
        }
        catch
        {
            // Corrupt file: keep whatever the repository already holds.
            return false;
        }
    }

    private static async Task WriteList<T>(string path, List<T> items)
    {
        // Atomic write: serialize to a UNIQUE temp file, then swap it in — a crash/kill mid-write leaves the
        // previous good file intact, and a per-write name prevents two overlapping saves from colliding.
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, items, Options);

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignored */ }
            throw;
        }
    }
}
