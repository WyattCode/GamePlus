using Netch.Core.Services.Library;
using Xunit;

namespace Netch.Tests.Library;

public class VdfParserTests
{
    [Fact]
    public void ParsesNestedObjects()
    {
        const string text = """
            "libraryfolders"
            {
                "0"
                {
                    "path" "C:\\Program Files (x86)\\Steam"
                    "label" ""
                }
                "1"
                {
                    "path" "D:\\SteamLibrary"
                }
            }
            """;

        var root = VdfParser.Parse(text);
        var libs = root.GetNode("libraryfolders");
        Assert.NotNull(libs);
        Assert.Equal(2, libs!.Count);

        var first = libs.GetNode("0");
        Assert.NotNull(first);
        Assert.Equal(@"C:\Program Files (x86)\Steam", first!.GetString("path"));
        Assert.Equal("", first.GetString("label"));

        var second = libs.GetNode("1");
        Assert.NotNull(second);
        Assert.Equal(@"D:\SteamLibrary", second!.GetString("path"));
    }

    [Fact]
    public void SkipsComments()
    {
        const string text = """
            // top comment
            "AppState"
            {
                "appid" "730" // trailing comment
                "name" "Counter-Strike 2"
            }
            """;

        var root = VdfParser.Parse(text);
        var state = root.GetNode("AppState");
        Assert.NotNull(state);
        Assert.Equal("730", state!.GetString("appid"));
        Assert.Equal("Counter-Strike 2", state.GetString("name"));
    }

    [Fact]
    public void HandlesEscapes()
    {
        const string text = """
            "root"
            {
                "quoted" "a \"b\" c"
                "backslash" "C:\\Games"
            }
            """;

        var root = VdfParser.Parse(text);
        var node = root.GetNode("root");
        Assert.NotNull(node);
        Assert.Equal("a \"b\" c", node!.GetString("quoted"));
        Assert.Equal(@"C:\Games", node.GetString("backslash"));
    }

    [Fact]
    public void ParsesOldLibraryFoldersFormat()
    {
        const string text = """
            "LibraryFolders"
            {
                "TimeNextStatsReport" "1234567890"
                "ContentStatsID" "-1234567890"
                "1" "D:\\SteamLibrary"
                "2" "E:\\Games\\Steam"
            }
            """;

        var root = VdfParser.Parse(text);
        var libs = root.GetNode("LibraryFolders");
        Assert.NotNull(libs);
        Assert.Equal(@"D:\SteamLibrary", libs!.GetString("1"));
        Assert.Equal(@"E:\Games\Steam", libs.GetString("2"));
    }

    [Fact]
    public void EmptyInputReturnsEmptyRoot()
    {
        var root = VdfParser.Parse("");
        Assert.Equal(0, root.Count);
    }
}
