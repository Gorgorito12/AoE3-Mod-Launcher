using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a room row can offer somebody looking at it.</summary>
public enum RoomOffer
{
    /// <summary>A playing seat is free.</summary>
    Join,

    /// <summary>The game is full, but a seat beside it is not.</summary>
    Watch,

    /// <summary>Nothing this viewer can take.</summary>
    Full,
}

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
/// <para><b>Derived from size, MINUS the seats that are not players.</b> A competitive room's
/// playing size is fixed by its format — 1v1 is 2 seats, 2v2 is 4, 3v3 is 6 — so once the
/// observer seats are taken off the top, <c>max_players</c> names the format one-to-one again,
/// and the server refuses any other playing size for a competitive room (it downgrades it to
/// casual, exactly as it already downgrades a mod with no ladder).</para>
///
/// <para><b>The price was stated here before it was paid, and this is the payment.</b> This file
/// used to say that format and size were married, and that the day a competitive room wanted
/// spectator seats the derivation would have to become a real column on <c>lobbies</c>. It did:
/// <c>lobbies.spectator_slots</c>, migration 0018. What it bought is that a 2v2 with one observer
/// is five seats and still a 2v2 — before, it was five seats, matched no format, and was
/// silently downgraded to casual, so the match did not score.</para>
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
    /// The format of a room with this competitive flag, this many seats, and this many of those
    /// seats reserved for people who are not playing.
    ///
    /// <para>A casual room has none — not "1v1 by default": its size says nothing about how it
    /// will be played, and treating a 2-seat casual room as a rated 1v1 is precisely the claim
    /// the competitive flag exists to prevent.</para>
    ///
    /// <para><paramref name="spectatorSlots"/> defaults to 0 so that a room from before the
    /// column existed, or from a server that does not send it, resolves to exactly what it
    /// resolved to yesterday. <b>A negative count is read as 0</b> rather than trusted: it is
    /// meaningless, and the arithmetic would otherwise ADD seats and promote a 1v1 room into a
    /// 2v2 — turning bad data into a rated team match nobody agreed to play.</para>
    /// </summary>
    public static RoomFormat Resolve(bool competitive, int maxPlayers, int spectatorSlots = 0)
    {
        if (!competitive) return RoomFormat.Casual;
        return PlayingSeats(maxPlayers, spectatorSlots) switch
        {
            OneVOnePlayers => RoomFormat.OneVOne,
            TwoVTwoPlayers => RoomFormat.TwoVTwo,
            ThreeVThreePlayers => RoomFormat.ThreeVThree,
            _ => RoomFormat.Unknown,
        };
    }

    /// <summary>
    /// How many of a room's seats are for players — the number the format is read off.
    ///
    /// <para>Its own function because both sides of the fence need the same answer: the launcher
    /// reads a format off it, and the server validates a size with it. Two copies of one
    /// subtraction would be free to disagree about the observer that makes a match score.</para>
    /// </summary>
    public static int PlayingSeats(int maxPlayers, int spectatorSlots)
        => maxPlayers - (spectatorSlots > 0 ? spectatorSlots : 0);

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

    /// <summary>
    /// A room's seats, split into the two kinds that fill independently.
    ///
    /// <para>Every number is clamped to something a room could actually have, because these
    /// come off the wire: a negative free seat would offer a chair that is not there, and a
    /// negative occupied one would hide a room that is full.</para>
    /// </summary>
    public readonly record struct RoomSeats(
        int PlayersSeated, int PlayerSeats, int Watching, int WatchSeats)
    {
        /// <summary>No room left to PLAY — which is not the same as no room left at all.</summary>
        public bool PlayingFull => PlayersSeated >= PlayerSeats;

        /// <summary>A watching seat is free, so the room can still be cast.</summary>
        public bool CanWatch => Watching < WatchSeats;

        /// <summary>Nobody can join in any capacity.</summary>
        public bool Full => PlayingFull && !CanWatch;
    }

    /// <summary>
    /// Split a room's occupancy into playing and watching.
    ///
    /// <para><c>currentPlayers</c> is the total head count — the server denormalises it and it
    /// has never distinguished roles — so the players seated are what is left after taking the
    /// watchers off it. A server that sends no watcher count sends 0, and this then says
    /// exactly what it said before observers existed.</para>
    /// </summary>
    public static RoomSeats SeatsOf(
        int maxPlayers, int spectatorSlots, int currentPlayers, int spectatorsPresent)
    {
        var watchSeats = spectatorSlots > 0 ? spectatorSlots : 0;
        if (watchSeats > maxPlayers) watchSeats = maxPlayers > 0 ? maxPlayers : 0;

        var watching = spectatorsPresent > 0 ? spectatorsPresent : 0;
        if (watching > watchSeats) watching = watchSeats;

        var playerSeats = maxPlayers - watchSeats;
        if (playerSeats < 0) playerSeats = 0;

        var seated = currentPlayers - watching;
        if (seated < 0) seated = 0;

        return new RoomSeats(seated, playerSeats, watching, watchSeats);
    }

    /// <summary>
    /// What this room can offer, given whether the viewer has observing available at all.
    ///
    /// <para><b><paramref name="observersEnabled"/> false does not merely hide a button.</b> It
    /// changes what "full" means, and that is the whole reason this is a function rather than
    /// two booleans read at the call site. A room whose PLAYING seats are taken but whose
    /// watching seat is free is not <see cref="RoomSeats.Full"/> — so dropping the Watch
    /// button on its own would let the row fall through to Join and offer a playing seat that
    /// the server refuses with <c>lobby_full</c>. To somebody who cannot watch, a room whose
    /// game is full is simply full.</para>
    ///
    /// <para>Observing is behind developer mode until the in-game half is proven: on a map
    /// without the observer script an "observer" starts as an ordinary player, with a town
    /// centre, and the match is quietly uneven for everybody else. That failure is invisible
    /// until the game ends, which is why the door is shut rather than merely inconvenient.</para>
    /// </summary>
    public static RoomOffer OfferFor(RoomSeats seats, bool observersEnabled)
    {
        if (!seats.PlayingFull) return RoomOffer.Join;
        return observersEnabled && seats.CanWatch ? RoomOffer.Watch : RoomOffer.Full;
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
