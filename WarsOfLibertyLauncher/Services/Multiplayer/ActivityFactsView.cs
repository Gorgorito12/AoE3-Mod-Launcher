using System;
using System.Collections.Generic;
using System.Globalization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a fact of the community block's data strip is.</summary>
public enum ActivityFactKind
{
    TopClimb,
    MostMatches,
    BestStreak,
    Matches,
    Players,
    MostPlayed,
}

/// <summary>
/// One fact of the data strip: its label (already in the strip's words, before capitals), a name
/// when it names someone or something, and its figure. <see cref="Streak"/> is set instead of a
/// plain figure for the best streak, which is drawn as the 🔥 pill.
/// </summary>
public sealed record ActivityFact(ActivityFactKind Kind, string Label, string? Name, string? Figure, int? Streak);

/// <summary>
/// The community block's data strip (design 60): the month's highlights and the community's
/// figures as ONE row of facts, each a small label over its value. They used to be two strips
/// with similar data in two styles — the highlights under the list and a line of figures beside
/// the panel's title.
///
/// <para><b>The order is 60's</b>: most matches and best streak of the month, then matches in
/// the window, players in theirs and the most played map — with the biggest climb FIRST when the
/// month has one (the maintainer's call: 60's sample simply had none). The four highlights added
/// later (most wins, best win rate, biggest upset, civilization of the month) are NOT here: they
/// live in Ranking › Highlights, in depth.</para>
///
/// <para><b>A fact with nothing to say is left out</b>, never shown as a dash or a zero-name. The
/// month's facts need the month to have enough behind it (<see cref="HighlightsView.HasCells"/>);
/// each window is the payload's own (<c>window_days</c>, <c>players_window_days</c>), never a
/// constant, or the label starts lying the day the server's window moves.</para>
/// </summary>
public static class ActivityFactsView
{
    /// <summary>The strip's facts for the highlights of <paramref name="month"/> and the payload's totals.</summary>
    /// <param name="month">The month whose highlights are shown (this one, or the last after "See September").</param>
    /// <param name="totals">The community's figures; null from a server that sends none.</param>
    /// <param name="culture">The launcher's language, for the month's abbreviation and the numbers.</param>
    /// <param name="label">How a label key and its argument become text (Strings.Format, injected so this stays pure).</param>
    public static IReadOnlyList<ActivityFact> Build(
        MonthHighlights? month,
        CommunityTotals? totals,
        CultureInfo culture,
        Func<string, object[], string> label)
    {
        var facts = new List<ActivityFact>();
        string N(int n) => n.ToString("N0", culture);

        if (month != null && HighlightsView.HasCells(month))
        {
            var mon = MonthAbbreviation(month.Month, culture) ?? month.Month;
            var (climber, _) = HighlightsView.TopClimb(month);
            if (climber?.Points is int points && points > 0)
                facts.Add(new(ActivityFactKind.TopClimb, label("MpFactTopGain", new object[] { mon }),
                    NameOf(climber), "+" + N(points), null));
            if (month.MostMatches?.Matches is int matches)
                facts.Add(new(ActivityFactKind.MostMatches, label("MpFactMostMatches", new object[] { mon }),
                    NameOf(month.MostMatches), N(matches), null));
            var (streaker, _) = HighlightsView.BestStreak(month);
            if (streaker?.Wins is int wins && wins > 0)
                facts.Add(new(ActivityFactKind.BestStreak, label("MpFactBestStreak", new object[] { mon }),
                    NameOf(streaker), null, wins));
        }

        if (totals != null)
        {
            if (totals.WindowDays > 0)
                facts.Add(new(ActivityFactKind.Matches, label("MpFactMatches", new object[] { totals.WindowDays }),
                    null, N(totals.Matches), null));
            if (totals.PlayersWindowDays > 0)
                facts.Add(new(ActivityFactKind.Players, label("MpFactPlayers", new object[] { totals.PlayersWindowDays }),
                    null, N(totals.Players), null));
            if (!string.IsNullOrWhiteSpace(totals.TopMap))
                facts.Add(new(ActivityFactKind.MostPlayed, label("MpFactMostPlayed", Array.Empty<object>()),
                    totals.TopMap!.Replace('_', ' '), null, null));
        }
        return facts;
    }

    /// <summary>
    /// "oct" / "Oct" for <c>yyyy-MM</c>, in <paramref name="culture"/>, without the full stop some
    /// cultures put on an abbreviation ("oct.") — in a label in capitals it reads as the end of a
    /// sentence. Null when the month cannot be read.
    /// </summary>
    public static string? MonthAbbreviation(string? month, CultureInfo culture)
        => DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? culture.DateTimeFormat.GetAbbreviatedMonthName(d.Month).TrimEnd('.')
            : null;

    private static string NameOf(HighlightPlayer p)
        => string.IsNullOrWhiteSpace(p.DisplayName) ? "?" : p.DisplayName;
}
