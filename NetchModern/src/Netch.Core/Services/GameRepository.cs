using System.Collections.ObjectModel;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models.Games;

namespace Netch.Core.Services;

public sealed class GameRepository : IGameRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _customPath;
    private readonly ISettingsService _settings;

    public GameRepository(ISettingsService settings)
    {
        _settings = settings;

        _customPath = Path.Combine(AppPaths.DataDirectory, "custom-games.json");

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

        _ = _settings.SaveAsync();
    }

    public void AddCustomGame(Game game)
    {
        game.IsCustom = true;
        if (string.IsNullOrWhiteSpace(game.Id))
            game.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];

        Games.Add(game);
        SaveCustom();
    }

    public void UpdateCustomGame(Game game) => SaveCustom();

    public void RemoveGame(Game game)
    {
        if (!game.IsCustom)
            return;

        Games.Remove(game);
        SaveCustom();
    }

    private List<Game> LoadCustom()
    {
        try
        {
            return File.Exists(_customPath)
                ? JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(_customPath), Options) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveCustom()
    {
        try
        {
            var custom = Games.Where(g => g.IsCustom).ToList();
            File.WriteAllText(_customPath, JsonSerializer.Serialize(custom, Options));
        }
        catch
        {
            // best-effort
        }
    }
}
