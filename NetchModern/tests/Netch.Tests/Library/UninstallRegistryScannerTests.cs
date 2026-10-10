using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class UninstallRegistryScannerTests
{
    private sealed class FakeSource : IUninstallSource
    {
        private readonly List<UninstallEntry> _entries;

        public FakeSource(params UninstallEntry[] entries) => _entries = entries.ToList();

        public IEnumerable<UninstallEntry> Read() => _entries;
    }

    [Fact]
    public void KeepsBlizzardGameAsBattleNet()
    {
        var source = new FakeSource(new UninstallEntry(
            "Overwatch", "Overwatch", "Blizzard Entertainment", null, null));

        var games = new UninstallRegistryScanner(source).Scan(CancellationToken.None).ToList();

        Assert.Single(games);
        Assert.Equal("Battle.net", games[0].Source);
        Assert.Equal("uninstall:Overwatch", games[0].SourceId);
    }

    [Fact]
    public void SkipsLauncherApps()
    {
        var source = new FakeSource(
            new UninstallEntry("Battle.net", "Battle.net", "Blizzard Entertainment", null, null),
            new UninstallEntry("EA", "EA app", "Electronic Arts", null, null),
            new UninstallEntry("Ubi", "Ubisoft Connect", "Ubisoft", null, null),
            new UninstallEntry("Riot", "Riot Client", "Riot Games", null, null));

        var games = new UninstallRegistryScanner(source).Scan(CancellationToken.None).ToList();
        Assert.Empty(games);
    }

    [Fact]
    public void SkipsUnknownPublisher()
    {
        var source = new FakeSource(new UninstallEntry("Foo", "Foo App", "Foo Corp", null, null));
        Assert.Empty(new UninstallRegistryScanner(source).Scan(CancellationToken.None));
    }

    [Fact]
    public void SkipsTencentNonGames()
    {
        var source = new FakeSource(
            new UninstallEntry("QQ", "QQ", "Tencent", null, null),
            new UninstallEntry("WeChat", "微信", "Tencent", null, null),
            new UninstallEntry("Meeting", "腾讯会议", "Tencent", null, null),
            new UninstallEntry("Guard", "电脑管家", "Tencent", null, null));

        Assert.Empty(new UninstallRegistryScanner(source).Scan(CancellationToken.None));
    }

    [Fact]
    public void ParsesDisplayIconExe()
    {
        var source = new FakeSource(new UninstallEntry(
            "Overwatch", "Overwatch", "Blizzard Entertainment", null, "\"C:\\Games\\Overwatch\\Overwatch.exe\",0"));

        var games = new UninstallRegistryScanner(source).Scan(CancellationToken.None).ToList();

        Assert.Single(games);
        Assert.Contains("Overwatch.exe", games[0].Executables);
    }

    [Fact]
    public void KeepsTencentGame()
    {
        var source = new FakeSource(new UninstallEntry(
            "LOL", "英雄联盟", "Tencent", null, null));

        var games = new UninstallRegistryScanner(source).Scan(CancellationToken.None).ToList();

        Assert.Single(games);
        Assert.Equal("其他", games[0].Source);
    }
}
