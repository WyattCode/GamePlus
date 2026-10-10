using Netch.Core.Models.Games;
using Netch.Core.Services;

namespace Netch.Tests;

public class EffectProbeTargetTests
{
    private static Game Game(params string[] domains) =>
        new() { Id = "g", Name = "G", ProxyDomains = domains.ToList() };

    [Fact]
    public void Resolve_StripsDomainPrefix()
    {
        var target = EffectProbeTarget.Resolve(Game("domain:game.example.com"));
        Assert.NotNull(target);
        Assert.Equal("game.example.com", target!.Value.Host);
        Assert.Equal(443, target.Value.Port);
    }

    [Fact]
    public void Resolve_StripsFullAndWildcard()
    {
        Assert.Equal("a.example.com", EffectProbeTarget.Resolve(Game("full:a.example.com"))!.Value.Host);
        Assert.Equal("b.example.com", EffectProbeTarget.Resolve(Game("*.b.example.com"))!.Value.Host);
        Assert.Equal("c.example.com", EffectProbeTarget.Resolve(Game(".c.example.com"))!.Value.Host);
    }

    [Fact]
    public void Resolve_SkipsRegexpKeywordAndWildcards()
    {
        var target = EffectProbeTarget.Resolve(Game("regexp:.*\\.example\\.com", "keyword:game", "wild*.example.com", "good.example.com"));
        Assert.NotNull(target);
        Assert.Equal("good.example.com", target!.Value.Host);
    }

    [Fact]
    public void Resolve_CidrUsesNetworkPlusOne()
    {
        var game = new Game { Id = "g", Name = "G", ProxyIps = new List<string> { "1.2.3.0/24" } };
        var target = EffectProbeTarget.Resolve(game);
        Assert.NotNull(target);
        Assert.Equal("1.2.3.1", target!.Value.Host);
        Assert.Equal(443, target.Value.Port);
    }

    [Fact]
    public void Resolve_PlainIp()
    {
        var game = new Game { Id = "g", Name = "G", ProxyIps = new List<string> { "5.6.7.8" } };
        Assert.Equal("5.6.7.8", EffectProbeTarget.Resolve(game)!.Value.Host);
    }

    [Fact]
    public void Resolve_NoRules_ReturnsNull()
    {
        Assert.Null(EffectProbeTarget.Resolve(new Game { Id = "g", Name = "G" }));
    }
}
