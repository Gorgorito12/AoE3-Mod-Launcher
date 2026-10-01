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
    /// <summary>The open panel, 248 px whatever the room count (design handoff turn 40). The list
    /// takes the rest of the column and scrolls inside it when it has to.</summary>
    Fixed,
}

/// <summary>
/// The Rooms page's layout decision (design handoff turns 38-40). Pure, so the rules — which
/// are the whole point of the change — are tested rather than read off a screenshot.
///
/// <para><b>The principle.</b> A small window shows the same blocks as a big one; only how
/// much fits in each changes. Turn 36 decided on the window alone and folded the panel away on
/// every laptop.</para>
///
/// <para><b>The open panel is ALWAYS 248 px (turn 40).</b> Turn 38a let it grow into whatever
/// a short list left over, which with no rooms at all meant a panel the whole height of the
/// column, packed with a dozen matches and fifteen ranks. The rooms have priority: when there
/// are few of them, the spare height stays empty in the LIST, where a new room will land, and
/// the panel keeps its fixed size and its fixed four matches and five ranks.</para>
///
/// <para><b>Folding is the player's choice.</b> Until they make one, the panel is open whenever
/// it can have its 248 px and the list still keeps room for <c>roomsMinHeight</c> of rooms — a
/// header and two rows — and folded otherwise.</para>
/// </summary>
public static class RoomsActivityLayout
{
    /// <summary>The open panel's height at the reference text size, at every room count.</summary>
    public const double ExpandedHeight = 248;

    /// <summary>The folded strip's height.</summary>
    public const double FoldedHeight = 44;

    /// <summary>The space between the list and the panel.</summary>
    public const double Gap = 14;

    /// <param name="columnHeight">The left column's height.</param>
    /// <param name="roomsMinHeight">The least the rooms block should keep when the panel opens by
    /// default.</param>
    /// <param name="choice">The player's saved choice: true open, false folded, null not chosen.</param>
    /// <param name="hasActivity">Whether there is anything to show at all.</param>
    /// <param name="expandedHeight">The open panel's real height (<see cref="ExpandedHeightFor"/>);
    /// <see cref="ExpandedHeight"/> when not known yet.</param>
    public static RoomsActivityMode Decide(double columnHeight, double roomsMinHeight, bool? choice,
        bool hasActivity, double expandedHeight = ExpandedHeight)
    {
        if (!hasActivity) return RoomsActivityMode.None;
        if (!(columnHeight > 0)) return choice == false ? RoomsActivityMode.Folded : RoomsActivityMode.Fixed;
        return IsExpanded(columnHeight, roomsMinHeight, choice, expandedHeight)
            ? RoomsActivityMode.Fixed
            : RoomsActivityMode.Folded;
    }

    /// <summary>Whether the panel is open: the player's choice, else whether it fits.</summary>
    public static bool IsExpanded(double columnHeight, double roomsMinHeight, bool? choice,
        double expandedHeight = ExpandedHeight)
        => choice ?? (columnHeight - Gap - Math.Max(ExpandedHeight, expandedHeight) >= roomsMinHeight);

    /// <summary>
    /// The open panel's height: <see cref="ExpandedHeight"/>, or more when a list's capped rows
    /// need more.
    ///
    /// <para>248 px is the handoff's height AT ITS TEXT SIZE — there four matches fit exactly.
    /// The launcher's text-size setting makes every row taller and nothing else, so at 110 % the
    /// fourth match no longer fitted and the card showed three over an empty band (reported with
    /// a screenshot). The rule is the one the settings rails already follow: the reference number
    /// is the size at the reference text size, and above it the panel grows by what its content
    /// needs. Never below it.</para>
    ///
    /// <para>Each list is its CHROME — everything in the panel that is not the list, i.e. the
    /// panel's height minus the list's — plus the list's natural height. Chrome does not change
    /// when the list is given more room, which is what makes the answer stable rather than a
    /// loop. A list with no usable chrome (not laid out yet, or collapsed) is ignored.</para>
    /// </summary>
    public static double ExpandedHeightFor(params (double Chrome, double Natural)[] lists)
    {
        var height = ExpandedHeight;
        foreach (var (chrome, natural) in lists)
        {
            if (!(chrome > 0) || !(natural > 0) || double.IsInfinity(chrome) || double.IsInfinity(natural))
                continue;
            height = Math.Max(height, Math.Ceiling(chrome + natural));
        }
        return height;
    }
}

/// <summary>
/// The community panel's sizing rules (design handoff turns 38-40), pure so they are tested.
/// </summary>
public static class ActivityFit
{
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
