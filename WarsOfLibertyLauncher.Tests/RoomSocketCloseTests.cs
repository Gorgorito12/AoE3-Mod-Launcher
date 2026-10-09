using System;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;
using Kind = WarsOfLibertyLauncher.Services.Multiplayer.RoomSocketClose.Kind;
using Status = WarsOfLibertyLauncher.Services.Multiplayer.MultiplayerSession.LobbyStatus;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What a room socket's close means, and what the tab does with one event — the table the three
/// handlers share.
///
/// <para><b>The rejections are the point.</b> A close treated as terminal that is not strands a
/// player in a dead room over a code nobody meant as final; a stale socket treated as current
/// stops the NEW room's socket, which is the bug that made "create a room from inside another
/// one" produce a room dead on arrival.</para>
/// </summary>
public class RoomSocketCloseTests
{
    [Theory]
    [InlineData("server_close:4007", Kind.MatchReported)]
    [InlineData("server_close:4404", Kind.Gone)]
    [InlineData("server_close:4006", Kind.Gone)]
    [InlineData("server_close:4002", Kind.MembershipLost)]
    [InlineData("server_close:4004", Kind.MembershipLost)]
    [InlineData("server_close:4010", Kind.TooOld)]
    // Everything else reconnects — including codes the backend does send, and every transport
    // error, whose message is localized and free text.
    [InlineData("server_close:4001", Kind.Transient)]
    [InlineData("server_close:4003", Kind.Transient)]
    [InlineData("server_close:4005", Kind.Transient)]
    [InlineData("server_close:4009", Kind.Transient)]
    [InlineData("server_close:1000", Kind.Transient)]
    [InlineData("The operation was canceled.", Kind.Transient)]
    [InlineData("Unable to connect to the remote server", Kind.Transient)]
    [InlineData("", Kind.Transient)]
    [InlineData(null, Kind.Transient)]
    // A near miss is not the code: the match is whole, never a prefix.
    [InlineData("server_close:40040", Kind.Transient)]
    [InlineData("server_close:4404 ", Kind.Transient)]
    public void TheWholeTable(string? reason, Kind expected)
        => Assert.Equal(expected, RoomSocketClose.Classify(reason));

    /// <summary>
    /// THE ONE THAT MATTERS: a close from a socket that is no longer this room's never acts on the
    /// room. The old socket's abort raises one last Disconnected while the tab is still subscribed,
    /// and acting on it stopped the new room's socket.
    /// </summary>
    [Theory]
    [InlineData("server_close:4006")]
    [InlineData("server_close:4404")]
    [InlineData("server_close:4004")]
    [InlineData("server_close:4010")]
    [InlineData("server_close:4007")]
    public void THE_ONE_THAT_MATTERS_ALateCloseFromThePreviousRoomNeverTouchesTheNewOne(string reason)
    {
        foreach (var inMatch in new[] { false, true })
        {
            var live = RoomSocketEvents.Route(isCurrent: false, isStopped: false, reason, inMatch);
            Assert.Equal(RoomSocketAction.StopStaleSender, live);

            // Already stopped — the usual case, since the swap disposed it — is just ignored.
            var dead = RoomSocketEvents.Route(isCurrent: false, isStopped: true, reason, inMatch);
            Assert.Equal(RoomSocketAction.IgnoreStale, dead);
        }
    }

    [Fact]
    public void AStaleTransientDropIsIgnoredAndNeverPaintsReconnecting()
    {
        Assert.Equal(RoomSocketAction.IgnoreStale,
            RoomSocketEvents.Route(false, false, "Unable to connect", inMatchOrResult: false));
        Assert.False(RoomSocketEvents.ShouldShowReconnecting(isCurrent: false, isStopped: false));
    }

    /// <summary>A socket WE stopped drops its connection when it is aborted — that is not a reconnect.</summary>
    [Fact]
    public void AStoppedCurrentSocketsLastDropPaintsNothing()
    {
        Assert.Equal(RoomSocketAction.IgnoreStopped,
            RoomSocketEvents.Route(isCurrent: true, isStopped: true, "server_close:1000", false));
        Assert.False(RoomSocketEvents.ShouldShowReconnecting(isCurrent: true, isStopped: true));
        Assert.True(RoomSocketEvents.ShouldShowReconnecting(isCurrent: true, isStopped: false));
    }

    /// <summary>
    /// The terminal branches still run on a stopped socket: stopping is what a terminal close
    /// does, and the order the events arrive in must not change what they mean.
    /// </summary>
    [Theory]
    [InlineData("server_close:4404", RoomSocketAction.Gone)]
    [InlineData("server_close:4006", RoomSocketAction.Gone)]
    [InlineData("server_close:4002", RoomSocketAction.MembershipLost)]
    [InlineData("server_close:4004", RoomSocketAction.MembershipLost)]
    [InlineData("server_close:4010", RoomSocketAction.TooOld)]
    public void ATerminalCloseOnTheCurrentSocketIsTerminalStoppedOrNot(string reason, RoomSocketAction expected)
    {
        Assert.Equal(expected, RoomSocketEvents.Route(true, false, reason, false));
        Assert.Equal(expected, RoomSocketEvents.Route(true, true, reason, false));
    }

    /// <summary>
    /// 4007 is also the KICK code: outside a match it is an ordinary drop, because the kick's own
    /// frame has already closed the window.
    /// </summary>
    [Fact]
    public void FourThousandSevenIsTheResultOnlyInAMatch()
    {
        Assert.Equal(RoomSocketAction.MatchReported,
            RoomSocketEvents.Route(true, false, "server_close:4007", inMatchOrResult: true));
        Assert.Equal(RoomSocketAction.Transient,
            RoomSocketEvents.Route(true, false, "server_close:4007", inMatchOrResult: false));
    }

    /// <summary>
    /// The tab reads <see cref="LobbyWebSocket.IsStopped"/> on a socket the session may already
    /// have disposed. Reading a disposed CancellationTokenSource's flag must not throw.
    /// </summary>
    [Fact]
    public async Task IsStoppedIsSafeAfterDispose()
    {
        var socket = new LobbyWebSocket(
            new Uri("ws://127.0.0.1:9/lobbies/x/ws"), LobbyWebSocket.HelloMode.SessionToken, "t");
        Assert.False(socket.IsStopped);
        await socket.DisposeAsync();
        Assert.True(socket.IsStopped);
        socket.StopReconnect();
        Assert.True(socket.IsStopped);
    }

    [Theory]
    [InlineData(Status.Idle, false)]
    [InlineData(Status.Joining, true)]
    [InlineData(Status.InLobby, true)]
    [InlineData(Status.InGame, true)]
    [InlineData(Status.Leaving, true)]
    public void TheRoomWindowFollowsTheSession(Status status, bool inARoom)
        => Assert.Equal(inARoom, RoomWindowRule.InARoom(status));

    [Fact]
    public void EveryLobbyStatusHasAnAnswer()
    {
        // A status added later must be decided on purpose, not inherit "not in a room".
        foreach (var status in Enum.GetValues<Status>())
            Assert.Equal(status != Status.Idle, RoomWindowRule.InARoom(status));
    }
}
