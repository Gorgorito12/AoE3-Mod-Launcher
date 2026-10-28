using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The quick match, phase 0 (docs/DISENO-PARTIDA-RAPIDA.md): with no matchmaking server, a search
/// is "join a competitive 1v1 room of my mod that is waiting for a rival, or open one and wait".
/// Everything it decides is here, pure, so the rules are tests rather than hopes.
///
/// <para><b>Convergence is the whole problem.</b> Two players who press the button within a
/// second of each other both find nothing and both open a room, and then each waits in his own
/// for ever. So a player waiting in his OWN room keeps looking, and moves to a compatible room
/// that is OLDER than his — <see cref="PickToMoveTo"/>. Older is a total order (the room's
/// <c>created_at</c>, then its id), so of any two waiters exactly one moves and they meet; the
/// newer room empties and closes.</para>
///
/// <para><b>The rating orders, it never excludes.</b> With a small community, refusing a room
/// because its host is 300 points away leaves both players waiting for nobody. Among several
/// waiting rooms the closest rating goes first; a host whose rating is withheld (he is still being
/// placed) sorts after those whose rating is visible, then by age.</para>
/// </summary>
public static class QuickMatchPlanner
{
    /// <summary>
    /// Whether a room is one the quick match may join: competitive, open, not private, not a
    /// tournament slot, the same mod AND the same install fingerprint (anything else is refused
    /// at the door with <c>mod_mismatch</c>), a 1v1 with its playing seat free, and not hosted
    /// by the searcher himself.
    /// </summary>
    public static bool IsCandidate(LobbySummary? room, string? myUserId, string modId, string modHash)
        => IsCompatibleExceptVersion(room, myUserId, modId)
           && string.Equals(room!.ModCombinedHash, modHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <see cref="IsCandidate"/> without the fingerprint: a room of the same mod waiting for a
    /// 1v1 rival that this install could not join because its version differs.
    /// </summary>
    private static bool IsCompatibleExceptVersion(LobbySummary? room, string? myUserId, string modId)
    {
        if (room == null || string.IsNullOrEmpty(modId)) return false;
        if (!room.Competitive || room.IsPrivate) return false;
        if (!string.Equals(room.Status, "open", StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(room.TournamentMatchId)) return false;
        if (!string.Equals(room.ModId, modId, StringComparison.OrdinalIgnoreCase)) return false;
        if (RoomFormats.Resolve(room.Competitive, room.MaxPlayers, room.SpectatorSlots) != RoomFormat.OneVOne)
            return false;
        if (RoomFormats.SeatsOf(room.MaxPlayers, room.SpectatorSlots, room.CurrentPlayers, room.SpectatorsPresent)
            .PlayingFull) return false;
        return !IsHostedBy(room, myUserId);
    }

    /// <summary>Whether this room's host is the searcher.</summary>
    public static bool IsHostedBy(LobbySummary room, string? userId)
        => !string.IsNullOrEmpty(userId) && string.Equals(room.Host?.Id, userId, StringComparison.Ordinal);

    /// <summary>
    /// Which of two rooms is OLDER: the earlier <c>created_at</c>, then the smaller id — a total
    /// order, so two waiters always agree which of them moves. A room whose time cannot be read
    /// counts as the newest. Negative when <paramref name="a"/> is older.
    /// </summary>
    public static int CompareAge(LobbySummary a, LobbySummary b)
    {
        var ta = RoomAgeFormat.ParseCreatedUtc(a.CreatedAt) ?? DateTime.MaxValue;
        var tb = RoomAgeFormat.ParseCreatedUtc(b.CreatedAt) ?? DateTime.MaxValue;
        var byTime = ta.CompareTo(tb);
        return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>
    /// The rooms to try joining, best first: the host's rating closest to <paramref name="myRating"/>
    /// when both are visible (a withheld one sorts after), then the oldest room. Never excludes a
    /// room for its rating. Empty when nothing is waiting.
    /// </summary>
    public static IReadOnlyList<LobbySummary> RoomsToJoin(
        IEnumerable<LobbySummary>? rooms, string? myUserId, string modId, string modHash, double? myRating)
    {
        var candidates = (rooms ?? Enumerable.Empty<LobbySummary>())
            .Where(r => IsCandidate(r, myUserId, modId, modHash))
            .ToList();
        candidates.Sort((a, b) =>
        {
            var da = Distance(a, myRating);
            var db = Distance(b, myRating);
            var byDistance = (da ?? double.MaxValue).CompareTo(db ?? double.MaxValue);
            return byDistance != 0 ? byDistance : CompareAge(a, b);
        });
        return candidates;
    }

    /// <summary>
    /// How far a room's host is from the searcher, or null when either rating is not visible — the
    /// host's is withheld while he is being placed, and placing hosts must not jump the queue on a
    /// number nobody may see.
    /// </summary>
    private static double? Distance(LobbySummary room, double? myRating)
    {
        if (myRating is not double mine) return null;
        if (room.Host?.InPlacement == true || room.Host?.Rating is not double theirs) return null;
        return Math.Abs(theirs - mine);
    }

    /// <summary>
    /// For a player WAITING in his own quick-match room: the room he should move to — the OLDEST
    /// compatible room older than his own — or null to stay. Null too when his own room is not in
    /// the list: something is out of date, and staying put costs one tick while moving on a stale
    /// list could send two waiters past each other.
    /// </summary>
    public static LobbySummary? PickToMoveTo(
        IEnumerable<LobbySummary>? rooms, string myLobbyId, string? myUserId, string modId, string modHash)
    {
        var list = (rooms ?? Enumerable.Empty<LobbySummary>()).ToList();
        var mine = list.FirstOrDefault(r => string.Equals(r.Id, myLobbyId, StringComparison.Ordinal));
        if (mine == null) return null;
        return list
            .Where(r => !string.Equals(r.Id, myLobbyId, StringComparison.Ordinal))
            .Where(r => IsCandidate(r, myUserId, modId, modHash))
            .Where(r => CompareAge(r, mine) < 0)
            .OrderBy(r => r, Comparer<LobbySummary>.Create(CompareAge))
            .FirstOrDefault();
    }

    /// <summary>
    /// How many rooms are waiting for a 1v1 rival in the same mod with ANOTHER version of it — what
    /// the search says when it finds nothing it can join, so a player on an old build learns why.
    /// </summary>
    public static int OtherVersionCount(IEnumerable<LobbySummary>? rooms, string? myUserId, string modId, string modHash)
        => (rooms ?? Enumerable.Empty<LobbySummary>())
            .Count(r => IsCompatibleExceptVersion(r, myUserId, modId)
                        && !string.Equals(r.ModCombinedHash, modHash, StringComparison.OrdinalIgnoreCase));

    /// <summary>How long a search waits in its own room before giving up and closing it.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(30);

    /// <summary>"02:13" — the time searched, minutes and seconds; hours fold into the minutes.</summary>
    public static string Elapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var minutes = (int)span.TotalMinutes;
        return $"{minutes:00}:{span.Seconds:00}";
    }
}
