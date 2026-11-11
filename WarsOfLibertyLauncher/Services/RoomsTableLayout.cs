using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>A column of the multiplayer "active rooms" table, in display order.</summary>
public enum RoomColumn
{
    Room,
    Host,
    Players,
    Ping,
    Action,
}

/// <summary>
/// How one column is sized. A null <paramref name="FixedWidth"/> means the column takes the
/// remaining space (the reference's <c>1fr</c>); anything else is a fixed pixel width.
/// <paramref name="ComfortWidth"/> is what the column's content needs before it starts getting
/// cut, and is what decides whether the column earns its place at a given width.
/// </summary>
/// <param name="ComfortWidth">
/// Measured against the LONGEST of the two shipped languages, because Spanish is the wide one —
/// <c>JUGADORES</c> against <c>PLAYERS</c>, <c>ANFITRIÓN</c> against <c>HOST</c>.
/// </param>
public readonly record struct RoomColumnSpec(
    RoomColumn Column,
    double? FixedWidth,
    double ComfortWidth);

/// <summary>
/// The one definition of the rooms table's columns, and the rule for which of them survive at a
/// given width.
///
/// <para><b>Why this exists.</b> The widths used to be written twice — once in
/// <c>MultiplayerTab.xaml</c> for the header strip and once as literals in
/// <c>BuildRoomCard</c> for each row — kept in step only by a comment in each place asking the
/// next reader to keep them identical. Header and rows drifting apart misaligns every row in the
/// table, so the two lists became one.</para>
///
/// <para><b>Five columns, from the design handoff.</b> MOD and STATUS are gone: the reference
/// folds the mod name into the room's second line (where it reads as context rather than as a
/// column nobody sorts by) and lets the action button carry the status, since "In game" and
/// "Full" are already why that button is disabled. Widths are the reference's own.</para>
///
/// <para><b>Why the drop rule survives a fixed-width design.</b> The reference is a static
/// mockup at 1240px and simply never meets the launcher's 900px minimum, so it is SILENT about
/// narrow windows rather than in disagreement with this. The rooms list disables horizontal
/// scrolling, so anything that does not fit is not scrolled to — it is clipped off the edge.
/// Keeping the rule costs nothing at the widths the reference does cover, where every column
/// fits and nothing is dropped.</para>
///
/// <para>Pure and WPF-free on purpose, so the drop order is pinned by tests instead of argued
/// about — see <c>RoomsTableLayoutTests</c>.</para>
/// </summary>
public static class RoomsTableLayout
{
    /// <summary>
    /// Every column, in display order. Fixed widths are the reference's
    /// (<c>minmax(0,1fr) 152px 88px 66px 96px</c>); Room takes what is left. Comfort widths are
    /// measured from the widest shipped label plus the adornment beside it (icon disc, capacity
    /// segments, avatar).
    /// </summary>
    public static readonly IReadOnlyList<RoomColumnSpec> All = new[]
    {
        // Design 86: `36px minmax(0,1fr) minmax(130px,190px) 74px 64px 136px` with a 12-px gap.
        // PING is NOT in this set: it is the ping to each host over Radmin, which only a server
        // that publishes the hosts' addresses makes possible, so it is added by WithPing.
        // The 36-px icon lives INSIDE the Room cell (with its gap, 48 px) because it has no
        // heading; every fixed width below carries the 12-px gap that opens its column, which
        // the row builder applies as a left margin. Room's comfort is the icon plus a name.
        new RoomColumnSpec(RoomColumn.Room,    null, 230),
        // The host's photo (28) and badge (22) beside his NAME over his rating: the reference's 190
        // + its gap + what the photo and badge take (the maintainer asked for both back over 86).
        new RoomColumnSpec(RoomColumn.Host,    250,  250),
        // "1/2" with the people icon in front.
        new RoomColumnSpec(RoomColumn.Players, 100,  100),
        // Wide enough for "Revisar mi copia" / "Entrar a mirar" at 12.5 semibold.
        new RoomColumnSpec(RoomColumn.Action,  148,  148),
    };

    /// <summary>
    /// The order columns are given up in as space runs out — least useful first.
    ///
    /// <para>Room, Players and Action are absent by design and must stay absent: which room,
    /// whether there is space in it, and the way in are the entire point of the table. Ping goes
    /// first because it is a nicety, then Host — and neither is actually lost, because
    /// <see cref="Hidden"/> tells the row builder to fold it into the room's second line.</para>
    ///
    /// <para>The rating rides INSIDE the Host cell, so it cannot outlive the name it belongs
    /// to — they are one element and drop together. That used to be a rule enforced by this
    /// array's order and a test; making it structural is better than either.</para>
    /// </summary>
    private static readonly RoomColumn[] DropOrder =
    {
        RoomColumn.Ping,
        RoomColumn.Host,
    };

    /// <summary>
    /// <see cref="All"/> with the PING column before the action: the ping to each host over
    /// Radmin. Only offered when the server publishes the hosts' addresses
    /// (<c>GET /lobbies/radmin</c>); without that there is no per-room figure to show.
    /// </summary>
    public static readonly IReadOnlyList<RoomColumnSpec> WithPing = All
        .Take(All.Count - 1)
        // Three signal bars in front of "38 ms".
        .Append(new RoomColumnSpec(RoomColumn.Ping, 100, 100))
        .Append(All[All.Count - 1])
        .ToArray();

    /// <summary>
    /// The columns worth showing in <paramref name="availableWidth"/> logical pixels, in display
    /// order. Never returns fewer than Room + Players + Action: past that point the table is as
    /// small as it is allowed to get, and the cells trim instead.
    /// </summary>
    public static IReadOnlyList<RoomColumnSpec> Resolve(double availableWidth, bool includePing = false)
    {
        var kept = (includePing ? WithPing : All).ToList();

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
