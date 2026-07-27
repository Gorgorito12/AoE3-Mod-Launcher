using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>A column of the multiplayer "active rooms" table, in display order.</summary>
public enum RoomColumn
{
    Room,
    Mod,
    Host,
    Players,
    Ping,
    Status,
    Action,
}

/// <summary>
/// How one column is sized. <paramref name="Weight"/> and <paramref name="MinWidth"/> feed WPF's
/// star sizing; <paramref name="ComfortWidth"/> is what the column's real content needs before it
/// starts getting cut, and is what decides whether the column earns its place at a given width.
/// </summary>
/// <param name="ComfortWidth">
/// Measured against the LONGEST of the two shipped languages, because Spanish is the wide one —
/// <c>JUGADORES</c> against <c>PLAYERS</c>, <c>ANFITRIÓN</c> against <c>HOST</c>.
/// </param>
public readonly record struct RoomColumnSpec(
    RoomColumn Column,
    double Weight,
    double MinWidth,
    double ComfortWidth);

/// <summary>
/// The one definition of the rooms table's columns, and the rule for which of them survive at a
/// given width.
///
/// <para><b>Why this exists.</b> The seven widths used to be written twice — once in
/// <c>MultiplayerTab.xaml</c> for the header strip and once as literals in
/// <c>BuildRoomCard</c> for each row — kept in step only by a comment in each place asking the
/// next reader to keep them identical. Header and rows drifting apart misaligns every row in the
/// table, so the two lists became one.</para>
///
/// <para><b>Why comfort width, not min width.</b> The seven minimums add up to well under the
/// space the table actually gets, so by that measure everything always "fits" — and yet the
/// content overflowed anyway, because a column's STAR SHARE can be narrower than the text inside
/// it. Deciding on the width the content really wants is what makes the table react before it
/// looks broken rather than after.</para>
///
/// <para>Pure and WPF-free on purpose, so the drop order is pinned by tests instead of argued
/// about — see <c>RoomsTableLayoutTests</c>.</para>
/// </summary>
public static class RoomsTableLayout
{
    /// <summary>
    /// Every column, in display order. The weights and minimums are the values the table has
    /// always used; the comfort widths are measured from the widest shipped label plus the
    /// adornment that sits beside it (icon disc, ping bars, status dot, sort arrow).
    /// </summary>
    public static readonly IReadOnlyList<RoomColumnSpec> All = new[]
    {
        new RoomColumnSpec(RoomColumn.Room,    2.3,  120, 210),
        new RoomColumnSpec(RoomColumn.Mod,     1.05,  58, 130),
        new RoomColumnSpec(RoomColumn.Host,    1.35,  66, 140),
        new RoomColumnSpec(RoomColumn.Players, 0.62,  46,  86),
        new RoomColumnSpec(RoomColumn.Ping,    0.62,  48,  84),
        new RoomColumnSpec(RoomColumn.Status,  0.9,   60, 104),
        new RoomColumnSpec(RoomColumn.Action,  0.95, 100, 112),
    };

    /// <summary>
    /// The order columns are given up in as space runs out — least useful first.
    ///
    /// <para>Room, Players and Action are absent by design and must stay absent: which room,
    /// whether there is space in it, and the way in are the entire point of the table. Ping goes
    /// first because it is a nicety, then Host, then Mod — and none of the three is actually lost,
    /// because <see cref="Hidden"/> tells the row builder to fold them into the room's second
    /// line.</para>
    /// </summary>
    private static readonly RoomColumn[] DropOrder =
    {
        RoomColumn.Ping,
        RoomColumn.Host,
        RoomColumn.Mod,
    };

    /// <summary>
    /// The columns worth showing in <paramref name="availableWidth"/> logical pixels, in display
    /// order. Never returns fewer than Room + Players + Action: past that point the table is as
    /// small as it is allowed to get, and the cells trim instead.
    /// </summary>
    public static IReadOnlyList<RoomColumnSpec> Resolve(double availableWidth)
    {
        var kept = All.ToList();

        foreach (var candidate in DropOrder)
        {
            if (kept.Sum(c => c.ComfortWidth) <= availableWidth) break;
            kept.RemoveAll(c => c.Column == candidate);
        }

        return kept;
    }

    /// <summary>
    /// The columns <see cref="Resolve"/> gave up at this width, so their values can be moved into
    /// the room's subtitle line. Returned in display order, which is the order they read best in.
    /// </summary>
    public static IReadOnlyList<RoomColumn> Hidden(double availableWidth) => Hidden(Resolve(availableWidth));

    /// <summary>
    /// The columns missing from an already-resolved set. The caller passes the very set it is
    /// rendering, so the subtitle can never claim to be covering for a column that is actually
    /// on screen — re-deriving it from a width could, if the two widths ever disagreed.
    /// </summary>
    public static IReadOnlyList<RoomColumn> Hidden(IReadOnlyList<RoomColumnSpec> kept)
    {
        var present = kept.Select(c => c.Column).ToHashSet();
        return All.Select(c => c.Column).Where(c => !present.Contains(c)).ToList();
    }

    /// <summary>Whether two resolved sets are the same, so a resize can skip re-rendering rows.</summary>
    public static bool SameColumns(IReadOnlyList<RoomColumnSpec>? a, IReadOnlyList<RoomColumnSpec>? b)
    {
        if (a == null || b == null) return ReferenceEquals(a, b);
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Column != b[i].Column) return false;
        return true;
    }
}
