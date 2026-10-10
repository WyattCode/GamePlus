using System.Net;
using System.Text.Json;
using Netch.Core.Abstractions;
using Netch.Core.Enums;
using Netch.Core.Models.Games;

namespace Netch.Core.Services.Rules;

/// <summary>Where the currently active rule set came from.</summary>
public enum RulesSource
{
    Bundled,
    Cached,
    Remote
}

/// <summary>Outcome of a rules update attempt.</summary>
public enum RulesUpdateStatus
{
    Updated,
    UpToDate,
    Failed
}

/// <summary>Result of <see cref="GameRulesService.UpdateAsync" />.</summary>
public record RulesUpdateResult(RulesUpdateStatus Status, string Message, int Version);

/// <summary>Snapshot of the active rule set, surfaced to the UI.</summary>
public record RulesStatus(int Version, string Source, int GameCount, DateTime? LastCheckUtc,
    string? LastError, string? LastResult);

/// <summary>
///     Holds the active game rule library. Starts from the bundled copy and can be replaced by a
///     versioned rules file downloaded from a configurable URL (cached on disk between runs).
/// </summary>
public sealed class GameRulesService
{
    /// <summary>Hard cap on the downloaded body size (5 MB).</summary>
    private const long MaxBodyBytes = 5 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly ILogService? _log;
    private readonly Func<IEnumerable<Game>> _bundled;
    private readonly int _bundledVersion;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();

    private int _version;
    private RulesSource _source = RulesSource.Bundled;
    private string? _cachedJson;
    private DateTime? _lastCheckUtc;
    private string? _lastError;
    private string? _lastResult;

    public GameRulesService(HttpClient http, string cachePath, ILogService? log = null,
        Func<IEnumerable<Game>>? bundled = null, int bundledVersion = SampleGames.RulesVersion,
        Func<DateTime>? clock = null)
    {
        _http = http;
        _cachePath = cachePath;
        _log = log;
        _bundled = bundled ?? SampleGames.Create;
        _bundledVersion = bundledVersion;
        _clock = clock ?? (() => DateTime.UtcNow);
        _version = bundledVersion;
    }

    /// <summary>
    ///     Loads the cached rule file (if any) when it is newer than the bundled copy. A corrupt cache is
    ///     logged and ignored — never deleted, so the user can inspect it.
    /// </summary>
    public void LoadInitial()
    {
        lock (_gate)
        {
            _version = _bundledVersion;
            _source = RulesSource.Bundled;
            _cachedJson = null;

            if (!File.Exists(_cachePath))
                return;

            string json;
            try
            {
                json = File.ReadAllText(_cachePath);
            }
            catch (Exception e)
            {
                _log?.Log(LogLevel.Warning, $"读取规则缓存失败，已忽略：{e.Message}");
                return;
            }

            var parsed = GameRulesFile.Parse(json, out var error);
            if (parsed is null)
            {
                _log?.Log(LogLevel.Warning, $"规则缓存无效，已忽略：{error}");
                return;
            }

            if (parsed.Version <= _bundledVersion)
                return;

            _version = parsed.Version;
            _source = RulesSource.Cached;
            _cachedJson = json;
        }
    }

    /// <summary>
    ///     Returns fresh <see cref="Game" /> instances on every call so callers may mutate them freely.
    /// </summary>
    public IEnumerable<Game> CreateGames()
    {
        string? cached;
        lock (_gate)
            cached = _cachedJson;

        if (cached is null)
            return _bundled().Select(Clone).ToList();

        var parsed = GameRulesFile.Parse(cached, out _);
        if (parsed is null)
            return _bundled().Select(Clone).ToList();

        return parsed.Games.Select(Clone).ToList();
    }

    /// <summary>Current status snapshot.</summary>
    public RulesStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new RulesStatus(_version, SourceName(_source), CreateGames().Count(),
                    _lastCheckUtc, _lastError, _lastResult);
            }
        }
    }

    /// <summary>
    ///     Downloads + validates the rules file at <paramref name="url" /> and, when it is newer than the
    ///     active version, caches it and switches the active rule set. Never throws.
    /// </summary>
    public async Task<RulesUpdateResult> UpdateAsync(string url, CancellationToken ct)
    {
        var checkTime = _clock();

        if (!TryValidateUrl(url, out var uri, out var urlError))
            return Fail(urlError!, checkTime);

        string body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Fail($"规则更新失败：服务器返回 {(int)response.StatusCode}", checkTime);

            if (response.Content.Headers.ContentLength is { } length && length > MaxBodyBytes)
                return Fail("规则文件过大（超过 5 MB）", checkTime);

            body = await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("规则更新超时（15 秒）", checkTime);
        }
        catch (Exception e)
        {
            return Fail($"规则更新失败：{e.Message}", checkTime);
        }

        if (body.Length > MaxBodyBytes)
            return Fail("规则文件过大（超过 5 MB）", checkTime);

        var parsed = GameRulesFile.Parse(body, out var error);
        if (parsed is null)
            return Fail($"规则文件无效：{error}", checkTime);

        lock (_gate)
        {
            if (parsed.Version <= _version)
            {
                _lastCheckUtc = checkTime;
                _lastError = null;
                _lastResult = $"规则库已是最新（版本 {_version}）";
                return new RulesUpdateResult(RulesUpdateStatus.UpToDate, _lastResult, _version);
            }
        }

        try
        {
            WriteCache(body);
        }
        catch (Exception e)
        {
            return Fail($"保存规则缓存失败：{e.Message}", checkTime);
        }

        lock (_gate)
        {
            _version = parsed.Version;
            _source = RulesSource.Remote;
            _cachedJson = body;
            _lastCheckUtc = checkTime;
            _lastError = null;
            _lastResult = $"规则库已更新到版本 {parsed.Version}，共 {parsed.Games.Count} 个游戏";
            return new RulesUpdateResult(RulesUpdateStatus.Updated, _lastResult, _version);
        }
    }

    private RulesUpdateResult Fail(string message, DateTime checkTime)
    {
        lock (_gate)
        {
            _lastCheckUtc = checkTime;
            _lastError = message;
            _lastResult = message;
            return new RulesUpdateResult(RulesUpdateStatus.Failed, message, _version);
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                throw new InvalidOperationException("规则文件过大（超过 5 MB）");
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteCache(string json)
    {
        var dir = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignored */ }
            throw;
        }
    }

    private static bool TryValidateUrl(string url, out Uri uri, out string? error)
    {
        uri = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "规则更新地址必须是 https:// 开头的网址";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            error = "规则更新地址必须是 https:// 开头的网址";
            return false;
        }

        if (parsed.Scheme == Uri.UriSchemeHttps)
        {
            uri = parsed;
            return true;
        }

        // Plain http is only tolerated for loopback hosts (local testing).
        if (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)
        {
            uri = parsed;
            return true;
        }

        error = "规则更新地址必须是 https:// 开头的网址";
        return false;
    }

    private static string SourceName(RulesSource source) => source switch
    {
        RulesSource.Cached => "cached",
        RulesSource.Remote => "remote",
        _ => "bundled"
    };

    private static Game Clone(Game g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        EnglishName = g.EnglishName,
        Icon = g.Icon,
        AccentColor = g.AccentColor,
        Category = g.Category,
        Aliases = new List<string>(g.Aliases),
        Regions = new List<string>(g.Regions),
        Platforms = new List<string>(g.Platforms),
        ProxyDomains = new List<string>(g.ProxyDomains),
        ProxyIps = new List<string>(g.ProxyIps),
        ProxyProcesses = new List<string>(g.ProxyProcesses),
        IsCustom = false,
        IsFavorite = false
    };
}
