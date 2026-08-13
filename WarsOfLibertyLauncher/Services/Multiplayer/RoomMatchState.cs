namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The two rules that follow from a room being in a match while YOUR game is not.
///
/// <para>That state exists because Age of Empires III can close on its own — a crash, a
/// mis-click, quitting to the desktop from the LAN screen — and the launcher then drops you back
/// into the lobby with the match still running for everybody else. Until this, a guest in that
/// state was stuck: the Start button belongs to the host, so there was nothing to press, and
/// leaving the room to re-join is refused by the backend with
/// <c>Conflict('Lobby already in game.')</c> for as long as the match lasts.</para>
///
/// <para>Pure and free of WPF so both rules can be tested, which matters more than their size
/// suggests: one decides whether a player is offered their way back, and the other is the last
/// thing standing between a mis-click and cutting a match short for everyone in it.</para>
/// </summary>
public static class RoomMatchState
{
    /// <summary>
    /// Whether to offer "open the game" — the button that relaunches AoE3 without touching the
    /// room or the server.
    ///
    /// <para><b>Never to the host</b>, and that is a rule about the protocol rather than about
    /// tidiness. The host is the LAN server: when their game closes, the launcher tells the
    /// backend the match ended, the room goes back to open, and everyone has to launch again —
    /// which is exactly what their own Start button does. A separate button for them would
    /// either duplicate Start or, worse, relaunch only their game into a match the others were
    /// no longer in.</para>
    /// </summary>
    public static bool ShouldOfferRejoin(bool roomMatchLive, bool ourGameRunning, bool weAreHost)
        => roomMatchLive && !ourGameRunning && !weAreHost;

    /// <summary>What leaving the room right now would cost, and therefore which warning to show.</summary>
    public enum LeaveWarning
    {
        /// <summary>Nothing is running. Leaving is ordinary — don't ask.</summary>
        None,

        /// <summary>We are the host and playing: leaving closes the game for every player.</summary>
        HostEndsForEveryone,

        /// <summary>We are a guest and playing: leaving closes our own game only.</summary>
        GuestLeavesMatch,

        /// <summary>
        /// Our game is already closed but the room is still playing — the case nobody can guess,
        /// because leaving looks free and is in fact one-way until the match ends.
        /// </summary>
        RoomStillPlayingCannotRejoin,
    }

    /// <summary>
    /// Which warning leaving the room deserves.
    ///
    /// <para>Order matters: a game of our own that is running is the more urgent fact, since
    /// leaving kills it (and, for a host, everyone else's too). Only once ours is already closed
    /// does "you will not be able to come back" become the thing worth saying.</para>
    /// </summary>
    public static LeaveWarning WarnOnLeave(bool roomMatchLive, bool ourGameRunning, bool weAreHost)
    {
        if (ourGameRunning)
            return weAreHost ? LeaveWarning.HostEndsForEveryone : LeaveWarning.GuestLeavesMatch;

        return roomMatchLive ? LeaveWarning.RoomStillPlayingCannotRejoin : LeaveWarning.None;
    }
}
