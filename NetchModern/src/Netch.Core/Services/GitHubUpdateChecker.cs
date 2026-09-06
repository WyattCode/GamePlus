using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Netch.Core.Services;

public sealed record UpdateInfo(Version Current, Version Latest, bool HasUpdate, string TagName, string? Url);

public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);
}

/// <summary>
///     Checks a GitHub repository's latest release and compares its tag to the running version.
///     Ported in spirit from the original Netch UpdateChecker.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;

    public GitHubUpdateChecker(HttpClient http, Version currentVersion, string owner = "NetchX", string repo = "Netch")
    {
        _http = http;
        CurrentVersion = currentVersion;
        Owner = owner;
        Repo = repo;
    }

    public Version CurrentVersion { get; }
    public string Owner { get; }
    public string Repo { get; }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("GamePlus-UpdateChecker");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct);
        if (release?.TagName == null)
            return null;

        var latest = ParseVersion(release.TagName);
        // Compare on normalized forms so "2.3" vs "2.3.0" (Version treats an absent Build/Revision as -1)
        // don't falsely register as newer/older.
        var hasUpdate = Normalize(latest) > Normalize(CurrentVersion);
        return new UpdateInfo(CurrentVersion, latest, hasUpdate, release.TagName, release.HtmlUrl);
    }

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
