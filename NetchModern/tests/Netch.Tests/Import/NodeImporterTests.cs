using Netch.Core.Models.Servers;
using Netch.Core.Services.Import;
using Xunit;

namespace Netch.Tests.Import;

public class NodeImporterTests
{
    [Fact]
    public void DetectsClashFormat()
    {
        const string yaml = """
proxies:
  - name: a
    type: ss
    server: 1.2.3.4
    port: 8388
    cipher: aes-128-gcm
    password: p
""";

        var result = NodeImporter.Parse(yaml);

        Assert.Equal("clash", result.Format);
        Assert.Single(result.Valid);
    }

    [Fact]
    public void DetectsLinksFormat()
    {
        const string text = "ss://YWVzLTEyOC1nY206cA==@1.2.3.4:8388#node";

        var result = NodeImporter.Parse(text);

        Assert.Equal("links", result.Format);
        Assert.Single(result.Valid);
    }

    [Fact]
    public void DetectsBase64Format()
    {
        // Base64 of "ss://YWVzLTEyOC1nY206cA==@1.2.3.4:8388#node"
        const string text = "c3M6Ly9ZV1Z6TFRFeU9DMW5ZMjA2Y0E9PUBLDIuMy40OjgzODg jbm9kZQ==";
        var result = NodeImporter.Parse(text);

        Assert.Equal("base64", result.Format);
    }

    [Fact]
    public void EmptyInputReturnsEmptyFormat()
    {
        var result = NodeImporter.Parse("   ");

        Assert.Equal("empty", result.Format);
        Assert.Empty(result.Valid);
    }

    [Fact]
    public void ValidateRejectsBadHost()
    {
        var server = new ShadowsocksServer { Hostname = "bad host", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p" };
        Assert.NotNull(NodeImporter.Validate(server));
    }

    [Fact]
    public void ValidateRejectsBadPort()
    {
        var server = new ShadowsocksServer { Hostname = "1.2.3.4", Port = 0, EncryptMethod = "aes-128-gcm", Password = "p" };
        Assert.NotNull(NodeImporter.Validate(server));
    }

    [Fact]
    public void ValidateRejectsMissingCredentials()
    {
        Assert.NotNull(NodeImporter.Validate(new ShadowsocksServer { Hostname = "1.2.3.4", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "" }));
        Assert.NotNull(NodeImporter.Validate(new VMessServer { Hostname = "1.2.3.4", Port = 443, UserId = "not-a-guid" }));
        Assert.NotNull(NodeImporter.Validate(new TrojanServer { Hostname = "1.2.3.4", Port = 443, Password = "" }));
        Assert.NotNull(NodeImporter.Validate(new Hysteria2Server { Hostname = "1.2.3.4", Port = 443, Password = "" }));
    }

    [Fact]
    public void ValidateAcceptsGoodServer()
    {
        var server = new VMessServer
        {
            Hostname = "1.2.3.4",
            Port = 443,
            UserId = "11111111-1111-1111-1111-111111111111"
        };
        Assert.Null(NodeImporter.Validate(server));
    }

    [Fact]
    public void DedupeDropsExistingAndWithinBatch()
    {
        var existing = new Server[]
        {
            new ShadowsocksServer { Hostname = "1.2.3.4", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p" }
        };

        var incoming = new Server[]
        {
            // Duplicate of existing (different remark — must still be dropped).
            new ShadowsocksServer { Hostname = "1.2.3.4", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p", Remark = "other" },
            // New.
            new ShadowsocksServer { Hostname = "5.6.7.8", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p" },
            // Duplicate within the batch.
            new ShadowsocksServer { Hostname = "5.6.7.8", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p", Remark = "dup" }
        };

        var (added, duplicates) = NodeImporter.Dedupe(existing, incoming);

        Assert.Single(added);
        Assert.Equal(2, duplicates);
        Assert.Equal("5.6.7.8", added[0].Hostname);
    }

    [Fact]
    public void IdentityKeyIgnoresRemark()
    {
        var a = new ShadowsocksServer { Hostname = "1.2.3.4", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p", Remark = "a" };
        var b = new ShadowsocksServer { Hostname = "1.2.3.4", Port = 8388, EncryptMethod = "aes-128-gcm", Password = "p", Remark = "b" };

        Assert.Equal(NodeImporter.IdentityKey(a), NodeImporter.IdentityKey(b));
    }
}
