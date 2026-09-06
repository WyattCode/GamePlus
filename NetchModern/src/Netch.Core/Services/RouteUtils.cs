using System.Net;
using System.Net.Sockets;
using Netch.Core.Interops;
using Netch.Core.Models;

namespace Netch.Core.Services;

/// <summary>
///     Route-table helpers over <see cref="RouteHelperInterop" />, ported from the original RouteUtils.
///     Adds/removes routes for a <see cref="NetRoute" /> template and a set of CIDR destinations.
/// </summary>
public static class RouteUtils
{
    public static bool CreateRoute(NetRoute route)
        => RouteHelperInterop.CreateRoute(AddressFamily.InterNetwork, route.Network, route.Cidr, route.Gateway,
            (ulong)route.InterfaceIndex, route.Metric);

    public static bool DeleteRoute(NetRoute route)
        => RouteHelperInterop.DeleteRoute(AddressFamily.InterNetwork, route.Network, route.Cidr, route.Gateway,
            (ulong)route.InterfaceIndex, route.Metric);

    public static void CreateRouteFill(NetRoute template, IEnumerable<string> cidrs)
    {
        foreach (var (network, cidr) in cidrs.Select(ParseCidr))
            CreateRoute(template.FillTemplate(network, cidr));
    }

    public static void DeleteRouteFill(NetRoute template, IEnumerable<string> cidrs)
    {
        foreach (var (network, cidr) in cidrs.Select(ParseCidr))
            DeleteRoute(template.FillTemplate(network, cidr));
    }

    /// <summary>
    ///     Parses "10.0.0.0/8" or a bare "1.2.3.4" (→ /32) into (network, prefix-length), masking any host
    ///     bits so the destination is a valid network address (e.g. "110.42.96.65/24" → "110.42.96.0/24").
    ///     A route with host bits set is rejected by the OS, so this masking is required.
    /// </summary>
    public static (string Network, byte Cidr) ParseCidr(string value)
    {
        var slash = value.IndexOf('/');
        string ipPart;
        byte cidr;
        if (slash < 0)
        {
            ipPart = value.Trim();
            cidr = 32;
        }
        else
        {
            ipPart = value[..slash].Trim();
            cidr = byte.TryParse(value[(slash + 1)..].Trim(), out var c) ? c : (byte)32;
        }

        // Zero the host bits for IPv4 so the destination is the network address the OS expects.
        if (cidr <= 32 && IPAddress.TryParse(ipPart, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            var addr = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
            var mask = cidr == 0 ? 0u : 0xFFFFFFFFu << (32 - cidr);
            addr &= mask;
            ipPart = $"{(addr >> 24) & 0xFF}.{(addr >> 16) & 0xFF}.{(addr >> 8) & 0xFF}.{addr & 0xFF}";
        }

        return (ipPart, cidr);
    }

    /// <summary>Counts the set bits of a dotted-decimal subnet mask, e.g. 255.255.255.0 → 24.</summary>
    public static byte SubnetToCidr(string netmask)
    {
        if (!IPAddress.TryParse(netmask, out var mask))
            return 32;

        byte bits = 0;
        foreach (var b in mask.GetAddressBytes())
            bits += (byte)System.Numerics.BitOperations.PopCount(b);

        return bits;
    }
}
