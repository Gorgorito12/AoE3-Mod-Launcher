using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A session the server refuses signs the player out, with a reason — instead of the chat
/// sitting on "Connecting…" with nobody online for ever while everything public keeps answering.
/// It happened to every launcher at once when the server's signing key was changed.
///
/// <para>The REFUSALS are the point here, as much as the sign-out: a 401 the launcher produced
/// itself, a 401 for a token the player has since replaced, and a rejection in the middle of a
/// match must all leave the session alone.</para>
/// </summary>
[Collection("wpf-and-language")]
public class SessionRejectionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(401, true, true)]
    [InlineData(401, false, false)]   // the launcher's own "sign in first", no request sent
    [InlineData(403, true, false)]
    [InlineData(404, true, false)]
    [InlineData(500, true, false)]
    public void OnlyA401ToARequestThatCarriedATokenIsARejection(int status, bool sentToken, bool expected)
        => Assert.Equal(expected, LobbyApiClient.IsSessionRejection(status, sentToken));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ASessionIsOnlySignedOutOutsideARoomAndAMatch(bool inRoom, bool matchActive, bool expected)
        => Assert.Equal(expected, SessionRejection.ShouldExpireNow(inRoom, matchActive));

    /// <summary>A server that answers every request 401 with the body the real one sends.</summary>
    private static (string BaseUrl, Task Server, CancellationTokenSource Stop) Answer401()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var stop = new CancellationTokenSource();
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
                    var body = "{\"code\":\"unauthorized\",\"message\":\"Authentication required.\"}";
                    var response = "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\n"
                        + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n" + body;
                    var bytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(bytes, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally { listener.Stop(); }
        });
        return ($"http://127.0.0.1:{port}", server, stop);
    }

    [Fact]
    public async Task THE_ONE_THAT_MATTERS_A401ForOurTokenSignsUsOutAndAnythingElseDoesNot()
    {
        var (url, server, stop) = Answer401();
        try
        {
            // Our token, refused by the server: the client says so, and the session passes it on.
            var config = SignedInConfig(url, "token-A");
            var session = new MultiplayerSession(config);
            Assert.Equal(MultiplayerSession.SessionStatus.SignedIn, session.Status);
            var rejected = 0;
            session.SessionRejected += (_, _) => rejected++;
            var ex = await Assert.ThrowsAsync<LobbyApiException>(() => session.Api.GetMeAsync());
            Assert.Equal(401, ex.Status);
            Assert.Equal(1, rejected);

            // The same 401 for a token the player has since replaced (a request that outlived a
            // new sign-in) must not sign the fresh session out.
            string? sent = null;
            session.Api.SessionRejected += t => sent = t;
            config.Multiplayer.SessionToken = "token-B";
            await Assert.ThrowsAsync<LobbyApiException>(() => session.Api.GetMeAsync());
            Assert.Equal("token-A", sent);
            Assert.Equal(1, rejected);

            // No token at all: the client refuses before sending anything, and that is the
            // launcher talking, not the server.
            var anonymous = new LobbyApiClient(url, sessionToken: null);
            var raised = false;
            anonymous.SessionRejected += _ => raised = true;
            await Assert.ThrowsAsync<LobbyApiException>(() => anonymous.GetMeAsync());
            Assert.False(raised);
        }
        finally
        {
            stop.Cancel();
            try { await server; } catch { }
        }
    }

    [Fact]
    public void ExpiringASessionSignsOutWithTheReasonOnIt()
    {
        var config = SignedInConfig("https://example.invalid", "token-A");
        var session = new MultiplayerSession(config);
        var changes = 0;
        string? errorWhenRaised = null;
        session.StateChanged += (_, _) =>
        {
            changes++;
            errorWhenRaised = session.LastError;
        };

        session.ExpireSession("Your multiplayer session ended.");

        Assert.Equal(MultiplayerSession.SessionStatus.SignedOut, session.Status);
        Assert.Equal("", config.Multiplayer.SessionToken);
        Assert.Null(config.Multiplayer.CachedUser);
        Assert.Equal(1, changes);
        // Set BEFORE the change was raised — the sign-in panel reads it while repainting.
        Assert.Equal("Your multiplayer session ended.", errorWhenRaised);
    }

    /// <summary>
    /// The tab signs out at once outside a match, and waits for the match to end inside one: a
    /// guest's room socket rides a join token that still works, so signing out mid-match would cut
    /// the match off for a token nothing in it needs.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ARejectionDuringAMatchWaitsForItToEnd()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // Outside any room: signed out on the spot.
            var tab = new MultiplayerTab();
            var session = new MultiplayerSession(SignedInConfig("https://example.invalid", "token-A"));
            typeof(MultiplayerTab).GetField("_session", Private)!.SetValue(tab, session);
            Call(tab, "OnSessionRejected", "test");
            Assert.Equal(MultiplayerSession.SessionStatus.SignedOut, session.Status);
            Assert.False(string.IsNullOrEmpty(session.LastError));

            // In a match: nothing yet, a pending flag instead.
            var tab2 = new MultiplayerTab();
            var session2 = new MultiplayerSession(SignedInConfig("https://example.invalid", "token-A"));
            typeof(MultiplayerTab).GetField("_session", Private)!.SetValue(tab2, session2);
            var phaseField = typeof(MultiplayerTab).GetField("_matchPhase", Private)!;
            var lobby = phaseField.GetValue(tab2)!;
            phaseField.SetValue(tab2, Enum.Parse(phaseField.FieldType, "InGame"));
            Call(tab2, "OnSessionRejected", "test");
            Assert.Equal(MultiplayerSession.SessionStatus.SignedIn, session2.Status);
            Assert.True((bool)typeof(MultiplayerTab).GetField("_sessionRejectedPending", Private)!.GetValue(tab2)!);

            // The match ends: the next chance to apply it signs out.
            phaseField.SetValue(tab2, lobby);
            Call(tab2, "MaybeApplyPendingSessionRejection");
            Assert.Equal(MultiplayerSession.SessionStatus.SignedOut, session2.Status);
            Assert.False((bool)typeof(MultiplayerTab).GetField("_sessionRejectedPending", Private)!.GetValue(tab2)!);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The global chat is the one place the server says <c>invalid_token</c> (REST answers 401
    /// <c>unauthorized</c> instead), and it used to be only logged. Read from the source because
    /// reaching the frame handler needs a live socket.
    /// </summary>
    [Fact]
    public void TheGlobalChatsInvalidTokenFrameEndsTheSession()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs")).Replace("\r\n", "\n");
        var errorCase = source.IndexOf("case \"error\":", StringComparison.Ordinal);
        Assert.True(errorCase > 0);
        var handled = source.IndexOf("if (code == \"invalid_token\")", errorCase, StringComparison.Ordinal);
        var rejected = source.IndexOf("OnSessionRejected(", handled, StringComparison.Ordinal);
        Assert.True(handled > errorCase && rejected > handled && rejected - handled < 200,
            "the global chat's invalid_token frame must end the session");
    }

    private static LauncherConfig SignedInConfig(string baseUrl, string token)
    {
        var config = new LauncherConfig();
        config.Multiplayer.LobbyBaseUrl = baseUrl;
        config.Multiplayer.SessionToken = token;
        config.Multiplayer.SessionExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        config.Multiplayer.CachedUser = new Models.Multiplayer.LobbyUserSummary { Id = "me", DiscordUsername = "me" };
        return config;
    }

    private static void Call(MultiplayerTab tab, string method, params object?[] args)
        => typeof(MultiplayerTab).GetMethod(method, Private)!.Invoke(tab, args);

    internal static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }
}
