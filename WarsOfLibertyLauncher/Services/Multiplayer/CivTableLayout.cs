using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>A column of the civilization-balance table, in display order.</summary>
public enum CivColumn
{
    /// <summary>The civilization's name, as its own mod calls it.</summary>
    Civ,

    /// <summary>How many rated 1v1s it was played in — including the ones nobody could read.</summary>
    Played,

    /// <summary>The record behind those: 8-5.</summary>
    Record,

    /// <summary>Wins over decided, as a bar. Green on red, so the balance reads without the
    /// number - and empty and grey when there is no sample to draw one from.</summary>
    WinBar,

    /// <summary>Win percentage, and only once there is enough behind it to state one.</summary>
    Percent,

    /// <summary>Mean match length. Not balance on its own, but it is how a slow civ reads.</summary>
    Length,
}

public readonly record struct CivColumnSpec(
    CivColumn Column,
    double? FixedWidth,
    bool RightAligned,
    double? MaxWidth = null,
    double Star = 1,
    double MinWidth = 0);

/// <summary>
/// Where the civilization table's columns are, in one place, so its header and its rows cannot
/// drift apart — a break no compile can see and no screenshot on a wide monitor shows.
///
/// <para><b>A copy of <see cref="RankingTableLayout"/>'s shape and deliberately NOT a reuse of
/// it.</b> They are different tables that happen to rhyme today; sharing the columns would tie
/// them together for ever, so that widening PLAYER to fit a long name would silently widen CIV
/// too.</para>
/// </summary>
public static class CivTableLayout
{
    /// <summary>How wide the name column may get before it stops growing.</summary>
    public const double CivMaxWidth = 320;

    /// <summary>How wide the win bar is. Fixed, unlike the ladder's.</summary>
    public const double WinBarWidth = 150;

    /// <summary>
    /// The flexible column is the NAME here, not the bar.
    ///
    /// <para>The ladder makes RATING flexible because a longer bar there means a longer
    /// reading of the same number. This bar is a PROPORTION - wins over decided - and a
    /// proportion says exactly as much at 150px as at 400. So the surplus still goes to the
    /// name, capped so a 2000-px window does not strand the counts a screen away from it.</para>
    ///
    /// <para>The bar was added when the table gained one; the paragraph above used to say this
    /// table had none, which stopped being true.</para>
    /// </summary>
    /// <remarks>
    /// Design 109 (`docs/design_14nov`): the balance splits into up to three columns of ~450, so
    /// the figures went narrow (PART. 40, RECORD 54, % 46, DUR. 54, 10 apart) and the name and the
    /// bar share the rest, 1.4 to 1, with a floor each. Supersedes the 74/86/150/58/72 above.
    /// </remarks>
    public static readonly IReadOnlyList<CivColumnSpec> All = new[]
    {
        new CivColumnSpec(CivColumn.Civ, null, RightAligned: false, Star: 1.4, MinWidth: 110),
        new CivColumnSpec(CivColumn.Played, 40, RightAligned: true),
        new CivColumnSpec(CivColumn.Record, 54, RightAligned: true),
        new CivColumnSpec(CivColumn.WinBar, null, RightAligned: false, Star: 1, MinWidth: 56),
        new CivColumnSpec(CivColumn.Percent, 46, RightAligned: true),
        new CivColumnSpec(CivColumn.Length, 54, RightAligned: true),
    };

    /// <summary>
    /// The matchup table, which sits directly under the civ table: the same four columns without
    /// the duration.
    ///
    /// <para>DERIVED from <see cref="All"/> rather than written out again, and that is the point.
    /// The two tables are stacked on one page, so their columns have to line up — with two
    /// hand-written lists a later tweak to one width would misalign them by a few pixels, which
    /// reads as a rendering fault rather than as an edit somebody made.</para>
    ///
    /// <para>There is no duration column because the average length of a specific pairing over
    /// three games is noise, and a column of blanks is worse than no column. The win bar goes
    /// for the same reason: a matchup row already IS a pair of records, and a bar of one side
    /// of it against the other would be the same figure drawn twice.</para>
    /// </summary>
    /// <remarks>Design 109: the pairs card is its own card now, beside the balance rather than
    /// under it, so its columns no longer have to line up with the balance's; they narrow so both
    /// names fit (pair 1 part, PART. 34, RECORD 54, % 46).</remarks>
    public static readonly IReadOnlyList<CivColumnSpec> Matchups = new[]
    {
        new CivColumnSpec(CivColumn.Civ, null, RightAligned: false),
        new CivColumnSpec(CivColumn.Played, 34, RightAligned: true),
        new CivColumnSpec(CivColumn.Record, 54, RightAligned: true),
        new CivColumnSpec(CivColumn.Percent, 46, RightAligned: true),
    };

    /// <summary>The gap between columns (design 109).</summary>
    public const double ColumnGap = 10;

    public static string HeaderKey(CivColumn column) => column switch
    {
        CivColumn.Civ => "MpCivColCiv",
        CivColumn.Played => "MpStatsColPlayedShort",
        CivColumn.Record => "MpStatsColRecord",
        CivColumn.WinBar => "MpStatsColWins",
        CivColumn.Percent => "MpActivityRankColPct",
        CivColumn.Length => "MpStatsColLengthShort",
        _ => "",
    };

    /// <summary>The tooltip of a shortened heading (design 109): PART. and DUR.</summary>
    public static string? HeaderTipKey(CivColumn column) => column switch
    {
        CivColumn.Played => "MpStatsColPlayedTip",
        CivColumn.Length => "MpStatsColLengthTip",
        _ => null,
    };

    /// <summary>
    /// The same headers, except the first column holds a PAIR rather than one civilization —
    /// "CIVILIZACIÓN" over "Chinos vs Otomanos" would be describing half the cell.
    /// </summary>
    public static string MatchupHeaderKey(CivColumn column) =>
        column == CivColumn.Civ ? "MpMatchupColPair" : HeaderKey(column);
}
