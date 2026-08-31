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

    /// <summary>
    /// How long a connection has to survive before it counts as a success worth resetting the
    /// backoff for.
    ///
    /// <para>The reset used to happen the moment <c>ConnectAsync</c> returned, on the reasonable-
    /// sounding theory that reaching the server means the trouble is over. It is not: the backend
    /// accepts the upgrade and THEN closes with a code — <c>4404 lobby_not_found</c> for a room
    /// that no longer exists — so every attempt "succeeded", the counter went back to zero, and
    /// the exponential backoff never left its first step. Measured on a real client: about two
    /// hundred reconnects in five minutes, roughly one a second, ending only because the player
    /// closed the window.</para>
    ///
    /// <para>Terminal close codes are also handled by name a layer up, which is the better fix
    /// for the cases we can enumerate. This is the one that covers the ones we cannot.</para>
    /// </summary>
    private const long StableConnectionMs = 5000;

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

    /// <summary>
    /// Stop reconnecting, but keep the object.
    ///
    /// <para>For the one case where the room is gone for a REASON — the backend closes it
    /// after a reported match and shuts the socket with 4007. The reconnect loop cannot
    /// tell that from a dropped connection, so it kept retrying, forever, backing off to
    /// 30 s: the lobby window survived with a dead chat, live buttons and a socket
    /// pointing at a room that no longer existed.</para>
    ///
    /// <para>Deliberately NOT <see cref="DisposeAsync"/> and deliberately not paired with
    /// nulling the caller's reference: dropping the socket object raises the session state
    /// change that tears the lobby window down, and the window is exactly what has to stay
    /// up to show the result. This kills the retries and nothing else.</para>
    /// </summary>
    public void StopReconnect()
    {
        try { _cts.Cancel(); } catch { /* already disposed */ }
        try { _ws?.Abort(); } catch { /* socket already dying */ }
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
        SendAsync(new { type = "start_game" }, ct);

    /// <summary>
    /// Host-only: ask the DO to cancel the active game. Triggers a
    /// <c>game_cancelled</c> broadcast back to every member so each
    /// client kills its AoE3 process and unlocks the room popup.
    /// Reuses the existing WS — no extra HTTP round-trip.
    /// </summary>
    public Task SendCancelGameAsync(string reason = "host_cancelled", CancellationToken ct = default) =>
        SendAsync(new { type = "cancel_game", reason }, ct);

    /// <summary>Host tells the server its game process exited so the room reverts
    /// from in_game → open (no grace window; the game actually ended).</summary>
    public Task SendGameEndedAsync(CancellationToken ct = default) =>
        SendAsync(new { type = "game_ended" }, ct);

    /// <summary>
    /// Report our Radmin VPN IP (26.x) so the server can put it in
    /// <c>room_state</c> / broadcast <c>member_net</c>, letting every peer
    /// ICMP-ping us for the in-game per-player ping column. Sent once we're
    /// actually on the VPN (at match launch), re-sent if it changes.
    /// </summary>
    public Task SendSetRadminIpAsync(string ip, CancellationToken ct = default) =>
        SendAsync(new { type = "set_radmin_ip", ip }, ct);

    /// <summary>
    /// Report our AoE3 profile name so the room can tell which recording slot belongs to which
    /// Discord account — the only link between the two, and the thing that lets a team game be
    /// recorded with real teams instead of everyone on team 0.
    ///
    /// <para><b>Self-reported because it cannot be inferred.</b> The profile name is per MOD and
    /// routinely nothing like the account: measured on one machine, the same person is
    /// <c>Gorgorito12</c> on Discord and <c>Gorgorito</c> / <c>gorgorito</c> / <c>sdfs</c> in
    /// three mods. Everyone in the room is about to read this name off each other's screens
    /// inside AoE3 anyway.</para>
    ///
    /// <para>Same shape as <see cref="SendSetRadminIpAsync"/>: the server stores it on the
    /// member, puts it in <c>room_state</c> and broadcasts <c>member_ingame_name</c>.</para>
    /// </summary>
    public Task SendSetInGameNameAsync(string name, CancellationToken ct = default) =>
        SendAsync(new { type = "set_ingame_name", name }, ct);

    /// <summary>
    /// Host-only: ask the server to kick a member. The server validates we're
    /// the host, tells the target it was kicked, and closes its socket (the
    /// normal disconnect path then drops it from the roster for everyone).
    /// </summary>
    public Task SendKickAsync(string userId, CancellationToken ct = default) =>
        SendAsync(new { type = "kick", user_id = userId }, ct);

    /// <summary>
    /// Host-only: rename the room while it's already open. The server validates
    /// (host + 3-80 chars), writes the new name and broadcasts
    /// <c>room_renamed</c> to EVERYONE including us — so the caller must not
    /// paint the new name locally, or host and peers could disagree.
    /// </summary>
    public Task SendRenameRoomAsync(string title, CancellationToken ct = default) =>
        SendAsync(new { type = "rename_room", title }, ct);

    // (Pre-n2n: a SendGameRelayAsync helper here tunneled UDP packets
    //  through the lobby WS when peer-to-peer hole-punching failed.
    //  With n2n the supernode handles all relaying transparently at
    //  the IP layer, so the launcher doesn't need a Worker-side game
    //  relay path anymore. The Worker still understands the frame for
    //  legacy clients but new launchers never send it.)

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
            //
            // The counter is reset by a connection that LASTED, never by one that merely
            // opened — see StableConnectionMs. Resetting on the upgrade alone is what turned
            // this into a flat 1 req/s loop against a room the server had already deleted.
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
        // Read on the upgrade request: the room socket is refused with 4010 when the build is
        // below the server's minimum, before the room ever sees it.
        _ws.Options.SetRequestHeader(
            "X-Launcher-Version", LauncherUpdateService.CurrentInformationalTag);

        await _ws.ConnectAsync(_uri, ct);
        var connectedAt = Environment.TickCount64;

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
            // Here rather than after ConnectAsync: by now we know whether the connection was
            // real or whether the server hung up on us the instant it opened.
            if (Environment.TickCount64 - connectedAt >= StableConnectionMs) _attempt = 0;
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
