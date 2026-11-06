namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// How the Statistics page divides a given width (design 98).
///
/// <para>The prototype says this in CSS: a gutter that changes at 1600, a right column of
/// <c>clamp(300px, 24cqw, 380px)</c>, and two tables under
/// <c>repeat(auto-fit, minmax(560px, 1fr))</c>. WPF has none of the three, so the arithmetic lives
/// here where it can be pinned, and the page only re-renders when one of the answers changes.</para>
/// </summary>
public readonly record struct StatsPageLayout(
    double Gutter,
    double RightColumn,
    double RowHeight,
    bool SideBySide,
    bool Wide)
{
    public const double RightColumnMin = 300;
    public const double RightColumnMax = 380;
    public const double RightColumnShare = .24;

    /// <summary>The narrowest a table may get before the two stop sharing a row.</summary>
    public const double TableMinWidth = 560;

    public static StatsPageLayout For(double width)
    {
        bool wide = width >= PageSpacing.WideFrom;
        double gutter = PageSpacing.For(width);
        double right = Math.Clamp(width * RightColumnShare, RightColumnMin, RightColumnMax);

        // What the two tables have once the page's own gutters and the right column are gone.
        double left = width - gutter * 2 - right - gutter;

        return new StatsPageLayout(
            gutter,
            right,
            wide ? 44 : 40,
            left >= TableMinWidth * 2 + gutter,
            wide);
    }
}
