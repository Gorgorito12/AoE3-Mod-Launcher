using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which matches the History tab is showing.</summary>
public enum HistoryFilter
{
    /// <summary>Everything that was played.</summary>
    All,

    /// <summary>Only the ones that moved a rating.</summary>
    Rated,

    /// <summary>Only the ones that did not — so a player can see WHY.</summary>
    Unrated,
}

/// <summary>What the History section should be drawing right now.</summary>
public enum HistorySection
{
    /// <summary>Nothing in hand yet and the fetch is on its way.</summary>
    Loading,

    /// <summary>Nothing in hand and the fetch failed. The message is worth more than a spinner.</summary>
    Error,

    /// <summary>A page in hand — draw it, filters and all.</summary>
    Rows,
}

/// <summary>One day of matches, newest day first.</summary>
public sealed record HistoryDay(DateTime LocalDate, IReadOnlyList<MatchHistoryRow> Matches);

/// <summary>
/// The four cells above the History list.
/// </summary>
/// <param name="Rating">Current rating, or null when the standing is not known.</param>
/// <param name="Delta">What the most recent RATED match did, or null when nothing did.</param>
/// <param name="TopMap">The map played most, or null when nothing has a name.</param>
/// <param name="TopMapCount">How many matches that map accounts for.</param>
public sealed record HistorySummary(
    int? Rating,
    int? Delta,
    int Wins,
    int Losses,
    int Unrated,
    string? TopMap,
    int TopMapCount);

/// <summary>
/// Everything the History tab works out for itself, kept pure so the rules can be pinned by
/// <c>MatchHistoryViewTests</c> rather than argued about over a screenshot.
///
/// <para><b>Nothing here asks the server anything.</b> "Most played map" and the tallies are
/// computed over the page of history the tab already downloaded — the handoff is explicit that
/// no new endpoint is invented for them, and inventing one would also mean the number could
/// disagree with the list printed underneath it.</para>
///
/// <para><b>A 0.5 is never a draw.</b> It is the backend's "the outcome could not be read", and
/// it is the majority of stored rows — so it is counted as neither a win nor a loss anywhere in
/// this file, and the tab shows it as a match that did not count, with the reason.</para>
/// </summary>
public static class MatchHistoryView
{
    /// <summary>
    /// Whether this match moved anybody's rating.
    ///
    /// <para><b>The server's answer is preferred to our own.</b> <c>rated</c> is a stored
    /// column and it knows things the launcher does not — whether the room was competitive,
    /// whether the mod has a ladder, whether the recording had already been reported. Only when
    /// it is absent (an older backend, or a row written before the column existed) does this
    /// fall back to reading the score, which is right for the common case and cannot see any of
    /// the others.</para>
    /// </summary>
    public static bool IsRated(MatchHistoryRow row)
        => row.Rated ?? MatchOutcomeView.Classify(row.Result) != MatchVerdict.NoResult;

    public static IReadOnlyList<MatchHistoryRow> Filter(
        IReadOnlyList<MatchHistoryRow>? rows, HistoryFilter filter)
    {
        if (rows == null) return Array.Empty<MatchHistoryRow>();
        return filter switch
        {
            HistoryFilter.Rated => rows.Where(IsRated).ToList(),
            HistoryFilter.Unrated => rows.Where(r => !IsRated(r)).ToList(),
            _ => rows.ToList(),
        };
    }

    /// <summary>
    /// The matches grouped under one heading per day, newest first, and newest first inside
    /// each day.
    ///
    /// <para>The point of grouping is that the date stops being repeated on every card — it
    /// used to be printed inside each one, so a page of six matches from one evening said the
    /// same date six times and the times were the only thing that differed.</para>
    ///
    /// <para><b>The day is the viewer's LOCAL day.</b> Timestamps arrive in UTC, and a match
    /// played at 21:00 in Lima is stored on the following UTC date — grouping by the raw date
    /// would put half of one evening under tomorrow's heading.</para>
    /// </summary>
    public static IReadOnlyList<HistoryDay> GroupByDay(IReadOnlyList<MatchHistoryRow>? rows)
    {
        if (rows == null || rows.Count == 0) return Array.Empty<HistoryDay>();

        var dated = new List<(DateTime When, MatchHistoryRow Row)>();
        foreach (var row in rows)
        {
            // A row whose timestamp cannot be read still belongs in the list — losing a match
            // from somebody's history because of a malformed date would be worse than filing
            // it under today. It sorts by DateTime.MinValue, i.e. last, where its own heading
            // makes the oddity visible rather than hiding it among real days.
            dated.Add((ParseLocal(row.StartedAt) ?? DateTime.MinValue, row));
        }

        return dated
            .GroupBy(x => x.When.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new HistoryDay(
                g.Key,
                g.OrderByDescending(x => x.When).Select(x => x.Row).ToList()))
            .ToList();
    }

    /// <summary>
    /// An ISO-8601 timestamp as the viewer's local time, or null when it cannot be read.
    ///
    /// <para>SQLite's <c>datetime('now')</c> writes <c>YYYY-MM-DD HH:MM:SS</c> with no zone
    /// marker, and <c>DateTime.Parse</c> would take that for local time — three to twelve
    /// hours out, depending on where the player lives. <c>AssumeUniversal</c> is what says
    /// otherwise, and it has to be paired with <c>AdjustToUniversal</c> or a value that DOES
    /// carry a Z comes back with its offset applied twice.</para>
    /// </summary>
    public static DateTime? ParseLocal(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        return DateTime.TryParse(
            iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var utc)
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
            : null;
    }

    /// <summary>
    /// The map played most often, and how many matches that is.
    ///
    /// <para>Ties break on the name so the card does not report a different favourite every
    /// time the page is redrawn — the same reasoning the server's own top-map query gives for
    /// its tiebreak.</para>
    /// </summary>
    public static (string? Map, int Count) TopMap(IReadOnlyList<MatchHistoryRow>? rows)
    {
        if (rows == null || rows.Count == 0) return (null, 0);

        var best = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.MapName))
            .GroupBy(r => r.MapName!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return best == null ? (null, 0) : (best.Key, best.Count());
    }

    /// <summary>
    /// The summary cells. <paramref name="standing"/> may be null — the rating cell is then
    /// blank rather than showing the 1500 the server hands everybody, which is the same
    /// refusal the Profile tab makes.
    /// </summary>
    /// <remarks>
    /// The delta is the most recent match that actually MOVED the rating, not the most recent
    /// match: with most rows unrated, taking the newest row's delta would show an em dash to a
    /// player who had just won.
    /// </remarks>
    public static HistorySummary Summarise(
        IReadOnlyList<MatchHistoryRow>? rows, EloSnapshot? standing)
    {
        var wins = 0;
        var losses = 0;
        var unrated = 0;
        int? delta = null;

        foreach (var row in rows ?? Array.Empty<MatchHistoryRow>())
        {
            switch (MatchOutcomeView.Classify(row.Result))
            {
                case MatchVerdict.Win: wins++; break;
                case MatchVerdict.Loss: losses++; break;
            }

            if (!IsRated(row)) unrated++;
            else delta ??= MatchOutcomeView.Delta(row.RatingBefore, row.RatingAfter);
        }

        var (map, mapCount) = TopMap(rows);

        return new HistorySummary(
            standing == null ? null : (int)Math.Round(standing.Rating),
            delta,
            wins,
            losses,
            unrated,
            map,
            mapCount);
    }

    /// <summary>
    /// The other player in a one-on-one, or null when there was not exactly one of them.
    ///
    /// <para>Used for the "against {name}" on a card's first line. Null past two players
    /// because a card headed "against Aluclown" for a four-player game would be naming one
    /// person out of three, and the roster underneath already lists everybody.</para>
    /// </summary>
    public static string? SoleOpponent(MatchHistoryRow row, string? meId)
    {
        if (row.Participants == null || row.Participants.Count != 2) return null;

        var others = row.Participants
            .Where(p => !string.Equals(p.UserId, meId, StringComparison.Ordinal))
            .ToList();
        if (others.Count != 1) return null;

        var them = others[0];
        return string.IsNullOrEmpty(them.DisplayName) ? them.DiscordUsername : them.DisplayName;
    }

    /// <summary>
    /// Which of the three states the section is in — pure, because the ORDER of these tests is
    /// the whole thing and it was wrong.
    ///
    /// <para><b>An error beats "loading", and that is the fix.</b> The UI used to ask "no rows and
    /// still refreshing?" FIRST, and the failure path repainted from inside its <c>catch</c>, i.e.
    /// before the <c>finally</c> cleared the refreshing flag — so at paint time both were still
    /// true, the spinner branch matched, and the error line underneath it was unreachable. The
    /// message was stored and never drawn: "Loading…" for ever, with the explanation in hand.</para>
    ///
    /// <para>It masked ANY first-fetch failure — a 429, a 500, no network — not just the
    /// deserialisation bug that exposed it. Deciding it here means the ordering can be argued with
    /// in a test instead of depending on when somebody happens to repaint.</para>
    ///
    /// <para>Rows in hand always win: a refresh that fails keeps the page it already had, because
    /// real matches are worth more than a line about a hiccup.</para>
    /// </summary>
    public static HistorySection SectionFor(
        IReadOnlyList<MatchHistoryRow>? rows, string? error, bool refreshing)
    {
        if (rows != null) return HistorySection.Rows;
        if (!string.IsNullOrWhiteSpace(error)) return HistorySection.Error;

        // Nothing yet and nothing wrong. Also the state a beat before the fetch is kicked, which
        // is why it does not depend on the flag being set already.
        _ = refreshing;
        return HistorySection.Loading;
    }
}
