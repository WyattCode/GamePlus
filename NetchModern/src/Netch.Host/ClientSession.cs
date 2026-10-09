using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Netch.Core.Abstractions;
using Netch.Core.Models;
using Netch.Core.Models.Games;

namespace Netch.Host;

/// <summary>
///     One WebSocket client. Reads JSON-RPC requests, dispatches them via the router, and pushes live
///     events (acceleration status ~1 Hz, log lines) back. All outbound frames go through a single channel
///     so event handlers on arbitrary threads never overlap a send.
/// </summary>
internal sealed class ClientSession
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Cap concurrent in-flight requests per session. A slow request (subs.update can take ~150s) must not
    // block a fast one (accel.stop), but we still bound how many tasks a single client can spawn.
    private const int MaxInFlight = 32;

    private readonly WebSocket _ws;
    private readonly RpcRouter _router;
    private readonly IServiceProvider _sp;
    private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
    private readonly IAcceleratorService _accel;
    private readonly ILogService _log;
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight, MaxInFlight);

    public ClientSession(WebSocket ws, RpcRouter router, IServiceProvider sp)
    {
        _ws = ws;
        _router = router;
        _sp = sp;
        _accel = sp.GetRequiredService<IAcceleratorService>();
        _log = sp.GetRequiredService<ILogService>();
    }

    public async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();

        _accel.Status.PropertyChanged += OnStatusChanged;
        _accel.StateChanged += OnStateChanged;
        _log.EntryAdded += OnLog;

        // Initial snapshot so the UI reflects current state on connect.
        Enqueue("accel.status", Mapping.StatusDto(_accel, _router.ServerIds));

        var writer = WriteLoopAsync(cts.Token);
        try
        {
            await ReadLoopAsync(cts.Token);
        }
        finally
        {
            _accel.Status.PropertyChanged -= OnStatusChanged;
            _accel.StateChanged -= OnStateChanged;
            _log.EntryAdded -= OnLog;
            _out.Writer.TryComplete();
            cts.Cancel();
            try { await writer; } catch { /* ignore */ }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var frame = new MemoryStream();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer, ct);
            }
            catch
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            // Accumulate raw bytes and decode once at end-of-message: a multibyte UTF-8 char can straddle
            // two receive fragments, and decoding each fragment separately would corrupt it.
            frame.Write(buffer, 0, result.Count);
            if (frame.Length > 8 * 1024 * 1024)
                break; // guard: no legitimate request approaches 8 MB — drop an oversized/runaway frame
            if (!result.EndOfMessage)
                continue;

            var message = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
            frame.SetLength(0);

            // Dispatch each request on its own task: a slow request (subs.update, accel.start) must not
            // block a fast one (accel.stop). Responses carry their own id, and the write channel is
            // thread-safe, so concurrent handlers are safe. The in-flight semaphore bounds the fan-out.
            await _inFlight.WaitAsync(ct);
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleRequestAsync(message, ct);
                }
                finally
                {
                    _inFlight.Release();
                }
            }, ct);
        }
    }

    private async Task HandleRequestAsync(string message, CancellationToken ct)
    {
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var method = root.GetProperty("method").GetString() ?? "";
            var @params = root.TryGetProperty("params", out var p) ? p : default;

            var result = await _router.DispatchAsync(method, @params, ct);
            Send(new { id, result });
        }
        catch (Exception e)
        {
            // Always answer, even on cancellation — the client is waiting on this id.
            Send(new { id, error = e.Message });
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _out.Reader.ReadAllAsync(ct))
            {
                var bytes = Encoding.UTF8.GetBytes(frame);
                // Honour ct: a client that stops reading would otherwise block SendAsync forever, hanging
                // the `await writer` in RunAsync's finally and leaking the session task.
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // session tearing down
        }
        catch
        {
            // socket dead — the read loop will observe it and unwind the session
        }
    }

    private void Send(object payload) => _out.Writer.TryWrite(JsonSerializer.Serialize(payload, Json));

    private void Enqueue(string @event, object data) =>
        _out.Writer.TryWrite(JsonSerializer.Serialize(new { @event, data }, Json));

    // Duration ticks once per second while accelerating — use it as the status heartbeat.
    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccelerationStatus.Duration) or nameof(AccelerationStatus.Latency))
            Enqueue("accel.status", Mapping.StatusDto(_accel, _router.ServerIds));
    }

    private void OnStateChanged(object? sender, AccelerationState e) =>
        Enqueue("accel.status", Mapping.StatusDto(_accel, _router.ServerIds));

    private void OnLog(object? sender, LogEntry e) => Enqueue("log", Mapping.ToDto(e));
}
