namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// When a session the server refused may be signed out. The server refuses a token that expired
/// or that was signed with a key it no longer has; either way the launcher has to sign the player
/// out and ask them to sign in again, since the Discord flow cannot run silently.
///
/// <para>Not in the middle of a room or a match, though. Signing out clears the room from the
/// session and closes the room window — and a guest's room socket rides a single-use join token
/// that still works, so their match would be cut off for a token nothing in the match needs. A
/// rejection there waits until the player is back out of the room.</para>
/// </summary>
internal static class SessionRejection
{
    /// <summary>Whether to sign out right away, or wait for the room and the match to end.</summary>
    /// <param name="inRoom">The session is joining, in, or playing in a room.</param>
    /// <param name="matchActive">A countdown, a match, or its result is still in progress.</param>
    internal static bool ShouldExpireNow(bool inRoom, bool matchActive) => !inRoom && !matchActive;
}
