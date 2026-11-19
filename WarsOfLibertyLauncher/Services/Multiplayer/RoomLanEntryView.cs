namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Whether this player's game CAN enter the room's LAN game by itself, from its folder.</summary>
public enum RoomLanAvailability
{
    /// <summary>Not looked at yet, or no game to look at.</summary>
    Unknown,
    /// <summary>The helper (or the mod's own DLL) is in place.</summary>
    Available,
    /// <summary>The helper is missing or old, and the launcher can put it there.</summary>
    NotSetUp,
    /// <summary>Nothing the launcher can do changes it (another tool's DLL, an unknown exe).</summary>
    CannotSetUp,
}

public enum RoomLanCardKind { Hidden, On, Off, NotSetUp, CannotSetUp }

/// <summary>What the room's "Automatic entry" card shows.</summary>
public readonly record struct RoomLanCard(
    RoomLanCardKind Kind, string? BodyKey, bool ShowSwitch, bool SwitchEnabled, string? ActionKey);

/// <summary>
/// The room's "Automatic entry" card (the card above PLAYERS). The switch is the player's
/// <c>LauncherConfig.LanAutoEntry</c>: it only decides whether the launch carries the arguments,
/// so it is offered only when the game can enter by itself, and only while the room is still a
/// room (the arguments are built when the game launches). Pure, so <c>RoomLanEntryViewTests</c>
/// pins every state.
/// </summary>
public static class RoomLanEntryView
{
    public static RoomLanCard For(RoomLanAvailability availability, bool switchOn, bool inLobbyPhase) => availability switch
    {
        RoomLanAvailability.Available => switchOn
            ? new RoomLanCard(RoomLanCardKind.On, "LanCardOnBody", true, inLobbyPhase, null)
            : new RoomLanCard(RoomLanCardKind.Off, "LanCardOffBody", true, inLobbyPhase, null),
        RoomLanAvailability.NotSetUp =>
            new RoomLanCard(RoomLanCardKind.NotSetUp, "LanCardNotSetUpBody", false, false, inLobbyPhase ? "LanCardSetUp" : null),
        RoomLanAvailability.CannotSetUp =>
            new RoomLanCard(RoomLanCardKind.CannotSetUp, "LanCardCannotBody", false, false, "LanCardWhy"),
        _ => new RoomLanCard(RoomLanCardKind.Hidden, null, false, false, null),
    };

    /// <summary>What this player tells the room about himself: he enters by himself only when he
    /// can AND the switch is on. Null while unknown, so no row claims anything.</summary>
    public static bool? EntersByItself(RoomLanAvailability availability, bool switchOn) => availability switch
    {
        RoomLanAvailability.Unknown => null,
        RoomLanAvailability.Available => switchOn,
        _ => false,
    };
}
