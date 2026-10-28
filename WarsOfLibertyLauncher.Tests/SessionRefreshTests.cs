using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Renewing the session token against a real socket, and the sign-in that must not destroy a
/// session it was started to renew.
///
/// <para><b>The refusals are the point</b>, as in <see cref="SessionRejectionTests"/>: a server
/// without the route (every server today) must change nothing and must not be asked on every tick;
/// a token that is not due must send nothing; a cancelled or failed re-sign-in must leave a valid
/// session signed in — it used to sign the player out of a session that still had hours on it.</para>
/// </summary>
[Collection("wpf-and-language")]
public class SessionRefreshTests
{
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>A server that answers every request with <paramref name="answer"/> and records the
    /// request heads it was sent.</summary>
    private static (string BaseUrl, Task Server, CancellationTokenSource Stop, ConcurrentQueue<string> Requests) Serve(
        Func<string, (int Status, string Reason, string Body)> answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var stop = new CancellationTokenSource();
        var requests = new ConcurrentQueue<string>();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    using var stream = client.GetStream();
                    var buffer = new byte[8192];
                    var read = 0;
                    while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n"))
                    {
                        var n = await stream.ReadAsync(buffer.AsMemory(read), stop.Token);
                        if (n == 0) break;
                        read += n;
                    }
                    var head = Encoding.ASCII.GetString(buffer, 0, read);
                    requests.Enqueue(head);
                    var (status, reason, body) = answer(head);
                    var response = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\n"
                        + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n" + body;
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally { listener.Stop(); }
        });
        return ($"http://127.0.0.1:{port}", server, stop, requests);
    }

    private static LauncherConfig SignedIn(string baseUrl, string token, long expiresAt)
    {
        var config = new LauncherConfig();
        config.Multiplayer.LobbyBaseUrl = baseUrl;
        config.Multiplayer.SessionToken = token;
        config.Multiplayer.SessionExpiresAt = expiresAt;
        config.Multiplayer.CachedUser = new LobbyUserSummary { Id = "me", DiscordUsername = "me" };
        return config;
    }

    private static async Task Finish(CancellationTokenSource stop, Task server)
    {
        stop.Cancel();
        try { await server; } catch { }
    }

    [Fact]
    public async Task ARenewalSwapsTheTokenEverywhereAndTellsTheSockets()
    {
        var newExpiry = Now + 7 * 86_400;
        var (url, server, stop, requests) = Serve(_ => (200, "OK",
            $"{{\"status\":\"ok\",\"token\":\"token-NEW\",\"expires_at\":{newExpiry}," +
            "\"user\":{\"id\":\"me\",\"discord_username\":\"me-renamed\"}}"));
        try
        {
            var config = SignedIn(url, "token-OLD", Now + 3600);
            var session = new MultiplayerSession(config);
            string? told = null;
            session.SessionRefreshed += (_, t) => told = t;

            Assert.True(await session.TryRefreshAsync("test"));

            Assert.Equal("token-NEW", config.Multiplayer.SessionToken);
            Assert.Equal(newExpiry, config.Multiplayer.SessionExpiresAt);
            Assert.Equal("token-NEW", session.Api.SessionToken);
            Assert.Equal("token-NEW", told);
            Assert.Equal("me-renamed", session.CurrentUser?.DiscordUsername);
            Assert.True(requests.TryDequeue(out var head));
            Assert.StartsWith("POST /auth/refresh ", head);
            Assert.Contains("Bearer token-OLD", head);
        }
        finally { await Finish(stop, server); }
    }

    /// <summary>
    /// THE ONE THAT MATTERS for every launcher talking to today's server: no route, no change — and
    /// no request on every tick after that.
    /// </summary>
    [Fact]
    public async Task THE_ONE_THAT_MATTERS_AServerWithoutTheRouteChangesNothing()
    {
        var (url, server, stop, requests) = Serve(_ => (404, "Not Found",
            "{\"message\":\"Route POST:/auth/refresh not found\",\"error\":\"Not Found\",\"statusCode\":404}"));
        try
        {
            var config = SignedIn(url, "token-OLD", Now + 3600);
            var session = new MultiplayerSession(config);
            var rejected = 0;
            session.SessionRejected += (_, _) => rejected++;

            Assert.False(await session.TryRefreshAsync("test"));
            Assert.False(await session.TryRefreshAsync("test again"));

            Assert.Equal("token-OLD", config.Multiplayer.SessionToken);
            Assert.Equal(MultiplayerSession.SessionStatus.SignedIn, session.Status);
            Assert.Equal(0, rejected);
            Assert.Single(requests);
        }
        finally { await Finish(stop, server); }
    }

    [Fact]
    public async Task ARefusedTokenIsARejectionExactlyAsBefore()
    {
        var (url, server, stop, _) = Serve(_ => (401, "Unauthorized",
            "{\"code\":\"unauthorized\",\"message\":\"Authentication required.\"}"));
        try
        {
            var session = new MultiplayerSession(SignedIn(url, "token-OLD", Now + 3600));
            var rejected = 0;
            session.SessionRejected += (_, _) => rejected++;

            Assert.False(await session.TryRefreshAsync("test"));
            Assert.Equal(1, rejected);
        }
        finally { await Finish(stop, server); }
    }

    [Fact]
    public async Task ATokenThatIsNotDueSendsNothing()
    {
        var (url, server, stop, requests) = Serve(_ => (500, "Error", "{}"));
        try
        {
            var session = new MultiplayerSession(SignedIn(url, "token-OLD", Now + 6 * 86_400));
            Assert.False(await session.TryRefreshAsync("test"));
            Assert.Empty(requests);
        }
        finally { await Finish(stop, server); }
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the warning before a room: "Sign in again" starts the Discord flow
    /// over a session that is still valid. A cancel, or a server that does not answer, must leave it
    /// signed in — it used to land on SignedOut (or stay SigningIn for good).
    /// </summary>
    [Fact]
    public async Task THE_ONE_THAT_MATTERS_AFailedReSignInKeepsAValidSession()
    {
        var (url, server, stop, _) = Serve(_ => (500, "Error", "{\"code\":\"internal\",\"message\":\"boom\"}"));
        try
        {
            var config = SignedIn(url, "token-OLD", Now + 3600);
            var session = new MultiplayerSession(config);

            // The start request fails.
            await Assert.ThrowsAsync<LobbyApiException>(() => session.StartSignInAsync());
            Assert.Equal(MultiplayerSession.SessionStatus.SignedIn, session.Status);
            Assert.Equal("me", session.CurrentUser?.Id);
            Assert.Equal("token-OLD", session.Api.SessionToken);

            // The player cancels while the flow waits for the browser.
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var start = new DeviceFlowStart { PollHandle = "h", IntervalSeconds = 10, ExpiresInSeconds = 60 };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.CompleteSignInAsync(start, cancelled.Token));
            Assert.Equal(MultiplayerSession.SessionStatus.SignedIn, session.Status);
            Assert.Equal("token-OLD", config.Multiplayer.SessionToken);
        }
        finally { await Finish(stop, server); }
    }

    [Fact]
    public async Task AFailedFirstSignInStillEndsSignedOut()
    {
        var (url, server, stop, _) = Serve(_ => (500, "Error", "{\"code\":\"internal\",\"message\":\"boom\"}"));
        try
        {
            var config = new LauncherConfig();
            config.Multiplayer.LobbyBaseUrl = url;
            var session = new MultiplayerSession(config);

            await Assert.ThrowsAsync<LobbyApiException>(() => session.StartSignInAsync());
            Assert.Equal(MultiplayerSession.SessionStatus.SignedOut, session.Status);
        }
        finally { await Finish(stop, server); }
    }
}
