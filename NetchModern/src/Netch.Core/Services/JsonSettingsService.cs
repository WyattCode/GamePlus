using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Models.Settings;

namespace Netch.Core.Services;

/// <summary>
///     Persists <see cref="AppSettings" /> as JSON under %AppData%\Game+\settings.json.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonSettingsService()
    {
        _path = Path.Combine(AppPaths.DataDirectory, "settings.json");
    }

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options);
            if (loaded != null)
                Settings = loaded;
        }
        catch
        {
            // Corrupt/incompatible file: fall back to defaults.
            Settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        // Atomic write with a UNIQUE temp name so an interrupted save can't corrupt the file and two
        // overlapping (fire-and-forget) saves can't collide on the same temp path.
        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, Settings, Options);

            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignored */ }
            throw;
        }
    }
}
