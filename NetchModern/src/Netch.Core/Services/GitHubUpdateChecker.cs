using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Netch.Core.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed,
    NotConfigured
}

/// <summary>
///     Result of an update check. Never thrown for network/HTTP/JSON errors — those are reported via
///     <see cref="Status" /> = <see cref="UpdateCheckStatus.Failed" /> and a short user-facing
///     <see cref="Error" /> (Simplified Chinese).
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version Current,
    Version? Latest = null,
    string? TagName = null,
    string? Url = null,
    string? Error = null)
{
    public static UpdateCheckResult NotConfigured(Version current, string reason) =>
        new(UpdateCheckStatus.NotConfigured, current, Error: reason);

    public static UpdateCheckResult Failed(Version current, string reason) =>
        new(UpdateCheckStatus.Failed, current, Error: reason);
}

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>
///     Checks an update source (a GitHub release API endpoint, or any URL returning a
///     GitHub-release-shaped JSON document with <c>tag_name</c>/<c>html_url</c>) and compares its tag
///     to the running version. Ported in spirit from the original Netch UpdateChecker.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly string? _sourceUrl;
    private readonly bool _configured;

    /// <param name="sourceUrl">
    ///     Full URL to fetch. When null, the GitHub releases API for <paramref name="owner" />/
    ///     <paramref name="repo" /> is used.
    /// </param>
    /// <param name="configured">
    ///     False when the update source was explicitly disabled by configuration; then
    ///     <see cref="CheckAsync" /> returns <see cref="UpdateCheckStatus.NotConfigured" />.
    /// </param>
    public GitHubUpdateChecker(
        HttpClient http,
        Version currentVersion,
        string owner = "WyattCode",
        string repo = "GamePlus",
        string? sourceUrl = null,
        bool configured = true)
    {
        _http = http;
        CurrentVersion = currentVersion;
        Owner = owner;
        Repo = repo;
        _sourceUrl = sourceUrl;
        _configured = configured;
    }

    public Version CurrentVersion { get; }
    public string Owner { get; }
    public string Repo { get; }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!_configured)
            return UpdateCheckResult.NotConfigured(CurrentVersion, "未配置更新源");

        var url = _sourceUrl ?? $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        GitHubRelease? release;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("GamePlus-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed(CurrentVersion, DescribeHttpFailure(response.StatusCode));

            release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // HttpClient timeout (not caller cancellation).
            return UpdateCheckResult.Failed(CurrentVersion, "检查更新超时，请稍后重试");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed(CurrentVersion, $"网络错误：{Short(ex.Message)}");
        }
        catch (System.Text.Json.JsonException)
        {
            return UpdateCheckResult.Failed(CurrentVersion, "更新源返回的数据无法解析");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(CurrentVersion, $"检查更新失败：{Short(ex.Message)}");
        }

        if (release?.TagName is null or "")
            return UpdateCheckResult.Failed(CurrentVersion, "更新源未返回版本信息");

        var latest = ParseVersion(release.TagName);
        // Compare on normalized forms so "2.3" vs "2.3.0" (Version treats an absent Build/Revision as -1)
        // don't falsely register as newer/older.
        var hasUpdate = Normalize(latest) > Normalize(CurrentVersion);
        return new UpdateCheckResult(
            hasUpdate ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            CurrentVersion,
            latest,
            release.TagName,
            release.HtmlUrl);
    }

    private static string DescribeHttpFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound =>
            "更新源不可访问（仓库可能是私有的或尚未发布版本）",
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
            "更新源请求过于频繁，请稍后重试",
        HttpStatusCode.Unauthorized =>
            "更新源需要授权，无法访问",
        _ => $"更新源返回错误（HTTP {(int)status}）"
    };

    private static string Short(string message) =>
        message.Length <= 120 ? message : message[..120];

    /// <summary>Turns a tag like "v2.3.1-beta" into a comparable <see cref="Version" />.</summary>
    public static Version ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V').Trim();
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0)
            s = s[..dash];

        // Version.TryParse needs at least major.minor; pad a single-component tag like "3" -> "3.0".
        if (!s.Contains('.'))
            s += ".0";

        return Version.TryParse(s, out var v) ? v : new Version(0, 0);
    }

    /// <summary>Forces Build to 0 (Revision stays -1 on both operands) so component-count differences don't skew comparison.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
