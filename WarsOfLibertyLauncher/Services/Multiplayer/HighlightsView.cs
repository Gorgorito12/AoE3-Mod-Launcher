using System;
using System.Collections.Generic;
using System.Globalization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>One of the month's highlights, in priority order: the order Ranking › Highlights draws its cards.</summary>
public enum HighlightCellKind
{
    TopClimb,
    MostWins,
    MostMatches,
    BestStreak,
    BestWinRate,
    BiggestUpset,
    TopCiv,
}

/// <summary>
/// The month's highlights (design 55l). The SERVER picks the players; this only decides which of
/// the two ladders a highlight names and whether a month has enough behind it to show any.
/// </summary>
public static class HighlightsView
{
    /// <summary>
    /// Fewest rated matches in a month before its highlights are drawn on the Rooms page. A
    /// DISPLAY threshold, not a rule: with three matches played, "most matches" is
    /// whoever played two of them, which says nothing worth a card. The server has no such floor
    /// and sends highlights for any month with a rated match.
    /// </summary>
    public const int MinMonthMatches = 10;

    /// <summary>Whether a month has enough behind it, and at least one cell to draw.</summary>
    public static bool HasCells(MonthHighlights? month)
        => month != null
           && month.TotalRated >= MinMonthMatches
           && Cells(month).Count > 0;

    /// <summary>
    /// The highlights a month has somebody for, IN PRIORITY ORDER. A highlight with nobody in it is
    /// left out (56a); a field an older server does not send reads as nobody. Since design 60 the
    /// Rooms page names only three of them (<see cref="ActivityFactsView"/>) and Ranking ›
    /// Highlights draws them all, from the top-five lists.
    /// </summary>
    public static IReadOnlyList<HighlightCellKind> Cells(MonthHighlights? month)
    {
        var cells = new List<HighlightCellKind>();
        if (month == null) return cells;
        if (TopClimb(month).Player?.Points is int) cells.Add(HighlightCellKind.TopClimb);
        if (month.MostWins?.Wins is > 0) cells.Add(HighlightCellKind.MostWins);
        if (month.MostMatches?.Matches is int) cells.Add(HighlightCellKind.MostMatches);
        if (BestStreak(month).Player?.Wins is int) cells.Add(HighlightCellKind.BestStreak);
        if (month.BestWinRate is { Percent: int, Matches: > 0 }) cells.Add(HighlightCellKind.BestWinRate);
        if (month.BiggestUpset is { Gap: > 0, Winners.Count: > 0 }) cells.Add(HighlightCellKind.BiggestUpset);
        if (month.TopCiv is { Picks: > 0 } civ && !string.IsNullOrWhiteSpace(civ.Civ)) cells.Add(HighlightCellKind.TopCiv);
        return cells;
    }

    /// <summary>The bigger climb of the two ladders, and which ladder it was ("default" / "team"). A tie goes to 1v1.</summary>
    public static (HighlightPlayer? Player, string Mode) TopClimb(MonthHighlights month)
        => Bigger(month.BiggestClimb, p => p.Points ?? 0);

    /// <summary>The longer streak of the two ladders, and which ladder it was. A tie goes to 1v1.</summary>
    public static (HighlightPlayer? Player, string Mode) BestStreak(MonthHighlights month)
        => Bigger(month.BestStreak, p => p.Wins ?? 0);

    private static (HighlightPlayer? Player, string Mode) Bigger(
        HighlightPerMode? perMode, Func<HighlightPlayer, int> value)
    {
        var solo = perMode?.Default;
        var team = perMode?.Team;
        if (team == null) return (solo, "default");
        if (solo == null) return (team, "team");
        return value(team) > value(solo) ? (team, "team") : (solo, "default");
    }

    /// <summary>
    /// The month's name — "octubre", "October" — for <c>yyyy-MM</c>, in
    /// <paramref name="culture"/>; null when the month cannot be read. Spanish month names are
    /// lowercase and English ones capitalised, and the culture already knows which.
    /// </summary>
    public static string? MonthName(string? month, CultureInfo culture)
        => DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? culture.DateTimeFormat.GetMonthName(d.Month)
            : null;
}
