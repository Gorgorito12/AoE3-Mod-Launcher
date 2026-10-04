using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>The columns of the Clasificación table (design 55a).</summary>
public enum RankingColumn
{
    /// <summary>"#": the server's place. Empty on a placement row.</summary>
    Rank,

    /// <summary>The badge, the name, and after it "YOU", the streak and INACTIVE.</summary>
    Player,

    /// <summary>The ELO and its bar (or, on a placement row, "1490?", the progress and the segments).</summary>
    Rating,

    /// <summary>The rated W-L record.</summary>
    Record,

    /// <summary>The win percentage.</summary>
    Percent,
}

/// <summary>
/// One column: a fixed width, or (null) a share of what is left — <see cref="Star"/> parts of it,
/// never less than <see cref="MinWidth"/> — and its alignment. That is CSS grid's
/// <c>minmax(MinWidth, Star fr)</c>, which a WPF star column with a <c>MinWidth</c> reproduces.
/// </summary>
public readonly record struct RankingColumnSpec(
    RankingColumn Column,
    double? FixedWidth,
    bool RightAligned,
    double Star = 1,
    double MinWidth = 0);

/// <summary>
/// The sizes of design 59 that follow the page's width: CSS <c>clamp()</c> values measured
/// against the frame the table sits in. <see cref="RankingTableLayout.Fluid"/> computes them.
/// </summary>
public readonly record struct RankingFluid(
    double Gap,
    double RowHeight,
    double PlacementRowHeight,
    double NameSize,
    double EloSize,
    double MatchLineSize);

/// <summary>
/// How the page shares its width between the table and the match list (design 59): side by
/// side in the proportion 60/40, or one above the other when the page is too narrow for both.
/// </summary>
public readonly record struct RankingSplit(double TableWidth, double PanelWidth, bool Stacked);

/// <summary>
/// The shape of the Clasificación table (design handoff 55a/55b, then 59), in ONE place that the
/// header and every row read — header and rows drifting apart misaligns every row, in a way no
/// compile can see.
///
/// <para><b>Rating v3:</b> the table is ordered by ELO, highest first, so the bar beside the
/// number can simply measure the number. The columns are <c># · JUGADOR · ELO · V-D · %</c>.</para>
///
/// <para><b>Design 59: every column grows in proportion.</b> The table fills the page again,
/// and a surplus that went to ONE column — first the name, then the ELO — always put something a
/// metre from the name it belonged to: on a real 2560-px window the bar measured ~1350 px and the
/// V-D and the % sat at the far edge. Now the five columns are <c>40px · minmax(180px,2fr) ·
/// minmax(200px,1.3fr) · minmax(64px,.45fr) · minmax(48px,.35fr)</c>, so the record and the
/// percentage keep their distance from the name, the bar stops at 300 px, and the page gives the
/// match list beside the table 40 % of the width (<see cref="Split"/>). The gap between columns,
/// the row heights and the type grow a little with the width too (<see cref="Fluid"/>), so a 32-inch
/// monitor does not show a laptop's table in the middle of a lot of air.</para>
/// </summary>
public static class RankingTableLayout
{
    /// <summary>The page width below which the table drops to three columns (design 55b).</summary>
    public const double NarrowBelow = 600;

    /// <summary>The longest the ELO bar and the placement segments get (design 59).</summary>
    public const double BarMaxWidth = 300;

    /// <summary>The ELO bar: 4 px high, radius 2, 5 px under the figure (design 59).</summary>
    public const double BarHeight = 4;
    public const double BarGap = 5;

    /// <summary>The placement segments: 4 px high, 3 px apart, radius 1 (design 59).</summary>
    public const double SegmentHeight = 4;
    public const double SegmentGap = 3;

    /// <summary>The column headings' row: 34 px, labels 16 px in from each side (design 59).</summary>
    public const double HeaderHeight = 34;
    public const double SidePadding = 16;

    /// <summary>
    /// <c>40 · minmax(180,2fr) · minmax(200,1.3fr) · minmax(64,.45fr) · minmax(48,.35fr)</c>
    /// (design 59).
    /// </summary>
    public static readonly IReadOnlyList<RankingColumnSpec> All = new[]
    {
        new RankingColumnSpec(RankingColumn.Rank, 40, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Player, null, RightAligned: false, Star: 2, MinWidth: 180),
        new RankingColumnSpec(RankingColumn.Rating, null, RightAligned: false, Star: 1.3, MinWidth: 200),
        new RankingColumnSpec(RankingColumn.Record, null, RightAligned: false, Star: 0.45, MinWidth: 64),
        new RankingColumnSpec(RankingColumn.Percent, null, RightAligned: true, Star: 0.35, MinWidth: 48),
    };

    /// <summary>55b, under 600 px: <c>28 · minmax(0,1fr) · 92</c> — W-L and % go.</summary>
    public static readonly IReadOnlyList<RankingColumnSpec> Narrow = new[]
    {
        new RankingColumnSpec(RankingColumn.Rank, 28, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Player, null, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Rating, 92, RightAligned: false),
    };

    /// <summary>The columns for a table this wide. 0 or less — not laid out yet — is the full set.</summary>
    public static IReadOnlyList<RankingColumnSpec> For(double width)
        => IsNarrow(width) ? Narrow : All;

    /// <summary>Whether a table this wide is the 55b variant.</summary>
    public static bool IsNarrow(double width) => width > 0 && width < NarrowBelow;

    /// <summary>The 55b variant's rows, which do not follow the width.</summary>
    public const double NarrowRowHeight = 44;
    public const double NarrowPlacementRowHeight = 44;

    /// <summary>
    /// The sizes that follow the page (design 59), each one a CSS <c>clamp(min, k·cqw, max)</c>
    /// where a <c>cqw</c> is a hundredth of the frame's CONTENT width — which is the ranking page
    /// itself, the page's margin being the frame's padding. So <paramref name="pageWidth"/> is
    /// what to pass. Up to about 1400 px every value sits at its minimum; they reach their
    /// maximums near 2700.
    ///
    /// <para>Rounded on purpose — the gap and the heights to whole pixels, the type to half a
    /// point — so a resize only rebuilds the table when something visible changes, not on every
    /// pixel. <paramref name="textFactor"/> is the launcher's text-size setting
    /// (<c>TextScale.CurrentFactor</c>): the type follows it like every token does; the heights
    /// are minimums, so a row with bigger text simply grows.</para>
    /// </summary>
    public static RankingFluid Fluid(double pageWidth, double textFactor = 1.0)
    {
        var cqw = pageWidth > 0 && !double.IsNaN(pageWidth) && !double.IsInfinity(pageWidth)
            ? pageWidth / 100.0
            : 0;
        var f = textFactor > 0 ? textFactor : 1.0;
        return new RankingFluid(
            Gap: Math.Round(Math.Clamp(cqw * 1.0, 14, 28)),
            RowHeight: Math.Round(Math.Clamp(cqw * 2.3, 48, 62)),
            PlacementRowHeight: Math.Round(Math.Clamp(cqw * 2.5, 54, 68)),
            NameSize: HalfPoint(Math.Clamp(cqw * 0.6, 13.5, 16) * f),
            EloSize: HalfPoint(Math.Clamp(cqw * 0.58, 13, 16) * f),
            MatchLineSize: HalfPoint(Math.Clamp(cqw * 0.55, 13, 15.5) * f));
    }

    private static double HalfPoint(double size)
        => Math.Round(size * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>The space between the table and the match list.</summary>
    public const double SplitGap = 14;

    /// <summary>The two cards' starting widths: <c>flex: 3 1 640px</c> and <c>flex: 2 1 340px</c>.</summary>
    public const double TableBasis = 640;
    public const double PanelBasis = 340;

    /// <summary>
    /// The table and the match list share the page as CSS's <c>flex: 3 1 640px</c> and
    /// <c>flex: 2 1 340px</c> with a 14-px gap do: each starts from its basis and takes 3/5 and
    /// 2/5 of what is left. Narrower than both bases and the gap, they wrap — each on a line of
    /// its own, at the page's full width.
    ///
    /// <para>In the launcher the stacked case is unreachable today: <c>UiScale</c> never lays
    /// the tab out under ~1100 logical px. It is computed anyway, because the handoff specifies
    /// it and a smaller minimum would reach it.</para>
    /// </summary>
    public static RankingSplit Split(double pageWidth, double gap = SplitGap)
    {
        if (!(pageWidth > 0) || double.IsInfinity(pageWidth)) return new RankingSplit(0, 0, Stacked: false);
        var both = TableBasis + PanelBasis + gap;
        if (pageWidth < both) return new RankingSplit(pageWidth, pageWidth, Stacked: true);
        var free = pageWidth - both;
        return new RankingSplit(TableBasis + free * 0.6, PanelBasis + free * 0.4, Stacked: false);
    }

    /// <summary>The header label for a column.</summary>
    public static string HeaderKey(RankingColumn column) => column switch
    {
        RankingColumn.Rank => "MpRankColNum",
        RankingColumn.Player => "MpRankColPlayer",
        RankingColumn.Rating => "MpRankColElo",
        RankingColumn.Record => "MpRankColWL",
        RankingColumn.Percent => "MpRankColPct",
        _ => "",
    };

    /// <summary>The bar is never drawn shorter than this, so a last place still has a bar.</summary>
    public const double MinBarFraction = 0.06;

    /// <summary>
    /// Where the bar starts counting. The handoff's bars are "proportional to first place" and,
    /// measured off its own widths (1748 → 100 %, 1612 → 80 %, 1502 → 66 %), they count from 1000:
    /// counted from zero every bar of a real table would sit between 85 and 100 % and the column
    /// would say nothing.
    /// </summary>
    public const double BarFloor = 1000;

    /// <summary>
    /// The bar under an ELO, as a fraction of the track: the rating against FIRST PLACE's, counted
    /// from <see cref="BarFloor"/>. The table is ordered by the same rating, so the bars descend
    /// down the page by construction.
    /// </summary>
    public static double BarFraction(double rating, double topRating)
    {
        var span = topRating - BarFloor;
        if (span <= 0.0001) return 1.0;
        var fraction = (rating - BarFloor) / span;
        if (double.IsNaN(fraction)) return MinBarFraction;
        return fraction < MinBarFraction ? MinBarFraction
             : fraction > 1 ? 1
             : fraction;
    }

    /// <summary>
    /// The brush for a win percentage (design 55a): green from 50 %, amber under it — and amber for
    /// an INACTIVE player whatever it is, as the handoff draws it, because a record nobody has added
    /// to in a month is not a current one.
    /// </summary>
    public static string PercentBrushKey(int percent, bool inactive = false)
        => !inactive && percent >= 50 ? "MpOkTextAlt" : "MpCaution";
}
