namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a row of the rooms list offers the person looking at it (design 86).</summary>
public enum RoomAction
{
    /// <summary>Your room, or one you are in: back to its window. The one solid button.</summary>
    OpenMine,
    /// <summary>A seat is free and the mod is ready: join.</summary>
    Join,
    /// <summary>The game is full but a watching seat is free (developer mode only, see
    /// <c>MultiplayerTab.ObserversUnlocked</c>).</summary>
    Watch,
    /// <summary>The room's mod is not on this PC: go and install it.</summary>
    InstallMod,
    /// <summary>The mod is here but another version of it: go and look at your copy.</summary>
    CheckMyCopy,
    /// <summary>The match has started: see who is playing.</summary>
    SeeMatch,
    /// <summary>Nothing to take: said as text, not a button.</summary>
    Full,
}

/// <summary>The three headings the list is grouped under, in the order they are drawn.</summary>
public enum RoomGroup
{
    CanEnter,
    InGame,
    Unavailable,
}

/// <summary>Everything about one room the decision needs, already worked out.</summary>
/// <param name="Mine">I am in this room, or I host it.</param>
/// <param name="InGame">The room's match has started.</param>
/// <param name="Offer">What its seats leave for me (<see cref="RoomFormats.OfferFor"/>).</param>
/// <param name="ModInstalled">The room's mod is installed here.</param>
/// <param name="VersionDiffers">My copy is known to be ANOTHER version of the room's mod. Unknown
/// (no hash yet on either side) is false: a row never claims a mismatch it has not measured.</param>
public readonly record struct RoomRowFacts(bool Mine, bool InGame, RoomOffer Offer, bool ModInstalled, bool VersionDiffers);

/// <summary>
/// The one decision behind a row's button and the heading it sits under, so the button and the
/// grouping can never disagree. Pure: <c>MultiplayerTab</c> gathers the facts.
/// </summary>
public static class RoomActionRule
{
    /// <summary>
    /// Mine first, because it is the only row about the viewer; then the match that has started,
    /// whatever its seats say; then "full"; and only for a room with room in it, what stands
    /// between the viewer and a seat — the mod, then its version.
    /// </summary>
    public static RoomAction Decide(RoomRowFacts f)
    {
        if (f.Mine) return RoomAction.OpenMine;
        if (f.InGame) return RoomAction.SeeMatch;
        if (f.Offer == RoomOffer.Full) return RoomAction.Full;
        if (!f.ModInstalled) return RoomAction.InstallMod;
        if (f.VersionDiffers) return RoomAction.CheckMyCopy;
        return f.Offer == RoomOffer.Watch ? RoomAction.Watch : RoomAction.Join;
    }

    /// <summary>
    /// The order the design sorts by: yours, then the rooms you can join, then the ones that need
    /// one step from you, then the matches under way, then what is not available now. Within a
    /// rank the server's order stands (the sort is stable).
    /// </summary>
    public static int Rank(RoomAction a) => a switch
    {
        RoomAction.OpenMine => 0,
        RoomAction.Join => 1,
        RoomAction.InstallMod or RoomAction.Watch => 2,
        RoomAction.SeeMatch => 3,
        _ => 4,
    };

    public static RoomGroup GroupOf(RoomAction a) => Rank(a) switch
    {
        <= 2 => RoomGroup.CanEnter,
        3 => RoomGroup.InGame,
        _ => RoomGroup.Unavailable,
    };

    /// <summary>The caption, a <c>Strings</c> key. "Full" is drawn as plain text, not a button.</summary>
    public static string CaptionKey(RoomAction a) => a switch
    {
        RoomAction.OpenMine => "RmActOpen",
        RoomAction.Join => "RmActJoin",
        RoomAction.Watch => "RmActWatch",
        RoomAction.InstallMod => "RmActInstall",
        RoomAction.CheckMyCopy => "RmActCheckCopy",
        RoomAction.SeeMatch => "RmActSeeMatch",
        _ => "RmActFull",
    };

    /// <summary>The heading over a group, a <c>Strings</c> key.</summary>
    public static string GroupKey(RoomGroup g) => g switch
    {
        RoomGroup.CanEnter => "RmGroupCanEnter",
        RoomGroup.InGame => "RmGroupInGame",
        _ => "RmGroupUnavailable",
    };

    /// <summary>The ping's colour band: green under 100 ms, amber over 150, plain between, faint
    /// when unknown.</summary>
    public static string PingBrushKey(int? ms) => ms switch
    {
        null or < 0 => "RmPingUnknown",
        < 100 => "RmPingGood",
        > 150 => "RmPingSlow",
        _ => "RmPingPlain",
    };
}
