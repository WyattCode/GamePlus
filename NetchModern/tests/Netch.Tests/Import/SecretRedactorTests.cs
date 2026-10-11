using Netch.Core.Utils;
using Xunit;

namespace Netch.Tests.Import;

public class SecretRedactorTests
{
    [Fact]
    public void RedactUrlRemovesTokenInPath()
    {
        var redacted = SecretRedactor.RedactUrl("https://example.com/sub/abc123token");

        Assert.Equal("https://example.com/…", redacted);
        Assert.DoesNotContain("abc123token", redacted);
    }

    [Fact]
    public void RedactUrlRemovesQueryString()
    {
        var redacted = SecretRedactor.RedactUrl("https://example.com/sub?token=secret");

        Assert.DoesNotContain("secret", redacted);
        Assert.StartsWith("https://example.com", redacted);
    }

    [Fact]
    public void RedactUrlRemovesUserInfo()
    {
        var redacted = SecretRedactor.RedactUrl("https://user:pass@example.com/sub");

        Assert.DoesNotContain("user", redacted);
        Assert.DoesNotContain("pass", redacted);
        Assert.StartsWith("https://example.com", redacted);
    }

    [Fact]
    public void RedactUrlNonUrlReturnsEllipsis()
    {
        Assert.Equal("…", SecretRedactor.RedactUrl("not a url"));
        Assert.Equal("…", SecretRedactor.RedactUrl(""));
    }

    [Fact]
    public void RedactShareLinkRemovesPassword()
    {
        var redacted = SecretRedactor.RedactShareLink("ss://YWVzLTEyOC1nY206cA==@1.2.3.4:8388#node");

        Assert.Equal("ss://…", redacted);
        Assert.DoesNotContain("cA==", redacted);
    }

    [Fact]
    public void RedactShareLinkNonLinkReturnsEllipsis()
    {
        Assert.Equal("…", SecretRedactor.RedactShareLink("just text"));
    }
}
