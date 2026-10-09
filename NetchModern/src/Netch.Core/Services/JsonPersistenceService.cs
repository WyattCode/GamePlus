using System.Collections.ObjectModel;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     Persists servers and subscriptions as JSON under %AppData%\Game+\.
///     On first run (no files) the seeded repository contents are written out as-is.
/// </summary>
public sealed class JsonPersistenceService : IPersistenceService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _dir;
    private readonly IServerRepository _servers;
    private readonly ISubscriptionRepository _subscriptions;

    // Serializes concurrent SaveAsync calls so two overlapping saves can't interleave their temp-file
    // swaps (each write already uses a unique temp name, but the final File.Move ordering still matters).
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public JsonPersistenceService(IServerRepository servers, ISubscriptionRepository subscriptions)
        : this(servers, subscriptions, AppPaths.DataDirectory)
    {
    }

    /// <summary>Test-friendly constructor: lets callers point the service at a temp directory.</summary>
    public JsonPersistenceService(IServerRepository servers, ISubscriptionRepository subscriptions, string directory)
    {
        _servers = servers;
        _subscriptions = subscriptions;

        _dir = directory;
    }

    private string ServersPath => Path.Combine(_dir, "servers.json");
    private string SubscriptionsPath => Path.Combine(_dir, "subscriptions.json");

    // NOTE: modes.json is no longer read or written. Any existing file on disk is left untouched.

    public async Task LoadAsync()
    {
        var loadedAny = false;
        loadedAny |= await LoadInto(ServersPath, _servers.Servers);
        loadedAny |= await LoadInto(SubscriptionsPath, _subscriptions.Subscriptions);

        // Migrate BEFORE the orphan purge: legacy servers still carry the old group key (remark/link) and
        // would otherwise be deleted as orphans.
        var migrated = MigrateSubscriptionIds();

        ReconcileSubscriptionServers();

        // First run: no files yet — persist the seeded data so it exists next time.
        if (!loadedAny || migrated)
            await SaveAsync();
    }

    /// <summary>
    ///     One-time migration for subscriptions created before <see cref="Subscription.Id" /> existed:
    ///     assign an id to every subscription, then re-tag servers whose <c>Group</c> matches a legacy key
    ///     (old formula: remark if not blank else link) to the new <c>"sub:&lt;id&gt;"</c> key. When several
    ///     subscriptions share the same legacy key, only the first one claims those servers — the others
    ///     will re-import their own nodes on their next update.
    /// </summary>
    private bool MigrateSubscriptionIds()
    {
        var subs = _subscriptions.Subscriptions.ToList();
        if (subs.Count == 0)
            return false;

        var changed = false;

        // Assign ids first so GroupKey is stable for the re-tagging pass below.
        foreach (var sub in subs)
        {
            if (string.IsNullOrWhiteSpace(sub.Id))
            {
                sub.EnsureId();
                changed = true;
            }
        }

        // Map legacy key -> first subscription that owns it.
        var legacyOwners = new Dictionary<string, Subscription>(StringComparer.Ordinal);
        foreach (var sub in subs)
        {
            var legacy = sub.LegacyGroupKey;
            if (string.IsNullOrWhiteSpace(legacy))
                continue;
            if (!legacyOwners.ContainsKey(legacy))
                legacyOwners[legacy] = sub;
        }

        foreach (var server in _servers.Servers.ToList())
        {
            if (!server.IsInGroup)
                continue;
            if (server.Group.StartsWith("sub:", StringComparison.Ordinal))
                continue; // already migrated
            if (legacyOwners.TryGetValue(server.Group, out var owner))
            {
                server.Group = owner.GroupKey;
                changed = true;
            }
        }

        return changed;
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
        var subscriptions = _subscriptions.Subscriptions.ToList();

        await _saveGate.WaitAsync();
        try
        {
            await WriteList(ServersPath, servers);
            await WriteList(SubscriptionsPath, subscriptions);
        }
        finally
        {
            _saveGate.Release();
        }
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
