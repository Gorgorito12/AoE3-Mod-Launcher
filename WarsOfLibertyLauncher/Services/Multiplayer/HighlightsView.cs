using System;
using System.Globalization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the monthly highlights card does with a month (design 55l).</summary>
public enum HighlightsCardState
{
    /// <summary>Nothing is drawn: no data, or an empty month past its first days.</summary>
    Hidden,

    /// <summary>The three cells.</summary>
    Cells,

    /// <summary>"The month has just started…" and, when there is one, the link to last month.</summary>
    JustStarted,
}

/// <summary>
/// The monthly highlights as the Rooms page shows them (design 55l). The SERVER picks the players
/// — biggest climb per ladder, most rated matches, best streak inside the month — and this only
/// decides which of the two ladders a cell names and whether the card is drawn at all.
/// </summary>
public static class HighlightsView
{
    /// <summary>
    /// Fewest rated matches in a month before its highlights are drawn — the number 55l's empty
    /// state names. A DISPLAY threshold, not a rule: with three matches played, "most matches" is
    /// whoever played two of them, which says nothing worth a card. The server has no such floor
    /// and sends highlights for any month with a rated match.
    /// </summary>
    public const int MinMonthMatches = 10;

    /// <summary>
    /// How long the empty state may say the month "has just started". Past it the sentence would
    /// be untrue, and an empty month leaves the card out instead.
    /// </summary>
    public const int JustStartedDays = 7;

    /// <summary>Whether a month has enough behind it, and at least one player to name.</summary>
    public static bool HasCells(MonthHighlights? month)
        => month != null
           && month.TotalRated >= MinMonthMatches
           && (month.MostMatches != null || TopClimb(month).Player != null || BestStreak(month).Player != null);

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
    /// What the card does for the month on screen: its cells when it has them; for the CURRENT
    /// month, the "just started" line during its first <see cref="JustStartedDays"/> days;
    /// otherwise nothing.
    /// </summary>
    public static HighlightsCardState StateOf(MonthHighlights? month, bool isCurrent, DateTime nowUtc)
    {
        if (month == null) return HighlightsCardState.Hidden;
        if (HasCells(month)) return HighlightsCardState.Cells;
        if (isCurrent && DaysInto(month, nowUtc) < JustStartedDays) return HighlightsCardState.JustStarted;
        return HighlightsCardState.Hidden;
    }

    /// <summary>Days since the month began; 0 when its start cannot be read.</summary>
    public static double DaysInto(MonthHighlights month, DateTime nowUtc)
        => DateTime.TryParse(month.StartsAt, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start)
            ? (nowUtc - start).TotalDays
            : 0;

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
