using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Netch.Host;

namespace Netch.Tests;

public class LoopbackWebSocketServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Server_Starts_On_Loopback_Port()
    {
        using var server = new LoopbackWebSocketServer(_ => true);
        var port = server.Start();
        Assert.InRange(port, 1, 65535);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Client_With_Correct_Token_Connects_And_Echoes()
    {
        using var server = new LoopbackWebSocketServer(t => t == "secret");
        var port = server.Start();

        using var cts = new CancellationTokenSource(Timeout);
        var acceptTask = server.AcceptLoopAsync(async ws =>
        {
            var buffer = new byte[1024];
            var result = await ws.ReceiveAsync(buffer, cts.Token);
            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var reply = Encoding.UTF8.GetBytes("echo:" + text);
            await ws.SendAsync(reply, WebSocketMessageType.Text, true, cts.Token);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        }, cts.Token);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?token=secret"), cts.Token);
        var payload = Encoding.UTF8.GetBytes("hello");
        await client.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token);

        var recvBuffer = new byte[1024];
        var recv = await client.ReceiveAsync(recvBuffer, cts.Token);
        Assert.Equal("echo:hello", Encoding.UTF8.GetString(recvBuffer, 0, recv.Count));

        cts.Cancel();
        try { await acceptTask; } catch { /* ignore */ }
    }

    [Fact]
    public async Task Client_With_Wrong_Token_Is_Rejected()
    {
        using var server = new LoopbackWebSocketServer(t => t == "secret");
        var port = server.Start();

        using var cts = new CancellationTokenSource(Timeout);
        var acceptTask = server.AcceptLoopAsync(_ => Task.CompletedTask, cts.Token);

        using var client = new ClientWebSocket();
        await Assert.ThrowsAnyAsync<WebSocketException>(async () =>
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?token=wrong"), cts.Token));

        cts.Cancel();
        try { await acceptTask; } catch { /* ignore */ }
    }

    [Fact]
    public async Task Non_WebSocket_Http_Request_Gets_4xx()
    {
        using var server = new LoopbackWebSocketServer(_ => true);
        var port = server.Start();

        using var cts = new CancellationTokenSource(Timeout);
        var acceptTask = server.AcceptLoopAsync(_ => Task.CompletedTask, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();
        var request = Encoding.ASCII.GetBytes("GET /ws HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        await stream.WriteAsync(request, cts.Token);
        await stream.FlushAsync(cts.Token);

        var buffer = new byte[512];
        var read = await stream.ReadAsync(buffer, cts.Token);
        var response = Encoding.ASCII.GetString(buffer, 0, read);
        Assert.StartsWith("HTTP/1.1 4", response);

        cts.Cancel();
        try { await acceptTask; } catch { /* ignore */ }
    }

    [Fact]
    public void Sha256_Token_Validator_Accepts_Right_And_Rejects_Others()
    {
        var token = "abc123";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var expected = Convert.FromHexString(digest);

        bool Validate(string? presented)
        {
            if (string.IsNullOrEmpty(presented)) return false;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }

        Assert.True(Validate(token));
        Assert.False(Validate("nope"));
        Assert.False(Validate(null));
        Assert.False(Validate(""));
    }
}
