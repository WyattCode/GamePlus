using Netch.Core.Abstractions;
using Netch.Core.Models.Settings;
using Netch.Core.Services;

namespace Netch.Host;

/// <summary>
///     Persists per-game direct baselines in <see cref="AppSettings.EffectBaselines" /> via
///     <see cref="ISettingsService" />. Saves are fire-and-forget (the caller is on the acceleration
///     path and must not block on disk I/O); failures are logged.
/// </summary>
internal sealed class SettingsBaselineStore : IBaselineStore
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public SettingsBaselineStore(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
    }

    public StoredBaseline? Get(string gameId)
    {
        return _settings.Settings.EffectBaselines.TryGetValue(gameId, out var b) ? b : null;
    }

    public void Save(string gameId, StoredBaseline baseline)
    {
        _settings.Settings.EffectBaselines[gameId] = baseline;
        SaveAsync();
    }

    public bool ConsumeRemeasure(string gameId)
    {
        var removed = _settings.Settings.RemeasureGames.TryRemove(gameId, out _);
        if (removed)
            SaveAsync();
        return removed;
    }

    public void MarkRemeasure(string gameId)
    {
        _settings.Settings.RemeasureGames[gameId] = true;
        SaveAsync();
    }

    private void SaveAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _settings.SaveAsync();
            }
            catch (Exception e)
            {
                _log.Log(Netch.Core.Enums.LogLevel.Warning, $"保存直连基线失败：{e.Message}");
            }
        });
    }
}
