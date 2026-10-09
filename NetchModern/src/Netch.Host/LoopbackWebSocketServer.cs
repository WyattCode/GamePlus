using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Netch.Host;

/// <summary>
///     Minimal loopback WebSocket server built on a plain <see cref="TcpListener" />.
///     <para>
///         We deliberately avoid <c>HttpListener</c>: on Windows it routes through http.sys, and registering
///         any prefix other than <c>localhost</c> requires admin rights or a URL ACL. A non-elevated host
///         (dev mode, or the fallback after the user declines UAC) would then fail to bind with "Access
///         denied" and nothing would work. A raw TCP listener needs no privileges and lets the OS assign a
///         free port atomically (no choose-then-bind race).
///     </para>
///     <para>
///         The HTTP/1.1 upgrade handshake is implemented by hand — we only need to accept a single
///         <c>GET /ws?token=...</c> request and reply <c>101 Switching Protocols</c>.
///     </para>
/// </summary>
internal sealed class LoopbackWebSocketServer : IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly Func<string?, bool> _tokenValidator;

    /// <param name="tokenValidator">
    ///     Called with the raw <c>token</c> query-string value (may be <c>null</c>). Returns <c>true</c> to
    ///     accept the connection. Runs on the connection's own task.
    /// </param>
    public LoopbackWebSocketServer(Func<string?, bool> tokenValidator)
    {
        _tokenValidator = tokenValidator;
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    /// <summary>Binds the listener and returns the OS-assigned loopback port.</summary>
    public int Start()
    {
        _listener.Start();
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    ///     Accepts connections until <paramref name="ct" /> is cancelled. Each accepted connection runs its
    ///     handshake (and then <paramref name="onClient" />) on its own task, so a slow or malicious client
    ///     can never block the accept loop.
    /// </summary>
    public async Task AcceptLoopAsync(Func<WebSocket, Task> onClient, CancellationToken ct)
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
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleConnectionAsync(client, onClient, ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, Func<WebSocket, Task> onClient, CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            var request = await ReadRequestAsync(stream, ct);
            if (request is null)
            {
                await WriteStatusAsync(stream, 400, "Bad Request", ct);
                return;
            }

            if (!string.Equals(request.Method, "GET", StringComparison.Ordinal) ||
                !string.Equals(request.Path, "/ws", StringComparison.Ordinal))
            {
                await WriteStatusAsync(stream, 404, "Not Found", ct);
                return;
            }

            if (!request.Headers.TryGetValue("Upgrade", out var upgrade) ||
                !upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase) ||
                !request.Headers.TryGetValue("Sec-WebSocket-Key", out var key) ||
                string.IsNullOrWhiteSpace(key))
            {
                await WriteStatusAsync(stream, 400, "Bad Request", ct);
                return;
            }

            if (!_tokenValidator(request.Query.GetValueOrDefault("token")))
            {
                await WriteStatusAsync(stream, 403, "Forbidden", ct);
                return;
            }

            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.UTF8.GetBytes(key.Trim() + WebSocketGuid)));

            var response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);

            using var ws = WebSocket.CreateFromStream(
                stream,
                new WebSocketCreationOptions
                {
                    IsServer = true,
                    KeepAliveInterval = TimeSpan.FromSeconds(20)
                });

            await onClient(ws);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch
        {
            // Malformed client / socket error — drop the connection.
        }
        finally
        {
            try
            {
                client.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HandshakeTimeout);

        var buffer = new byte[1024];
        var accumulated = new MemoryStream();
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null; // handshake read timed out
            }

            if (read <= 0)
                return null;

            accumulated.Write(buffer, 0, read);
            if (accumulated.Length > MaxHeaderBytes)
                return null;

            var span = accumulated.GetBuffer().AsSpan(0, (int)accumulated.Length);
            headerEnd = span.IndexOf("\r\n\r\n"u8);
        }

        var headerText = Encoding.ASCII.GetString(accumulated.GetBuffer(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        if (lines.Length == 0)
            return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var target = requestLine[1];
        var queryIndex = target.IndexOf('?');
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        var queryString = queryIndex >= 0 ? target[(queryIndex + 1)..] : "";

        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                query[Uri.UnescapeDataString(pair)] = "";
            else
                query[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0)
                continue;
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        return new HttpRequest(requestLine[0], path, query, headers);
    }

    private static async Task WriteStatusAsync(NetworkStream stream, int status, string reason, CancellationToken ct)
    {
        var body = $"{status} {reason}";
        var response =
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
            "Connection: close\r\n\r\n" +
            body;
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
            await stream.FlushAsync(ct);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        try
        {
            _listener.Stop();
        }
        catch
        {
            // ignore
        }
    }

    private sealed record HttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Query,
        Dictionary<string, string> Headers);
}
