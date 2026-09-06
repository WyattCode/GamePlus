using Netch.Core.Models;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

/// <summary>
///     First-run seed data. Servers/subscriptions start empty — the user adds their own real ones
///     (Add dialog, clipboard import, or subscription). Only the useful mode templates are seeded.
/// </summary>
public static class SampleData
{
    public static IEnumerable<Server> CreateServers() => Enumerable.Empty<Server>();

    public static IEnumerable<Mode> CreateModes()
    {
        var process = new Mode { Remark = "游戏与浏览器", Type = ModeType.ProcessMode };
        foreach (var p in new[] { "chrome.exe", "msedge.exe", "Discord.exe", "steam.exe" })
            process.Handle.Add(p);
        yield return process;

        var global = new Mode { Remark = "全局 (TUN)", Type = ModeType.TunMode };
        global.Handle.Add("0.0.0.0/0");
        global.Bypass.Add("192.168.0.0/16");
        yield return global;

        var bypassCn = new Mode { Remark = "绕过中国大陆", Type = ModeType.TunMode };
        bypassCn.Handle.Add("0.0.0.0/0");
        foreach (var c in new[] { "geoip:cn", "geoip:private" })
            bypassCn.Bypass.Add(c);
        yield return bypassCn;

        var share = new Mode { Remark = "局域网共享", Type = ModeType.ShareMode };
        yield return share;
    }

    public static IEnumerable<Subscription> CreateSubscriptions() => Enumerable.Empty<Subscription>();
}
