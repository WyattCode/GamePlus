using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Netch.Core.Models.Servers;
using Netch.Core.Utils;

namespace Netch.Core.Services;

/// <summary>
///     Parses share links / subscription payloads into <see cref="Server" /> models.
///     Ported from the original Netch per-protocol <c>*Util.ParseUri</c> logic, BCL-only.
///     Supports: ss://, trojan://, vmess://, vless://, wireguard://, wg://, and Telegram socks links.
/// </summary>
public static partial class ShareLinkParser
{
    /// <summary>
    ///     Parses free-form text: a single link, many newline-separated links, or a Base64-encoded
    ///     subscription blob wrapping such lines.
    /// </summary>
    public static IReadOnlyList<Server> ParseText(string text)
    {
        var results = new List<Server>();
        if (string.IsNullOrWhiteSpace(text))
            return results;

        text = text.Trim();

        // Subscription bodies are often a Base64 blob of newline-separated links.
        if (!text.Contains("://") && ShareLinkUtils.TryBase64UrlDecode(text, out var decoded) && decoded.Contains("://"))
            text = decoded;

        foreach (var line in text.GetLines())
        {
            try
            {
                var server = ParseUri(line);
                if (server != null)
                    results.Add(server);
            }
            catch
            {
                // Skip malformed lines; a bad entry shouldn't abort the whole import.
            }
        }

        return results;
    }

    public static Server? ParseUri(string text)
    {
        text = text.Trim();

        if (text.StartsWith("tg://socks?") || text.StartsWith("https://t.me/socks?"))
            return ParseSocks(text);
        if (text.StartsWith("ss://"))
            return ParseShadowsocks(text);
        if (text.StartsWith("trojan://"))
            return ParseTrojan(text);
        if (text.StartsWith("vmess://"))
            return ParseVMess(text);
        if (text.StartsWith("vless://"))
            return ParseVless(text);
        if (text.StartsWith("hysteria2://") || text.StartsWith("hy2://"))
            return ParseHysteria2(text);
        if (text.StartsWith("wireguard://") || text.StartsWith("wg://"))
            return ParseWireGuard(text);

        return null;
    }

    /// <summary>hysteria2://password@host:port/?sni=&amp;insecure=1&amp;obfs=salamander&amp;obfs-password=#remark</summary>
    private static Hysteria2Server ParseHysteria2(string text)
    {
        var (body, remark) = SplitFragment(text);
        var data = new Hysteria2Server();
        if (remark != null)
            data.Remark = remark;

        body = body.Replace("hysteria2://", string.Empty).Replace("hy2://", string.Empty);

        var qIndex = body.IndexOf('?');
        var query = qIndex >= 0 ? ShareLinkUtils.ParseQuery(body[(qIndex + 1)..]) : new Dictionary<string, string>();
        if (qIndex >= 0)
            body = body[..qIndex];
        body = body.TrimEnd('/');

        var atIndex = body.LastIndexOf('@');
        if (atIndex < 0)
            throw new FormatException("Invalid hysteria2 link");

        data.Password = ShareLinkUtils.UrlDecode(body[..atIndex]);

        var hostPort = body[(atIndex + 1)..];
        var colon = hostPort.LastIndexOf(':');
        if (colon < 0)
            throw new FormatException("Invalid hysteria2 host:port");

        data.Hostname = hostPort[..colon];
        data.Port = ushort.Parse(hostPort[(colon + 1)..]);

        data.Sni = query.GetValueOrDefault("sni") ?? query.GetValueOrDefault("peer");
        var insecure = query.GetValueOrDefault("insecure");
        data.AllowInsecure = insecure is "1" || string.Equals(insecure, "true", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(query.GetValueOrDefault("obfs"), "salamander", StringComparison.OrdinalIgnoreCase))
            data.ObfsPassword = query.GetValueOrDefault("obfs-password");

        // Optional bandwidth hints (Mbps) — presence enables the Brutal congestion controller.
        // Accept both the short (up/down) and explicit (upmbps/downmbps) spellings.
        if (int.TryParse(query.GetValueOrDefault("up") ?? query.GetValueOrDefault("upmbps"), out var up) && up > 0)
            data.UpMbps = up;
        if (int.TryParse(query.GetValueOrDefault("down") ?? query.GetValueOrDefault("downmbps"), out var down) && down > 0)
            data.DownMbps = down;

        return data;
    }

    /// <summary>
    ///     wireguard://&lt;url-encoded private key&gt;@host:port?publickey=..&amp;address=10.0.0.2/32,fd00::2/128
    ///     &amp;mtu=1420&amp;reserved=1,2,3&amp;presharedkey=..#remark
    /// </summary>
    private static WireGuardServer ParseWireGuard(string text)
    {
        var (body, remark) = SplitFragment(text);
        var data = new WireGuardServer();
        if (remark != null)
            data.Remark = remark;

        body = body.Replace("wireguard://", string.Empty).Replace("wg://", string.Empty);

        var qIndex = body.IndexOf('?');
        var query = qIndex >= 0 ? ShareLinkUtils.ParseQuery(body[(qIndex + 1)..]) : new Dictionary<string, string>();
        if (qIndex >= 0)
            body = body[..qIndex];
        body = body.TrimEnd('/');

        var atIndex = body.LastIndexOf('@');
        if (atIndex < 0)
            throw new FormatException("Invalid wireguard link");

        // WireGuard keys are standard base64 and many share links leave '+' unescaped. UrlDecode turns a
        // literal '+' into a space, which is never valid base64 — restore it.
        data.PrivateKey = ShareLinkUtils.UrlDecode(body[..atIndex]).Replace(' ', '+');

        var hostPort = body[(atIndex + 1)..];
        var colon = hostPort.LastIndexOf(':');
        if (colon < 0)
            throw new FormatException("Invalid wireguard host:port");

        data.Hostname = hostPort[..colon];
        data.Port = ushort.Parse(hostPort[(colon + 1)..]);

        data.PublicKey = (query.GetValueOrDefault("publickey")
                          ?? query.GetValueOrDefault("public_key")
                          ?? query.GetValueOrDefault("pk")
                          ?? string.Empty).Replace(' ', '+');
        data.LocalAddresses = query.GetValueOrDefault("address")
                              ?? query.GetValueOrDefault("ip")
                              ?? string.Empty;
        var psk = query.GetValueOrDefault("presharedkey")
                  ?? query.GetValueOrDefault("preshared_key")
                  ?? query.GetValueOrDefault("psk");
        data.PreSharedKey = psk?.Replace(' ', '+');
        data.Reserved = query.GetValueOrDefault("reserved");
        if (int.TryParse(query.GetValueOrDefault("mtu"), out var mtu) && mtu > 0)
            data.Mtu = mtu;

        return data;
    }

    private static (string body, string? remark) SplitFragment(string text)
    {
        var hashIndex = text.IndexOf('#');
        if (hashIndex < 0)
            return (text, null);

        var remark = ShareLinkUtils.UrlDecode(text[(hashIndex + 1)..]);
        return (text[..hashIndex], remark);
    }

    #region Shadowsocks

    [GeneratedRegex(@"^ss://(?<base64>.+?)@(?<server>.+):(?<port>\d+)")]
    private static partial Regex SsUserInfoRegex();

    [GeneratedRegex(@"^(?<method>.+?):(?<password>.+)$")]
    private static partial Regex SsMethodPasswordRegex();

    [GeneratedRegex(@"^(?<method>.+?):(?<password>.+)@(?<server>.+):(?<port>\d+)$")]
    private static partial Regex SsFullBase64Regex();

    private static ShadowsocksServer ParseShadowsocks(string text)
    {
        var data = new ShadowsocksServer();
        text = text.Replace("/?", "?");

        var (body, remark) = SplitFragment(text);
        if (remark != null)
            data.Remark = remark;
        text = body;

        // Optional ?plugin=... query.
        var qIndex = text.IndexOf('?');
        if (qIndex >= 0)
        {
            var query = ShareLinkUtils.ParseQuery(text[(qIndex + 1)..]);
            if (query.TryGetValue("plugin", out var plugins) && plugins.Length > 0)
            {
                // A plugin may have no ";options" part (e.g. plugin=obfs-local) — keep the whole value as the name.
                var semi = plugins.IndexOf(';');
                data.Plugin = semi >= 0 ? plugins[..semi] : plugins;
                data.PluginOption = semi >= 0 ? plugins[(semi + 1)..] : string.Empty;
            }

            text = text[..qIndex];
        }

        if (text.Contains('@'))
        {
            // ss://base64(method:password)@host:port
            var match = SsUserInfoRegex().Match(text);
            if (!match.Success)
                throw new FormatException("Invalid ss link");

            data.Hostname = match.Groups["server"].Value;
            data.Port = ushort.Parse(match.Groups["port"].Value);

            var userInfo = ShareLinkUtils.Base64UrlDecode(match.Groups["base64"].Value);
            var mp = SsMethodPasswordRegex().Match(userInfo);
            if (!mp.Success)
                throw new FormatException("Invalid ss userinfo");

            data.EncryptMethod = mp.Groups["method"].Value;
            data.Password = mp.Groups["password"].Value;
        }
        else
        {
            // ss://base64(method:password@host:port)
            var decoded = ShareLinkUtils.Base64UrlDecode(text["ss://".Length..]);
            var match = SsFullBase64Regex().Match(decoded);
            if (!match.Success)
                throw new FormatException("Invalid ss link");

            data.Hostname = match.Groups["server"].Value;
            data.Port = ushort.Parse(match.Groups["port"].Value);
            data.EncryptMethod = match.Groups["method"].Value;
            data.Password = match.Groups["password"].Value;
        }

        return data;
    }

    #endregion

    #region Trojan

    [GeneratedRegex(@"^trojan://(?<psk>.+?)@(?<server>.+):(?<port>\d+)")]
    private static partial Regex TrojanRegex();

    private static TrojanServer ParseTrojan(string text)
    {
        var data = new TrojanServer();
        text = text.Replace("/?", "?");

        var (body, remark) = SplitFragment(text);
        if (remark != null)
            data.Remark = remark;
        text = body;

        var qIndex = text.IndexOf('?');
        if (qIndex >= 0)
        {
            var query = ShareLinkUtils.ParseQuery(text[(qIndex + 1)..]);
            if (query.TryGetValue("peer", out var peer) || query.TryGetValue("sni", out peer))
                data.Sni = peer;
            if (query.TryGetValue("allowInsecure", out var insecure) || query.TryGetValue("insecure", out insecure))
                data.AllowInsecure = insecure is "1" or "true";

            ApplyTransport(data, query, out var network);
            if (!string.IsNullOrEmpty(network))
                data.Network = network;

            text = text[..qIndex];
        }

        var match = TrojanRegex().Match(text);
        if (!match.Success)
            throw new FormatException("Invalid trojan link");

        data.Password = ShareLinkUtils.UrlDecode(match.Groups["psk"].Value);
        data.Hostname = match.Groups["server"].Value;
        data.Port = ushort.Parse(match.Groups["port"].Value);
        return data;
    }

    #endregion

    #region VMess

    private sealed class V2rayNJObject
    {
        public string ps { get; set; } = string.Empty;
        public string add { get; set; } = string.Empty;
        public ushort port { get; set; }
        public string id { get; set; } = string.Empty;
        public int aid { get; set; }
        public string scy { get; set; } = "auto";
        public string net { get; set; } = "tcp";
        public string? tls { get; set; }
        public string? sni { get; set; }
        public string? host { get; set; }
        public string? path { get; set; }
        public string? type { get; set; }
        public string? alpn { get; set; }
        public string? fp { get; set; }
    }

    private static readonly JsonSerializerOptions VmessJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
    };

    private static VMessServer ParseVMess(string text)
    {
        var json = ShareLinkUtils.Base64UrlDecode(text["vmess://".Length..]);
        var vmess = JsonSerializer.Deserialize<V2rayNJObject>(json, VmessJsonOptions)
                    ?? throw new FormatException("Invalid vmess json");

        return new VMessServer
        {
            Remark = vmess.ps,
            Hostname = vmess.add,
            Port = vmess.port,
            UserId = vmess.id,
            AlterId = vmess.aid,
            // An explicit "scy":null / "net":null in the JSON overrides the property initializer, so re-apply
            // the defaults here to avoid a null Security/Network (NRE-prone in export/MaskedData).
            Security = string.IsNullOrEmpty(vmess.scy) ? "auto" : vmess.scy,
            Network = string.IsNullOrEmpty(vmess.net) ? "tcp" : vmess.net,
            Tls = string.Equals(vmess.tls, "tls", StringComparison.OrdinalIgnoreCase),
            Sni = string.IsNullOrEmpty(vmess.sni) ? null : vmess.sni,
            Host = string.IsNullOrEmpty(vmess.host) ? null : vmess.host,
            Path = string.IsNullOrEmpty(vmess.path) ? null : vmess.path,
            ServiceName = string.IsNullOrEmpty(vmess.path) ? null : vmess.path,
            Mode = string.IsNullOrEmpty(vmess.type) ? null : vmess.type,
            Alpn = string.IsNullOrEmpty(vmess.alpn) ? null : vmess.alpn,
            Fingerprint = string.IsNullOrEmpty(vmess.fp) ? null : vmess.fp
        };
    }

    #endregion

    #region VLESS

    [GeneratedRegex(@"^(?<uuid>.+?)@(?<server>.+):(?<port>\d+)$")]
    private static partial Regex VlessUserInfoRegex();

    /// <summary>
    ///     Parses a vless link. Handles two shapes:
    ///     <list type="bullet">
    ///         <item>Standard: <c>vless://uuid@host:port?type=&amp;security=reality&amp;pbk=&amp;sni=&amp;flow=#remark</c></item>
    ///         <item>Legacy base64: <c>vless://base64(":uuid@host:port")?remarks=&amp;tls=1&amp;peer=&amp;pbk=&amp;xtls=2</c></item>
    ///     </list>
    ///     Both REALITY (pbk/sid/fp/spx) and plain TLS are supported.
    /// </summary>
    private static VlessServer ParseVless(string text)
    {
        var data = new VlessServer();

        var (body, remark) = SplitFragment(text);
        if (remark != null)
            data.Remark = remark;
        text = body;

        // Separate the query string from the userinfo/host part.
        Dictionary<string, string> query = new(StringComparer.OrdinalIgnoreCase);
        var qIndex = text.IndexOf('?');
        if (qIndex >= 0)
        {
            query = ShareLinkUtils.ParseQuery(text[(qIndex + 1)..]);
            text = text[..qIndex];
        }

        // Legacy links carry the name in a "remarks"/"remark" query param instead of a #fragment.
        if (string.IsNullOrEmpty(data.Remark) &&
            (query.TryGetValue("remarks", out var qRemark) || query.TryGetValue("remark", out qRemark)) &&
            !string.IsNullOrEmpty(qRemark))
            data.Remark = qRemark;

        var core = text["vless://".Length..];

        // Standard links carry a literal '@'; the legacy variant base64-encodes ":uuid@host:port".
        if (!core.Contains('@'))
            core = ShareLinkUtils.Base64UrlDecode(core);

        var match = VlessUserInfoRegex().Match(core);
        if (!match.Success)
            throw new FormatException("Invalid vless link");

        // The legacy encoder prefixes the uuid with an (empty) "method:" segment → strip up to the last ':'.
        var userInfo = ShareLinkUtils.UrlDecode(match.Groups["uuid"].Value);
        var colon = userInfo.LastIndexOf(':');
        data.UserId = colon >= 0 ? userInfo[(colon + 1)..] : userInfo;
        data.Hostname = match.Groups["server"].Value;
        data.Port = ushort.Parse(match.Groups["port"].Value);

        if (query.TryGetValue("type", out var net) || query.TryGetValue("net", out net))
            data.Network = net;

        // SNI: "sni" (standard) or "peer" (legacy).
        if (query.TryGetValue("sni", out var sni) || query.TryGetValue("peer", out sni))
            data.Sni = sni;

        // REALITY fields.
        if (query.TryGetValue("pbk", out var pbk) && !string.IsNullOrEmpty(pbk))
            data.PublicKey = pbk;
        if (query.TryGetValue("sid", out var sid))
            data.ShortId = sid;
        if (query.TryGetValue("spx", out var spx))
            data.SpiderX = spx;
        if (query.TryGetValue("fp", out var fp))
            data.Fingerprint = fp;

        // Security: explicit "security" wins; otherwise infer from pbk / tls flags.
        if (query.TryGetValue("security", out var sec) && !string.IsNullOrEmpty(sec))
            data.Security = sec;
        else if (!string.IsNullOrEmpty(data.PublicKey))
            data.Security = "reality";
        else if (query.TryGetValue("tls", out var tls) && tls is "1" or "tls" or "true")
            data.Security = "tls";

        // Flow: explicit "flow", else the legacy numeric "xtls" (>0 → vision, the only modern flow).
        if (query.TryGetValue("flow", out var flow) && !string.IsNullOrEmpty(flow))
            data.Flow = flow;
        else if (query.TryGetValue("xtls", out var xtls) && xtls is not ("" or "0"))
            data.Flow = "xtls-rprx-vision";

        ApplyTransport(data, query, out _);

        if (query.TryGetValue("allowInsecure", out var insecure) || query.TryGetValue("insecure", out insecure))
            data.AllowInsecure = insecure is "1" or "true";

        return data;
    }

    #endregion

    #region Transport helpers

    /// <summary>
    ///     Copies the shared transport query params (host/path/serviceName/mode/alpn/fp) onto the model.
    ///     Returns the network via <paramref name="network" /> when the link specifies one.
    /// </summary>
    private static void ApplyTransport(Server server, Dictionary<string, string> query, out string? network)
    {
        network = null;

        if (query.TryGetValue("type", out var net) || query.TryGetValue("net", out net))
            network = net;

        var host = query.GetValueOrDefault("host");
        var path = query.GetValueOrDefault("path");
        var serviceName = query.GetValueOrDefault("serviceName") ?? query.GetValueOrDefault("servicename");
        var mode = query.GetValueOrDefault("mode");
        var alpn = query.GetValueOrDefault("alpn");
        var fp = query.GetValueOrDefault("fp");

        switch (server)
        {
            case VMessServer vm:
                if (!string.IsNullOrEmpty(host)) vm.Host = host;
                if (!string.IsNullOrEmpty(path)) vm.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) vm.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(mode)) vm.Mode = mode;
                if (!string.IsNullOrEmpty(alpn)) vm.Alpn = alpn;
                if (!string.IsNullOrEmpty(fp)) vm.Fingerprint = fp;
                break;
            case VlessServer vl:
                if (!string.IsNullOrEmpty(host)) vl.Host = host;
                if (!string.IsNullOrEmpty(path)) vl.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) vl.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(mode)) vl.Mode = mode;
                if (!string.IsNullOrEmpty(alpn)) vl.Alpn = alpn;
                if (!string.IsNullOrEmpty(fp)) vl.Fingerprint = fp;
                break;
            case TrojanServer tr:
                if (!string.IsNullOrEmpty(host)) tr.Host = host;
                if (!string.IsNullOrEmpty(path)) tr.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) tr.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(mode)) tr.Mode = mode;
                if (!string.IsNullOrEmpty(alpn)) tr.Alpn = alpn;
                if (!string.IsNullOrEmpty(fp)) tr.Fingerprint = fp;
                break;
        }
    }

    #endregion

    #region Socks

    private static Socks5Server ParseSocks(string text)
    {
        var qIndex = text.IndexOf('?');
        var query = ShareLinkUtils.ParseQuery(qIndex >= 0 ? text[(qIndex + 1)..] : text);

        if (!query.TryGetValue("server", out var server) || !query.TryGetValue("port", out var port))
            throw new FormatException("Invalid socks link");

        var data = new Socks5Server
        {
            Hostname = server,
            Port = ushort.Parse(port)
        };

        if (query.TryGetValue("user", out var user) && !string.IsNullOrWhiteSpace(user))
            data.Username = user;
        if (query.TryGetValue("pass", out var pass) && !string.IsNullOrWhiteSpace(pass))
            data.Password = pass;

        return data;
    }

    #endregion
}
