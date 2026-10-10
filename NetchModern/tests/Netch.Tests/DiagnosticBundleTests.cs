using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Netch.Core.Services.Diagnostics;
using Xunit;

namespace Netch.Tests;

public class DiagnosticBundleTests
{
    private const string PlantedSecret = "SUPER-SECRET-PASSWORD-1234";

    // --- ConfigSanitizer ---

    [Fact]
    public void Sanitize_RedactsNestedSensitiveKeys()
    {
        var node = JsonNode.Parse("""
        {
          "name": "harmless",
          "password": "hunter2",
          "nested": { "Uuid": "11111111-2222-3333-4444-555555555555", "token": "abc" },
          "list": [ { "apiKey": "k" }, { "value": 42 } ]
        }
        """)!;

        var result = ConfigSanitizer.Sanitize(node)!;

        Assert.Equal("harmless", result["name"]!.GetValue<string>());
        Assert.Equal("***", result["password"]!.GetValue<string>());
        Assert.Equal("***", result["nested"]!["Uuid"]!.GetValue<string>());
        Assert.Equal("***", result["nested"]!["token"]!.GetValue<string>());
        Assert.Equal("***", result["list"]![0]!["apiKey"]!.GetValue<string>());
        Assert.Equal(42, result["list"]![1]!["value"]!.GetValue<int>());
    }

    [Fact]
    public void Sanitize_RedactsUrlsButKeepsHarmlessValues()
    {
        var node = JsonNode.Parse("""
        {
          "link": "https://example.com/sub?token=abc",
          "url": "vmess://eyJhZGQiOiIxLjIuMy40In0=",
          "note": "hello world",
          "count": 7
        }
        """)!;

        var result = ConfigSanitizer.Sanitize(node)!;

        Assert.DoesNotContain("token=abc", result["link"]!.GetValue<string>());
        Assert.StartsWith("https://example.com", result["link"]!.GetValue<string>());
        Assert.Equal("vmess://…", result["url"]!.GetValue<string>());
        Assert.Equal("hello world", result["note"]!.GetValue<string>());
        Assert.Equal(7, result["count"]!.GetValue<int>());
    }

    [Fact]
    public void Sanitize_DoesNotMutateOriginal()
    {
        var node = JsonNode.Parse("""{ "password": "secret" }""")!;
        var result = ConfigSanitizer.Sanitize(node)!;
        Assert.Equal("secret", node["password"]!.GetValue<string>());
        Assert.Equal("***", result["password"]!.GetValue<string>());
    }

    // --- LogSanitizer ---

    [Fact]
    public void LogSanitizer_RemovesShareLinkAndTokenQuery()
    {
        var line = "connecting vmess://eyJhZGQiOiIxLjIuMy40In0= and https://sub.example.com/x?token=abc123 done";
        var sanitized = LogSanitizer.Sanitize(line);

        Assert.DoesNotContain("vmess://eyJhZGQiOiIxLjIuMy40In0=", sanitized);
        Assert.Contains("vmess://…", sanitized);
        Assert.DoesNotContain("token=abc123", sanitized);
        Assert.DoesNotContain("abc123", sanitized);
    }

    [Fact]
    public void LogSanitizer_RedactsPasswordPairs()
    {
        var sanitized = LogSanitizer.Sanitize("auth password=hunter2&user=bob");
        Assert.DoesNotContain("hunter2", sanitized);
        Assert.Contains("password=***", sanitized);
    }

    // --- DiagnosticBundleBuilder ---

    private static BundleInput SampleInput(IReadOnlyList<string>? logs = null) => new(
        AppVersion: "3.0.0",
        HostVersion: "3.0.0",
        Environment: new Dictionary<string, string> { ["os"] = "Windows", ["elevated"] = "True" },
        Settings: JsonNode.Parse($$"""{ "password": "{{PlantedSecret}}", "theme": "dark" }"""),
        Servers: new List<object> { new { type = "SS", remark = "香港 01", port = 8388, group = "NONE" } },
        Subscriptions: new List<object> { new { remark = "sub", serverCount = 3, host = "https://example.com/…" } },
        LogLines: logs ?? new List<string> { "info started", $"error password={PlantedSecret}" },
        CrashFiles: new List<(string, string)> { ("crash-host-1.txt", $"boom password={PlantedSecret}") },
        Diagnostics: new { summary = "ok" },
        Client: JsonNode.Parse("""{ "appVersion": "3.0.0" }"""));

    [Fact]
    public void Build_ContainsExpectedEntries()
    {
        var bytes = DiagnosticBundleBuilder.Build(SampleInput(), new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();

        Assert.Contains("README.txt", names);
        Assert.Contains("info.json", names);
        Assert.Contains("config/settings.json", names);
        Assert.Contains("config/servers.json", names);
        Assert.Contains("config/subscriptions.json", names);
        Assert.Contains("logs/backend.log", names);
        Assert.Contains("crash/crash-host-1.txt", names);
        Assert.Contains("diagnostics.json", names);
        Assert.Contains("client/client.json", names);
    }

    [Fact]
    public void Build_LeaksNoPlantedSecretAnywhere()
    {
        var bytes = DiagnosticBundleBuilder.Build(SampleInput(), new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var text = reader.ReadToEnd();
            Assert.DoesNotContain(PlantedSecret, text);
        }
    }

    [Fact]
    public void Build_TruncatesLogsToCap()
    {
        // 6000 lines, each ~40 bytes → well over the 20 MB cap is not reached, but the 5000-line cap is.
        var logs = Enumerable.Range(0, 6000).Select(i => $"line {i}").ToList();
        var bytes = DiagnosticBundleBuilder.Build(SampleInput(logs), new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("logs/backend.log")!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd();
        var lineCount = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.True(lineCount <= 5000, $"expected <= 5000 lines, got {lineCount}");
        Assert.Contains("line 5999", text);
        Assert.DoesNotContain("line 0\n", text);
    }
}
