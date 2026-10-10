using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netch.Core.Services;
using Xunit;

namespace Netch.Tests;

/// <summary>
///     Loopback-only tests for <see cref="TlsHandshakeProbe" />. They spin up an in-test TLS server
///     (and, for the SOCKS case, a minimal in-test SOCKS5 server) on 127.0.0.1 so no external network
///     access is required. These tests would have caught the regression where the certificate
///     validation callback was passed both to the <see cref="SslStream" /> constructor and via
///     <see cref="SslClientAuthenticationOptions.RemoteCertificateValidationCallback" />, which makes
///     .NET throw <see cref="InvalidOperationException" /> and every probe return -4.
/// </summary>
public sealed class EffectProbeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DirectAsync_ToLoopbackTlsServer_ReturnsPositive()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        using var server = new LoopbackTlsServer();
        await server.StartAsync(cts.Token);

        var probe = new TlsHandshakeProbe();
        var result = await probe.DirectAsync("127.0.0.1", server.Port, cts.Token);

        Assert.True(result > 0, $"Expected positive latency, got {result}");
    }

    [Fact]
    public async Task ViaSocksAsync_ThroughLoopbackSocks5_ReturnsPositive()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        using var tlsServer = new LoopbackTlsServer();
        await tlsServer.StartAsync(cts.Token);

        using var socksServer = new LoopbackSocks5Server();
        await socksServer.StartAsync(cts.Token);

        var probe = new TlsHandshakeProbe();
        var result = await probe.ViaSocksAsync(
            "127.0.0.1", socksServer.Port, "localhost", tlsServer.Port, cts.Token);

        Assert.True(result > 0, $"Expected positive latency, got {result}");
    }

    [Fact]
    public async Task DirectAsync_ToClosedPort_ReturnsNegativeQuickly()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Bind a listener to obtain a free port, then close it so the port is (almost certainly) closed.
        var port = GetClosedPort();

        var probe = new TlsHandshakeProbe();
        var sw = Stopwatch.StartNew();
        var result = await probe.DirectAsync("127.0.0.1", port, cts.Token);
        sw.Stop();

        Assert.True(result < 0, $"Expected negative failure code, got {result}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3.5),
            $"Expected fast failure, took {sw.Elapsed}");
    }

    private static int GetClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    ///     Minimal loopback TLS server: accepts connections and performs a server-side TLS handshake
    ///     with a self-signed certificate. Application data is never read.
    /// </summary>
    private sealed class LoopbackTlsServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public LoopbackTlsServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _certificate = CreateSelfSignedCertificate();
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = AcceptLoopAsync(_cts.Token);
        }

        public int Port { get; }

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = HandleClientAsync(client, ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
                {
                    var options = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate
                    };
                    await ssl.AuthenticateAsServerAsync(options, ct);
                }
            }
            catch
            {
                // Client-side failures are expected (e.g. probe timeouts); ignore.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _cts.Dispose();
            _certificate.Dispose();
        }

        private static X509Certificate2 CreateSelfSignedCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1));

            // Export/re-import as PFX so the private key is usable on Linux (ephemeral keys from
            // CreateSelfSigned are not always usable for server-side TLS on non-Windows platforms).
            var pfx = cert.Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null);
        }
    }

    /// <summary>
    ///     Minimal in-test SOCKS5 server: no-auth only, supports CONNECT with ATYP 1/3/4. It replies
    ///     success immediately (like xray) and then dials the target and pipes bytes both ways.
    /// </summary>
    private sealed class LoopbackSocks5Server : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public LoopbackSocks5Server()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = AcceptLoopAsync(_cts.Token);
        }

        public int Port { get; }

        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = HandleClientAsync(client, ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();

                    // Greeting: VER NMETHODS METHODS...
                    var head = new byte[2];
                    await ReadExactAsync(stream, head, ct);
                    if (head[0] != 0x05)
                        return;
                    var methods = new byte[head[1]];
                    await ReadExactAsync(stream, methods, ct);

                    // No-auth only.
                    await stream.WriteAsync(new byte[] { 0x05, 0x00 }, ct);

                    // Request: VER CMD RSV ATYP DST.ADDR DST.PORT
                    var req = new byte[4];
                    await ReadExactAsync(stream, req, ct);
                    if (req[0] != 0x05 || req[1] != 0x01)
                        return;

                    string targetHost;
                    switch (req[3])
                    {
                        case 0x01:
                        {
                            var addr = new byte[4];
                            await ReadExactAsync(stream, addr, ct);
                            targetHost = new IPAddress(addr).ToString();
                            break;
                        }
                        case 0x04:
                        {
                            var addr = new byte[16];
                            await ReadExactAsync(stream, addr, ct);
                            targetHost = new IPAddress(addr).ToString();
                            break;
                        }
                        case 0x03:
                        {
                            var lenBuf = new byte[1];
                            await ReadExactAsync(stream, lenBuf, ct);
                            var name = new byte[lenBuf[0]];
                            await ReadExactAsync(stream, name, ct);
                            targetHost = Encoding.ASCII.GetString(name);
                            break;
                        }
                        default:
                            return;
                    }

                    var portBuf = new byte[2];
                    await ReadExactAsync(stream, portBuf, ct);
                    var targetPort = (portBuf[0] << 8) | portBuf[1];

                    // Reply success immediately (like xray) with BND.ADDR 0.0.0.0:0.
                    await stream.WriteAsync(
                        new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, ct);

                    using var upstream = new TcpClient();
                    await upstream.ConnectAsync(targetHost, targetPort, ct);
                    var upstreamStream = upstream.GetStream();

                    var t1 = PumpAsync(stream, upstreamStream, ct);
                    var t2 = PumpAsync(upstreamStream, stream, ct);
                    await Task.WhenAny(t1, t2);
                }
            }
            catch
            {
                // Ignore: probe may close the connection at any time.
            }
        }

        private static async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = await from.ReadAsync(buffer.AsMemory(), ct);
                    if (read <= 0)
                        return;
                    await to.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            catch
            {
                // Ignore: connection closed.
            }
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

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }
}
