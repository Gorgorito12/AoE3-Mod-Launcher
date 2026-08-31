using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What shape of match a room was created for.</summary>
public enum RoomFormat
{
    /// <summary>Not a competitive room. Nothing about it rates, and its size is the host's own choice.</summary>
    Casual,
    OneVOne,
    TwoVTwo,
    ThreeVThree,

    /// <summary>
    /// A competitive room whose size does not name a format — today that means one created before
    /// formats existed, or by a client that did not go through the dialog.
    ///
    /// <para><b>It must never be read as 1v1.</b> That is the whole reason this value exists
    /// rather than a null or a quiet fallback: guessing would promise the abandonment rule and a
    /// place on the 1v1 ladder to a room nobody declared as one.</para>
    /// </summary>
    Unknown,
}

/// <summary>
/// Which format a room is, worked out from the two things every surface already knows about it.
///
/// <para><b>Deliberately DERIVED rather than stored.</b> A competitive room's size is fixed by its
/// format — 1v1 is 2 seats, 2v2 is 4, 3v3 is 6 — so <c>max_players</c> already names the format
/// one-to-one, and the server refuses any other size for a competitive room (it downgrades it to
/// casual, exactly as it already downgrades a mod with no ladder). That buys the feature with no
/// migration and without threading a new field through the nine hops <c>competitive</c> travels.
/// </para>
///
/// <para><b>The price, stated now because it will be paid eventually:</b> format and size are
/// married. The day a competitive room wants spectator seats, this stops deriving and has to
/// become a real column on <c>lobbies</c>.</para>
///
/// <para>Pure and WPF-free, like <see cref="MatchTeamMap"/> beside it, because what matters here
/// is what it refuses to answer.</para>
/// </summary>
public static class RoomFormats
{
    /// <summary>The room sizes a competitive room may have, in format order.</summary>
    public const int OneVOnePlayers = 2;
    public const int TwoVTwoPlayers = 4;
    public const int ThreeVThreePlayers = 6;

    /// <summary>
    /// The format of a room with this competitive flag and this many seats.
    ///
    /// <para>A casual room has none — not "1v1 by default": its size says nothing about how it
    /// will be played, and treating a 2-seat casual room as a rated 1v1 is precisely the claim
    /// the competitive flag exists to prevent.</para>
    /// </summary>
    public static RoomFormat Resolve(bool competitive, int maxPlayers)
    {
        if (!competitive) return RoomFormat.Casual;
        return maxPlayers switch
        {
            OneVOnePlayers => RoomFormat.OneVOne,
            TwoVTwoPlayers => RoomFormat.TwoVTwo,
            ThreeVThreePlayers => RoomFormat.ThreeVThree,
            _ => RoomFormat.Unknown,
        };
    }

    /// <summary>How many seats this format needs, or 0 when it does not name a number.</summary>
    public static int PlayersFor(RoomFormat format) => format switch
    {
        RoomFormat.OneVOne => OneVOnePlayers,
        RoomFormat.TwoVTwo => TwoVTwoPlayers,
        RoomFormat.ThreeVThree => ThreeVThreePlayers,
        _ => 0,
    };

    /// <summary>
    /// Whether this format is played in teams — the question every rule written for a 1v1 has to
    /// ask before it applies itself.
    ///
    /// <para><c>Unknown</c> answers false, and so does <c>Casual</c>: neither is a team game we
    /// can act on, and a rule that fired on "not 1v1" would fire on both.</para>
    /// </summary>
    public static bool IsTeam(RoomFormat format)
        => format is RoomFormat.TwoVTwo or RoomFormat.ThreeVThree;

    /// <summary>
    /// Whether the abandonment rule can apply — <b>1v1 only</b>, and this is not a UI nicety.
    ///
    /// <para>The server's <c>decideByAbandon</c> refuses anything but two participants, so in a
    /// team room the launcher used to threaten a forfeit the backend never carries out. Any text
    /// that mentions walking out has to ask this rather than asking whether the room is
    /// competitive.</para>
    /// </summary>
    public static bool AbandonmentApplies(RoomFormat format) => format == RoomFormat.OneVOne;

    /// <summary>
    /// Whether the teams read out of the recording are the ones the room said it would play.
    ///
    /// <para><b>A declared format is a promise, and this is what checks it.</b> A room created as
    /// 2v2 that turns out to have been played 1v3, or free-for-all, would otherwise have those
    /// real-but-wrong sides written into four people's history. Refusing costs the teams of one
    /// match; accepting records something nobody agreed to.</para>
    ///
    /// <para><c>Casual</c> and <c>Unknown</c> declared nothing, so there is nothing to
    /// contradict and whatever the recording says is kept — that is how a casual team game still
    /// shows its sides in the history.</para>
    /// </summary>
    public static bool TeamsAgreeWithFormat(
        RoomFormat format, IReadOnlyDictionary<string, int>? teams) => format switch
    {
        // Two players and no sides. Teams here would mean the recording is not this match.
        RoomFormat.OneVOne => teams == null,
        RoomFormat.TwoVTwo => HasSides(teams, 2),
        RoomFormat.ThreeVThree => HasSides(teams, 3),
        _ => true,
    };

    /// <summary>Exactly two sides, each exactly this many players.</summary>
    private static bool HasSides(IReadOnlyDictionary<string, int>? teams, int perSide)
    {
        if (teams == null) return false;
        var sides = teams.GroupBy(kv => kv.Value).Select(g => g.Count()).ToList();
        return sides.Count == 2 && sides.All(n => n == perSide);
    }

    /// <summary>The localization key naming this format, or null for one that has no name.</summary>
    public static string? LabelKey(RoomFormat format) => format switch
    {
        RoomFormat.OneVOne => "MpFormat1v1",
        RoomFormat.TwoVTwo => "MpFormat2v2",
        RoomFormat.ThreeVThree => "MpFormat3v3",
        _ => null,
    };
}
