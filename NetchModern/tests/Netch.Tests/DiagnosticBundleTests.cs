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

    [Fact]
    public void LogSanitizer_ReplacesLongestHostFirst()
    {
        var sanitized = LogSanitizer.Sanitize(
            "via a.b.example.com and b.example.com done",
            new[] { "b.example.com", "a.b.example.com" });

        Assert.Equal("via <node> and <node> done", sanitized);
        Assert.DoesNotContain("a.<node>", sanitized);
        Assert.DoesNotContain("example.com", sanitized);
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

    private static string ReadEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

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

    [Fact]
    public void Build_ReplacesSensitiveHostsInLogs()
    {
        var logs = new List<string>
        {
            "System-proxy routing 3 domain(s) via jp1.example.net",
            "Capturing 2 IP rule(s) via TUN → 203.0.113.7"
        };
        var input = SampleInput(logs) with { SensitiveHosts = new[] { "jp1.example.net", "203.0.113.7" } };
        var bytes = DiagnosticBundleBuilder.Build(input, new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var text = ReadEntry(zip, "logs/backend.log");

        Assert.DoesNotContain("jp1.example.net", text);
        Assert.DoesNotContain("203.0.113.7", text);
        Assert.Contains("<node>", text);
    }

    [Fact]
    public void Build_RedactsEmbeddedUrlsInClientJson()
    {
        var input = SampleInput() with
        {
            Client = JsonNode.Parse("""{ "message": "失败：https://sub.example.com/p?token=abc" }""")
        };
        var bytes = DiagnosticBundleBuilder.Build(input, new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var text = ReadEntry(zip, "client/client.json");

        Assert.DoesNotContain("token=abc", text);
        Assert.DoesNotContain("/p?", text);
    }

    [Fact]
    public void Build_RedactsSensitiveHostsInSettingsJson()
    {
        var input = SampleInput() with
        {
            Settings = JsonNode.Parse("""{ "proxy": { "endpoint": "203.0.113.7:8080" } }"""),
            SensitiveHosts = new[] { "203.0.113.7" }
        };
        var bytes = DiagnosticBundleBuilder.Build(input, new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var text = ReadEntry(zip, "config/settings.json");

        Assert.DoesNotContain("203.0.113.7", text);
        Assert.Contains("<node>:8080", text);
    }

    [Fact]
    public void Build_RedactsSensitiveHostsInDiagnosticsJson()
    {
        var input = SampleInput() with
        {
            Diagnostics = new { route = "selected via 198.51.100.5" },
            SensitiveHosts = new[] { "198.51.100.5" }
        };
        var bytes = DiagnosticBundleBuilder.Build(input, new DateTime(2025, 1, 2, 3, 4, 5));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var text = ReadEntry(zip, "diagnostics.json");

        Assert.DoesNotContain("198.51.100.5", text);
        Assert.Contains("<node>", text);
    }
}
