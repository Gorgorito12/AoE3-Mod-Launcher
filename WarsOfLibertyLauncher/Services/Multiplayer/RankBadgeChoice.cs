namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which of a player's two ladders a badge stands for.</summary>
public enum BadgeKind
{
    /// <summary>The 1v1 ladder: one shield.</summary>
    Solo,

    /// <summary>The team ladder (2v2 and 3v3 share it): two shields of the same age.</summary>
    Team,
}

/// <summary>
/// The player's stored preference for the badge beside their name, where no match decides it
/// (design handoff 51c). Stored on the server as <c>highest | 1v1 | team</c>.
/// </summary>
public enum BadgeMode
{
    /// <summary>The badge of the higher AGE; a tie goes to 1v1. The default.</summary>
    Highest,

    /// <summary>Always the 1v1 badge.</summary>
    Solo,

    /// <summary>Always the team badge — while the player has one to wear.</summary>
    Team,
}

/// <summary>The wire spelling of <see cref="BadgeMode"/>.</summary>
public static class BadgeModes
{
    /// <summary>
    /// The server's value, leniently: null, empty or anything unknown is <see cref="BadgeMode.Highest"/>,
    /// which is what an older server that sends nothing meant all along.
    /// </summary>
    public static BadgeMode Parse(string? wire) => wire switch
    {
        "1v1" => BadgeMode.Solo,
        "team" => BadgeMode.Team,
        _ => BadgeMode.Highest,
    };

    public static string ToWire(BadgeMode mode) => mode switch
    {
        BadgeMode.Solo => "1v1",
        BadgeMode.Team => "team",
        _ => "highest",
    };
}

/// <summary>
/// The badge a surface draws, and what its tooltip needs to name the OTHER one.
/// <see cref="OtherAge"/> null means the other ladder is unknown (an older server).
/// </summary>
public readonly record struct ShownBadge(
    BadgeKind Kind,
    RankAge Age,
    int Position,
    RankAge? OtherAge,
    int OtherPosition);

/// <summary>
/// Everything the account block's badge is built from — the badge, its seed, and whether
/// effects are reduced — so a re-push with the same answer keeps the badge already on screen
/// instead of building a new animated one with its own clocks.
/// </summary>
internal readonly record struct AccountBadgeKey(ShownBadge Badge, string Seed, bool Still);

/// <summary>
/// Which rank badge to draw beside a player's name (design handoff 51b). Pure and WPF-free, so
/// every rule below is a test rather than a hope.
///
/// <para><b>The order of authority.</b> Inside a room whose format is KNOWN, the room decides,
/// for every player in it: a 1v1 room wears 1v1 badges, a 2v2 or 3v3 room team badges — the
/// player's preference never reaches it. Everywhere else (no room, a casual room, a room whose
/// format cannot be read) the preference decides. A casual room's size says nothing about how
/// it will be played, which is the doctrine <see cref="RoomFormats.Resolve"/> already keeps, so
/// its format is "not known" here rather than guessed from the seat count.</para>
///
/// <para><b>Highest compares AGE</b> — never the position and never the rating. A 1v1 Industrial
/// and a team Industrial tie, and a tie goes to 1v1: the ladder that existed first, and the one
/// most people play.</para>
/// </summary>
public static class RankBadgeChoice
{
    /// <summary>
    /// Whether a player has a team badge to WEAR by choice: a place on the team ladder. Discovery
    /// (no decided team match) and unknown (an older server) both mean no.
    /// </summary>
    public static bool HasTeamBadge(RankAge? teamAge) => teamAge is { } t && t != RankAge.Discovery;

    /// <summary>The kind of badge to draw. See the class remarks for the rules.</summary>
    public static BadgeKind Decide(RoomFormat? room, BadgeMode preference, RankAge? soloAge, RankAge? teamAge)
    {
        switch (room)
        {
            case RoomFormat.OneVOne:
                return BadgeKind.Solo;
            case RoomFormat.TwoVTwo or RoomFormat.ThreeVThree:
                // Every player wears the team badge here, including one with no team match yet
                // (the Discovery double shield). Only an UNKNOWN team age — a server that does
                // not send it — falls back to 1v1, which is what that launcher drew before.
                return teamAge is null ? BadgeKind.Solo : BadgeKind.Team;
        }

        return preference switch
        {
            BadgeMode.Solo => BadgeKind.Solo,
            BadgeMode.Team => HasTeamBadge(teamAge) ? BadgeKind.Team : BadgeKind.Solo,
            _ => HasTeamBadge(teamAge) && (soloAge is null || teamAge!.Value > soloAge.Value)
                ? BadgeKind.Team
                : BadgeKind.Solo,
        };
    }

    /// <summary>
    /// The badge to draw, from the raw positions the server sends and the two ladder sizes, or
    /// null when the chosen ladder's position is unknown — the existing refusal: no badge rather
    /// than a wrong one. <see cref="RankAges"/> is used as is, on either list.
    /// </summary>
    public static ShownBadge? Resolve(
        RoomFormat? room, BadgeMode preference,
        int? soloPosition, int? soloLadderSize,
        int? teamPosition, int? teamLadderSize)
    {
        var solo = RankAges.ForOptional(soloPosition, soloLadderSize);
        var team = RankAges.ForOptional(teamPosition, teamLadderSize);
        var kind = Decide(room, preference, solo, team);
        return kind == BadgeKind.Team
            ? team is { } t ? new ShownBadge(BadgeKind.Team, t, teamPosition ?? 0, solo, soloPosition ?? 0) : null
            : solo is { } s ? new ShownBadge(BadgeKind.Solo, s, soloPosition ?? 0, team, teamPosition ?? 0) : null;
    }

    /// <summary>The string-table key naming a badge's mode: "1v1" or "Teams".</summary>
    public static string ModeKey(BadgeKind kind) => kind == BadgeKind.Team ? "MpModeTeams" : "MpFormat1v1";
}
