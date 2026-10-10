using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Netch.Core.Services;

/// <summary>
///     Measures the elapsed time of a "TCP connect + TLS handshake" to a game host, either directly or
///     through the local SOCKS5 inbound. Returns ms on success, or a negative failure code (-4).
/// </summary>
public interface IEffectProbe
{
    /// <summary>Direct (no proxy) TCP connect + TLS handshake to <paramref name="host" />:<paramref name="port" />.</summary>
    Task<int> DirectAsync(string host, int port, CancellationToken ct);

    /// <summary>
    ///     TCP connect + TLS handshake to <paramref name="host" />:<paramref name="port" /> through the local
    ///     SOCKS5 inbound at <paramref name="socksHost" />:<paramref name="socksPort" />.
    /// </summary>
    Task<int> ViaSocksAsync(string socksHost, int socksPort, string host, int port, CancellationToken ct);
}

/// <summary>
///     Real probe: a full TCP connect + TLS handshake to the game's own host, measured directly and
///     through the local SOCKS5 inbound.
///
///     <para>Rationale: proxy cores answer the SOCKS CONNECT immediately and dial the upstream lazily, so
///     timing only the CONNECT would measure nothing. Only a real TLS round trip through the tunnel
///     exercises the accelerated path. The same handshake is used for the direct baseline so the two
///     numbers are comparable. The probe never sends application data beyond the TLS handshake.</para>
/// </summary>
public sealed class TlsHandshakeProbe : IEffectProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public Task<int> DirectAsync(string host, int port, CancellationToken ct) =>
        MeasureAsync(async token =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token);
            await HandshakeAsync(client.GetStream(), host, token);
        }, ct);

    public Task<int> ViaSocksAsync(string socksHost, int socksPort, string host, int port, CancellationToken ct) =>
        MeasureAsync(async token =>
        {
            using var client = new TcpClient();
            await client.ConnectAsync(socksHost, socksPort, token);
            var stream = client.GetStream();
            await Socks5ConnectAsync(stream, host, port, token);
            await HandshakeAsync(stream, host, token);
        }, ct);

    private static async Task<int> MeasureAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var sw = Stopwatch.StartNew();
            await action(timeout.Token);
            sw.Stop();
            return Math.Max(1, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return -4;
        }
    }

    private static async Task HandshakeAsync(Stream stream, string host, CancellationToken ct)
    {
        // The validation callback must be supplied exactly once: either via the SslStream constructor
        // or via SslClientAuthenticationOptions. Passing both makes .NET throw
        // InvalidOperationException("The 'RemoteCertificateValidationCallback' option was already set
        // in the SslStream constructor."). Keep it only in the options.
        using var ssl = new SslStream(stream, leaveInnerStreamOpen: true);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        };
        await ssl.AuthenticateAsClientAsync(options, ct);
    }

    /// <summary>
    ///     Performs the SOCKS5 handshake (no auth, method 0x00) and a CONNECT request for
    ///     <paramref name="host" />:<paramref name="port" />. ATYP is chosen by the target: 0x03 for
    ///     hostnames, 0x01 for IPv4, 0x04 for IPv6.
    /// </summary>
    private static async Task Socks5ConnectAsync(NetworkStream stream, string host, int port, CancellationToken ct)
    {
        // Greeting: version 5, one method (0x00 = no auth).
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, ct);
        var greeting = new byte[2];
        await ReadExactAsync(stream, greeting, ct);
        if (greeting[0] != 0x05 || greeting[1] != 0x00)
            throw new IOException("SOCKS5 handshake failed");

        // CONNECT request.
        var request = new List<byte> { 0x05, 0x01, 0x00 };
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                request.Add(0x04);
                request.AddRange(ip.GetAddressBytes());
            }
            else
            {
                request.Add(0x01);
                request.AddRange(ip.GetAddressBytes());
            }
        }
        else
        {
            var name = Encoding.ASCII.GetBytes(host);
            request.Add(0x03);
            request.Add((byte)name.Length);
            request.AddRange(name);
        }

        request.Add((byte)(port >> 8));
        request.Add((byte)(port & 0xFF));
        await stream.WriteAsync(request.ToArray(), ct);

        // Reply: VER REP ATYP BND.ADDR BND.PORT — skip the bound address by ATYP length.
        var head = new byte[4];
        await ReadExactAsync(stream, head, ct);
        if (head[1] != 0x00)
            throw new IOException($"SOCKS5 connect failed: {head[1]}");

        var skip = head[3] switch
        {
            0x01 => 4 + 2,
            0x04 => 16 + 2,
            0x03 => await ReadDomainLengthAsync(stream, ct),
            _ => throw new IOException("SOCKS5 bad ATYP")
        };
        if (skip > 0)
        {
            var discard = new byte[skip];
            await ReadExactAsync(stream, discard, ct);
        }
    }

    private static async Task<int> ReadDomainLengthAsync(NetworkStream stream, CancellationToken ct)
    {
        var len = new byte[1];
        await ReadExactAsync(stream, len, ct);
        return len[0] + 2;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read <= 0)
                throw new IOException("SOCKS5 stream closed");
            offset += read;
        }
    }
}
