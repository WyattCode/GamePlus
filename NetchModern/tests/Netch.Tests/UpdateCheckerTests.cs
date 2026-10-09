using System.Net;
using System.Net.Http;
using System.Text;
using Netch.Core.Services;

namespace Netch.Tests;

public class UpdateCheckerTests
{
    private static HttpClient Client(HttpMessageHandler handler) => new(handler);

    private static GitHubUpdateChecker Checker(
        HttpMessageHandler handler,
        Version? current = null,
        string? sourceUrl = null,
        bool configured = true) =>
        new(Client(handler), current ?? new Version(3, 0, 0), "WyattCode", "GamePlus", sourceUrl, configured);

    [Fact]
    public async Task NotFound_ReturnsFailed_NotUpToDate()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Contains("更新源", result.Error!);
    }

    [Fact]
    public async Task NewerTag_ReturnsUpdateAvailable()
    {
        var handler = new FakeHandler(_ => Json("""{"tag_name":"v3.1.0","html_url":"https://example.com/r"}"""));
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(3, 1, 0), result.Latest);
        Assert.Equal("v3.1.0", result.TagName);
        Assert.Equal("https://example.com/r", result.Url);
    }

    [Fact]
    public async Task SameVersion_ReturnsUpToDate()
    {
        var handler = new FakeHandler(_ => Json("""{"tag_name":"v3.0.0","html_url":"https://example.com/r"}"""));
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal(new Version(3, 0, 0), result.Latest);
    }

    [Fact]
    public async Task MalformedJson_ReturnsFailed()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not json", Encoding.UTF8, "application/json")
        });
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task CustomUrlSource_HitsThatUrl()
    {
        Uri? seen = null;
        var handler = new FakeHandler(req =>
        {
            seen = req.RequestUri;
            return Json("""{"tag_name":"v9.9.9","html_url":"https://example.com/x"}""");
        });

        var result = await Checker(handler, sourceUrl: "https://example.com/latest.json").CheckAsync();

        Assert.Equal("https://example.com/latest.json", seen?.ToString());
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(9, 9, 9), result.Latest);
    }

    [Fact]
    public async Task HttpRequestException_ReturnsFailed()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("dns failure"));
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Disabled_ReturnsNotConfigured()
    {
        var handler = new FakeHandler(_ => Json("""{"tag_name":"v9.9.9"}"""));
        var result = await Checker(handler, configured: false).CheckAsync();

        Assert.Equal(UpdateCheckStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task MissingTag_ReturnsFailed()
    {
        var handler = new FakeHandler(_ => Json("""{"html_url":"https://example.com/r"}"""));
        var result = await Checker(handler).CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
