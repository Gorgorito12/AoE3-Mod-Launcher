using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Turning a flat list of bracket matches into something that can be drawn, and deciding
/// what the button on each card says.
///
/// <para>Pure and WPF-free on purpose, like <c>RankingTableLayout</c> and
/// <c>PlayerStanding</c>: the geometry is integer arithmetic that is trivial to pin in a
/// test and impossible to check by looking at a screenshot, and the button state is a rule
/// with eight branches that would otherwise live inline in a 14,000-line file.</para>
///
/// <para><b>The client decides nothing about the tournament itself.</b> Whether a match is
/// playable, who won, who may seed — all of that comes from the server and is only read
/// here. What this class decides is what to DRAW.</para>
/// </summary>
internal static class BracketLayout
{
    /// <summary>One card, and the rows of the grid it spans.</summary>
    internal sealed record BracketCell(TournamentMatch Match, int RowStart, int RowSpan);

    /// <summary>One round, drawn as a column.</summary>
    internal sealed record BracketColumn(int Round, IReadOnlyList<BracketCell> Cells);

    internal sealed record BracketGrid(IReadOnlyList<BracketColumn> Columns, int RowCount);

    /// <summary>
    /// Lay the bracket out in columns of rounds and rows of first-round slots.
    ///
    /// <para>The unit is one first-round match. A match at <c>(round r, position p)</c>
    /// covers rows <c>p * 2^(r-1)</c> through <c>(p+1) * 2^(r-1) - 1</c>, which is exactly
    /// what centres every later card on the two that feed it — the same arithmetic that
    /// makes a bracket look like a bracket on paper.</para>
    ///
    /// <para>Deterministic integers rather than measured positions, so the whole layout can
    /// be asserted without constructing a single WPF element.</para>
    /// </summary>
    internal static BracketGrid Build(IReadOnlyList<TournamentMatch>? matches)
    {
        if (matches == null || matches.Count == 0)
        {
            return new BracketGrid(Array.Empty<BracketColumn>(), 0);
        }

        var rounds = matches.Select(m => m.Round).Distinct().OrderBy(r => r).ToList();
        int firstRoundCount = matches.Count(m => m.Round == rounds[0]);
        // A bracket whose first round has N matches is N rows tall, whatever the rounds
        // above it do.
        int rowCount = Math.Max(1, firstRoundCount);

        var columns = new List<BracketColumn>(rounds.Count);
        foreach (int round in rounds)
        {
            int span = 1 << (round - rounds[0]);
            var cells = matches
                .Where(m => m.Round == round)
                .OrderBy(m => m.Position)
                .Select(m => new BracketCell(m, m.Position * span, span))
                .ToList();
            columns.Add(new BracketColumn(round, cells));
        }

        return new BracketGrid(columns, rowCount);
    }

    /// <summary>
    /// The localisation key naming a round.
    ///
    /// <para>Named rounds rather than numbers where a name exists, because "Final" is what
    /// people call it and "Round 4" is not.</para>
    /// </summary>
    internal static string RoundLabelKey(int round, int? roundsTotal)
    {
        if (roundsTotal is not int total || total <= 0) return "MpTournamentRoundN";
        if (round == total) return "MpTournamentRoundFinal";
        if (round == total - 1) return "MpTournamentRoundSemi";
        if (round == total - 2) return "MpTournamentRoundQuarter";
        return "MpTournamentRoundN";
    }

    /// <summary>
    /// What became of one side of a match.
    ///
    /// <para><b>A figure only where a game happened.</b> The handoff draws 1 and 0, one on each
    /// side, and that is right for a match somebody played: both rows carry a value and the card
    /// reads at a glance. It is a LIE on a walkover or a disqualification — nobody played, so
    /// there is nothing that finished 1-0 — and those keep the tag they always had, with the
    /// losing side left blank. The wire has no score field at all; "1" and "0" are this launcher
    /// saying who won, in the reference's own notation, not a scoreline it received.</para>
    ///
    /// <para>Pure and here rather than inline in the renderer, because the walkover case is the
    /// whole point and it is not something a screenshot of the ordinary bracket would ever show.
    /// Pinned by <c>BracketLayoutTests</c>.</para>
    /// </summary>
    internal static SideMarker MarkerFor(bool bye, bool decided, bool won, string? outcome)
    {
        if (bye) return SideMarker.ByeTag;
        if (!decided || !won) return SideMarker.None;

        return outcome switch
        {
            "walkover" => SideMarker.WalkoverTag,
            "dq" => SideMarker.DqTag,
            _ => SideMarker.One,
        };
    }

    /// <summary>
    /// The losing side of the same match. Separate from <see cref="MarkerFor"/> because only a
    /// PLAYED match gives the loser anything at all: a walkover leaves it blank, which is what
    /// stops the card from claiming a game that never happened.
    /// </summary>
    internal static SideMarker LoserMarkerFor(bool decided, bool known, string? outcome)
        => decided && known && outcome is not ("walkover" or "dq")
            ? SideMarker.Zero
            : SideMarker.None;
}

/// <summary>What the card for one bracket match offers the person looking at it.</summary>
internal enum MatchCardState
{
    /// <summary>Nobody played; somebody walked through.</summary>
    Bye,

    /// <summary>Mine, but the other side has not been decided yet.</summary>
    WaitingOpponent,

    /// <summary>Mine, both sides known, no room open. This is the "play my match" card.</summary>
    Playable,

    /// <summary>Mine, and the OTHER side has already opened the room.</summary>
    JoinRoom,

    /// <summary>Mine, and the room I opened is still there.</summary>
    ReturnToRoom,

    /// <summary>A room exists for it but it is not my match.</summary>
    InProgress,

    /// <summary>
    /// Same match, seen by the person who RUNS the tournament: a room exists, it is not mine,
    /// and I am the owner or a co-organiser.
    ///
    /// <para><b>Watching, never joining.</b> The distinction is the whole state: whoever can
    /// settle a match by hand should be able to look at it first, and looking is not a seat.
    /// </para>
    ///
    /// <para><b>Preview surface.</b> The server refuses this today — three separate times: a
    /// lobby in <c>in_game</c> rejects every join before it looks at seats or roles, a lobby
    /// bound to a bracket slot admits only that slot's entrants with no owner exemption, and
    /// tournament rooms are created with zero spectator slots. So this state currently exists
    /// only under the fabricated tournaments, where it can be looked at and argued about
    /// before any of those three is opened.</para>
    /// </summary>
    SuperviseRoom,

    /// <summary>Settled, one way or another.</summary>
    Done,

    /// <summary>Not my match, nothing happening.</summary>
    NotMine,
}

/// <summary>What one side of a decided card shows on its right-hand edge.</summary>
internal enum SideMarker
{
    /// <summary>Nothing. An undecided side, and the losing side of a match nobody played.</summary>
    None,

    /// <summary>The winner of a match that was actually played.</summary>
    One,

    /// <summary>The loser of a match that was actually played.</summary>
    Zero,

    /// <summary>Nobody was there to play. Sits on the single side a bye card draws.</summary>
    ByeTag,

    /// <summary>The other side never turned up.</summary>
    WalkoverTag,

    /// <summary>The other side was disqualified.</summary>
    DqTag,
}

/// <summary>
/// Deciding, in one place, what a bracket card lets you do.
///
/// <para>It lives apart from the rendering because it is a rule and not a drawing: eight
/// outcomes, several of which differ only by who I am, and every one of them worth a test.
/// The version of this that lives inline in a click handler is the version that quietly
/// offers "Play my match" to a spectator.</para>
/// </summary>
internal static class MatchCards
{
    /// <summary>
    /// What this card offers me.
    /// </summary>
    /// <param name="match">The bracket slot.</param>
    /// <param name="myUserId">My backend user id, or null when signed out.</param>
    /// <param name="entrants">Every entrant of the tournament, for the frozen rosters.</param>
    /// <param name="canSupervise">
    /// Whether I run this tournament — <c>TournamentPermissions.IsOwnerOrManager</c>, which is
    /// the same test that already decides who may settle a match by hand.
    ///
    /// <para>Optional, and that is deliberate: every existing caller keeps its meaning and the
    /// answer for a plain viewer is unchanged, so the cases already pinned in
    /// <c>BracketLayoutTests</c> still describe what they described. Only the caller that
    /// knows who is looking passes it.</para>
    ///
    /// <para>It can never turn one of MY cards into a watching one — see the guard below.
    /// An organiser who is also an entrant plays their own match; running the thing does not
    /// take that away.</para>
    /// </param>
    internal static MatchCardState For(
        TournamentMatch? match,
        string? myUserId,
        IReadOnlyList<TournamentEntrant>? entrants,
        bool canSupervise = false)
    {
        if (match == null) return MatchCardState.NotMine;
        if (string.Equals(match.Status, "bye", StringComparison.Ordinal)) return MatchCardState.Bye;
        if (string.Equals(match.Status, "done", StringComparison.Ordinal)) return MatchCardState.Done;

        bool mine = IsMine(match, myUserId, entrants);

        if (!mine)
        {
            // Somebody else's match. A room on it is worth showing as "being played" but
            // never as something to JOIN — the server refuses that anyway, and it still
            // does. What changed is that the person running the tournament is offered a way
            // to LOOK, which is a different request and a different answer: they are the one
            // who may have to decide this match by hand afterwards.
            if (match.Lobby != null && canSupervise) return MatchCardState.SuperviseRoom;
            return match.Lobby != null ? MatchCardState.InProgress : MatchCardState.NotMine;
        }

        // Everything below here is MY match, so supervising never reaches it: an organiser
        // who entered their own tournament gets Playable / JoinRoom / ReturnToRoom exactly as
        // any other entrant does. Running it is not a reason to lose your own match card.

        // Mine, and one side is still unknown. There is nothing to play yet.
        if (string.IsNullOrEmpty(match.Entrant1Id) || string.IsNullOrEmpty(match.Entrant2Id))
        {
            return MatchCardState.WaitingOpponent;
        }

        if (match.Lobby == null) return MatchCardState.Playable;

        // A room exists. Whether I open it or join it depends on whether it is mine, and
        // the answer comes from the server's host id rather than from anything local.
        return string.Equals(match.Lobby.HostUserId, myUserId, StringComparison.Ordinal)
            ? MatchCardState.ReturnToRoom
            : MatchCardState.JoinRoom;
    }

    /// <summary>
    /// Whether I am on either side of this match, by the FROZEN roster.
    ///
    /// <para>Frozen and not live: a saved team can drop somebody the day after entering,
    /// and the person who was registered is the person who plays. Reading a team's current
    /// members here would lock a registered player out of their own match.</para>
    ///
    /// <para>A disqualified or withdrawn entrant is deliberately still "mine" — the card
    /// says so through its status, and pretending the match belongs to nobody would make
    /// the bracket harder to read, not easier.</para>
    /// </summary>
    internal static bool IsMine(
        TournamentMatch match,
        string? myUserId,
        IReadOnlyList<TournamentEntrant>? entrants)
    {
        if (string.IsNullOrEmpty(myUserId) || entrants == null) return false;
        return InEntrant(match.Entrant1Id, myUserId, entrants)
            || InEntrant(match.Entrant2Id, myUserId, entrants);
    }

    private static bool InEntrant(
        string? entrantId, string myUserId, IReadOnlyList<TournamentEntrant> entrants)
    {
        if (string.IsNullOrEmpty(entrantId)) return false;
        var e = entrants.FirstOrDefault(x => string.Equals(x.Id, entrantId, StringComparison.Ordinal));
        if (e?.MemberIds == null) return false;
        return e.MemberIds.Any(u => string.Equals(u, myUserId, StringComparison.Ordinal));
    }

    /// <summary>Whether a disqualified or withdrawn side means this card offers nothing.</summary>
    internal static bool EntrantIsOut(TournamentEntrant? e)
        => e != null
           && (string.Equals(e.Status, "disqualified", StringComparison.Ordinal)
               || string.Equals(e.Status, "withdrawn", StringComparison.Ordinal)
               || string.Equals(e.Status, "rejected", StringComparison.Ordinal));
}

/// <summary>
/// What the person looking at a tournament is allowed to do with it.
///
/// <para>Every one of these is re-checked by the server, which is what actually enforces
/// them. This exists so the launcher does not offer a button that will answer 403 — the
/// same relationship <c>_isHostInCurrentRoom</c> has with the room's kick and start.</para>
///
/// <para><b>The server sends the identity; the client derives the verdict.</b> That is why
/// the detail carries <c>owner_user_id</c> rather than an <c>is_owner</c> flag.</para>
/// </summary>
internal static class TournamentPermissions
{
    internal static bool IsOwner(TournamentSummary? t, string? myUserId)
        => t != null
           && !string.IsNullOrEmpty(myUserId)
           && string.Equals(t.OwnerUserId, myUserId, StringComparison.Ordinal);

    /// <summary>
    /// The owner, or somebody the owner appointed to help run this tournament.
    ///
    /// <para><b>Separate from <see cref="IsOwner"/> and not a loosening of it.</b> Nine
    /// predicates delegate to <c>IsOwner</c>, and two of them must NOT widen: cancelling is
    /// irreversible and is the owner's alone, and "created by you" is a statement of fact a
    /// co-organiser cannot make. Widening <c>IsOwner</c> in place would have widened all
    /// nine silently, including those two.</para>
    ///
    /// <para>Takes a <see cref="TournamentDetail"/> because that is the only payload
    /// carrying the list: <c>IsOwner</c> takes a summary so the list card can call it, and
    /// the list does not know about managers. That is deliberate — see
    /// <see cref="TournamentDetail.ManagerUserIds"/>.</para>
    /// </summary>
    internal static bool IsOwnerOrManager(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId)
           || (t?.ManagerUserIds != null
               && !string.IsNullOrEmpty(myUserId)
               && t.ManagerUserIds.Any(u => string.Equals(u, myUserId, StringComparison.Ordinal)));

    /// <summary>Registration is open and I am not already in it.</summary>
    internal static bool CanEnter(TournamentDetail? t, string? myUserId)
    {
        if (t == null || string.IsNullOrEmpty(myUserId)) return false;
        if (!string.Equals(t.Status, "registration", StringComparison.Ordinal)) return false;
        return MyEntrant(t, myUserId) == null;
    }

    /// <summary>I am in, and the bracket has not been drawn. Only the captain may pull a
    /// whole line-up out — everybody else has to ask them.</summary>
    internal static bool CanWithdraw(TournamentDetail? t, string? myUserId)
    {
        if (t == null || string.IsNullOrEmpty(myUserId)) return false;
        if (string.Equals(t.Status, "running", StringComparison.Ordinal)) return false;
        if (string.Equals(t.Status, "finished", StringComparison.Ordinal)) return false;
        var mine = MyEntrant(t, myUserId);
        return mine != null
               && string.Equals(mine.CaptainUserId, myUserId, StringComparison.Ordinal);
    }

    // ---- Running the bracket: the owner, or anybody the owner appointed. CanStart and
    // CanAwardMatch inherit this through CanSeed and CanAwardOrDisqualify.

    internal static bool CanOpenRegistration(TournamentDetail? t, string? myUserId)
        => IsOwnerOrManager(t, myUserId)
           && (string.Equals(t!.Status, "draft", StringComparison.Ordinal)
               || string.Equals(t.Status, "ready", StringComparison.Ordinal));

    internal static bool CanCloseRegistration(TournamentDetail? t, string? myUserId)
        => IsOwnerOrManager(t, myUserId)
           && string.Equals(t!.Status, "registration", StringComparison.Ordinal);

    internal static bool CanSeed(TournamentDetail? t, string? myUserId)
        => IsOwnerOrManager(t, myUserId) && string.Equals(t!.Status, "ready", StringComparison.Ordinal);

    /// <summary>Seeded, closed, and at least two confirmed entrants. Checked here so the
    /// button is not offered for a bracket the server would refuse to draw.</summary>
    internal static bool CanStart(TournamentDetail? t, string? myUserId)
    {
        if (!CanSeed(t, myUserId)) return false;
        var playing = (t!.Entrants ?? new List<TournamentEntrant>())
            .Where(e => string.Equals(e.Status, "confirmed", StringComparison.Ordinal))
            .ToList();
        return playing.Count >= 2 && playing.All(e => e.Seed.HasValue);
    }

    /// <summary>
    /// Cancelling stays the OWNER's, and this is the one predicate that must never widen:
    /// it is irreversible and it ends everybody else's tournament too.
    /// </summary>
    internal static bool CanCancel(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId)
           && !string.Equals(t!.Status, "finished", StringComparison.Ordinal)
           && !string.Equals(t.Status, "cancelled", StringComparison.Ordinal)
           && !string.Equals(t.Status, "abandoned", StringComparison.Ordinal);

    /// <summary>Applications only exist in approval mode, and only while pending.</summary>
    internal static bool CanDecideEntrant(TournamentDetail? t, string? myUserId, TournamentEntrant? e)
        => IsOwnerOrManager(t, myUserId)
           && e != null && string.Equals(e.Status, "pending", StringComparison.Ordinal);

    /// <summary>Awarding a match by hand, and throwing somebody out. Both are the owner's,
    /// and both only make sense once the bracket exists.</summary>
    internal static bool CanAwardOrDisqualify(TournamentDetail? t, string? myUserId)
        => IsOwnerOrManager(t, myUserId)
           && string.Equals(t!.Status, "running", StringComparison.Ordinal);

    /// <summary>
    /// Whether THIS match may be handed to one side by hand.
    ///
    /// <para>Owner and running come from <see cref="CanAwardOrDisqualify"/>; the rest is about
    /// the match. Both sides have to be known — awarding an empty slot would advance somebody
    /// into a round nobody has reached, and the server refuses it — and nothing may be settled
    /// yet, because the launcher has no way to unsettle it: undoing a bracket result is the
    /// maintainer's CLI on purpose.</para>
    ///
    /// <para>Separate from the renderer so the four conditions can be pinned. A card that
    /// offered this on a finished match would be offering an action that comes back as a 409.</para>
    /// </summary>
    internal static bool CanAwardMatch(
        TournamentDetail? t, string? myUserId, TournamentMatch? m)
    {
        if (!CanAwardOrDisqualify(t, myUserId) || m == null) return false;
        if (string.Equals(m.Status, "done", StringComparison.Ordinal)
            || string.Equals(m.Status, "bye", StringComparison.Ordinal)) return false;
        return !string.IsNullOrEmpty(m.Entrant1Id) && !string.IsNullOrEmpty(m.Entrant2Id);
    }

    /// <summary>
    /// Whether I may appoint or remove a co-organiser. The OWNER only, always.
    ///
    /// <para>A co-organiser who can appoint co-organisers can be talked into handing the
    /// tournament around, which is the same hole <c>tournament:transfer</c> is a maintainer
    /// command to avoid. The server refuses it too; this only keeps the button away.</para>
    /// </summary>
    /// <summary>
    /// Whether I may tell these two to play their tie again.
    ///
    /// <para><b>Pending only, and that is the whole safety of it.</b> A decided match has its
    /// winner seated in the round above and its game already counted for the ladder; putting
    /// it back would have to un-seat one and cannot un-rate the other, which is why undoing a
    /// played result lives in the maintainer's CLI and not here. A pending match has none of
    /// that: nothing to reverse, nothing rated, and the two entrants could already open a new
    /// room themselves. What the organiser adds is that somebody SAYS so.</para>
    ///
    /// <para>Both sides have to be known, for the same reason awarding needs them: a tie
    /// waiting on a feeder has nobody to tell.</para>
    /// </summary>
    internal static bool CanReplayMatch(TournamentDetail? t, string? myUserId, TournamentMatch? m)
    {
        if (m == null) return false;
        if (!string.Equals(m.Status, "pending", StringComparison.Ordinal)) return false;
        if (string.IsNullOrEmpty(m.Entrant1Id) || string.IsNullOrEmpty(m.Entrant2Id)) return false;
        return CanAwardOrDisqualify(t, myUserId);
    }

    internal static bool CanAppointManagers(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId)
           && !string.Equals(t!.Status, "cancelled", StringComparison.Ordinal)
           && !string.Equals(t.Status, "finished", StringComparison.Ordinal);

    /// <summary>The entrant I am part of, or null.</summary>
    internal static TournamentEntrant? MyEntrant(TournamentDetail? t, string? myUserId)
    {
        if (t?.Entrants == null || string.IsNullOrEmpty(myUserId)) return null;
        return t.Entrants.FirstOrDefault(e =>
            e.MemberIds != null
            && e.MemberIds.Any(u => string.Equals(u, myUserId, StringComparison.Ordinal))
            && !MatchCards.EntrantIsOut(e));
    }
}
