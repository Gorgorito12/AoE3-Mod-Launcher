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

    /// <summary>
    /// The longest the launcher may hold a competitive room open after the game closes.
    ///
    /// <para>A ceiling, not a duration. In the good case — the recording is there and readable
    /// on the first pass — the hold lasts under two seconds and nobody notices it. The full
    /// thirty are only ever spent when the recording is slow or absent, which is exactly when
    /// leaving early would cost the result.</para>
    ///
    /// <para>It exists because the alternative is a hold with no end: the retry ladder can stall
    /// on a folder of half-written files, and a player trapped in a room by a bug of ours is a
    /// worse outcome than a lost rating.</para>
    /// </summary>
    public const double ResultGraceSeconds = 30;

    /// <summary>
    /// How long the launcher keeps a guest waiting for the host's report before it stops
    /// promising one is coming and points at the History instead.
    ///
    /// <para>Four times <see cref="ResultGraceSeconds"/>, and the two numbers answer different
    /// questions on purpose. The grace is how long a BUTTON may be held shut, which has to be
    /// short because it takes a choice away. This is how long a LINE OF TEXT may say "waiting",
    /// which costs the player nothing and so can afford to outlast a host who wandered off to
    /// read the score screen.</para>
    /// </summary>
    public const double ResultWaitCeilingSeconds = 120;

    /// <summary>What the launcher is still doing with the match that just ended.</summary>
    public enum ResultPhase
    {
        /// <summary>Nothing outstanding — the result is settled, or there was never one to settle.</summary>
        None,

        /// <summary>Still looking for and reading the recording that says who won.</summary>
        ReadingRecording,

        /// <summary>The verdict is known and on its way to the server.</summary>
        SendingResult,

        /// <summary>
        /// Our own reading is finished — or there was nothing to read — and the only thing left
        /// is the HOST reporting the match.
        ///
        /// <para>Guest-side, and the state that did not exist. A guest arrived here with nothing
        /// on screen at all: the launcher had done everything it could, and what remained was
        /// somebody else's machine. Measured on a real match, that silence lasted sixteen
        /// seconds and was then replaced by the room disappearing.</para>
        /// </summary>
        WaitingForHost,
    }

    /// <summary>
    /// Whether leaving the room has to wait a moment.
    ///
    /// <para><b>Why this is not merely polite.</b> The server refuses a report from anyone who is
    /// no longer the room's host (<c>matches/rest.ts</c>), and leaving hands the host role
    /// straight to the opponent (<c>reassignHost</c>) — so a host who walks out in the seconds
    /// after the game closes destroys the result for both players, silently and with no way to
    /// get it back. That is what this holds shut.</para>
    ///
    /// <para><b>It holds both players now, for two different reasons</b>, and the difference is
    /// the thing to keep hold of before anyone widens this.</para>
    ///
    /// <para>For the <b>guest</b> it is information, not correctness. Their leaving costs nobody
    /// the report — which is exactly what the old <c>weAreHost</c> clause reasoned from, and why
    /// they used to be let go instantly. But the guest is the one who cannot report and therefore
    /// cannot know: their game closes first (the player who lost leaves first), the host is still
    /// on the victory screen, and the result arrives seconds later. On a real match that was
    /// sixteen seconds of an empty screen, ended by the room vanishing.</para>
    ///
    /// <para><b>The ceiling is what makes holding a guest defensible, and it must never be raised
    /// for them.</b> A host waits on his own machine reading his own recording. A guest waits on
    /// WHEN THE OTHER PLAYER CLOSES HIS GAME — which can be minutes, or never if he force-quits.
    /// Holding somebody on something a third party controls is how a player ends up trapped in a
    /// room, so past <see cref="ResultGraceSeconds"/> the button comes back whether or not
    /// anything arrived; the card carries on explaining.</para>
    ///
    /// <para><b>Competitive only</b>, because in a casual room there is no rating to protect and
    /// being held anywhere is an annoyance.</para>
    /// </summary>
    public static bool HoldLeave(bool competitive, ResultPhase phase, double secondsSinceGameExit)
        => competitive
           && phase != ResultPhase.None
           && secondsSinceGameExit < ResultGraceSeconds;

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
