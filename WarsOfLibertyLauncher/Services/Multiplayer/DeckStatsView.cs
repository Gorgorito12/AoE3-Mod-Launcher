using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One card, as the community deck draws it.</summary>
/// <param name="Card">The internal name, for the icon and the tooltip lookups.</param>
/// <param name="Label">What to show. Already resolved; never an internal name if a name exists.</param>
/// <param name="Players">How many of that civilization's shared decks carry it.</param>
/// <param name="Percent">
/// The share of that civilization's decks, or NULL when there is not enough sample to state one.
/// Null means nothing is drawn in its place — not a dash, and never a 0.
/// </param>
public readonly record struct DeckCardRow(string Card, string Label, int Players, int? Percent);

/// <summary>
/// Every card carried by the same number of a civilization's decks, under one heading.
///
/// <para>The heading is where the percentage lives now — said once per band with its
/// denominator beside it ("83 % · in 5 of the 6") instead of once per row in a column that sat
/// under the win-rate column with a different meaning. <see cref="Percent"/> is null below the
/// sample minimum, and then the heading states the count alone.</para>
/// </summary>
/// <param name="Players">How many of the civilization's decks carry every card in the band.</param>
/// <param name="Percent">That as a share of the civilization's decks, or null below the minimum.</param>
/// <param name="Cards">The cards, alphabetical by label.</param>
public sealed record DeckBand(int Players, int? Percent, IReadOnlyList<DeckCardRow> Cards);

/// <summary>One civilization's group.</summary>
/// <param name="Civ">The internal name, used as the fold key so it survives a repaint.</param>
/// <param name="CivLabel">What to show as the group header.</param>
/// <param name="DistinctCards">Every distinctive card seen for it, tail included.</param>
/// <param name="Decks">
/// How many decks were shared for this civilization — the denominator. See
/// <see cref="DeckStatsView.Group"/> for why it is the maximum and not a field from the server.
/// </param>
/// <param name="Bands">The cards worth naming, grouped by how many decks carry them, most first.</param>
/// <param name="Shown">Every card in <see cref="Bands"/>, flattened in band order.</param>
/// <param name="Tail">The ones in too few decks to name, counted and folded into one line.</param>
public sealed record DeckCivGroup(
    string Civ,
    string CivLabel,
    int DistinctCards,
    int Decks,
    IReadOnlyList<DeckBand> Bands,
    IReadOnlyList<DeckCardRow> Shown,
    IReadOnlyList<DeckCardRow> Tail);

/// <summary>
/// Turns <c>/stats/decks</c> into the per-civilization deck the STATS tab draws.
///
/// <para>Pure and WPF-free, like <see cref="CivStatsView"/> and <see cref="CommunityStatsView"/>
/// beside it, for the same reason: what is worth getting right here is a set of decisions, not a
/// layout.</para>
///
/// <para><b>What the table looked like before.</b> A flat <c>Take(60)</c> over the server's rows,
/// then a per-civilization list capped at seven rows with everything past the cap folded into a
/// line that said "seen once". That cap is the defect the redesign started from: the fold held
/// cards seen once AND cards that had merely fallen past rank seven, and the label asserted the
/// first for all of them — a card in five of six decks printed as an example of "seen once". Now
/// a card is named or it is not, decided by how many decks carry it and by nothing positional,
/// so the tail's label is true by construction.</para>
/// </summary>
public static class DeckStatsView
{
    /// <summary>
    /// How many shared decks a civilization needs before a percentage is published for it.
    ///
    /// <para>Deliberately the same number as <see cref="CivStatsView.MinDecidedForPercent"/>.
    /// Two tables on the same page with two different thresholds is a page that contradicts
    /// itself; if one of them moves, both should.</para>
    ///
    /// <para>Below it the civilization has NO bands: every card goes to the tail and the line
    /// says how many there are. A heading with a percentage over a sample this size is the same
    /// thing the civilization balance refuses to print, for the same reason.</para>
    /// </summary>
    public const int MinDecksForPercent = CivStatsView.MinDecidedForPercent;

    /// <summary>
    /// A card in this many decks or fewer is counted, not named. One or two decks out of a
    /// handful says nothing about what the community prefers; three is where a band starts.
    /// </summary>
    public const int TailMaxPlayers = 2;

    /// <summary>
    /// Group the server's rows by civilization, and each civilization's cards into bands.
    ///
    /// <para><b>Where the denominator comes from, and why not from the payload.</b> The response
    /// carries <c>Contributors</c>: how many players shared a deck for this MOD. Dividing a
    /// Mexican card by that counts everyone who never played Mexico, so it is not the
    /// denominator for anything. There is no per-civilization deck count on the wire either. But
    /// every deck of every civilization contains the generic resource shipments — "Cords of 300
    /// wood" and its siblings appear in all of them — so the LARGEST <c>Players</c> value inside
    /// a civilization is the number of decks shared for that civilization. That is the
    /// denominator, and it is computed here rather than guessed at the view.</para>
    ///
    /// <para><b>A band per distinct count, most-carried first, and never a positional cut.</b>
    /// The cut is what made the old tail lie (see the class remarks). Ordering by times seen
    /// rather than by rarity is the same rule as before: the other direction puts the card
    /// somebody brought once at the top and calls it notable. Ties inside a band break on the
    /// label so the deck does not reshuffle itself between two visits to the tab.</para>
    /// </summary>
    /// <param name="rows">The payload's rows. Nulls and rows with no card are skipped.</param>
    /// <param name="label">Resolves a card's internal name to what the mod calls it.</param>
    /// <param name="civLabel">The same for a civilization.</param>
    /// <param name="expanded">
    /// Civilizations whose tail the player has opened. Those get a band for EVERY count, the
    /// tail included and the sample minimum notwithstanding — the heading then says the count
    /// and no percentage — and carry no tail.
    /// </param>
    public static IReadOnlyList<DeckCivGroup> Group(
        IReadOnlyList<DeckCardEntry>? rows,
        Func<string, string> label,
        Func<string, string> civLabel,
        ISet<string>? expanded = null)
    {
        if (rows == null || rows.Count == 0) return Array.Empty<DeckCivGroup>();

        var byCiv = new Dictionary<string, List<DeckCardEntry>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.Card)) continue;
            var civ = row.Civ ?? "";
            if (!byCiv.TryGetValue(civ, out var list)) byCiv[civ] = list = new List<DeckCardEntry>();
            list.Add(row);
        }

        var groups = new List<DeckCivGroup>();
        foreach (var (civ, entries) in byCiv)
        {
            // The denominator: see the remarks. Never zero — every entry has at least one
            // player behind it, or the server would not have sent the row.
            int decks = entries.Max(e => e.Players);
            bool sampled = decks >= MinDecksForPercent;

            var all = entries
                .Select(e => new DeckCardRow(
                    e.Card,
                    label(e.Card),
                    e.Players,
                    sampled ? Percent(e.Players, decks) : null))
                .OrderByDescending(r => r.Players)
                .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            bool open = expanded != null && expanded.Contains(civ);

            // Which cards earn a heading. Opened: all of them, whatever the sample - the
            // player asked. Otherwise only a civilization with a real sample names anything,
            // and only the cards enough of its decks carry.
            var named = open
                ? all
                : sampled ? all.Where(r => r.Players > TailMaxPlayers).ToList() : new List<DeckCardRow>();

            var bands = named
                .GroupBy(r => r.Players)
                .OrderByDescending(g => g.Key)
                .Select(g => new DeckBand(
                    g.Key,
                    sampled ? Percent(g.Key, decks) : null,
                    g.ToList()))
                .ToList();

            var shown = bands.SelectMany(b => b.Cards).ToList();
            // Keyed by card rather than by row equality: two rows can carry the same numbers.
            var kept = new HashSet<string>(shown.Select(r => r.Card), StringComparer.Ordinal);
            var tail = all.Where(r => !kept.Contains(r.Card)).ToList();

            groups.Add(new DeckCivGroup(civ, civLabel(civ), all.Count, decks, bands, shown, tail));
        }

        return groups
            .OrderByDescending(g => g.DistinctCards)
            .ThenBy(g => g.CivLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Which civilization's deck the page opens on.
    ///
    /// <para>The one the player picked, while it is still in the set — a mod change clears it,
    /// and a civilization that vanished from the payload cannot be shown. Failing that, the one
    /// the player PLAYS: the top civilization of their own ladder row, matched by the label the
    /// server stored (the resolved name) or by the internal one, since the two tables name
    /// civilizations in different namespaces. Failing that, the best-sampled civilization —
    /// most decks, then most cards, then the name — because that is the deck with the most to
    /// say. Never the first group as sorted: that order is by card count, and a civilization
    /// with the most DISTINCT cards is usually the one with the least agreement.</para>
    /// </summary>
    public static DeckCivGroup? PickDefault(
        IReadOnlyList<DeckCivGroup> groups, string? selectedCiv, string? preferredCiv)
    {
        if (groups == null || groups.Count == 0) return null;

        if (!string.IsNullOrEmpty(selectedCiv))
        {
            var chosen = groups.FirstOrDefault(
                g => string.Equals(g.Civ, selectedCiv, StringComparison.Ordinal));
            if (chosen != null) return chosen;
        }

        if (!string.IsNullOrWhiteSpace(preferredCiv))
        {
            var mine = groups.FirstOrDefault(
                g => string.Equals(g.CivLabel, preferredCiv, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(g.Civ, preferredCiv, StringComparison.OrdinalIgnoreCase));
            if (mine != null) return mine;
        }

        return groups
            .OrderByDescending(g => g.Decks)
            .ThenByDescending(g => g.DistinctCards)
            .ThenBy(g => g.CivLabel, StringComparer.CurrentCultureIgnoreCase)
            .First();
    }

    /// <summary>
    /// How many civilizations the pill row names before it folds. The handoff's own number, and
    /// the point of it: a row that never folds becomes three lines of pills above the deck on a
    /// mod with forty civilizations, which is more chrome than the deck it is choosing.
    /// </summary>
    public const int MaxCivPills = 7;

    /// <summary>
    /// Which civilizations the pill row draws, and how many it is holding back.
    ///
    /// <para><b>Ordered best-sampled first</b> — most decks, then most distinct cards, then the
    /// name — which is deliberately the SAME order <see cref="PickDefault"/> falls back to, so the
    /// first pill is the civilization the page opens on. The order <see cref="Group"/> returns is
    /// by distinct cards and is wrong for this: the civilization with the most distinct cards is
    /// usually the one with the least agreement.</para>
    ///
    /// <para><b>⚠ The selected civilization is never one of the hidden ones.</b> It takes the LAST
    /// shown slot rather than the first, so choosing a civilization from the fold does not shuffle
    /// the pills in front of it. Without this rule the page draws a deck whose pill is off the row
    /// and NO pill lit — which is worse than having no row at all, because the one thing the row
    /// exists to say is whose cards these are.</para>
    /// </summary>
    /// <param name="max">The cap, or anything &lt;= 0 for "no fold" — which is what the row passes
    /// once the player has opened it.</param>
    public static (IReadOnlyList<DeckCivGroup> Shown, int Hidden) CivPills(
        IReadOnlyList<DeckCivGroup>? groups, string? selectedCiv, int max = MaxCivPills)
    {
        if (groups == null || groups.Count == 0)
            return (Array.Empty<DeckCivGroup>(), 0);

        var ordered = groups
            .OrderByDescending(g => g.Decks)
            .ThenByDescending(g => g.DistinctCards)
            .ThenBy(g => g.CivLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (max <= 0 || ordered.Count <= max) return (ordered, 0);

        var shown = ordered.Take(max).ToList();

        if (!string.IsNullOrEmpty(selectedCiv)
            && !shown.Any(g => string.Equals(g.Civ, selectedCiv, StringComparison.Ordinal)))
        {
            var chosen = ordered.FirstOrDefault(
                g => string.Equals(g.Civ, selectedCiv, StringComparison.Ordinal));
            if (chosen != null) shown[max - 1] = chosen;
        }

        return (shown, ordered.Count - max);
    }

    /// <summary>
    /// The share of a civilization's decks, rounded to whole points.
    ///
    /// <para>Rounded away from zero so a card in one deck out of two hundred reads as 1 %, not
    /// as 0 % — a card that IS in somebody's deck must never be reported as being in nobody's.</para>
    /// </summary>
    public static int Percent(int players, int decks)
    {
        if (decks <= 0 || players <= 0) return 0;
        return (int)Math.Max(1, Math.Round(players * 100.0 / decks, MidpointRounding.AwayFromZero));
    }
}
