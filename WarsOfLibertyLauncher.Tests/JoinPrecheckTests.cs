using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What pressing <em>Join</em> does when the session already claims a room.
///
/// <para><b>The bug these exist for.</b> A player was shown "No se pudo unir a la sala" over
/// the raw English "Leave the current lobby first." with one OK button, while the server's own
/// presence panel listed him as being in no room at all. His client had drifted into
/// <c>Lobby != Idle</c> with <c>CurrentLobbyId == null</c> — a pair the only recovery path then
/// refused to repair — so every join for the rest of the process threw the same sentence.</para>
///
/// <para>Three of the four answers are a refusal or a repair, and those are the point: the
/// happy path was never the thing that was broken.</para>
/// </summary>
public class JoinPrecheckTests
{
    [Fact]
    public void NotInARoomJoinsStraightAway()
        => Assert.Equal(
            JoinPrecheck.Decision.Proceed,
            JoinPrecheck.Decide(isInLobby: false, hasLobbyId: false, matchInProgress: false, resultHold: false));

    /// <summary>
    /// THE ONE THAT MATTERS. Claiming a room with no id for it is the wedged state itself.
    /// There is no room, so there is nothing to ask about — clear it and carry on. A confirm
    /// here would name a room that does not exist, and an outright refusal is what the player
    /// was already stuck behind.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ClaimingARoomWithNoIdIsRepairedRatherThanRefused()
        => Assert.Equal(
            JoinPrecheck.Decision.SelfHeal,
            JoinPrecheck.Decide(isInLobby: true, hasLobbyId: false, matchInProgress: false, resultHold: false));

    /// <summary>
    /// THE OTHER ONE THAT MATTERS, and the reason the order inside <c>Decide</c> is fixed.
    /// The reported wedge came from pressing Leave during a countdown, which leaves the match
    /// phase every bit as stale as the session state it drifted from. If the match check ran
    /// first, one stale flag would hold the other hostage and the player would be told to
    /// finish a match that is not running — for ever, since nothing else resets either.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void THE_ONE_THAT_MATTERS_AStaleMatchPhaseCannotBlockTheRepair(bool matchInProgress, bool resultHold)
        => Assert.Equal(
            JoinPrecheck.Decision.SelfHeal,
            JoinPrecheck.Decide(isInLobby: true, hasLobbyId: false, matchInProgress, resultHold));

    /// <summary>
    /// A real room and a real match: refuse. Leaving now can cost both players the result,
    /// which is exactly what the post-match hold exists to prevent.
    /// </summary>
    [Fact]
    public void ALiveMatchIsNotWalkedOutOfForAClickOnJoin()
        => Assert.Equal(
            JoinPrecheck.Decision.BlockedInMatch,
            JoinPrecheck.Decide(isInLobby: true, hasLobbyId: true, matchInProgress: true, resultHold: false));

    /// <summary>A result that has not been sent yet holds the room just as a live match does.</summary>
    [Fact]
    public void AnUnsentResultAlsoBlocks()
        => Assert.Equal(
            JoinPrecheck.Decision.BlockedInMatch,
            JoinPrecheck.Decide(isInLobby: true, hasLobbyId: true, matchInProgress: false, resultHold: true));

    /// <summary>
    /// Genuinely in another idle room — the only case where there is something real to lose,
    /// so it is the only case that asks. Leaving a room you host closes or migrates it for
    /// everyone still inside, which is why this is never silent.
    /// </summary>
    [Fact]
    public void ReallyInAnotherIdleRoomAsksFirst()
        => Assert.Equal(
            JoinPrecheck.Decision.Confirm,
            JoinPrecheck.Decide(isInLobby: true, hasLobbyId: true, matchInProgress: false, resultHold: false));

    /// <summary>
    /// Nothing about a match can reach the decision while we are not in a lobby at all —
    /// otherwise a stale phase left over from a previous room would refuse a perfectly
    /// ordinary join.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AStaleMatchPhaseWithNoRoomNeverBlocksAJoin(bool matchInProgress, bool resultHold)
        => Assert.Equal(
            JoinPrecheck.Decision.Proceed,
            JoinPrecheck.Decide(isInLobby: false, hasLobbyId: false, matchInProgress, resultHold));
}
