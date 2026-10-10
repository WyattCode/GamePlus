using Netch.Core.Models.Servers;
using YamlDotNet.RepresentationModel;

namespace Netch.Core.Services.Import;

/// <summary>
///     Parses a Clash / Clash.Meta YAML configuration into <see cref="Server" /> models.
///     Uses YamlDotNet's representation model (no type deserialization of arbitrary types) so that
///     hostile or malformed input cannot instantiate unexpected CLR types.
///     Never throws on bad input — malformed entries are skipped and counted.
/// </summary>
public static class ClashConfigParser
{
    /// <summary>Hard cap on the input size (5 MB) to bound memory use on hostile input.</summary>
    public const int MaxInputBytes = 5 * 1024 * 1024;

    /// <summary>Hard cap on the number of proxies parsed from a single document.</summary>
    public const int MaxProxies = 5000;

    /// <summary>
    ///     Parses the top-level <c>proxies:</c> sequence (block or flow style).
    ///     Returns the servers that could be mapped; unknown types and malformed entries are skipped.
    /// </summary>
    public static IReadOnlyList<Server> Parse(string yaml)
    {
        var results = new List<Server>();
        if (string.IsNullOrWhiteSpace(yaml))
            return results;

        // Cap input size before handing it to the YAML reader.
        if (yaml.Length > MaxInputBytes)
            yaml = yaml[..MaxInputBytes];

        YamlStream stream;
        try
        {
            stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch
        {
            // Malformed YAML — return whatever we have (nothing).
            return results;
        }

        if (stream.Documents.Count == 0)
            return results;

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            return results;

        if (!root.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode))
            return results;

        if (proxiesNode is not YamlSequenceNode sequence)
            return results;

        foreach (var item in sequence.Children)
        {
            if (results.Count >= MaxProxies)
                break;

            if (item is not YamlMappingNode mapping)
                continue;

            try
            {
                var server = MapProxy(mapping);
                if (server != null)
                    results.Add(server);
            }
            catch
            {
                // Skip malformed entries; a bad proxy must not abort the whole import.
            }
        }

        return results;
    }

    private static Server? MapProxy(YamlMappingNode mapping)
    {
        var type = GetString(mapping, "type");
        if (string.IsNullOrEmpty(type))
            return null;

        var host = GetString(mapping, "server");
        if (string.IsNullOrEmpty(host))
            return null;

        var port = GetPort(mapping, "port");
        if (port == 0)
            return null;

        var remark = GetString(mapping, "name") ?? string.Empty;

        switch (type.ToLowerInvariant())
        {
            case "ss":
                return MapShadowsocks(mapping, host, port, remark);
            case "vmess":
                return MapVMess(mapping, host, port, remark);
            case "trojan":
                return MapTrojan(mapping, host, port, remark);
            case "vless":
                return MapVless(mapping, host, port, remark);
            case "hysteria2":
                return MapHysteria2(mapping, host, port, remark);
            case "socks5":
                return MapSocks5(mapping, host, port, remark);
            default:
                return null;
        }
    }

    private static ShadowsocksServer MapShadowsocks(YamlMappingNode m, string host, ushort port, string remark)
    {
        var server = new ShadowsocksServer
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            EncryptMethod = GetString(m, "cipher") ?? GetString(m, "method") ?? "aes-128-gcm",
            Password = GetString(m, "password") ?? string.Empty
        };

        var plugin = GetString(m, "plugin");
        if (!string.IsNullOrEmpty(plugin))
        {
            var semi = plugin.IndexOf(';');
            server.Plugin = semi >= 0 ? plugin[..semi] : plugin;
            server.PluginOption = semi >= 0 ? plugin[(semi + 1)..] : string.Empty;
        }

        return server;
    }

    private static VMessServer MapVMess(YamlMappingNode m, string host, ushort port, string remark)
    {
        var server = new VMessServer
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            UserId = GetString(m, "uuid") ?? string.Empty,
            AlterId = GetInt(m, "alterId") ?? GetInt(m, "alterid") ?? 0,
            Security = GetString(m, "cipher") ?? GetString(m, "security") ?? "auto",
            Network = GetString(m, "network") ?? "tcp",
            Tls = GetBool(m, "tls") ?? false,
            Sni = GetString(m, "servername") ?? GetString(m, "sni"),
            AllowInsecure = GetBool(m, "skip-cert-verify") ?? false
        };

        ApplyTransport(server, m);
        return server;
    }

    private static TrojanServer MapTrojan(YamlMappingNode m, string host, ushort port, string remark)
    {
        var server = new TrojanServer
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            Password = GetString(m, "password") ?? string.Empty,
            Network = GetString(m, "network") ?? "tcp",
            Sni = GetString(m, "sni") ?? GetString(m, "servername"),
            AllowInsecure = GetBool(m, "skip-cert-verify") ?? false
        };

        ApplyTransport(server, m);
        return server;
    }

    private static VlessServer MapVless(YamlMappingNode m, string host, ushort port, string remark)
    {
        var server = new VlessServer
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            UserId = GetString(m, "uuid") ?? string.Empty,
            Flow = GetString(m, "flow") ?? string.Empty,
            Network = GetString(m, "network") ?? "tcp",
            Sni = GetString(m, "servername") ?? GetString(m, "sni"),
            AllowInsecure = GetBool(m, "skip-cert-verify") ?? false
        };

        // Security: explicit "security" wins; otherwise infer from tls / reality-opts.
        var security = GetString(m, "security");
        if (!string.IsNullOrEmpty(security))
            server.Security = security;
        else if (GetBool(m, "tls") == true)
            server.Security = "tls";

        if (TryGetMapping(m, "reality-opts", out var reality))
        {
            server.Security = "reality";
            server.PublicKey = GetString(reality, "public-key") ?? GetString(reality, "public_key");
            server.ShortId = GetString(reality, "short-id") ?? GetString(reality, "short_id");
        }

        ApplyTransport(server, m);
        return server;
    }

    private static Hysteria2Server MapHysteria2(YamlMappingNode m, string host, ushort port, string remark)
    {
        var server = new Hysteria2Server
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            Password = GetString(m, "password") ?? GetString(m, "auth") ?? string.Empty,
            Sni = GetString(m, "sni") ?? GetString(m, "servername"),
            AllowInsecure = GetBool(m, "skip-cert-verify") ?? false
        };

        if (TryGetMapping(m, "obfs", out var obfs))
        {
            var obfsType = GetString(obfs, "type");
            if (string.Equals(obfsType, "salamander", StringComparison.OrdinalIgnoreCase))
                server.ObfsPassword = GetString(obfs, "password");
        }

        var up = GetInt(m, "up") ?? GetInt(m, "up-mbps") ?? GetInt(m, "upmbps");
        if (up is > 0)
            server.UpMbps = up.Value;
        var down = GetInt(m, "down") ?? GetInt(m, "down-mbps") ?? GetInt(m, "downmbps");
        if (down is > 0)
            server.DownMbps = down.Value;

        return server;
    }

    private static Socks5Server MapSocks5(YamlMappingNode m, string host, ushort port, string remark)
    {
        return new Socks5Server
        {
            Hostname = host,
            Port = port,
            Remark = remark,
            Username = GetString(m, "username"),
            Password = GetString(m, "password")
        };
    }

    /// <summary>
    ///     Copies the shared transport fields (network / ws-opts / grpc-opts / alpn / client-fingerprint)
    ///     onto the model. Only ws and grpc are mapped, matching the task scope.
    /// </summary>
    private static void ApplyTransport(Server server, YamlMappingNode m)
    {
        var network = GetString(m, "network");
        var alpn = GetString(m, "alpn");
        var fingerprint = GetString(m, "client-fingerprint") ?? GetString(m, "fingerprint");

        string? host = null;
        string? path = null;
        string? serviceName = null;

        if (TryGetMapping(m, "ws-opts", out var ws))
        {
            path = GetString(ws, "path");
            if (TryGetMapping(ws, "headers", out var headers))
                host = GetString(headers, "Host") ?? GetString(headers, "host");
        }

        if (TryGetMapping(m, "grpc-opts", out var grpc))
            serviceName = GetString(grpc, "grpc-service-name") ?? GetString(grpc, "grpc_service_name");

        switch (server)
        {
            case VMessServer vm:
                if (!string.IsNullOrEmpty(network)) vm.Network = network;
                if (!string.IsNullOrEmpty(host)) vm.Host = host;
                if (!string.IsNullOrEmpty(path)) vm.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) vm.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(alpn)) vm.Alpn = alpn;
                if (!string.IsNullOrEmpty(fingerprint)) vm.Fingerprint = fingerprint;
                break;
            case VlessServer vl:
                if (!string.IsNullOrEmpty(network)) vl.Network = network;
                if (!string.IsNullOrEmpty(host)) vl.Host = host;
                if (!string.IsNullOrEmpty(path)) vl.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) vl.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(alpn)) vl.Alpn = alpn;
                if (!string.IsNullOrEmpty(fingerprint)) vl.Fingerprint = fingerprint;
                break;
            case TrojanServer tr:
                if (!string.IsNullOrEmpty(network)) tr.Network = network;
                if (!string.IsNullOrEmpty(host)) tr.Host = host;
                if (!string.IsNullOrEmpty(path)) tr.Path = path;
                if (!string.IsNullOrEmpty(serviceName)) tr.ServiceName = serviceName;
                if (!string.IsNullOrEmpty(alpn)) tr.Alpn = alpn;
                if (!string.IsNullOrEmpty(fingerprint)) tr.Fingerprint = fingerprint;
                break;
        }
    }

    #region YAML helpers

    private static bool TryGetMapping(YamlMappingNode mapping, string key, out YamlMappingNode value)
    {
        value = null!;
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return false;
        if (node is not YamlMappingNode child)
            return false;
        value = child;
        return true;
    }

    private static string? GetString(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return null;
        if (node is not YamlScalarNode scalar)
            return null;
        return scalar.Value;
    }

    private static ushort GetPort(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (string.IsNullOrEmpty(raw))
            return 0;
        return ushort.TryParse(raw, out var port) ? port : (ushort)0;
    }

    private static int? GetInt(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (string.IsNullOrEmpty(raw))
            return null;
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static bool? GetBool(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (string.IsNullOrEmpty(raw))
            return null;
        if (bool.TryParse(raw, out var value))
            return value;
        // YAML 1.1 style booleans.
        return raw switch
        {
            "1" or "yes" or "on" => true,
            "0" or "no" or "off" => false,
            _ => null
        };
    }

    #endregion
}
