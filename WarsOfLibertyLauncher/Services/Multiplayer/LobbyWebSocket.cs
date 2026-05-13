using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Thin wrapper over <see cref="ClientWebSocket"/> for the lobby room
/// WebSocket. Encapsulates:
///   * URL construction (Worker's https→wss, /lobbies/:id/ws).
///   * Auth: sends the first <c>hello</c> frame with either a join token
///     (joiner) or a session JWT (host).
///   * A background receive loop that surfaces every frame via the
///     <see cref="FrameReceived"/> event on the UI thread (the event
///     itself fires on a background thread; callers marshal back as
///     they would for any other async event).
///   * Auto-reconnect with exponential backoff up to 30 s.
///   * 30-second ping heartbeat (matches Worker's 90-second idle kick).
///
/// One instance per lobby session. <see cref="DisposeAsync"/> cleanly
/// closes the socket and stops reconnect attempts.
/// </summary>
public sealed class LobbyWebSocket : IAsyncDisposable
{
    public enum HelloMode
    {
        /// <summary>The hello frame carries a <c>join_token</c> (joiner path).</summary>
        JoinToken,
        /// <summary>The hello frame carries the user's JWT (host path).</summary>
        SessionToken,
    }

    public sealed class FrameReceivedEventArgs : EventArgs
    {
        public required string Type { get; init; }
        public required JsonElement Json { get; init; }
    }

    public event EventHandler<FrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<string>? Disconnected;     // arg = reason
    public event EventHandler<string>? Reconnecting;     // arg = next attempt label

    private readonly Uri _uri;
    private readonly HelloMode _mode;
    private readonly string _credential;
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket? _ws;
    private Task? _runLoop;
    private int _attempt = 0;

    public LobbyWebSocket(Uri wsUri, HelloMode mode, string credential)
    {
        _uri = wsUri;
        _mode = mode;
        _credential = credential;
    }

    /// <summary>Compose a wss URL from the Worker base URL + a relative path.</summary>
    public static Uri BuildWsUri(Uri httpsBase, string relativePath)
    {
        // ws:// over http://, wss:// over https://. The Worker is always
        // HTTPS in production, but local `wrangler dev` runs HTTP.
        var scheme = httpsBase.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var b = new UriBuilder(httpsBase)
        {
            Scheme = scheme,
            Path = (httpsBase.AbsolutePath.TrimEnd('/') + "/" + relativePath.TrimStart('/')).TrimStart('/'),
        };
        return b.Uri;
    }

    public void Start()
    {
        if (_runLoop != null) return;
        _runLoop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public ValueTask DisposeAsync()
    {
        // Aggressive close: cancel the loop's CTS and Abort the socket
        // directly instead of doing CloseOutputAsync. The polite close
        // frame was adding ~2 s to every Leave because Workers' WS
        // doesn't always echo back promptly; we don't care since the
        // REST /leave call already told the server we're gone.
        try { _cts.Cancel(); } catch { /* already disposed */ }
        try { _ws?.Abort(); } catch { /* socket already dying */ }
        try { _ws?.Dispose(); } catch { /* ditto */ }
        try { _cts.Dispose(); } catch { /* ditto */ }
        // _runLoop is left to its own devices — it'll see the
        // cancelled token on its next iteration and return. Awaiting
        // it here added latency without giving us anything we could
        // act on (we're disposing).
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Send an arbitrary frame. Caller passes a serialisable object — we
    /// JSON-encode and write it on the wire. Safe to call concurrently:
    /// a single semaphore serialises socket writes.
    /// </summary>
    public Task SendAsync(object payload, CancellationToken ct = default) =>
        SendRawAsync(JsonSerializer.Serialize(payload), ct);

    public Task SendChatAsync(string body, CancellationToken ct = default) =>
        SendAsync(new { type = "chat", body }, ct);

    public Task SendReadyAsync(bool ready, CancellationToken ct = default) =>
        SendAsync(new { type = "ready", ready }, ct);

    public Task SendStartAsync(CancellationToken ct = default) =>
        SendAsync(new { type = "start" }, ct);

    /// <summary>
    /// Tunnel a game packet via the lobby DO when direct hole-punch
    /// to the peer has failed. The payload is base64 because JSON
    /// WS frames don't carry binary cleanly; AoE3 packets are small
    /// (&lt; 1 KB typical) so the base64 overhead is acceptable.
    /// </summary>
    public Task SendGameRelayAsync(string toUserId, ushort srcPort, ushort dstPort, byte[] payload,
        CancellationToken ct = default)
        => SendAsync(new
        {
            type = "game_relay",
            to_user = toUserId,
            src_port = (int)srcPort,
            dst_port = (int)dstPort,
            payload_b64 = Convert.ToBase64String(payload),
        }, ct);

    // ---------- internals -----------------------------------------

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndPumpAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"LobbyWebSocket: pump error: {ex.Message}");
                Disconnected?.Invoke(this, ex.Message);
            }

            if (ct.IsCancellationRequested) return;

            // Backoff: 1 s, 2 s, 4 s … capped at 30 s.
            var delay = Math.Min(30, 1 << Math.Min(5, _attempt));
            _attempt++;
            Reconnecting?.Invoke(this, $"in {delay}s");
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConnectAndPumpAsync(CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        // Workers' Hibernation API is happy with default subprotocol; no
        // extra headers needed beyond user-agent for politeness.
        _ws.Options.SetRequestHeader("User-Agent", "Aoe3ModLauncher/1.0");

        await _ws.ConnectAsync(_uri, ct);
        _attempt = 0;     // reset backoff after a successful connect

        // First frame must be hello.
        var hello = _mode == HelloMode.JoinToken
            ? (object)new { type = "hello", join_token = _credential }
            : new { type = "hello", token = _credential };
        await SendRawAsync(JsonSerializer.Serialize(hello), ct);

        // Background heartbeat — ping every 30 s. The Worker idle-kicks
        // at 90 s of silence; one ping per 30 s gives us 3× margin and
        // also keeps any intermediate NAT routes alive.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), heartbeatCts.Token);
                    try { await SendAsync(new { type = "ping" }, heartbeatCts.Token); }
                    catch { /* fall through to the receive loop's error handling */ }
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        }, heartbeatCts.Token);

        try
        {
            await ReceiveLoopAsync(_ws, ct);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat; } catch { /* ignored */ }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        var assembled = new System.IO.MemoryStream();

        while (!ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, ct);
            }
            catch (Exception ex)
            {
                Disconnected?.Invoke(this, ex.Message);
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Disconnected?.Invoke(this, $"server_close:{(int)(result.CloseStatus ?? WebSocketCloseStatus.Empty)}");
                return;
            }

            assembled.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(assembled.ToArray());
            assembled.SetLength(0);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
                FrameReceived?.Invoke(this, new FrameReceivedEventArgs
                {
                    Type = type,
                    Json = root.Clone(),  // detach from `doc` lifetime
                });
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"LobbyWebSocket: bad frame ignored: {ex.Message}");
            }
        }
    }
}
