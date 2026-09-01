using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>The player faced most often, and how it has gone against them.</summary>
public sealed record FrequentOpponent(
    string Name, string? AvatarUrl, int Wins, int Losses, int Matches);

/// <summary>How many matches the player has, split by whether they were decided.</summary>
public sealed record ProfileTotals(int Played, int Decided, int Unrated);

/// <summary>
/// Everything the Profile tab works out for itself, kept pure so the rules are pinned by
/// <c>ProfileSummaryViewTests</c>.
///
/// <para><b>Nothing here asks the server anything</b> — "most played map" and "usual opponent"
/// are computed over the page of history the tab already has. The handoff says so explicitly,
/// and it is also what stops those cards from being able to disagree with the list of matches
/// printed under them.</para>
/// </summary>
public static class ProfileSummaryView
{
    /// <summary>
    /// The rating after each RATED match, oldest first — the points of the profile's curve.
    ///
    /// <para>Rated matches only, and in reverse of the order they arrive (the server sends
    /// newest first). Including unrated ones would draw a flat step for every match that moved
    /// nothing, which is most of them, and the shape of the line is the whole content of that
    /// card.</para>
    ///
    /// <para>The FIRST point is the rating BEFORE the oldest match shown, so a player with one
    /// match gets a line from where they started to where they are rather than a single dot.
    /// That is also what makes the "1500 initial" label under it true.</para>
    /// </summary>
    public static IReadOnlyList<double> RatingCurve(IReadOnlyList<MatchHistoryRow>? rows)
    {
        if (rows == null || rows.Count == 0) return Array.Empty<double>();

        var rated = rows
            .Where(r => MatchHistoryView.IsRated(r) && r.RatingAfter.HasValue)
            .Reverse()
            .ToList();
        if (rated.Count == 0) return Array.Empty<double>();

        var points = new List<double>();
        if (rated[0].RatingBefore is double first) points.Add(first);
        foreach (var r in rated) points.Add(r.RatingAfter!.Value);
        return points;
    }

    /// <summary>
    /// Matches played, of which decided, of which did not count.
    ///
    /// <para>All three are stated because the first on its own is misleading: most matches are
    /// not decided, so "3 matches" beside a record of 0-1 reads as an inconsistency until the
    /// other two numbers explain it.</para>
    /// </summary>
    public static ProfileTotals Totals(IReadOnlyList<MatchHistoryRow>? rows)
    {
        var played = 0;
        var decided = 0;
        var unrated = 0;
        foreach (var row in rows ?? Array.Empty<MatchHistoryRow>())
        {
            played++;
            if (MatchOutcomeView.Classify(row.Result) != MatchVerdict.NoResult) decided++;
            if (!MatchHistoryView.IsRated(row)) unrated++;
        }
        return new ProfileTotals(played, decided, unrated);
    }

    /// <summary>
    /// Who this player meets most often, with the record against them.
    ///
    /// <para>Counted over every match with a named roster, not only one-on-ones: somebody you
    /// keep ending up in team games with is as much a "usual opponent" as a 1v1 regular, and
    /// restricting it to duels would leave the card empty for anybody who plays teams.</para>
    ///
    /// <para><b>Teammates are excluded.</b> A four-player match lists everybody, so counting
    /// the roster wholesale would eventually name the player's own partner as their rival. A
    /// participant on the caller's own team is skipped, which for a 1v1 (both stored as team 0)
    /// would exclude the opponent too — hence the exception: with exactly two players the other
    /// one is the opponent by construction, whatever the stored team says.</para>
    ///
    /// <para>Ties break on the name so the card does not name a different rival every time it
    /// is drawn.</para>
    /// </summary>
    public static FrequentOpponent? FrequentOpponent(
        IReadOnlyList<MatchHistoryRow>? rows, string? meId)
    {
        if (rows == null || rows.Count == 0 || string.IsNullOrEmpty(meId)) return null;

        var tally = new Dictionary<string, (string Name, string? Avatar, int W, int L, int N)>(
            StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (row.Participants == null || row.Participants.Count < 2) continue;

            var me = row.Participants.FirstOrDefault(
                p => string.Equals(p.UserId, meId, StringComparison.Ordinal));
            if (me == null) continue;

            var duel = row.Participants.Count == 2;
            foreach (var them in row.Participants)
            {
                if (string.Equals(them.UserId, meId, StringComparison.Ordinal)) continue;
                if (!duel && them.Team == me.Team) continue;

                var name = string.IsNullOrEmpty(them.DisplayName)
                    ? them.DiscordUsername : them.DisplayName;
                if (string.IsNullOrWhiteSpace(name)) continue;

                tally.TryGetValue(them.UserId, out var acc);
                var verdict = MatchOutcomeView.Classify(me.Result);
                tally[them.UserId] = (
                    name,
                    them.AvatarUrl,
                    acc.W + (verdict == MatchVerdict.Win ? 1 : 0),
                    acc.L + (verdict == MatchVerdict.Loss ? 1 : 0),
                    acc.N + 1);
            }
        }

        if (tally.Count == 0) return null;

        var best = tally
            .OrderByDescending(kv => kv.Value.N)
            .ThenBy(kv => kv.Value.Name, StringComparer.Ordinal)
            .First()
            .Value;

        return new FrequentOpponent(best.Name, best.Avatar, best.W, best.L, best.N);
    }

    /// <summary>
    /// How many more rated matches this player needs before they are on the ladder, or 0 when
    /// they already are.
    ///
    /// <para><b>The threshold is the SERVER's</b> (<c>min_decided</c>, which arrives with the
    /// community stats) and never a number written here. The two have disagreed before, and
    /// this sentence is exactly where a player would read the wrong one. A server that did not
    /// send it gives 0 — say nothing rather than guess a rule.</para>
    ///
    /// <para>The count is <c>games_played</c> from <c>elo_ratings</c>, which the server only
    /// advances on a match it actually scored — the same number its own entry predicate
    /// compares.</para>
    /// </summary>
    public static int MatchesToLadder(int minDecided, int gamesPlayed)
        => minDecided <= 0 ? 0 : Math.Max(0, minDecided - Math.Max(0, gamesPlayed));

    /// <summary>
    /// Whether this player's rating is still provisional — i.e. whether they have yet to reach
    /// the ladder's entry bar.
    ///
    /// <para><b>Deliberately NOT <see cref="MatchOutcomeView.IsProvisional"/> here</b>, which
    /// asks whether the Glicko deviation has settled. That question was measured against this
    /// community's real table and answers "yes, provisional" for practically everybody —
    /// the deviation does not fall under 110 until about the fourteenth rated match, and never
    /// at all for a player who keeps winning, because a rising rating re-inflates it as fast as
    /// the update shrinks it. A label that is true of everyone distinguishes nobody.</para>
    ///
    /// <para>"Not on the ladder yet" is a state a player can leave, can see the distance to,
    /// and that the profile can state in matches rather than in units of deviation.</para>
    /// </summary>
    public static bool IsProvisional(int minDecided, int gamesPlayed)
        => MatchesToLadder(minDecided, gamesPlayed) > 0;
}
