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

/// <summary>One column: a fixed width (null = the flexible one) and its alignment.</summary>
public readonly record struct RankingColumnSpec(
    RankingColumn Column,
    double? FixedWidth,
    bool RightAligned);

/// <summary>
/// The shape of the Clasificación table (design handoff 55a/55b), in ONE place that the header
/// and every row read — header and rows drifting apart misaligns every row, in a way no compile
/// can see.
///
/// <para><b>Rating v3:</b> the table is ordered by ELO, highest first, so the bar beside the
/// number can simply measure the number. The conservative-rating bar (rating − 2·rd) this class
/// used to draw belonged to a table ordered by that floor; it went with the ordering. The CIVS
/// and DECIDED columns went too: the handoff's table is <c># · JUGADOR · ELO · V-D · %</c>.</para>
/// </summary>
public static class RankingTableLayout
{
    /// <summary>The page width below which the table drops to three columns (design 55b).</summary>
    public const double NarrowBelow = 600;

    /// <summary>The space between two columns: the handoff's <c>gap: 0 10px</c>.</summary>
    public const double ColumnGap = 10;

    /// <summary>The handoff's <c>40 · minmax(0,1fr) · 140 · 64 · 52</c>.</summary>
    public static readonly IReadOnlyList<RankingColumnSpec> All = new[]
    {
        new RankingColumnSpec(RankingColumn.Rank, 40, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Player, null, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Rating, 140, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Record, 64, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Percent, 52, RightAligned: true),
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

    /// <summary>Heights (design 55a/55b): a ranked row, a placement row, and a placement row
    /// in the narrow variant, where the progress shares the ELO's line.</summary>
    public const double RowHeight = 44;
    public const double PlacementRowHeight = 54;
    public const double NarrowPlacementRowHeight = 44;

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
