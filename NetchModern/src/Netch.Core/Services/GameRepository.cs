using System.Collections.ObjectModel;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models.Games;

namespace Netch.Core.Services;

public sealed class GameRepository : IGameRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _customPath;
    private readonly ISettingsService _settings;
    private readonly ILogService? _log;

    public GameRepository(ISettingsService settings)
        : this(settings, Path.Combine(AppPaths.DataDirectory, "custom-games.json"), null)
    {
    }

    /// <summary>Test-friendly constructor: lets callers point the repository at a temp file.</summary>
    public GameRepository(ISettingsService settings, string customPath, ILogService? log = null)
    {
        _settings = settings;
        _customPath = customPath;
        _log = log;

        foreach (var game in SampleGames.Create())
            Games.Add(game);

        foreach (var game in LoadCustom())
        {
            game.IsCustom = true;
            Games.Add(game);
        }

        var favorites = settings.Settings.FavoriteGames;
        foreach (var game in Games)
            game.IsFavorite = favorites.Contains(game.Id);
    }

    public ObservableCollection<Game> Games { get; } = new();

    public void ToggleFavorite(Game game)
    {
        game.IsFavorite = !game.IsFavorite;

        var favorites = _settings.Settings.FavoriteGames;
        if (game.IsFavorite)
        {
            if (!favorites.Contains(game.Id))
                favorites.Add(game.Id);
        }
        else
        {
            favorites.Remove(game.Id);
        }

        // Callers are responsible for persisting settings (the RPC router awaits SettingsSvc.SaveAsync).
        // Saving here too would race the caller's save.
    }

    public void AddCustomGame(Game game)
    {
        game.IsCustom = true;
        if (string.IsNullOrWhiteSpace(game.Id))
            game.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];

        Games.Add(game);
        try
        {
            SaveCustom();
        }
        catch
        {
            // Roll back the in-memory change so the UI doesn't show a game that failed to persist.
            Games.Remove(game);
            throw;
        }
    }

    public void UpdateCustomGame(Game game) => SaveCustom();

    public void RemoveGame(Game game)
    {
        if (!game.IsCustom)
            return;

        var index = Games.IndexOf(game);
        Games.Remove(game);
        try
        {
            SaveCustom();
        }
        catch
        {
            // Roll back the in-memory change so the UI doesn't hide a game that's still on disk.
            if (index >= 0 && index <= Games.Count)
                Games.Insert(index, game);
            else
                Games.Add(game);
            throw;
        }
    }

    private List<Game> LoadCustom()
    {
        if (!File.Exists(_customPath))
            return new();

        try
        {
            return JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(_customPath), Options) ?? new();
        }
        catch (Exception e)
        {
            // Corrupt file: log it and rename it aside so the next save doesn't silently overwrite the
            // user's (possibly recoverable) data.
            _log?.Log(LogLevel.Warning,
                $"自定义游戏文件解析失败，已重命名为 .corrupt 备份：{e.Message}");
            try
            {
                var backup = _customPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(_customPath, backup, overwrite: true);
            }
            catch
            {
                // best-effort
            }
            return new();
        }
    }

    private void SaveCustom()
    {
        var custom = Games.Where(g => g.IsCustom).ToList();
        var json = JsonSerializer.Serialize(custom, Options);

        // Atomic write: temp file + move, so a crash mid-write leaves the previous good file intact.
        var tmp = _customPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_customPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(tmp, json);
            File.Move(tmp, _customPath, overwrite: true);
        }
        catch (Exception e)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignored */ }
            throw new InvalidOperationException($"保存自定义游戏失败：{e.Message}", e);
        }
    }
}
