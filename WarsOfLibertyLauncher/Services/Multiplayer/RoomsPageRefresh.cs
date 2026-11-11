namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// When the Rooms page's background work may run: the 5-s list poll and the 3-s cell tick (ping,
/// ages).
///
/// <para>Neither used to look at the room window. The list poll re-renders every row whenever the
/// player's own room changes players or status, and all of it ran BEHIND the room window — the
/// window the player is actually looking at. So both pause while a room window is open and the
/// launcher's main window is not the one in front; activating the launcher resumes them, and
/// closing the room window refreshes the list at once. The "updated X ago" label keeps ticking,
/// so it tells the truth meanwhile.</para>
/// </summary>
public static class RoomsPageRefresh
{
    public static bool ShouldPollList(bool signedIn, bool onRooms, bool roomWindowOpen, bool launcherActive)
        => signedIn && onRooms && (!roomWindowOpen || launcherActive);

    public static bool ShouldTickCells(bool onRooms, bool roomWindowOpen, bool launcherActive)
        => onRooms && (!roomWindowOpen || launcherActive);

    /// <summary>
    /// A room was announced on the live channel (<c>lobby_created</c>, or <c>lobbies_changed</c> from a
    /// server that sends it): the list is asked for at once instead of on the next 5-s poll, when the
    /// list is on screen and the poll itself would run. A burst of announcements is one request
    /// (<see cref="AnnouncementDebounceMs"/>).
    /// </summary>
    public static bool ShouldRefreshOnAnnouncement(bool signedIn, bool onRooms, bool tabVisible, bool roomWindowOpen, bool launcherActive)
        => tabVisible && ShouldPollList(signedIn, onRooms, roomWindowOpen, launcherActive);

    /// <summary>Announcements this close together share one request.</summary>
    public const int AnnouncementDebounceMs = 300;

    /// <summary>
    /// The list poll's interval: 5 s, or 15 s once the server has shown it sends
    /// <c>lobbies_changed</c> on a live channel that is still connected - every change then arrives
    /// as an announcement and the poll is only the fallback, so it costs a third of the requests.
    /// A dropped channel goes back to 5 s until the frame is seen again.
    /// </summary>
    public static TimeSpan PollInterval(bool liveChangesSeen)
        => TimeSpan.FromSeconds(liveChangesSeen ? 15 : 5);
}
