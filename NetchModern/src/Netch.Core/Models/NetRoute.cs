using System.Net;
using Netch.Core.Interops;

namespace Netch.Core.Models;

/// <summary>
///     A route-table template: an interface + gateway + metric that CIDR destinations get stamped onto.
///     Ported from the original Netch <c>NetRoute</c>.
/// </summary>
public struct NetRoute
{
    public int InterfaceIndex;
    public string Gateway;
    public string Network;
    public byte Cidr;
    public int Metric;

    public static NetRoute TemplateBuilder(string gateway, int interfaceIndex, int metric = 0)
        => new() { Gateway = gateway, InterfaceIndex = interfaceIndex, Metric = metric };

    /// <summary>Resolves the machine's current best route (default gateway + outbound interface).</summary>
    public static NetRoute GetBestRouteTemplate()
    {
        var probe = BitConverter.ToUInt32(IPAddress.Parse("114.114.114.114").GetAddressBytes(), 0);
        if (RouteHelperInterop.GetBestRoute(probe, 0, out var row) != 0)
            throw new InvalidOperationException("GetBestRoute failed");

        var gateway = new IPAddress(row.dwForwardNextHop);
        return TemplateBuilder(gateway.ToString(), (int)row.dwForwardIfIndex);
    }

    /// <summary>Stamps a destination network/cidr onto a copy of this template. Metric can't be 0 for routes.</summary>
    public NetRoute FillTemplate(string network, byte cidr, int? metric = null)
    {
        var copy = this;
        copy.Network = network;
        copy.Cidr = cidr;
        copy.Metric = metric ?? 1;
        return copy;
    }
}
