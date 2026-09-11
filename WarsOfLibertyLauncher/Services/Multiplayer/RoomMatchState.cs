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

    /// <summary>
    /// How far into a competitive match a walkout has to be before it forfeits — the
    /// launcher's copy of the server's <c>COMPETITIVE_ABANDON_SECONDS</c> (300).
    ///
    /// <para><b>A walkout means the SOCKET dying — leaving the room, or closing the launcher —
    /// and never closing the game.</b> The server can see a closed game and deliberately does
    /// not decide on it: in a 1v1 both games end together, so the two timestamps look the same
    /// after a dodge and after an ordinary ending. What settles those is the opponent's
    /// recording.</para>
    ///
    /// <para><b>It decides nothing. It only decides what to SAY.</b> The verdict is the
    /// server's alone (<c>src/elo/abandon.ts</c>), measured on the server's clock, and this
    /// side has no vote in it. What this number buys is the launcher being able to warn a
    /// player before he does the thing, instead of after — which is the whole difference
    /// between a rule and a trap.</para>
    ///
    /// <para><b>So the two can drift, and the safe direction is deliberate.</b> Warning
    /// slightly too often costs a player one extra confirmation; warning too rarely costs
    /// him a rating he was never told about. If the server's value moves, move this one —
    /// and the two strings that spell "five minutes" out in words, which is the other half
    /// of the same promise.</para>
    /// </summary>
    public const double ForfeitAfterSeconds = 300;

    /// <summary>
    /// Whether leaving RIGHT NOW would be scored as a forfeit, and therefore whether the
    /// player has to be told so before he does it.
    ///
    /// <para>Competitive because a casual room has no rating to lose; 1v1 because the
    /// server's <c>decideByAbandon</c> refuses anything else, and threatening a forfeit the
    /// backend will never carry out is the one thing worse than not warning at all.</para>
    ///
    /// <para><b>"Leaving" is the room or the launcher</b> — see <see cref="ForfeitAfterSeconds"/>.
    /// The end-of-match chat line reads this predicate too, and reads it literally: leaving now
    /// would forfeit. It is not a claim that the game closing did.</para>
    /// </summary>
    public static bool LeavingNowForfeits(
        bool competitive, bool abandonmentApplies, double secondsIntoMatch)
        => competitive && abandonmentApplies && secondsIntoMatch >= ForfeitAfterSeconds;

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

    /// <summary>
    /// How long a match has to have been running before the launcher stops treating it as
    /// disposable.
    ///
    /// <para>The same number as <c>MultiplayerTab.MinReportableSeconds</c>, which aliases it, and
    /// the sharing is the point rather than a coincidence: a match long enough to be worth
    /// REPORTING is a match long enough to be worth PROTECTING. Two independent 180s would drift,
    /// and the day they did, the launcher would be closing games it had just decided were real.</para>
    /// </summary>
    public const int PlayedMatchSeconds = 180;

    /// <summary>
    /// Whether a <c>game_cancelled</c> frame may close the AoE3 that is running on THIS machine.
    ///
    /// <para><b>It used to be "always", and that is the bug this exists for.</b> When the host's
    /// game exits, their launcher sends <c>game_ended</c>; the server turns that into a
    /// <c>game_cancelled</c> broadcast to everyone else; and the receiving launcher killed the
    /// local game with <c>killEntireTree</c> without asking a single question. A player reported
    /// exactly that shape — <i>"first his closed, and then mine"</i> — on two 25-minute matches
    /// whose recordings, measured afterwards, carry a complete and valid outcome block. The engine
    /// had finished the match and written the file; the launcher then shut the window on it.</para>
    ///
    /// <para><b>The rule is about the difference between a decision and an event.</b>
    /// <c>host_cancelled</c> and <c>aborted</c> are somebody pressing Cancel — a deliberate "stop
    /// this for everyone", and the case the kill was written for, because an AoE3 launched into a
    /// match nobody else joined sits forever hunting for peers. <c>ended</c> is not a decision at
    /// all: it says the host's own game closed, which is a fact about the host's machine and says
    /// nothing whatsoever about the match on this one. An unrecognised reason is treated as
    /// <c>ended</c> rather than as a cancellation — a frame we do not understand must not be a
    /// licence to destroy something.</para>
    ///
    /// <para><b><see cref="PlayedMatchSeconds"/> overrides every reason, including a real
    /// cancellation.</b> The stranded-launch problem the kill solves only exists in the first
    /// seconds; past that the player is IN a game, and no remote frame is worth more than the
    /// match they are playing. If a host really does abandon a long game, closing it is the
    /// player's call, and the chat line says so.</para>
    /// </summary>
    /// <param name="reason">The frame's <c>reason</c> field, verbatim.</param>
    /// <param name="secondsSinceLaunch">How long our own AoE3 has been running.</param>
    public static bool ShouldKillOnRemoteCancel(string? reason, double secondsSinceLaunch)
    {
        if (secondsSinceLaunch >= PlayedMatchSeconds) return false;

        return reason is "host_cancelled" or "aborted";
    }
}
