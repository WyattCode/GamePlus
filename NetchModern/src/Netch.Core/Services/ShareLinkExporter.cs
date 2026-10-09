using System.Text.Json.Nodes;
using Netch.Core.Models.Servers;
using Netch.Core.Utils;

namespace Netch.Core.Services;

/// <summary>
///     Serialises a <see cref="Server" /> back into a share link — the inverse of <see cref="ShareLinkParser" />.
///     Round-trips with the parser for every supported protocol.
/// </summary>
public static class ShareLinkExporter
{
    public static string? ToShareLink(Server server) => server switch
    {
        ShadowsocksServer ss => Shadowsocks(ss),
        TrojanServer tr => Trojan(tr),
        VMessServer vm => VMess(vm),
        VlessServer vl => Vless(vl),
        Socks5Server s5 => Socks(s5),
        Hysteria2Server hy2 => Hysteria2(hy2),
        WireGuardServer wg => WireGuard(wg),
        _ => null
    };

    private static string Hysteria2(Hysteria2Server s)
    {
        var query = string.Empty;
        if (!string.IsNullOrEmpty(s.Sni))
            query += $"{(query.Length == 0 ? "?" : "&")}sni={Uri.EscapeDataString(s.Sni)}";
        if (s.AllowInsecure)
            query += $"{(query.Length == 0 ? "?" : "&")}insecure=1";
        if (!string.IsNullOrEmpty(s.ObfsPassword))
            query += $"{(query.Length == 0 ? "?" : "&")}obfs=salamander&obfs-password={Uri.EscapeDataString(s.ObfsPassword)}";
        if (s.UpMbps > 0)
            query += $"{(query.Length == 0 ? "?" : "&")}up={s.UpMbps}";
        if (s.DownMbps > 0)
            query += $"{(query.Length == 0 ? "?" : "&")}down={s.DownMbps}";

        return $"hysteria2://{Uri.EscapeDataString(s.Password)}@{s.Hostname}:{s.Port}{query}{Fragment(s.Remark)}";
    }

    private static string WireGuard(WireGuardServer s)
    {
        var query = $"?publickey={Uri.EscapeDataString(s.PublicKey)}";
        if (!string.IsNullOrEmpty(s.LocalAddresses))
            query += $"&address={Uri.EscapeDataString(s.LocalAddresses)}";
        if (s.Mtu > 0 && s.Mtu != 1420)
            query += $"&mtu={s.Mtu}";
        if (!string.IsNullOrEmpty(s.Reserved))
            query += $"&reserved={Uri.EscapeDataString(s.Reserved)}";
        if (!string.IsNullOrEmpty(s.PreSharedKey))
            query += $"&presharedkey={Uri.EscapeDataString(s.PreSharedKey)}";

        return $"wireguard://{Uri.EscapeDataString(s.PrivateKey)}@{s.Hostname}:{s.Port}{query}{Fragment(s.Remark)}";
    }

    private static string Fragment(string remark)
        => string.IsNullOrEmpty(remark) ? string.Empty : "#" + Uri.EscapeDataString(remark);

    private static string Shadowsocks(ShadowsocksServer s)
    {
        var userInfo = ShareLinkUtils.Base64UrlEncode($"{s.EncryptMethod}:{s.Password}");
        var query = string.Empty;
        if (!string.IsNullOrEmpty(s.Plugin))
        {
            var plugin = s.Plugin;
            if (!string.IsNullOrEmpty(s.PluginOption))
                plugin += ";" + s.PluginOption;
            query = "?plugin=" + Uri.EscapeDataString(plugin);
        }

        return $"ss://{userInfo}@{s.Hostname}:{s.Port}{query}{Fragment(s.Remark)}";
    }

    private static string Trojan(TrojanServer s)
    {
        var query = $"?sni={Uri.EscapeDataString(string.IsNullOrEmpty(s.Sni) ? s.Hostname : s.Sni)}";
        if (s.AllowInsecure)
            query += "&allowInsecure=1";
        if (!string.IsNullOrEmpty(s.Network) && !string.Equals(s.Network, "tcp", StringComparison.OrdinalIgnoreCase))
            query += $"&type={Uri.EscapeDataString(s.Network)}";
        if (!string.IsNullOrEmpty(s.Host))
            query += $"&host={Uri.EscapeDataString(s.Host)}";
        if (!string.IsNullOrEmpty(s.Path))
            query += $"&path={Uri.EscapeDataString(s.Path)}";
        if (!string.IsNullOrEmpty(s.ServiceName))
            query += $"&serviceName={Uri.EscapeDataString(s.ServiceName)}";
        if (!string.IsNullOrEmpty(s.Mode))
            query += $"&mode={Uri.EscapeDataString(s.Mode)}";
        if (!string.IsNullOrEmpty(s.Alpn))
            query += $"&alpn={Uri.EscapeDataString(s.Alpn)}";
        if (!string.IsNullOrEmpty(s.Fingerprint))
            query += $"&fp={Uri.EscapeDataString(s.Fingerprint)}";

        return $"trojan://{Uri.EscapeDataString(s.Password)}@{s.Hostname}:{s.Port}{query}{Fragment(s.Remark)}";
    }

    private static string VMess(VMessServer s)
    {
        var json = new JsonObject
        {
            ["v"] = "2",
            ["ps"] = s.Remark,
            ["add"] = s.Hostname,
            ["port"] = s.Port.ToString(),
            ["id"] = s.UserId,
            ["aid"] = s.AlterId.ToString(),
            ["scy"] = s.Security,
            ["net"] = s.Network,
            ["tls"] = s.Tls ? "tls" : string.Empty
        };

        if (!string.IsNullOrEmpty(s.Host))
            json["host"] = s.Host;
        if (!string.IsNullOrEmpty(s.Path))
            json["path"] = s.Path;
        if (!string.IsNullOrEmpty(s.Sni))
            json["sni"] = s.Sni;
        if (!string.IsNullOrEmpty(s.Alpn))
            json["alpn"] = s.Alpn;
        if (!string.IsNullOrEmpty(s.Fingerprint))
            json["fp"] = s.Fingerprint;
        if (!string.IsNullOrEmpty(s.Mode))
            json["type"] = s.Mode;

        return "vmess://" + ShareLinkUtils.Base64UrlEncode(json.ToJsonString());
    }

    private static string Vless(VlessServer s)
    {
        var query = $"?type={Uri.EscapeDataString(s.Network)}&security={Uri.EscapeDataString(s.Security)}";
        if (!string.IsNullOrEmpty(s.Sni))
            query += $"&sni={Uri.EscapeDataString(s.Sni)}";
        if (!string.IsNullOrEmpty(s.Flow))
            query += $"&flow={Uri.EscapeDataString(s.Flow)}";
        if (!string.IsNullOrEmpty(s.PublicKey))
            query += $"&pbk={Uri.EscapeDataString(s.PublicKey)}";
        if (!string.IsNullOrEmpty(s.ShortId))
            query += $"&sid={Uri.EscapeDataString(s.ShortId)}";
        if (!string.IsNullOrEmpty(s.SpiderX))
            query += $"&spx={Uri.EscapeDataString(s.SpiderX)}";
        if (!string.IsNullOrEmpty(s.Fingerprint))
            query += $"&fp={Uri.EscapeDataString(s.Fingerprint)}";
        if (!string.IsNullOrEmpty(s.Host))
            query += $"&host={Uri.EscapeDataString(s.Host)}";
        if (!string.IsNullOrEmpty(s.Path))
            query += $"&path={Uri.EscapeDataString(s.Path)}";
        if (!string.IsNullOrEmpty(s.ServiceName))
            query += $"&serviceName={Uri.EscapeDataString(s.ServiceName)}";
        if (!string.IsNullOrEmpty(s.Mode))
            query += $"&mode={Uri.EscapeDataString(s.Mode)}";
        if (!string.IsNullOrEmpty(s.Alpn))
            query += $"&alpn={Uri.EscapeDataString(s.Alpn)}";
        if (s.AllowInsecure)
            query += "&allowInsecure=1";

        return $"vless://{s.UserId}@{s.Hostname}:{s.Port}{query}{Fragment(s.Remark)}";
    }

    private static string Socks(Socks5Server s)
    {
        var link = $"https://t.me/socks?server={s.Hostname}&port={s.Port}";
        if (s.Auth())
            link += $"&user={Uri.EscapeDataString(s.Username!)}&pass={Uri.EscapeDataString(s.Password!)}";

        return link;
    }
}
