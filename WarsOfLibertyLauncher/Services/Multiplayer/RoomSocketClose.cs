namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What a room socket's close reason MEANS — the one table every handler reads, so the tab
/// cannot answer "should I keep retrying?" differently in two places.
///
/// <para><b>Why the terminal codes are terminal.</b> <see cref="LobbyWebSocket"/> resets its
/// backoff on a connection that ESTABLISHES, and each of these closes immediately after the
/// upgrade succeeds — so retrying never slows down past its first step. A real client retried a
/// deleted room about two hundred times in five minutes. Gone and MembershipLost are split
/// because they say different things to the player: the first means the room no longer exists,
/// the second that THIS player lost their place in a room that may well still be open (4002 is a
/// spent single-use join token; 4004 is a membership row the server deletes on every close, so
/// a reconnect can never bring it back).</para>
///
/// <para><b>Everything not listed is Transient, deliberately</b> — including 4001, 4003, 4005,
/// 4009, an empty reason and every transport error. Treating an unknown close as terminal would
/// strand a player in a dead room over a code nobody meant as final; the cost of the opposite
/// mistake is a retry loop the server can end with a real terminal code.</para>
/// </summary>
public static class RoomSocketClose
{
    public enum Kind
    {
        /// <summary>Anything that is worth reconnecting after.</summary>
        Transient,
        /// <summary><c>4007</c>: the room closed because its match was reported — or a kick.</summary>
        MatchReported,
        /// <summary><c>4404 lobby_not_found</c> / <c>4006 lobby_closed</c>: the room is gone.</summary>
        Gone,
        /// <summary><c>4002</c> / <c>4004</c>: our place in the room is gone; the room may not be.</summary>
        MembershipLost,
        /// <summary><c>4010</c>: this build is below the server's minimum.</summary>
        TooOld,
    }

    public static Kind Classify(string? reason) => reason switch
    {
        "server_close:4007" => Kind.MatchReported,
        "server_close:4404" or "server_close:4006" => Kind.Gone,
        "server_close:4002" or "server_close:4004" => Kind.MembershipLost,
        "server_close:4010" => Kind.TooOld,
        _ => Kind.Transient,
    };
}

/// <summary>What the tab does with one event from a room socket.</summary>
public enum RoomSocketAction
{
    /// <summary>The event came from a socket that is no longer this room's. Log it, touch nothing.</summary>
    IgnoreStale,
    /// <summary>A stale socket closed for good: stop IT, and touch nothing else.</summary>
    StopStaleSender,
    /// <summary>A socket we already stopped dropped its connection. Not a reconnect, so no chip.</summary>
    IgnoreStopped,
    /// <summary>The room closed because the match we were in was reported.</summary>
    MatchReported,
    Gone,
    MembershipLost,
    TooOld,
    /// <summary>An ordinary drop: show "Reconnecting".</summary>
    Transient,
}

/// <summary>
/// Routes a room-socket event, given whether its sender is still the session's socket.
///
/// <para><b>The sender is the point.</b> Creating a room while in another one disposes the old
/// socket synchronously, and the abort makes it raise <c>Disconnected</c> on a pool thread while
/// the tab is still subscribed — so every "create from inside a room" queues a callback from the
/// OLD socket. The handlers used to ignore their sender and act on whatever socket was current
/// when the callback ran: a late <c>4006</c> from the room just left stopped the NEW room's socket
/// (dead on arrival, while the server kept it open, joinable and host-less), and every kick and
/// every reported match painted a "Reconnecting…" chip for a socket nobody was going to
/// reconnect. The answer is decided INSIDE the dispatcher callback, because "current" is a fact
/// about the moment the callback runs, not the moment the event was raised.</para>
/// </summary>
public static class RoomSocketEvents
{
    public static RoomSocketAction Route(bool isCurrent, bool isStopped, string? reason, bool inMatchOrResult)
    {
        var kind = RoomSocketClose.Classify(reason);
        if (!isCurrent)
            return kind == RoomSocketClose.Kind.Transient || isStopped
                ? RoomSocketAction.IgnoreStale
                : RoomSocketAction.StopStaleSender;

        switch (kind)
        {
            // Outside a match 4007 is the kick code, and a kick has already closed the window
            // through its own frame — so it falls through to the ordinary path, as it always did.
            case RoomSocketClose.Kind.MatchReported when inMatchOrResult:
                return RoomSocketAction.MatchReported;
            case RoomSocketClose.Kind.Gone:
                return RoomSocketAction.Gone;
            case RoomSocketClose.Kind.MembershipLost:
                return RoomSocketAction.MembershipLost;
            case RoomSocketClose.Kind.TooOld:
                return RoomSocketAction.TooOld;
        }

        return isStopped ? RoomSocketAction.IgnoreStopped : RoomSocketAction.Transient;
    }

    /// <summary>Whether a <c>Reconnecting</c> event may paint the chip.</summary>
    public static bool ShouldShowReconnecting(bool isCurrent, bool isStopped) => isCurrent && !isStopped;
}
