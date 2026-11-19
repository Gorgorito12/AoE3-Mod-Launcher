namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// When the host's launcher starts the countdown on its own: everyone ready, the room full.
///
/// <para><b>Full, not merely "everyone present"</b>: a 6-seat room with three players once
/// launched the moment those three marked ready, and the other three were left behind. In a room
/// that is not full the footer gives the host Start instead.</para>
///
/// <para><b>Only on a ready change that came after the last start.</b> The server keeps everyone's
/// ready flag through a cancelled countdown and through a finished match, so "everyone is ready"
/// stays true the moment the room is back in the lobby. Read on its own, the next room state would
/// start the countdown again right after somebody cancelled it, or a new match the moment the last
/// one ended. <c>readyEpoch</c> counts ready changes; <c>spentEpoch</c> is the count at the last
/// automatic start. A start needs somebody to mark ready again.</para>
/// </summary>
internal static class AutoStartRule
{
    internal readonly record struct Facts(
        bool IsHost,
        bool InLobbyPhase,
        int Players,
        int NotReady,
        int Seats,
        bool GateOpen,
        int ReadyEpoch,
        int SpentEpoch);

    internal static bool ShouldFire(Facts f) =>
        f.IsHost
        && f.InLobbyPhase
        && f.Players >= 2
        && f.Seats > 0
        && f.Players >= f.Seats
        && f.NotReady == 0
        && f.GateOpen
        && f.ReadyEpoch > f.SpentEpoch;
}
