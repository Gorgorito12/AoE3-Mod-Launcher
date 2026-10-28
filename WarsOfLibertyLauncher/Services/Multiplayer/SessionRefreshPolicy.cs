using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// When the launcher renews its session token, and when it warns that the token will not
/// outlast a match.
///
/// <para><b>Why.</b> The server mints a seven-day token and the launcher never renewed it, so
/// every player who stayed signed in was signed out seven days after his last sign-in — usually
/// noticed at a reconnect, hours later, as "your multiplayer session ended". The cure is the
/// server's <c>POST /auth/refresh</c>; this is the client's half of it, and it is inert until the
/// server has the route (a 404 is remembered for <see cref="RetryUnsupportedAfter"/>).</para>
///
/// <para><b>And the warning, for as long as the server cannot renew.</b> A token that expires
/// between creating a room and reporting its match loses the match: the report comes back 401
/// and nothing sends it again. Six hours is room for a long match and the room around it.</para>
///
/// <para>Pure, in Unix seconds, so the thresholds are pinned by tests rather than by comments.</para>
/// </summary>
internal static class SessionRefreshPolicy
{
    /// <summary>Renew once this little is left. A renewal grants a fresh seven days, so an active
    /// launcher asks about once every four.</summary>
    internal static readonly TimeSpan RefreshWhenLeft = TimeSpan.FromDays(3);

    /// <summary>A failed attempt is not repeated sooner than this.</summary>
    internal static readonly TimeSpan MinGapBetweenAttempts = TimeSpan.FromMinutes(30);

    /// <summary>After a 404 (a server without the route) the launcher asks again only this late —
    /// a backend can be updated while a tray launcher keeps running for days.</summary>
    internal static readonly TimeSpan RetryUnsupportedAfter = TimeSpan.FromHours(6);

    /// <summary>Entering a room with less than this left is warned about.</summary>
    internal static readonly TimeSpan WarnBeforeRoomWhenLeft = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether to ask the server for a new token now. An already expired token is still tried:
    /// a server that grants a short grace would rescue it, and one that does not answers 401 —
    /// the same sign-out the next authenticated call would have caused anyway.
    /// </summary>
    internal static bool IsDue(long nowUnix, long expiresAtUnix, long lastAttemptUnix, long unsupportedUntilUnix)
    {
        if (expiresAtUnix <= 0) return false;
        if (unsupportedUntilUnix > nowUnix) return false;
        if (lastAttemptUnix > 0 && nowUnix - lastAttemptUnix < (long)MinGapBetweenAttempts.TotalSeconds) return false;
        return expiresAtUnix - nowUnix < (long)RefreshWhenLeft.TotalSeconds;
    }

    /// <summary>Whether a room entered now could outlast the session.</summary>
    internal static bool ShouldWarnBeforeRoom(long nowUnix, long expiresAtUnix)
        => expiresAtUnix > 0 && expiresAtUnix - nowUnix < (long)WarnBeforeRoomWhenLeft.TotalSeconds;
}
