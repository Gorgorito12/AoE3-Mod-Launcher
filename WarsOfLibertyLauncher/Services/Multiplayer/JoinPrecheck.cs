namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What to do when the player presses <em>Join</em> while the session already claims a room.
///
/// <para>This exists because the answer used to be a refusal and nothing else:
/// <c>MultiplayerSession.JoinLobbyAsync</c> threw a hardcoded English
/// <c>"Leave the current lobby first."</c>, the join flow rendered
/// <c>ex.Message</c> verbatim as the dialog body, and the only button was OK. A player
/// reported it with a screenshot — Spanish title, English body, nothing to press — while
/// the server's own presence panel listed him as being in no room at all.</para>
///
/// <para>Pure on purpose. The session is not constructible in a test, so the DECISION lives
/// here where it can be pinned; the caller does the leaving, the asking and the logging.
/// Three of the four answers are refusals or repairs, and those are the point.</para>
/// </summary>
internal static class JoinPrecheck
{
    internal enum Decision
    {
        /// <summary>Not in a room — join straight away.</summary>
        Proceed,

        /// <summary>
        /// The session claims a room but holds no id for it, so there is no room: the two
        /// fields have drifted. Clear the state and carry on WITHOUT asking — a confirm
        /// naming a room that does not exist is the bug wearing a dialog.
        /// </summary>
        SelfHeal,

        /// <summary>
        /// Really in another room. Ask before leaving it: if the player hosts it, leaving
        /// closes or migrates that room for everyone else inside.
        /// </summary>
        Confirm,

        /// <summary>
        /// A live match, or a result that has not been sent yet. Refuse — the post-match
        /// hold exists so a competitive result reaches the server, and a click on Join is
        /// not a reason to walk out of it.
        /// </summary>
        BlockedInMatch,
    }

    /// <param name="isInLobby">The session's own <c>IsInLobby</c> (anything but Idle).</param>
    /// <param name="hasLobbyId">Whether <c>CurrentLobbyId</c> is set.</param>
    /// <param name="matchInProgress">Any match phase other than Lobby.</param>
    /// <param name="resultHold">The post-match hold that keeps the room open until the result is sent.</param>
    internal static Decision Decide(
        bool isInLobby,
        bool hasLobbyId,
        bool matchInProgress,
        bool resultHold)
    {
        if (!isInLobby) return Decision.Proceed;

        // ORDER IS LOAD-BEARING: the drift check outranks the match check. Without an id
        // there is no room, so there is no match to protect and no result that can still
        // arrive — and the match phase is exactly as likely to be stale as the session
        // state it drifted from. Asking about the match first would let one stale flag
        // hold the other one hostage, which is the wedge this whole helper exists to end.
        if (!hasLobbyId) return Decision.SelfHeal;

        if (matchInProgress || resultHold) return Decision.BlockedInMatch;

        return Decision.Confirm;
    }
}
