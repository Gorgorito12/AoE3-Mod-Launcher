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

    /// <summary>Win percentage, and only once there is enough behind it to state one.</summary>
    Percent,

    /// <summary>Mean match length. Not balance on its own, but it is how a slow civ reads.</summary>
    Length,
}

public readonly record struct CivColumnSpec(
    CivColumn Column,
    double? FixedWidth,
    bool RightAligned,
    double? MaxWidth = null);

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

    /// <summary>
    /// The flexible column is the NAME here, not the count.
    ///
    /// <para>The ladder makes RATING flexible because that cell carries the comparative bar, so a
    /// wide window lengthens a piece of data. This table has no bar — its numbers are all short
    /// and fixed-width — so the only thing worth giving the surplus to is the name, capped so a
    /// 2000-px window does not strand the counts a screen away from it.</para>
    /// </summary>
    public static readonly IReadOnlyList<CivColumnSpec> All = new[]
    {
        new CivColumnSpec(CivColumn.Civ, null, RightAligned: false, MaxWidth: CivMaxWidth),
        new CivColumnSpec(CivColumn.Played, 74, RightAligned: true),
        new CivColumnSpec(CivColumn.Record, 86, RightAligned: true),
        new CivColumnSpec(CivColumn.Percent, 58, RightAligned: true),
        new CivColumnSpec(CivColumn.Length, 72, RightAligned: true),
    };

    /// <summary>The gap between columns, matching the ladder's.</summary>
    public const double ColumnGap = 12;

    public static string HeaderKey(CivColumn column) => column switch
    {
        CivColumn.Civ => "MpCivColCiv",
        CivColumn.Played => "MpCivColPlayed",
        CivColumn.Record => "MpRankColRecord",
        CivColumn.Percent => "MpActivityRankColPct",
        CivColumn.Length => "MpCivColLength",
        _ => "",
    };
}
