using System.Collections.ObjectModel;
using Netch.Core.Models;
using Netch.Core.Models.Modes;
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

    Task PingAllAsync(CancellationToken ct = default);
}

public interface IGameRepository
{
    System.Collections.ObjectModel.ObservableCollection<Netch.Core.Models.Games.Game> Games { get; }

    void ToggleFavorite(Netch.Core.Models.Games.Game game);

    void AddCustomGame(Netch.Core.Models.Games.Game game);

    void UpdateCustomGame(Netch.Core.Models.Games.Game game);

    void RemoveGame(Netch.Core.Models.Games.Game game);
}

public interface IModeRepository
{
    ObservableCollection<Mode> Modes { get; }

    void Add(Mode mode);

    void Remove(Mode mode);
}

public interface ISubscriptionRepository
{
    ObservableCollection<Subscription> Subscriptions { get; }

    void Add(Subscription subscription);

    void Remove(Subscription subscription);

    Task UpdateAsync(Subscription subscription, CancellationToken ct = default);
}

public interface IPersistenceService
{
    /// <summary>Loads persisted servers/modes/subscriptions, replacing seeded data when files exist.</summary>
    Task LoadAsync();

    /// <summary>Persists the current servers/modes/subscriptions to disk.</summary>
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
