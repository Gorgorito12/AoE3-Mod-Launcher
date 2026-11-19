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

public enum RoomLanCardKind { Hidden, On, Off, NotSetUp, CannotSetUp, NeedsHomeCity }

/// <summary>What the room's "Automatic entry" strip shows: the short sentence on it
/// (<paramref name="ShortKey"/>, design 116) and the full one in its tooltip (<paramref name="BodyKey"/>).</summary>
public readonly record struct RoomLanCard(
    RoomLanCardKind Kind, string? BodyKey, bool ShowSwitch, bool SwitchEnabled, string? ActionKey,
    string? ShortKey = null);

/// <summary>
/// The room's "Automatic entry" card (the card above PLAYERS). The switch is the player's
/// <c>LauncherConfig.LanAutoEntry</c>: it only decides whether the launch carries the arguments,
/// so it is offered only when the game can enter by itself, and only while the room is still a
/// room (the arguments are built when the game launches). Pure, so <c>RoomLanEntryViewTests</c>
/// pins every state.
/// </summary>
public static class RoomLanEntryView
{
    /// <param name="city">Whether the player has a home city for this mod (2026-11-18). With none,
    /// the game would ask for one the first time it enters LAN, and the automatic entry does not
    /// wait for that screen — so that launch goes by hand and the card says why. Unknown changes
    /// nothing.</param>
    public static RoomLanCard For(RoomLanAvailability availability, bool switchOn, bool inLobbyPhase,
        HomeCityState city = HomeCityState.Unknown) => availability switch
    {
        RoomLanAvailability.Available when switchOn && city == HomeCityState.None =>
            new RoomLanCard(RoomLanCardKind.NeedsHomeCity, "LanCardNoCityBody", false, false, null, "LanCardNoCityShort"),
        RoomLanAvailability.Available => switchOn
            ? new RoomLanCard(RoomLanCardKind.On, "LanCardOnBody", true, inLobbyPhase, null, "LanCardOnShort")
            : new RoomLanCard(RoomLanCardKind.Off, "LanCardOffBody", true, inLobbyPhase, null, "LanCardOffShort"),
        RoomLanAvailability.NotSetUp =>
            new RoomLanCard(RoomLanCardKind.NotSetUp, "LanCardNotSetUpBody", false, false, inLobbyPhase ? "LanCardSetUp" : null, "LanCardNotSetUpShort"),
        RoomLanAvailability.CannotSetUp =>
            new RoomLanCard(RoomLanCardKind.CannotSetUp, "LanCardCannotBody", false, false, "LanCardWhy", "LanCardCannotShort"),
        _ => new RoomLanCard(RoomLanCardKind.Hidden, null, false, false, null),
    };

    /// <summary>What this player tells the room about himself: he enters by himself only when he
    /// can AND the switch is on. Null while unknown, so no row claims anything.</summary>
    public static bool? EntersByItself(RoomLanAvailability availability, bool switchOn,
        HomeCityState city = HomeCityState.Unknown) => availability switch
    {
        RoomLanAvailability.Unknown => null,
        RoomLanAvailability.Available => switchOn && city != HomeCityState.None,
        _ => false,
    };

    /// <summary>Whether this launch may carry the automatic-entry arguments: the room's switch on,
    /// and not a player who has no home city yet (Unknown still enters, as before).</summary>
    public static bool MayAutoEnter(bool switchOn, HomeCityState city) => switchOn && city != HomeCityState.None;
}
