using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

using Warning = WarsOfLibertyLauncher.Services.Multiplayer.RoomMatchState.LeaveWarning;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RoomMatchState"/> — what to do when the ROOM is in a match and YOUR game is
/// not.
///
/// <para>That state is reached whenever Age of Empires III closes on its own: a crash, a
/// mis-click, quitting to the desktop from the LAN screen. A guest who landed there was stuck —
/// the Start button belongs to the host, so there was nothing to press, and leaving the room to
/// re-join is refused by the backend with <c>Conflict('Lobby already in game.')</c> until the
/// match ends.</para>
/// </summary>
public class RoomMatchStateTests
{
    // ---------- ShouldOfferRejoin ----------

    [Fact]
    public void AGuestWhoseGameClosedWhileTheRoomPlaysOn_IsOfferedTheWayBack()
        => Assert.True(RoomMatchState.ShouldOfferRejoin(
            roomMatchLive: true, ourGameRunning: false, weAreHost: false));

    /// <summary>
    /// <b>Never the host</b>, and this is a rule about the protocol rather than about tidiness:
    /// the host is the LAN server, so when their game closes the launcher tells the backend the
    /// match ended, the room reopens, and everyone relaunches together — which is what their own
    /// Start button already does. A second button would either duplicate it or, worse, put the
    /// host back into a match the others had already been thrown out of.
    /// </summary>
    [Fact]
    public void TheHostIsNeverOfferedIt()
        => Assert.False(RoomMatchState.ShouldOfferRejoin(
            roomMatchLive: true, ourGameRunning: false, weAreHost: true));

    [Fact]
    public void NotWhileOurOwnGameIsStillRunning()
        => Assert.False(RoomMatchState.ShouldOfferRejoin(
            roomMatchLive: true, ourGameRunning: true, weAreHost: false));

    [Fact]
    public void NotWhenTheRoomIsNotInAMatch()
        => Assert.False(RoomMatchState.ShouldOfferRejoin(
            roomMatchLive: false, ourGameRunning: false, weAreHost: false));

    // ---------- WarnOnLeave ----------

    [Fact]
    public void LeavingAnIdleRoom_AsksNothing()
        => Assert.Equal(Warning.None, RoomMatchState.WarnOnLeave(
            roomMatchLive: false, ourGameRunning: false, weAreHost: false));

    /// <summary>
    /// The incident this warning was written for: the host walked out of the lobby mid-match,
    /// which closed Age of Empires III for every player at once and left the game with no winner.
    /// </summary>
    [Fact]
    public void AHostPlaying_IsToldItEndsForEveryone()
        => Assert.Equal(Warning.HostEndsForEveryone, RoomMatchState.WarnOnLeave(
            roomMatchLive: true, ourGameRunning: true, weAreHost: true));

    [Fact]
    public void AGuestPlaying_IsToldOnlyTheirOwnGameCloses()
        => Assert.Equal(Warning.GuestLeavesMatch, RoomMatchState.WarnOnLeave(
            roomMatchLive: true, ourGameRunning: true, weAreHost: false));

    /// <summary>
    /// The one nobody can guess: with your own game already closed, leaving looks free, and it is
    /// in fact one-way until the match ends.
    /// </summary>
    [Fact]
    public void OurGameClosedButTheRoomStillPlaying_IsToldItIsOneWay()
        => Assert.Equal(Warning.RoomStillPlayingCannotRejoin, RoomMatchState.WarnOnLeave(
            roomMatchLive: true, ourGameRunning: false, weAreHost: false));

    /// <summary>
    /// A running game outranks a running room, for host and guest alike: leaving kills it either
    /// way, and that is the more urgent thing to say. "You will not be able to come back" only
    /// becomes the point once there is nothing left to kill.
    /// </summary>
    [Theory]
    [InlineData(true, Warning.HostEndsForEveryone)]
    [InlineData(false, Warning.GuestLeavesMatch)]
    public void AGameOfOurOwnOutranksTheRoom(bool weAreHost, Warning expected)
        => Assert.Equal(expected, RoomMatchState.WarnOnLeave(
            roomMatchLive: true, ourGameRunning: true, weAreHost));

    // ---------- HoldLeave ----------
    //
    // Holding somebody in a room is the most intrusive thing in this file, so what these pin is
    // mostly the ways it must let go.
    //
    // It holds BOTH players now, for two different reasons. For the host it is correctness: the
    // server refuses a report from anyone who is no longer the room's host, and leaving hands
    // that role straight to the opponent, so walking out destroys the result for both of them.
    // For the guest it is information — their leaving costs nobody the report, only their own
    // sight of the result, which on a real match was sixteen seconds away when their game closed.

    [Fact]
    public void ACompetitiveHostIsHeldWhileTheResultIsStillBeingSettled()
        => Assert.True(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.ReadingRecording, secondsSinceGameExit: 3));

    /// <summary>
    /// The guest's half, and the reason this stopped being host-only. Their game closes first —
    /// the player who lost leaves first — so they reach this moment with nothing outstanding of
    /// their own and everything still to learn.
    /// </summary>
    [Fact]
    public void AGuestIsHeldWhileWaitingForTheHostToReport()
        => Assert.True(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.WaitingForHost, secondsSinceGameExit: 3));

    /// <summary>
    /// <b>The one that matters most for the guest, and the reason their hold may never be
    /// lengthened.</b> A host waits on his own machine reading his own recording. A guest waits
    /// on WHEN THE OTHER PLAYER CLOSES HIS GAME — minutes, or never if he force-quits. Holding
    /// somebody on something a third party controls is how a player ends up trapped in a room.
    /// </summary>
    [Fact]
    public void TheGuestIsReleasedAtTheCeilingEvenThoughNothingArrived()
        => Assert.False(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.WaitingForHost,
            secondsSinceGameExit: RoomMatchState.ResultGraceSeconds + 0.1));

    /// <summary>
    /// <b>The ceiling, and the one that matters most.</b> Everything above can stall — a folder
    /// of half-written recordings, a server that never answers — and a player shut in a room by a
    /// bug of ours is a worse outcome than a lost rating. Past the grace it lets go regardless of
    /// what is still outstanding.
    /// </summary>
    [Fact]
    public void PastTheGraceItLetsGoNoMatterWhatIsStillOutstanding()
        => Assert.False(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.SendingResult,
            secondsSinceGameExit: RoomMatchState.ResultGraceSeconds + 0.1));

    /// <summary>Nothing outstanding, so nothing to wait for — the ordinary case, and it must be free.</summary>
    [Fact]
    public void WithTheResultSettledLeavingIsImmediate()
        => Assert.False(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.None, secondsSinceGameExit: 0));

    /// <summary>
    /// A casual room has no rating to protect, and being held anywhere is an annoyance. This is
    /// what keeps the whole feature invisible to everyone who did not opt into it — and it is
    /// the only clause left that can refuse outright, now that being a guest no longer does.
    /// </summary>
    [Theory]
    [InlineData(RoomMatchState.ResultPhase.ReadingRecording)]
    [InlineData(RoomMatchState.ResultPhase.SendingResult)]
    [InlineData(RoomMatchState.ResultPhase.WaitingForHost)]
    public void ACasualRoomIsNeverHeld(RoomMatchState.ResultPhase phase)
        => Assert.False(RoomMatchState.HoldLeave(
            competitive: false, phase: phase, secondsSinceGameExit: 1));

    /// <summary>
    /// The boundary itself, spelled out: at exactly the grace it is already released. Written
    /// down because "&lt;" versus "&lt;=" here is the difference between a hold that ends and one
    /// that can sit on the edge forever if the clock stops advancing.
    /// </summary>
    [Fact]
    public void TheGraceBoundaryIsExclusive()
        => Assert.False(RoomMatchState.HoldLeave(
            competitive: true,
            phase: RoomMatchState.ResultPhase.ReadingRecording,
            secondsSinceGameExit: RoomMatchState.ResultGraceSeconds));

    /// <summary>
    /// The hold is meant to be brief. A ceiling of minutes would be a different feature — one
    /// nobody agreed to — so the number itself is pinned rather than left to drift.
    /// </summary>
    [Fact]
    public void TheHoldIsShort()
        => Assert.InRange(RoomMatchState.ResultGraceSeconds, 5, 60);

    /// <summary>
    /// The two ceilings answer different questions and must not be collapsed into one. The grace
    /// is how long a BUTTON may be held shut, which has to be short because it takes a choice
    /// away. The wait is how long a LINE OF TEXT may say "waiting", which costs the player
    /// nothing — so it can afford to outlast a host still reading his score screen.
    /// </summary>
    [Fact]
    public void TheWaitOutlastsTheHoldButIsStillBounded()
    {
        Assert.True(RoomMatchState.ResultWaitCeilingSeconds > RoomMatchState.ResultGraceSeconds);
        Assert.InRange(RoomMatchState.ResultWaitCeilingSeconds, 60, 600);
    }
}
