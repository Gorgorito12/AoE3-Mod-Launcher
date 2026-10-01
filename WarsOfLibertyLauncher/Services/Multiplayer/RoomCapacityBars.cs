using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The occupancy bars under a room's "4/6": ONE BAR PER SEAT (design handoff turn 36).
///
/// <para>They used to be four segments whatever the room's size, each standing for a quarter
/// of it — so a 1v1 with one player and a 4v4 with four drew the same two lit segments, and the
/// picture said less than the number above it. One bar per seat makes the bars and the number
/// the same fact.</para>
///
/// <para>Bars are 10 px wide up to four seats and 7 px above that, the handoff's own values, so
/// eight seats still fit the PLAYERS column. A room larger than <see cref="MaxBars"/> (a casual
/// room may be) is drawn as eight proportional segments rather than a row of slivers.</para>
///
/// <para>Pure, pinned by <c>RoomCapacityBarsTests</c>.</para>
/// </summary>
public static class RoomCapacityBars
{
    /// <summary>The most bars one cell draws.</summary>
    public const int MaxBars = 8;

    /// <summary>Bar width when the room has at most four seats.</summary>
    public const double WideBar = 10;

    /// <summary>Bar width above four seats.</summary>
    public const double NarrowBar = 7;

    /// <summary>The bars to draw for <paramref name="current"/> of <paramref name="max"/> seats.</summary>
    /// <returns>How many bars, how many of them are lit, and how wide each one is.</returns>
    public static (int Bars, int Filled, double BarWidth) Layout(int current, int max)
    {
        if (max <= 0) return (0, 0, WideBar);

        var bars = Math.Min(max, MaxBars);
        var width = max <= 4 ? WideBar : NarrowBar;
        var taken = Math.Max(0, current);

        int filled;
        if (max <= MaxBars)
        {
            filled = Math.Min(taken, bars);
        }
        else
        {
            filled = (int)Math.Ceiling(Math.Min(taken, max) / (double)max * bars);
            if (taken > 0) filled = Math.Max(1, filled);
        }

        return (bars, filled, width);
    }
}
