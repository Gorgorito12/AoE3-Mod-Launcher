namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The Multiplayer tab's spacing, in ONE place (the maintainer's rule): one outer margin M on
/// the four sides of the content area — under the sub-bar, at the sides and at the bottom — and
/// one gap G between panels, across (Rooms↔Chat) and down (Rooms↔Community activity), and between
/// the community cards. M = G: 12 below <see cref="WideFrom"/> px of width, 16 from there.
///
/// <para>It replaced a sum of margins: the content grid's <c>24,2,24,16</c>, the sub-bar's 10,
/// a 16-px gutter beside a 10-px one, and every panel's own inset on top — which measured as
/// 27 / 24 / 8 / 20 around the panels, gaps of 17 and 13, and "Rooms" starting 15 px before the
/// panels it sits over. The tab writes these values into its resources
/// (<c>MpPageGutter</c>, <c>MpPanelGap</c>…) and everything reads them from there; nothing
/// carries a margin of its own that adds to them.</para>
/// </summary>
public static class PageSpacing
{
    /// <summary>The margin and the gap below <see cref="WideFrom"/> px.</summary>
    public const double Narrow = 12;

    /// <summary>The margin and the gap from <see cref="WideFrom"/> px.</summary>
    public const double Wide = 16;

    /// <summary>The width from which the page breathes more (the chat column's own threshold).</summary>
    public const double WideFrom = 1600;

    /// <summary>The padding inside each panel (Rooms, Community activity, Chat), on all four sides.</summary>
    public const double PanelPadding = 16;

    /// <summary>
    /// M and G for a page <paramref name="pageWidth"/> wide. A width not laid out yet (0, NaN,
    /// infinite) is the narrow value, the one the resources start with.
    /// </summary>
    public static double For(double pageWidth)
        => pageWidth >= WideFrom && !double.IsInfinity(pageWidth) ? Wide : Narrow;
}
