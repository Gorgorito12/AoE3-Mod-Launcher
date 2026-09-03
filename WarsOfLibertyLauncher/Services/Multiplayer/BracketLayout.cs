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

    /// <summary>Settled, one way or another.</summary>
    Done,

    /// <summary>Not my match, nothing happening.</summary>
    NotMine,
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
    internal static MatchCardState For(
        TournamentMatch? match,
        string? myUserId,
        IReadOnlyList<TournamentEntrant>? entrants)
    {
        if (match == null) return MatchCardState.NotMine;
        if (string.Equals(match.Status, "bye", StringComparison.Ordinal)) return MatchCardState.Bye;
        if (string.Equals(match.Status, "done", StringComparison.Ordinal)) return MatchCardState.Done;

        bool mine = IsMine(match, myUserId, entrants);

        if (!mine)
        {
            // Somebody else's match. A room on it is worth showing as "being played" but
            // never as something to join — the server refuses that anyway.
            return match.Lobby != null ? MatchCardState.InProgress : MatchCardState.NotMine;
        }

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

    internal static bool CanOpenRegistration(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId)
           && (string.Equals(t!.Status, "draft", StringComparison.Ordinal)
               || string.Equals(t.Status, "ready", StringComparison.Ordinal));

    internal static bool CanCloseRegistration(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId) && string.Equals(t!.Status, "registration", StringComparison.Ordinal);

    internal static bool CanSeed(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId) && string.Equals(t!.Status, "ready", StringComparison.Ordinal);

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

    internal static bool CanCancel(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId)
           && !string.Equals(t!.Status, "finished", StringComparison.Ordinal)
           && !string.Equals(t.Status, "cancelled", StringComparison.Ordinal)
           && !string.Equals(t.Status, "abandoned", StringComparison.Ordinal);

    /// <summary>Applications only exist in approval mode, and only while pending.</summary>
    internal static bool CanDecideEntrant(TournamentDetail? t, string? myUserId, TournamentEntrant? e)
        => IsOwner(t, myUserId)
           && e != null
           && string.Equals(e.Status, "pending", StringComparison.Ordinal);

    /// <summary>Awarding a match by hand, and throwing somebody out. Both are the owner's,
    /// and both only make sense once the bracket exists.</summary>
    internal static bool CanAwardOrDisqualify(TournamentDetail? t, string? myUserId)
        => IsOwner(t, myUserId) && string.Equals(t!.Status, "running", StringComparison.Ordinal);

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
