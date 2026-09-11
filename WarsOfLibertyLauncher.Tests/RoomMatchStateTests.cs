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

    // ---------- ShouldKillOnRemoteCancel ----------
    //
    // Almost every case here is a REFUSAL, and that is the point: the method exists because the
    // launcher used to have no refusals at all. A game_cancelled frame closed whatever AoE3 was
    // running, and the reported shape was a player 25 minutes into a healthy 1v1 watching it
    // vanish because his opponent's window had shut a few seconds earlier. The three recordings
    // from that night each carry a complete, valid outcome block with two humans in it — the
    // engine finished the match and wrote the file; the launcher then closed it.

    /// <summary>
    /// THE ONE THAT MATTERS. <c>ended</c> means the HOST's game exited, which is a fact about the
    /// host's machine and says nothing about the match on this one. A long game is never closed
    /// by a remote frame.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ALongMatchIsNeverClosedBecauseSomebodyElsesGameEnded()
        => Assert.False(RoomMatchState.ShouldKillOnRemoteCancel("ended", secondsSinceLaunch: 1500));

    /// <summary>...and not early on either. "The host's game closed" is not a decision about us
    /// at any point in the match, so the grace window does not rescue this reason.</summary>
    [Fact]
    public void NorASecondsOldOneEither()
        => Assert.False(RoomMatchState.ShouldKillOnRemoteCancel("ended", secondsSinceLaunch: 10));

    /// <summary>
    /// The case the kill was written for, and it survives: somebody pressed Cancel during the
    /// countdown, so an AoE3 that just launched would otherwise sit forever hunting for peers
    /// who are never going to arrive.
    /// </summary>
    [Theory]
    [InlineData("host_cancelled")]
    [InlineData("aborted")]
    public void ADeliberateCancelInTheFirstSecondsStillClosesIt(string reason)
        => Assert.True(RoomMatchState.ShouldKillOnRemoteCancel(reason, secondsSinceLaunch: 4));

    /// <summary>
    /// But not once the match is real. The stranded-launch problem only exists at the start;
    /// past that the player is IN a game, and if the host truly walked out then closing it is
    /// the player's call. <see cref="RoomMatchState.PlayedMatchSeconds"/> beats every reason.
    /// </summary>
    [Theory]
    [InlineData("host_cancelled")]
    [InlineData("aborted")]
    [InlineData("ended")]
    public void PastThePlayedMatchThresholdNothingClosesIt(string reason)
    {
        Assert.False(RoomMatchState.ShouldKillOnRemoteCancel(
            reason, RoomMatchState.PlayedMatchSeconds));
        Assert.False(RoomMatchState.ShouldKillOnRemoteCancel(
            reason, RoomMatchState.PlayedMatchSeconds + 0.5));
    }

    /// <summary>
    /// A reason we do not recognise is not a licence to destroy something. A future server may
    /// send anything here, and the safe reading of a frame nobody has implemented yet is "leave
    /// the game alone" — the player can always close it, and nobody can un-close it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("host_disconnected")]
    [InlineData("Aborted")]        // case matters: the protocol's values are lower-case
    [InlineData("ended_by_server")]
    public void AnUnrecognisedReasonNeverClosesIt(string? reason)
        => Assert.False(RoomMatchState.ShouldKillOnRemoteCancel(reason, secondsSinceLaunch: 4));

    /// <summary>
    /// The threshold is shared with the report gate on purpose — a match long enough to be
    /// worth REPORTING is long enough to be worth PROTECTING — so it is pinned here rather
    /// than left to drift into a number that closes games the launcher has just called real.
    /// </summary>
    [Fact]
    public void TheThresholdIsTheSameOneThatDecidesAMatchIsWorthReporting()
        => Assert.Equal(180, RoomMatchState.PlayedMatchSeconds);

    // ---------- LeavingNowForfeits ----------

    /// <summary>
    /// THE ONE THAT MATTERS. Past the threshold, in a competitive 1v1, walking out is scored
    /// as a defeat — so the player has to be told BEFORE he does it, which is the entire
    /// difference between a rule and a trap.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_LeavingACompetitive1v1PastTheThresholdIsADefeat()
        => Assert.True(RoomMatchState.LeavingNowForfeits(
            competitive: true, abandonmentApplies: true, secondsIntoMatch: 600));

    /// <summary>
    /// A casual room has no rating to lose, so nothing is at stake and nothing may claim
    /// otherwise — whatever the format or how long the match has run.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACasualRoomNeverForfeits(bool abandonmentApplies)
        => Assert.False(RoomMatchState.LeavingNowForfeits(
            competitive: false, abandonmentApplies, secondsIntoMatch: 3600));

    /// <summary>
    /// A TEAM room never forfeits, and this is the refusal that was actually shipped broken:
    /// the server's <c>decideByAbandon</c> refuses anything but two participants, so for
    /// months the create-room hint and the lobby checklist threatened a 2v2 with a forfeit the
    /// backend was never going to carry out. Threatening what will not happen is worse than
    /// saying nothing.
    /// </summary>
    [Fact]
    public void ATeamRoomNeverForfeitsHoweverLongTheMatchRan()
        => Assert.False(RoomMatchState.LeavingNowForfeits(
            competitive: true, abandonmentApplies: false, secondsIntoMatch: 3600));

    /// <summary>
    /// Under the threshold nothing is forfeited. Measured on the incident that produced the
    /// backend fix: a player left at 4:40 and was charged a defeat because the rule was reading
    /// the REPORT's clock instead of the walkout's.
    /// </summary>
    [Fact]
    public void AWalkoutInsideTheFirstFiveMinutesIsNotAForfeit()
        => Assert.False(RoomMatchState.LeavingNowForfeits(
            competitive: true, abandonmentApplies: true, secondsIntoMatch: 280));

    /// <summary>
    /// The boundary is inclusive, and it errs the safe way: warning a shade too eagerly costs
    /// one extra confirmation, while warning too late costs a rating the player was never told
    /// about. The server's clock is the one that decides; this one only decides what to SAY.
    /// </summary>
    [Fact]
    public void TheThresholdIsInclusive()
    {
        Assert.True(RoomMatchState.LeavingNowForfeits(
            true, true, RoomMatchState.ForfeitAfterSeconds));
        Assert.False(RoomMatchState.LeavingNowForfeits(
            true, true, RoomMatchState.ForfeitAfterSeconds - 0.5));
    }

    /// <summary>
    /// Five minutes, in seconds — the launcher's copy of the server's
    /// <c>COMPETITIVE_ABANDON_SECONDS</c>. The two strings that spell it out in words are the
    /// third place it lives; move one and move all three.
    /// </summary>
    [Fact]
    public void TheThresholdIsFiveMinutes()
        => Assert.Equal(300, RoomMatchState.ForfeitAfterSeconds);

    /// <summary>
    /// A negative duration — clocks disagreeing about a room that started after somebody left
    /// it — must not read as a forfeit. Same safe direction the backend's own too-early branch
    /// takes.
    /// </summary>
    [Fact]
    public void ANonsensicalDurationNeverForfeits()
        => Assert.False(RoomMatchState.LeavingNowForfeits(true, true, -5));
}
