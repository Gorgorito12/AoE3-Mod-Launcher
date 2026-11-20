using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The rules of the Highlights mosaic (design 119) that need no window: how many columns a width
/// gets, how long a bar is, and where a player stands on the ladder that is already loaded.
/// </summary>
public static class HighlightsMosaic
{
    /// <summary>From this width of the mosaic, four columns (the design's 1480 less the page's 40 of padding).</summary>
    public const double WideFrom = 1440;

    /// <summary>Under this width two cards side by side no longer fit their rows: one column.</summary>
    public const double SingleBelow = 700;

    /// <summary>The shortest bar drawn: a place far behind the first still shows one.</summary>
    public const double BarFloor = .04;

    /// <summary>Four columns on a wide page, two on a laptop, one when even two do not fit.</summary>
    public static int Columns(double width)
        => width >= WideFrom ? 4 : width >= SingleBelow ? 2 : 1;

    /// <summary>
    /// How much of its track a place's bar fills: its figure against the first place's, between
    /// <see cref="BarFloor"/> and the whole track. No new data: both figures are already on screen.
    /// </summary>
    public static double BarShare(double value, double top)
    {
        if (!(top > 0) || !(value > 0)) return BarFloor;
        return Math.Clamp(value / top, BarFloor, 1);
    }

    /// <summary>
    /// The player's place on a ladder the page already has, or null when he is not on it: being
    /// placed, keeping his rating private, or past the page the server sent. Null draws no badge;
    /// it is never read as last place.
    /// </summary>
    public static int? RankOf(string? userId, IEnumerable<LeaderboardRow>? ladder)
    {
        if (string.IsNullOrEmpty(userId) || ladder == null) return null;
        foreach (var row in ladder)
            if (row.Rank > 0 && string.Equals(row.UserId, userId, StringComparison.Ordinal)) return row.Rank;
        return null;
    }
}
