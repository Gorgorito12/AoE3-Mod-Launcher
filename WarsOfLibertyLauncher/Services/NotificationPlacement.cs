using System;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Where the bell's panel goes (design 120). It hangs from the bell: its right edge is the bell's,
/// six under the title bar, and it is as tall as its notices up to twelve from the bottom of the
/// window. It used to be pinned twelve from the window's right edge, under the caption buttons,
/// and stretched to the bottom whatever it held.
///
/// <para>The bell's position is measured, never assumed: the update pill and the caption buttons
/// beside it change width. Pure, so <c>NotificationPlacementTests</c> pins it.</para>
/// </summary>
public static class NotificationPlacement
{
    public const double PanelWidth = 420;

    /// <summary>The least room kept between the panel and the window's left and bottom edges.</summary>
    public const double Gutter = 12;

    /// <summary>From the title bar down to the panel.</summary>
    public const double Drop = 6;

    /// <summary>The tip is the sheet's 12-px square turned 45°: this wide, half as tall.</summary>
    public const double CaretWidth = 17;
    public const double CaretHeight = 8.5;

    /// <summary>
    /// The panel's right margin inside its host: the bell's right edge, unless that would leave the
    /// panel's left edge under <see cref="Gutter"/> from the window — then just enough less.
    /// </summary>
    public static double RightMargin(double hostWidth, double bellRight, double panelWidth = PanelWidth)
    {
        if (double.IsNaN(hostWidth) || double.IsNaN(bellRight) || hostWidth <= 0) return Gutter;
        var wanted = Math.Max(0, hostWidth - bellRight);
        var most = Math.Max(0, hostWidth - panelWidth - Gutter);
        return Math.Min(wanted, most);
    }

    /// <summary>The panel may grow to <see cref="Gutter"/> from the bottom of the host.</summary>
    public static double MaxHeight(double hostHeight) =>
        double.IsNaN(hostHeight) ? 0 : Math.Max(0, hostHeight - Drop - Gutter);

    /// <summary>The tip's right margin inside the host: centred under the bell, wherever the panel
    /// ended up.</summary>
    public static double CaretRightMargin(double hostWidth, double bellLeft, double bellRight)
    {
        if (double.IsNaN(hostWidth) || double.IsNaN(bellLeft) || double.IsNaN(bellRight)) return 0;
        var centre = (bellLeft + bellRight) / 2;
        return Math.Max(0, hostWidth - centre - CaretWidth / 2);
    }

    /// <summary>
    /// Whether the tip still touches the panel: a panel pushed away from the bell by a narrow window
    /// may no longer be under it, and a tip pointing from nowhere is worse than none.
    /// </summary>
    public static bool CaretFits(double panelRightMargin, double caretRightMargin, double panelWidth = PanelWidth, double corner = 12)
    {
        double left = caretRightMargin + CaretWidth, right = caretRightMargin;
        return right >= panelRightMargin + corner && left <= panelRightMargin + panelWidth - corner;
    }
}
