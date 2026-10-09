using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netch.Core.Services;

/// <summary>
///     Resolves where the update check should look, in priority order:
///     <list type="number">
///         <item>env <c>GAMEPLUS_UPDATE_URL</c> — any URL returning GitHub-release-shaped JSON.</item>
///         <item>env <c>GAMEPLUS_UPDATE_OWNER</c> / <c>GAMEPLUS_UPDATE_REPO</c>.</item>
///         <item><c>update.json</c> next to the host exe (<see cref="AppContext.BaseDirectory" />).</item>
///         <item>default owner/repo.</item>
///     </list>
///     A config file with <c>{ "enabled": false }</c> disables the check entirely.
/// </summary>
public sealed class UpdateSourceOptions
{
    public const string DefaultOwner = "WyattCode";
    public const string DefaultRepo = "GamePlus";

    public string Owner { get; init; } = DefaultOwner;
    public string Repo { get; init; } = DefaultRepo;

    /// <summary>Full URL to fetch; when set, overrides owner/repo.</summary>
    public string? Url { get; init; }

    /// <summary>False when updates were explicitly disabled by configuration.</summary>
    public bool Enabled { get; init; } = true;

    public static UpdateSourceOptions Resolve(
        Func<string, string?>? getEnv = null,
        string? baseDirectory = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        baseDirectory ??= AppContext.BaseDirectory;

        var url = getEnv("GAMEPLUS_UPDATE_URL");
        if (!string.IsNullOrWhiteSpace(url))
            return new UpdateSourceOptions { Url = url.Trim() };

        var owner = getEnv("GAMEPLUS_UPDATE_OWNER");
        var repo = getEnv("GAMEPLUS_UPDATE_REPO");
        if (!string.IsNullOrWhiteSpace(owner) || !string.IsNullOrWhiteSpace(repo))
            return new UpdateSourceOptions
            {
                Owner = string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner.Trim(),
                Repo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim()
            };

        var file = TryReadFile(baseDirectory);
        if (file is not null)
            return file;

        return new UpdateSourceOptions();
    }

    private static UpdateSourceOptions? TryReadFile(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, "update.json");
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<UpdateFileConfig>(json);
            if (cfg is null)
                return null;

            if (cfg.Enabled == false)
                return new UpdateSourceOptions { Enabled = false };

            if (!string.IsNullOrWhiteSpace(cfg.Url))
                return new UpdateSourceOptions { Url = cfg.Url.Trim() };

            if (!string.IsNullOrWhiteSpace(cfg.Owner) || !string.IsNullOrWhiteSpace(cfg.Repo))
                return new UpdateSourceOptions
                {
                    Owner = string.IsNullOrWhiteSpace(cfg.Owner) ? DefaultOwner : cfg.Owner.Trim(),
                    Repo = string.IsNullOrWhiteSpace(cfg.Repo) ? DefaultRepo : cfg.Repo.Trim()
                };

            return null;
        }
        catch
        {
            // A malformed/unreadable config file must not crash startup; fall back to defaults.
            return null;
        }
    }

    private sealed class UpdateFileConfig
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("owner")]
        public string? Owner { get; set; }

        [JsonPropertyName("repo")]
        public string? Repo { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }
    }
}
