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
}
