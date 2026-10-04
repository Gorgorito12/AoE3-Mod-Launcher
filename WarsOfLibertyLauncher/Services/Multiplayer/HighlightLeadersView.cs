using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// One row of a highlight card in Ranking › Highlights: its place, who (a player, a side, or a
/// civilization), the figure — or the 🔥 streak — and a detail line under the name.
/// </summary>
/// <param name="Place">1 to 5.</param>
/// <param name="Name">The player, the winners joined with "and", or the civilization.</param>
/// <param name="UserIds">Every player the row names, for marking the viewer's own row.</param>
/// <param name="AvatarUrl">The (first) player's picture; null for a civilization.</param>
/// <param name="Figure">"+96", "23", "82 %"; null for a streak.</param>
/// <param name="Streak">Wins in a row, drawn as the 🔥 pill; null otherwise.</param>
/// <param name="Positive">The figure is a gain (drawn green).</param>
/// <param name="Detail">The line under the name; null when there is nothing to add.</param>
/// <param name="CivModId">The civilization's mod, for its flag; null for players.</param>
/// <param name="Civ">The civilization as the server stored it; null for players.</param>
public sealed record LeaderRow(
    int Place,
    string Name,
    IReadOnlyList<string> UserIds,
    string? AvatarUrl,
    string? Figure,
    int? Streak,
    bool Positive,
    string? Detail,
    string? CivModId,
    string? Civ);

/// <summary>
/// One card of Ranking › Highlights: a highlight's top five.
/// </summary>
/// <param name="Kind">Which highlight; also the card's place in the grid.</param>
/// <param name="TitleKey">The card's title (Strings key).</param>
/// <param name="Rule">The rule in one line under the title; null when the server did not say the threshold.</param>
/// <param name="Rows">Up to five rows.</param>
/// <param name="Team">The rows are the team ladder's (climb and streak only).</param>
/// <param name="HasBothLadders">Climb and streak: both ladders have someone, so the card offers the switch.</param>
public sealed record LeaderCard(
    HighlightCellKind Kind,
    string TitleKey,
    string? Rule,
    IReadOnlyList<LeaderRow> Rows,
    bool Team,
    bool HasBothLadders);

/// <summary>
/// Ranking › Highlights, decided without WPF: which cards there are, in the strip's priority
/// order (<see cref="HighlightCellKind"/>), and what each row says. The SERVER picks the players
/// and their order (<c>leaders</c>, <c>GET /stats/highlights</c>); this only words them.
///
/// <para>A card with nobody in it is left out, never drawn empty — the same rule as the strip
/// (56a). Climb and streak are per ladder: the card shows the ladder asked for, or the other one
/// when the asked one is empty, and offers the switch only when both have someone.</para>
/// </summary>
public static class HighlightLeadersView
{
    /// <summary>The month's cards.</summary>
    /// <param name="month">The month, with its <c>leaders</c>; null or without them gives nothing.</param>
    /// <param name="climbTeam">Show the team ladder in the climb card.</param>
    /// <param name="streakTeam">Show the team ladder in the streak card.</param>
    /// <param name="culture">The launcher's language, for numbers.</param>
    /// <param name="text">How a key and its arguments become text (Strings.Format, injected so this stays pure).</param>
    public static IReadOnlyList<LeaderCard> Cards(
        MonthHighlights? month, bool climbTeam, bool streakTeam, CultureInfo culture,
        Func<string, object[], string> text)
    {
        var cards = new List<LeaderCard>();
        var leaders = month?.Leaders;
        if (month == null || leaders == null) return cards;
        string N(int n) => n.ToString("N0", culture);
        // A rating is never grouped: the table prints 1460, not 1.460 (Spanish groups four digits).
        static string R(int n) => n.ToString(CultureInfo.InvariantCulture);
        string T(string key, params object[] args) => text(key, args);

        // Biggest climb, per ladder.
        {
            var (rows, team, both) = PerLadder(leaders.BiggestClimb, climbTeam);
            var built = rows.Select((p, i) => Player(i, p,
                    figure: "+" + N(p.Points ?? 0),
                    positive: true,
                    detail: p.RatingFrom is int from && p.RatingTo is int to
                        ? T("MpHlDetailClimb", R(from), R(to), N(p.Matches ?? 0))
                        : p.Matches is int m ? T("MpHlDetailMatches", N(m)) : null))
                .ToList();
            if (built.Count > 0)
                cards.Add(new(HighlightCellKind.TopClimb, "MpHlStripTopGain",
                    month.MinMatches > 0 ? T("MpHlRuleClimb", N(month.MinMatches)) : null, built, team, both));
        }

        // Most wins.
        {
            var built = (leaders.MostWins ?? new()).Where(p => p.Wins is > 0).Take(5)
                .Select((p, i) => Player(i, p,
                    figure: N(p.Wins ?? 0),
                    positive: false,
                    detail: p.Matches is int m && m > 0
                        ? T("MpHlDetailWins", N(m), N((int)Math.Round(100.0 * (p.Wins ?? 0) / m)))
                        : null))
                .ToList();
            if (built.Count > 0) cards.Add(new(HighlightCellKind.MostWins, "MpHlStripMostWins", T("MpHlRuleWins"), built, false, false));
        }

        // Most matches.
        {
            var built = (leaders.MostMatches ?? new()).Where(p => p.Matches is int).Take(5)
                .Select((p, i) => Player(i, p,
                    figure: N(p.Matches ?? 0),
                    positive: false,
                    detail: p.Wins is int w ? T("MpHlDetailWon", N(w)) : null))
                .ToList();
            if (built.Count > 0) cards.Add(new(HighlightCellKind.MostMatches, "MpHlStripMostGames", T("MpHlRuleMatches"), built, false, false));
        }

        // Best streak, per ladder.
        {
            var (rows, team, both) = PerLadder(leaders.BestStreak, streakTeam);
            var built = rows.Where(p => p.Wins is > 0)
                .Select((p, i) => new LeaderRow(i + 1, NameOf(p), new[] { p.UserId }, p.AvatarUrl,
                    null, p.Wins, false, null, null, null))
                .ToList();
            if (built.Count > 0)
                cards.Add(new(HighlightCellKind.BestStreak, "MpHlStripBestStreak", T("MpHlRuleStreak"), built, team, both));
        }

        // Best win rate.
        {
            var built = (leaders.BestWinRate ?? new()).Where(p => p.Percent is int && p.Matches is > 0).Take(5)
                .Select((p, i) => Player(i, p,
                    figure: N(p.Percent ?? 0) + " %",
                    positive: false,
                    detail: T("MpHlDetailRate", N(p.Wins ?? 0), N(p.Matches ?? 0))))
                .ToList();
            if (built.Count > 0)
                cards.Add(new(HighlightCellKind.BestWinRate, "MpHlStripBestRate",
                    month.MinRateMatches > 0 ? T("MpHlRuleRate", N(month.MinRateMatches)) : null, built, false, false));
        }

        // Biggest upset: the winners as one row, against the losers in the detail.
        {
            var built = (leaders.BiggestUpset ?? new()).Where(u => u.Gap > 0 && (u.Winners?.Count ?? 0) > 0).Take(5)
                .Select((u, i) =>
                {
                    var winners = u.Winners ?? new();
                    var losers = u.Losers ?? new();
                    return new LeaderRow(i + 1,
                        NameList.Join(winners.Select(NameOf)),
                        winners.Select(w => w.UserId).ToList(),
                        winners[0].AvatarUrl,
                        "+" + N(u.Gap),
                        null,
                        true,
                        T(winners.Count > 1 ? "MpHlDetailUpsetTeam" : "MpHlDetailUpset",
                            NameList.Join(losers.Select(NameOf)), R(u.WinnersRating), R(u.LosersRating)),
                        null, null);
                })
                .ToList();
            if (built.Count > 0) cards.Add(new(HighlightCellKind.BiggestUpset, "MpHlStripUpset", T("MpHlRuleUpset"), built, false, false));
        }

        // Civilization of the month.
        {
            var built = (leaders.TopCiv ?? new()).Where(c => c.Picks > 0 && !string.IsNullOrWhiteSpace(c.Civ)).Take(5)
                .Select((c, i) => new LeaderRow(i + 1, c.Civ, Array.Empty<string>(), null,
                    N(c.Picks), null, false,
                    T("MpHlDetailCiv", N(c.Wins), N((int)Math.Round(100.0 * c.Wins / c.Picks))),
                    c.ModId, c.Civ))
                .ToList();
            if (built.Count > 0)
                cards.Add(new(HighlightCellKind.TopCiv, "MpHlStripTopCiv",
                    month.MinCivPicks > 0 ? T("MpHlRuleCiv", N(month.MinCivPicks)) : null, built, false, false));
        }

        return cards;
    }

    /// <summary>The ladder asked for, or the other when it is empty, and whether both have someone.</summary>
    private static (IReadOnlyList<HighlightPlayer> Rows, bool Team, bool Both) PerLadder(HighlightPerModeList? lists, bool wantTeam)
    {
        var solo = (lists?.Default ?? new()).Take(5).ToList();
        var team = (lists?.Team ?? new()).Take(5).ToList();
        var both = solo.Count > 0 && team.Count > 0;
        if (wantTeam && team.Count > 0) return (team, true, both);
        if (solo.Count > 0) return (solo, false, both);
        return (team, team.Count > 0, both);
    }

    private static LeaderRow Player(int index, HighlightPlayer p, string figure, bool positive, string? detail)
        => new(index + 1, NameOf(p), new[] { p.UserId }, p.AvatarUrl, figure, null, positive, detail, null, null);

    private static string NameOf(HighlightPlayer p)
        => string.IsNullOrWhiteSpace(p.DisplayName) ? "?" : p.DisplayName;
}
