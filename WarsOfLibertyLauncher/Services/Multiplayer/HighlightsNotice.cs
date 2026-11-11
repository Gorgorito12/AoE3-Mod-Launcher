using System;
using System.Collections.Generic;
using System.Globalization;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the bell says about last month's highlights, when it says anything.</summary>
/// <param name="Month">The month the decision is about ("2026-10"); null when there is none.</param>
/// <param name="MarkSeen">Record the month so it is never looked at again (also for an empty month).</param>
internal sealed record HighlightsNoticeDecision(string? Month, bool Notify, bool MarkSeen, string? Title, string? Body);

/// <summary>
/// The month's highlights in the bell (2026-11-10): the Discord gets them on the first of the month,
/// and the bell carries them to everybody else. Pure, so every refusal is a test.
///
/// <para>Said ONCE per month and PC (<see cref="Models.LauncherConfig.NotifiedHighlightsMonth"/>), only
/// in the first <see cref="DaysToSay"/> days of the month that follows (a launcher first run in late
/// November never announces October), never for a month with no rated match (marked as seen
/// silently), and never for an older month than the one just ended.</para>
/// </summary>
internal static class HighlightsNotice
{
    /// <summary>The bell says it during the first week of the month.</summary>
    internal const int DaysToSay = 7;

    internal static HighlightsNoticeDecision Decide(MonthlyHighlights? highlights, DateTime nowLocal, string? notifiedMonth)
    {
        var justEnded = nowLocal.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var previous = highlights?.Previous;
        if (previous == null || !string.Equals(previous.Month, justEnded, StringComparison.Ordinal))
            return new(null, false, false, null, null);
        if (string.Equals(notifiedMonth, previous.Month, StringComparison.Ordinal))
            return new(previous.Month, false, false, null, null);
        if (nowLocal.Day > DaysToSay)
            return new(previous.Month, false, false, null, null);
        if (previous.TotalRated <= 0)
            return new(previous.Month, false, true, null, null);

        var monthName = MonthName(previous.Month);
        var title = Strings.Format("NotifHighlightsTitle", monthName);
        var body = Body(previous);
        return new(previous.Month, true, true, title, body);
    }

    /// <summary>
    /// Up to three facts, in the order of the Discord card: who climbed most, who played most, the
    /// best streak. A category with nobody is left out; with none at all, the month's match count.
    /// </summary>
    internal static string Body(MonthHighlights month)
    {
        var parts = new List<string>();
        var climb = month.BiggestClimb?.Default ?? month.BiggestClimb?.Team;
        if (climb is { Points: > 0 } c)
            parts.Add(Strings.Format("NotifHighlightsClimb", c.DisplayName, c.Points));
        if (month.MostMatches is { Matches: > 0 } m)
            parts.Add(Strings.Format("NotifHighlightsMatches", m.DisplayName, m.Matches));
        var streak = month.BestStreak?.Default ?? month.BestStreak?.Team;
        if (streak is { Wins: > 1 } s)
            parts.Add(Strings.Format("NotifHighlightsStreak", s.DisplayName, s.Wins));
        return parts.Count > 0
            ? string.Join(" · ", parts)
            : Strings.Format("NotifHighlightsCount", month.TotalRated);
    }

    /// <summary>"octubre" / "October", in the launcher's language.</summary>
    internal static string MonthName(string month)
        => DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMMM", Strings.Culture)
            : month;
}
