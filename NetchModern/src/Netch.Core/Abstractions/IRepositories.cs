using System.Collections.ObjectModel;
using Netch.Core.Models;
using Netch.Core.Models.Servers;
using Netch.Core.Models.Settings;

namespace Netch.Core.Abstractions;

public interface IServerRepository
{
    ObservableCollection<Server> Servers { get; }

    void Add(Server server);

    void Remove(Server server);

    /// <summary>Replaces <paramref name="existing" /> with <paramref name="replacement" /> in place.</summary>
    void Replace(Server existing, Server replacement);

    /// <summary>Pings every server currently in <see cref="Servers" />.</summary>
    Task PingAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Pings the given servers (a snapshot taken under a state lock). Network I/O only — safe to call
    ///     outside any state lock.
    /// </summary>
    Task PingAsync(IEnumerable<Server> servers, CancellationToken ct = default);
}

public interface IGameRepository
{
    System.Collections.ObjectModel.ObservableCollection<Netch.Core.Models.Games.Game> Games { get; }

    void ToggleFavorite(Netch.Core.Models.Games.Game game);

    void AddCustomGame(Netch.Core.Models.Games.Game game);

    void UpdateCustomGame(Netch.Core.Models.Games.Game game);

    void RemoveGame(Netch.Core.Models.Games.Game game);

    /// <summary>
    ///     Replaces all non-custom (seed) games with <paramref name="games" />, keeping custom games and
    ///     re-applying favourites. Used when the remote rule library is refreshed.
    /// </summary>
    void ReplaceSeedGames(IEnumerable<Netch.Core.Models.Games.Game> games);
}

public interface ISubscriptionRepository
{
    ObservableCollection<Subscription> Subscriptions { get; }

    void Add(Subscription subscription);

    void Remove(Subscription subscription);

    /// <summary>
    ///     Downloads + parses the subscription and applies the result. Kept for compatibility; callers that
    ///     must not hold a lock across network I/O should use <see cref="FetchAsync" /> + <see cref="Apply" />.
    /// </summary>
    Task UpdateAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>
    ///     Downloads + parses the subscription body into servers. Performs network I/O only — does not touch
    ///     shared state, so it is safe to call outside any state lock.
    /// </summary>
    Task<IReadOnlyList<Server>> FetchAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>
    ///     Replaces this subscription's previously-imported servers with <paramref name="servers" /> and
    ///     updates the subscription's bookkeeping. Mutates shared state — call under a state lock.
    /// </summary>
    void Apply(Subscription subscription, IReadOnlyList<Server> servers);
}

public interface IPersistenceService
{
    /// <summary>Loads persisted servers/subscriptions, replacing seeded data when files exist.</summary>
    Task LoadAsync();

    /// <summary>Persists the current servers/subscriptions to disk.</summary>
    Task SaveAsync();
}

public interface ISettingsService
{
    AppSettings Settings { get; }

    Task LoadAsync();

    Task SaveAsync();
}

public interface ILogService
{
    IReadOnlyList<LogEntry> Recent { get; }

    event EventHandler<LogEntry>? EntryAdded;

    void Log(Enums.LogLevel level, string message);

    void Clear();
}
