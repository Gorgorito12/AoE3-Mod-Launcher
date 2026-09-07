using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>A column of the Clasificación table, in display order.</summary>
public enum RankingColumn
{
    /// <summary>The position on the ladder. The server's number, never renumbered here.</summary>
    Rank,

    /// <summary>Avatar, name, and the PROVISIONAL tag when the rating has not settled.</summary>
    Player,

    /// <summary>The rating, and the bar that shows how far it is from first place.</summary>
    Rating,

    /// <summary>The player's three most-played civilizations, as flags. Only drawn when the
    /// server sends them — see <see cref="RankingTableLayout.For"/>.</summary>
    Civs,

    /// <summary>How many of this player's matches were actually decided.</summary>
    Decided,

    /// <summary>The record behind that number — <c>8-5</c>.</summary>
    Record,

    /// <summary>Win percentage, coloured.</summary>
    Percent,
}

/// <summary>
/// How one column is sized. A null <paramref name="FixedWidth"/> means the column shares the
/// remaining space; <paramref name="MaxWidth"/> then caps how much of it that column may take,
/// and the surplus goes to the other flexible column.
/// </summary>
public readonly record struct RankingColumnSpec(
    RankingColumn Column,
    double? FixedWidth,
    bool RightAligned,
    double? MaxWidth = null);

/// <summary>
/// The one definition of the Clasificación table's columns.
///
/// <para><b>Why this exists.</b> The widths were written twice — in
/// <c>MultiplayerTab.BuildRankingHeader</c> and again in <c>BuildLeaderboardRow</c> — kept in
/// step only by a comment in each asking the next reader to remember, and
/// <c>.claude/rules/multiplayer.md</c> recorded that as a standing hazard rather than as a
/// solved problem. Header and rows drifting apart misaligns every row in the table, and it is
/// the kind of break that a compile cannot see and a screenshot on a wide monitor does not
/// show. Same treatment <c>RoomsTableLayout</c> already gave the rooms table.</para>
///
/// <para><b>Six columns, from the design handoff</b> (<c>44px · minmax(0,1fr) · 132px · 74px ·
/// 86px · 58px</c>), with two of them new and the flexible one moved — see <see cref="All"/>
/// for why RATING grows rather than PLAYER. <b>RECORD</b> is the one that matters: DECIDED on
/// its own says how many matches a player has had settled and nothing about how they went, so
/// a column of bare counts invited the reader to compare numbers that were not comparable. And
/// the rating column carries a BAR beside the number — the table is ordered by the
/// conservative rating (<c>rating - 2 × rd</c>) rather than by the rating it prints, so the
/// printed numbers do not descend down the page; the bar is what makes the order legible
/// without contradicting the number.</para>
///
/// <para><b>And a seventh, CIVS, that is only there when the server is.</b> Three flags for
/// the civilizations a player picks most, asked for after the ladder sat beside a
/// "civilization balance" strip that named civilizations and nobody. It is a column of the
/// table rather than a strip below it because the question it answers — "what does THIS
/// player play?" — is a question about a row. See <see cref="For"/>.</para>
///
/// <para>Pure and WPF-free, so the columns are pinned by <c>RankingTableLayoutTests</c>
/// instead of by a comment.</para>
/// </summary>
public static class RankingTableLayout
{
    /// <summary>How wide the PLAYER column is allowed to get before it stops growing.</summary>
    public const double PlayerMaxWidth = 340;

    /// <summary>How many civilizations the CIVS cell shows. The server sends at most this many.</summary>
    public const int MaxTopCivs = 3;

    /// <summary>The flags in the MOST PLAYED cell, in DIPs. Bigger than the 20 the statistics
    /// tables and the match list use: those sit inline with text, this one is the whole cell
    /// of a 42-px row beside a 24-px avatar, and at 20 it read as a speck.</summary>
    public const double CivFlagSize = 28;

    /// <summary>The gap between two flags in the CIVS cell.</summary>
    public const double CivFlagGap = 5;

    /// <summary>Three flags and the two gaps between them come to 70; the column is wider
    /// because its heading is "MOST PLAYED" / "MÁS JUGADAS" — a heading that says what the
    /// column counts, which "CIVS" did not — and a heading that overflows its column is read
    /// as belonging to the next one.</summary>
    public const double CivsWidth = 96;

    /// <summary>
    /// Every column, in display order.
    ///
    /// <para>The first two are left-aligned and the four data columns right-aligned, which is
    /// what lets a reader compare down a column: a ragged right edge on numbers of different
    /// lengths is the reason tables like this are hard to scan. CIVS is left-aligned with them
    /// not against them: flags are read left to right, most played first, and a right-aligned
    /// run of them would put the one that matters furthest from the name.</para>
    ///
    /// <para><b>RATING is the column that grows, and PLAYER is capped. Getting this the other
    /// way round is the whole defect this table was rebuilt to fix.</b> The page fills the
    /// window now, so SOMETHING has to absorb the surplus — and with PLAYER flexible (which is
    /// what the handoff's fixed-width mockup implies) a 2000-px window puts the name hard left
    /// and its rating about 1500 px away, which is exactly the complaint the handoff opens
    /// with. RATING's cell holds the number AND the comparative bar, so the surplus goes to a
    /// bar that gets longer: the name stays beside its own figure, the gap is filled by data
    /// rather than by nothing, and the bar literally draws the line between the two.</para>
    ///
    /// <para>Both flexible columns share equally while there is little to share, so a 900-px
    /// window is unaffected: PLAYER only stops at <see cref="PlayerMaxWidth"/> once the window
    /// is wide enough for that to be generous.</para>
    /// </summary>
    public static readonly IReadOnlyList<RankingColumnSpec> All = new[]
    {
        new RankingColumnSpec(RankingColumn.Rank, 44, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Player, null, RightAligned: false,
                              MaxWidth: PlayerMaxWidth),
        new RankingColumnSpec(RankingColumn.Rating, null, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Civs, CivsWidth, RightAligned: false),
        new RankingColumnSpec(RankingColumn.Decided, 74, RightAligned: true),
        new RankingColumnSpec(RankingColumn.Record, 86, RightAligned: true),
        new RankingColumnSpec(RankingColumn.Percent, 58, RightAligned: true),
    };

    /// <summary>
    /// The columns for THIS table: <see cref="All"/>, minus CIVS when no row carries any.
    ///
    /// <para>Null <c>TopCivs</c> on every row means the backend predates the field, and a
    /// heading over a column of empty cells would announce a feature the server does not
    /// have. An EMPTY list on every row means the server looked and nobody has a civilization
    /// on record yet — the ordinary state of a community whose reports only started carrying
    /// it in 1.0.14 — and that too is a column with nothing to say. It comes back the moment
    /// one row has one.</para>
    /// </summary>
    public static IReadOnlyList<RankingColumnSpec> For(
        IReadOnlyList<Models.Multiplayer.LeaderboardRow>? rows)
    {
        var anyCivs = rows != null && rows.Any(r => r.TopCivs is { Count: > 0 });
        return anyCivs ? All : All.Where(c => c.Column != RankingColumn.Civs).ToList();
    }

    /// <summary>The gap between columns, in DIPs. The handoff's <c>gap: 0 12px</c>.</summary>
    public const double ColumnGap = 12;

    /// <summary>The localisation key for a column's heading.</summary>
    public static string HeaderKey(RankingColumn column) => column switch
    {
        RankingColumn.Rank => "MpActivityRankColHash",
        RankingColumn.Player => "MpActivityRankColPlayer",
        RankingColumn.Rating => "MpRankColRating",
        RankingColumn.Civs => "MpRankColCivs",
        RankingColumn.Decided => "MpRankColDecided",
        RankingColumn.Record => "MpRankColRecord",
        RankingColumn.Percent => "MpActivityRankColPct",
        _ => "",
    };

    /// <summary>
    /// How long a rating's bar is, as a fraction of the width available to it: this rating
    /// against the highest one on the table.
    ///
    /// <para><b>It is scaled from the FLOOR of the visible table, not from zero</b>, and that
    /// is the whole reason it says anything. Ratings cluster in the 1300-1600 band, so bars
    /// measured from zero would all be within a few percent of full and the column would be a
    /// row of identical stripes. Measured from the lowest rating shown, the same data spreads
    /// across the bar.</para>
    ///
    /// <para>The bottom row therefore gets a MINIMUM rather than an empty cell: an empty bar
    /// reads as "no rating", which is a different claim and one the table cannot make about
    /// somebody who qualified for it.</para>
    ///
    /// <para>Degenerate input — one row, or a table where everyone is level — gives every bar
    /// the same full length, which is true.</para>
    /// </summary>
    public const double MinBarFraction = 0.12;

    public static double BarFraction(double rating, double lowest, double highest)
    {
        var span = highest - lowest;
        if (span <= 0.0001) return 1.0;

        var fraction = (rating - lowest) / span;
        if (double.IsNaN(fraction)) return MinBarFraction;

        return fraction < MinBarFraction ? MinBarFraction
             : fraction > 1 ? 1
             : fraction;
    }

    /// <summary>
    /// Which brush a win percentage is painted in. The handoff's three bands.
    ///
    /// <para>Colour is the only reason the column earns its width — a table of bare
    /// percentages is read one row at a time, and a coloured one is read at a glance.</para>
    /// </summary>
    public static string PercentBrushKey(int percent)
        => percent >= 50 ? "MpOkTextAlt"
         : percent >= 30 ? "MpCaution"
         : "MpDestructiveText";
}
