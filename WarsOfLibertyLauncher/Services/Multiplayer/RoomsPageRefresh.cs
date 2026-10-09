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
}
