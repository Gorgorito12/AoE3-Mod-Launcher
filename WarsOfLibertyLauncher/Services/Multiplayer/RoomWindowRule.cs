namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// When the room window belongs on screen: whenever the session is in a room, whatever the
/// main window happens to be showing.
///
/// <para>The window used to open and close only from the ROOMS subtab's render, so pressing
/// Leave while the main window showed Ranking, Statistics or Tournaments left a room window
/// standing over an idle session. One rule, read by both the Rooms render and the pass that
/// runs on every session change, is what keeps the two from disagreeing.</para>
/// </summary>
internal static class RoomWindowRule
{
    internal static bool InARoom(MultiplayerSession.LobbyStatus status)
        => status is MultiplayerSession.LobbyStatus.Joining
            or MultiplayerSession.LobbyStatus.InLobby
            or MultiplayerSession.LobbyStatus.InGame
            or MultiplayerSession.LobbyStatus.Leaving;
}
