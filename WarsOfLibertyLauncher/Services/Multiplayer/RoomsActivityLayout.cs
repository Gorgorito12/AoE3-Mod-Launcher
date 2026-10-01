using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>How the Rooms page splits its left column between the room list and the community
/// panel.</summary>
public enum RoomsActivityMode
{
    /// <summary>No community data: the list takes the whole column.</summary>
    None,
    /// <summary>The 44-px strip under the list (design handoff turn 39a). List: <c>*</c>.</summary>
    Folded,
    /// <summary>Few rooms (38a): the list measures its rows, the panel fills the rest.</summary>
    Fill,
    /// <summary>Many rooms (38b/39b): the panel is 248 px, the list scrolls inside the rest.</summary>
    Fixed,
}

/// <summary>
/// The Rooms page's layout decision (design handoff turns 38-39). Pure, so the rules — which
/// are the whole point of the change — are tested rather than read off a screenshot.
///
/// <para><b>The principle.</b> A small window shows the same blocks as a big one; only how
/// much fits in each changes. So the decision depends on the CONTENT as well as the window:
/// with one room the list is one row tall and the panel takes everything below it, with eight
/// the panel stops at 248 px and the list scrolls. Turn 36 decided on the window alone and
/// folded the panel away on every laptop.</para>
///
/// <para><b>Folding is the player's choice.</b> Until they make one, the panel is open whenever
/// it can have its 248 px and the list still keeps room for <paramref name="roomsMinHeight"/>
/// of rooms — a header and two rows — and folded otherwise.</para>
/// </summary>
public static class RoomsActivityLayout
{
    /// <summary>The open panel's height when the rooms need the rest of the column.</summary>
    public const double ExpandedHeight = 248;

    /// <summary>The folded strip's height.</summary>
    public const double FoldedHeight = 44;

    /// <summary>The space between the list and the panel.</summary>
    public const double Gap = 14;

    /// <param name="columnHeight">The left column's height.</param>
    /// <param name="roomsNaturalHeight">The rooms block at its natural height: section header,
    /// column header and every row, unscrolled.</param>
    /// <param name="roomsMinHeight">The least the rooms block should keep when the panel opens by
    /// default.</param>
    /// <param name="choice">The player's saved choice: true open, false folded, null not chosen.</param>
    /// <param name="hasActivity">Whether there is anything to show at all.</param>
    public static RoomsActivityMode Decide(double columnHeight, double roomsNaturalHeight,
        double roomsMinHeight, bool? choice, bool hasActivity)
    {
        if (!hasActivity) return RoomsActivityMode.None;
        if (!(columnHeight > 0)) return choice == false ? RoomsActivityMode.Folded : RoomsActivityMode.Fixed;

        if (!IsExpanded(columnHeight, roomsMinHeight, choice)) return RoomsActivityMode.Folded;

        // The handoff's rule: if the list leaves the panel less than 248 px, it is "many rooms".
        return columnHeight - roomsNaturalHeight - Gap >= ExpandedHeight
            ? RoomsActivityMode.Fill
            : RoomsActivityMode.Fixed;
    }

    /// <summary>Whether the panel is open: the player's choice, else whether it fits.</summary>
    public static bool IsExpanded(double columnHeight, double roomsMinHeight, bool? choice)
        => choice ?? (columnHeight - Gap - ExpandedHeight >= roomsMinHeight);
}

/// <summary>
/// The community panel's sizing rules (design handoff turns 38-39), pure so they are tested.
/// </summary>
public static class ActivityFit
{
    /// <summary>
    /// The peak-hours bars' height for a card of <paramref name="cardHeight"/>: 34 px in the
    /// 248-px panel, taller as the panel grows (60 in the handoff's few-rooms frame).
    /// </summary>
    public static double PeakBarsHeight(double cardHeight)
        => double.IsNaN(cardHeight) || cardHeight <= 0 ? 34 : Math.Max(34, Math.Round(cardHeight * 0.11));

    /// <summary>
    /// Which of the folded strip's segments stay visible in <paramref name="available"/> px.
    /// Segments are never trimmed — the handoff forbids "E…" — so when they do not fit, whole
    /// segments leave in <paramref name="dropOrder"/> (indices into <paramref name="widths"/>)
    /// until they do. A segment not in the drop order is never removed; if even those do not
    /// fit, the strip clips at its edge rather than lose them.
    /// </summary>
    public static bool[] VisibleSegments(double available, IReadOnlyList<double> widths, IReadOnlyList<int> dropOrder)
    {
        var shown = new bool[widths.Count];
        for (var i = 0; i < shown.Length; i++) shown[i] = widths[i] > 0;
        if (double.IsNaN(available) || double.IsInfinity(available)) return shown;

        double Total()
        {
            double t = 0;
            for (var i = 0; i < shown.Length; i++) if (shown[i]) t += widths[i];
            return t;
        }

        foreach (var drop in dropOrder)
        {
            if (Total() <= available) break;
            if (drop >= 0 && drop < shown.Length) shown[drop] = false;
        }
        return shown;
    }
}
