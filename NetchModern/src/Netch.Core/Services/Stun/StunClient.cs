using System.Net;
using System.Net.Sockets;
using Netch.Core.Enums;

namespace Netch.Core.Services.Stun;

public sealed record StunResult(NatType NatType, IPEndPoint? PublicEndPoint);

/// <summary>
///     NAT-type discovery via STUN, following the classic RFC 3489 test flow (Test I/II/III) with RFC 5389
///     message formatting. Full cone-type classification requires a STUN server with a secondary IP/port;
///     against single-address servers the reflexive (public) mapping from Test I is still reported.
/// </summary>
public sealed class StunClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public async Task<StunResult> DiscoverAsync(string host, int port, CancellationToken ct = default)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
        if (addresses.Length == 0)
            return new StunResult(NatType.Unknown, null);

        var serverEp = new IPEndPoint(addresses[0], port);

        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 0));

        var localEp = new IPEndPoint(GetLocalAddress(serverEp), ((IPEndPoint)udp.LocalEndPoint!).Port);

        // Test I — plain binding request.
        var test1 = await TransactAsync(udp, serverEp, false, false, ct);
        if (test1?.MappedAddress == null)
            return new StunResult(NatType.Blocked, null);

        var mapped = test1.MappedAddress;
        var isNatted = !mapped.Equals(localEp);

        // Test II — ask the server to reply from a different IP and port.
        var test2 = await TransactAsync(udp, serverEp, true, true, ct);

        if (!isNatted)
        {
            // No NAT: open internet if Test II is answered, else a symmetric UDP firewall.
            return new StunResult(test2 != null ? NatType.FullCone : NatType.Blocked, mapped);
        }

        if (test2?.MappedAddress != null)
            return new StunResult(NatType.FullCone, mapped);

        // Test I against the server's OTHER-ADDRESS to detect symmetric NAT.
        if (test1.OtherAddress != null)
        {
            var test1b = await TransactAsync(udp, test1.OtherAddress, false, false, ct);
            if (test1b?.MappedAddress != null && !test1b.MappedAddress.Equals(mapped))
                return new StunResult(NatType.Symmetric, mapped);
        }

        // Test III — change port only.
        var test3 = await TransactAsync(udp, serverEp, false, true, ct);
        return new StunResult(test3 != null ? NatType.RestrictedCone : NatType.PortRestrictedCone, mapped);
    }

    private static async Task<StunMessage.Response?> TransactAsync(
        Socket udp, IPEndPoint server, bool changeIp, bool changePort, CancellationToken ct)
    {
        var txid = StunMessage.NewTransactionId();
        var request = StunMessage.BuildBindingRequest(txid, changeIp, changePort);
        var buffer = new byte[512];

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await udp.SendToAsync(request, server, ct);

                using var timeout = new CancellationTokenSource(Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                var result = await udp.ReceiveFromAsync(buffer, new IPEndPoint(IPAddress.Any, 0), linked.Token);

                var parsed = StunMessage.Parse(buffer.AsSpan(0, result.ReceivedBytes), txid);
                if (parsed != null)
                    return parsed;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out — retry.
            }
        }

        return null;
    }

    private static IPAddress GetLocalAddress(IPEndPoint server)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(server);
            return ((IPEndPoint)probe.LocalEndPoint!).Address;
        }
        catch
        {
            return IPAddress.Loopback;
        }
    }
}
