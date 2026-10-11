using Netch.Core.Services.Launch;
using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests;

public class LaunchTargetValidatorTests
{
    private const string ErrorMessage = "启动方式无效：只支持 Steam/Epic 启动链接或本机 .exe 路径";

    private static bool Yes(string _) => true;
    private static bool No(string _) => false;

    // ---------- Normalize: accepted forms ----------

    [Theory]
    [InlineData("steam://rungameid/730")]
    [InlineData("steam://rungameid/1")]
    [InlineData("steam://rungameid/1234567890")]
    public void Normalize_AcceptsSteamLinks(string target)
    {
        Assert.Equal(target, LaunchTargetValidator.Normalize(target, Yes));
    }

    [Fact]
    public void Normalize_AcceptsEpicLink()
    {
        const string target = "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true";
        Assert.Equal(target, LaunchTargetValidator.Normalize(target, Yes));
    }

    [Fact]
    public void Normalize_AcceptsEpicLinkWithEncodedAppId()
    {
        const string target = "com.epicgames.launcher://apps/My%20Game:Season_2?action=launch&silent=true";
        Assert.Equal(target, LaunchTargetValidator.Normalize(target, Yes));
    }

    [Fact]
    public void Normalize_AcceptsExistingExePath()
    {
        const string target = @"C:\Games\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe";
        Assert.Equal(target, LaunchTargetValidator.Normalize(target, Yes));
    }

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        Assert.Equal("steam://rungameid/730", LaunchTargetValidator.Normalize("  steam://rungameid/730  ", Yes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrEmpty_ReturnsNull(string? raw)
    {
        Assert.Null(LaunchTargetValidator.Normalize(raw, Yes));
    }

    // ---------- Normalize: rejected forms ----------

    [Theory]
    [InlineData("http://example.com/game")]
    [InlineData("https://store.steampowered.com/app/730")]
    [InlineData("steam://run/730")]
    [InlineData("steam://rungameid/12a")]
    [InlineData("steam://rungameid/")]
    [InlineData("steam://rungameid/12345678901")] // 11 digits
    [InlineData(@"\\server\share\game.exe")] // UNC
    [InlineData(@"relative\path\game.exe")] // relative
    [InlineData("game.exe")] // bare file name
    [InlineData(@"C:\Games\launcher.bat")] // not .exe
    [InlineData("com.epicgames.launcher://apps/has space?action=launch&silent=true")]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch")] // wrong suffix
    public void Normalize_RejectsInvalidTargets(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LaunchTargetValidator.Normalize(raw, Yes));
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void Normalize_RejectsNonExistingExe()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LaunchTargetValidator.Normalize(@"C:\Games\missing.exe", No));
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void Normalize_RejectsPathWithQuotes()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LaunchTargetValidator.Normalize("\"C:\\Games\\game.exe\"", Yes));
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void Normalize_RejectsPathWithNewline()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LaunchTargetValidator.Normalize("C:\\Games\\game.exe\n", Yes));
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void Normalize_RejectsTooLongPath()
    {
        // 261 characters total: "C:\" + 254 'a' + ".exe".
        var path = @"C:\" + new string('a', 254) + ".exe";
        Assert.Equal(261, path.Length);
        var ex = Assert.Throws<InvalidOperationException>(() => LaunchTargetValidator.Normalize(path, Yes));
        Assert.Equal(ErrorMessage, ex.Message);
    }

    [Fact]
    public void Normalize_AcceptsPathAtMaxLength()
    {
        // Exactly 260 characters: "C:\" + 253 'a' + ".exe".
        var path = @"C:\" + new string('a', 253) + ".exe";
        Assert.Equal(260, path.Length);
        Assert.Equal(path, LaunchTargetValidator.Normalize(path, Yes));
    }

    // ---------- SuggestFrom ----------

    [Fact]
    public void SuggestFrom_SteamSource_SuggestsRungameidLink()
    {
        var installed = new InstalledGame("Steam", "steam:730", "Counter-Strike 2", null, new[] { "cs2.exe" });
        Assert.Equal(new[] { "steam://rungameid/730" }, LaunchTargetValidator.SuggestFrom(installed, Yes));
    }

    [Fact]
    public void SuggestFrom_SteamSourceWithInvalidAppId_SuggestsNothing()
    {
        var installed = new InstalledGame("Steam", "steam:abc", "Weird", null, new[] { "x.exe" });
        Assert.Empty(LaunchTargetValidator.SuggestFrom(installed, Yes));
    }

    [Fact]
    public void SuggestFrom_EpicSource_SuggestsEpicLink()
    {
        var installed = new InstalledGame("Epic", "epic:Fortnite", "Fortnite", null, new[] { "Fortnite.exe" });
        Assert.Equal(
            new[] { "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true" },
            LaunchTargetValidator.SuggestFrom(installed, Yes));
    }

    [Fact]
    public void SuggestFrom_OtherSource_SuggestsExistingExePathsOnly()
    {
        var dir = Path.Combine("C:", "Games", "Riot Games");
        var installed = new InstalledGame("Riot", "uninstall:Riot", "VALORANT", dir,
            new[] { "VALORANT-Win64.exe", "missing.exe", "readme.txt" });

        bool FileExists(string p) => p == Path.Combine(dir, "VALORANT-Win64.exe");

        Assert.Equal(
            new[] { Path.Combine(dir, "VALORANT-Win64.exe") },
            LaunchTargetValidator.SuggestFrom(installed, FileExists));
    }

    [Fact]
    public void SuggestFrom_OtherSourceWithoutInstallDir_SuggestsNothing()
    {
        var installed = new InstalledGame("其他", "uninstall:x", "Some Game", null, new[] { "game.exe" });
        Assert.Empty(LaunchTargetValidator.SuggestFrom(installed, Yes));
    }

    [Fact]
    public void SuggestFrom_OtherSourceWithNoExistingExe_SuggestsNothing()
    {
        var installed = new InstalledGame("其他", "uninstall:x", "Some Game", @"C:\Games\X", new[] { "game.exe" });
        Assert.Empty(LaunchTargetValidator.SuggestFrom(installed, No));
    }
}
