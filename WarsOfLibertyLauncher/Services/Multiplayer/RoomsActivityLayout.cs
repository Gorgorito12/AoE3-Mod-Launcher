using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>How the Rooms page shows the community block (designs 57b, 60 and 61).</summary>
public enum RoomsActivityMode
{
    /// <summary>No community data: nothing under the list.</summary>
    None,
    /// <summary>The block folded to its header line: title, the facts and "Show ▴".</summary>
    Folded,
    /// <summary>The block open, anchored at the bottom of the column: header line and the three cards (61).</summary>
    Fixed,
    /// <summary>
    /// The three cards laid OVER the bottom of the list, because the player asked for them and
    /// they do not fit under it (design 57a: "Show activity opens the cards on top of the list").
    /// The block stays folded in its row, offering to hide them again.
    /// </summary>
    Overlay,
}

/// <summary>What <see cref="RoomsActivityLayout.Plan"/> decides for the left column.</summary>
/// <param name="Mode">How the community block is drawn.</param>
/// <param name="CardsHeight">The three cards' height, open or laid over the list; 0 folded.</param>
public readonly record struct RoomsSpacePlan(RoomsActivityMode Mode, double CardsHeight);

/// <summary>
/// The community block's sizes, MEASURED (design 61): what the open block adds to its cards
/// (header line, padding, the gap and the rim), the cards' height — the Ranking card with its five
/// rows, the maintainer's limit — and the block folded to its header line.
/// </summary>
public readonly record struct ActivitySizes(double Chrome, double MinCards, double Folded)
{
    /// <summary>The design's own sizes (61a, at the reference text size), until something is measured.</summary>
    public static ActivitySizes Reference => new(
        RoomsActivityLayout.ExpandedChrome,
        RoomsActivityLayout.MinCardsHeight,
        RoomsActivityLayout.FoldedHeight);
}

/// <summary>
/// The sizes of the community block that follow the page (design 61), each a CSS
/// <c>clamp(min, k·cqw, max)</c> with <c>cqw</c> a hundredth of the page's width — the same
/// device as Ranking's <see cref="RankingFluid"/>. Type sizes already carry the launcher's text
/// size; heights are minimums.
/// </summary>
public readonly record struct ActivityFluid(
    double TitleSize,
    double FactLabelSize,
    double FactValueSize,
    double PeakBarsMin,
    double PeakGap,
    double PeakLineSize,
    double PeakSubSize,
    double MatchRowPadding,
    double MatchNameSize,
    double MatchSubSize,
    double RankRowHeight,
    double RankNameSize,
    double RankEloSize,
    double EmptyTitleSize,
    double EmptyBodySize);

/// <summary>
/// The Rooms page's layout decisions (design handoff turns 38-40, then 57, 60 and 61). Pure, so
/// the rules — which are the whole point of the change — are tested rather than read off a
/// screenshot.
///
/// <para><b>The community block is ANCHORED AT THE BOTTOM of the column (61, and the maintainer:
/// "always at the bottom").</b> The rooms take everything above it — their row is always the
/// star, and an empty list centres its notice in the space rather than shrinking to it (which is
/// what left a huge gap under the block on a big screen).</para>
///
/// <para><b>The cards END AT THE FIFTH PLAYER</b> (the maintainer: "the limit should be the 5th best
/// player"): they are exactly as tall as the Ranking card with its five rows, whatever the
/// column's height, and the rooms take everything above. The peak card's bars and the matches
/// card fit inside that — the bars fill it, the matches card shows the whole rows it holds. It
/// replaced a third of the column, which on a tall screen left a band of nothing under the 5th
/// player. When that height does not fit beside the rooms' minimum (four rows, all of them when
/// fewer, or the notice), the block folds.</para>
///
/// <para><b>Folding is the player's choice, and it is remembered.</b> Hidden: folded. Not chosen:
/// open when it fits, folded when it does not. Shown: open when it fits, and when it does not, the
/// cards OVER the bottom of the list — 57a's "Show activity opens the cards on top of the list" —
/// with the block folded in its row.</para>
/// </summary>
public static class RoomsActivityLayout
{
    /// <summary>
    /// What the open block adds to its cards: its 16-px padding top and bottom, the 28-px header
    /// line, the gap under it (at most <see cref="PageSpacing.Wide"/>) and the rim. Stands in
    /// until it is measured.
    /// </summary>
    public const double ExpandedChrome = 2 * PageSpacing.PanelPadding + 28 + PageSpacing.Wide + 2;

    /// <summary>
    /// The cards' height (61a): the ranking card's five 26-px rows, its title and padding
    /// (<see cref="EstimateMinCards"/> at the laptop's sizes). Stands in until it is measured.
    /// </summary>
    public const double MinCardsHeight = 186;

    /// <summary>The ranking card around its rows: padding 10 + 10, the rim, the title line and the 6 px under it.</summary>
    public const double RankingCardChrome = 40;

    /// <summary>The space between two ranking rows.</summary>
    public const double RankRowSpacing = 4;

    /// <summary>
    /// The cards' height before anything is measured: the ranking card with its five rows at the
    /// page's row height — the fifth player is the limit. Never below <see cref="MinCardsHeight"/>.
    /// </summary>
    public static double EstimateMinCards(ActivityFluid fluid)
        => Math.Max(MinCardsHeight, Math.Ceiling(RankingCardChrome + 5 * fluid.RankRowHeight + 4 * RankRowSpacing));

    /// <summary>The block folded to its header line: its 16-px padding top and bottom, the line and the rim.</summary>
    public const double FoldedHeight = 2 * PageSpacing.PanelPadding + 28 + 2;

    /// <summary>
    /// The space between the rooms and the block when none is given: the tab's narrow G
    /// (<see cref="PageSpacing"/>). The tab passes its own, 12 or 16 by the page's width; 61's 10
    /// gave way to the maintainer's one-gap rule.
    /// </summary>
    public const double Gap = PageSpacing.Narrow;

    /// <summary>The fewest room rows the list keeps before anything below it is drawn (57b).</summary>
    public const int MinRoomRows = 4;

    /// <summary>The space under each room row (the row style's bottom margin).</summary>
    public const double RowSpacing = 6;

    /// <summary>
    /// The least height the rooms block keeps: its own chrome (title, column headings, the
    /// list's padding) and <see cref="MinRoomRows"/> rows — all of them when there are fewer.
    /// </summary>
    public static double RoomsMinHeight(double chrome, int rows, double rowHeight)
        => Math.Max(0, chrome) + Math.Min(MinRoomRows, Math.Max(0, rows)) * (rowHeight + RowSpacing);

    /// <summary>
    /// Share the column (57b, 61).
    /// </summary>
    /// <param name="columnHeight">The left column's height; 0 or less when not laid out yet.</param>
    /// <param name="roomsMinHeight">What the rooms keep (<see cref="RoomsMinHeight"/>), or the
    /// empty notice's height when there are no rooms.</param>
    /// <param name="roomsEmpty">No rooms: the cards never lie over the notice.</param>
    /// <param name="hasActivity">Whether the block has anything to show at all.</param>
    /// <param name="choice">The player's saved choice: true shown, false hidden, null not chosen.</param>
    /// <param name="sizes">The block's measured sizes; <see cref="ActivitySizes.Reference"/> when null.</param>
    /// <param name="gap">The space between the rooms and the block.</param>
    public static RoomsSpacePlan Plan(
        double columnHeight, double roomsMinHeight, bool roomsEmpty,
        bool hasActivity, bool? choice, ActivitySizes? sizes = null, double gap = Gap)
    {
        var s = Sane(sizes ?? ActivitySizes.Reference);
        if (!hasActivity) return new RoomsSpacePlan(RoomsActivityMode.None, 0);
        if (choice == false) return new RoomsSpacePlan(RoomsActivityMode.Folded, 0);
        // Not laid out yet: draw the block at its height and let the first real pass decide.
        if (!(columnHeight > 0)) return new RoomsSpacePlan(RoomsActivityMode.Fixed, s.MinCards);

        // The cards end at the fifth player, whatever the column: the rooms keep the rest.
        var room = columnHeight - Math.Max(0, roomsMinHeight) - gap - s.Chrome;
        if (room >= s.MinCards) return new RoomsSpacePlan(RoomsActivityMode.Fixed, s.MinCards);
        if (choice == true && OverlayFits(columnHeight, roomsEmpty, s, gap))
            return new RoomsSpacePlan(RoomsActivityMode.Overlay, s.MinCards);
        return new RoomsSpacePlan(RoomsActivityMode.Folded, 0);
    }

    /// <summary>
    /// What the card laid over the list adds to the cards themselves: its padding (16 + 16), its
    /// rim (1 + 1) and the 16 px it keeps off the list's bottom edge — inside the Rooms panel's
    /// own padding.
    /// </summary>
    public const double OverlayChrome = 2 * PageSpacing.PanelPadding + 2 + PageSpacing.PanelPadding;

    /// <summary>
    /// Whether the cards can lie over the list at all: the rooms' row — the column less the
    /// folded block under it — must hold them whole. Too short, and they would be cut by the row
    /// they lie in, so folded it is. Never over an EMPTY list: its notice and its "Create room"
    /// are the thing to see there.
    /// </summary>
    private static bool OverlayFits(double columnHeight, bool roomsEmpty, ActivitySizes s, double gap)
    {
        if (roomsEmpty) return false;
        var roomsRow = columnHeight - (gap + s.Folded);
        return roomsRow >= s.MinCards + OverlayChrome;
    }

    /// <summary>A measurement is only taken when it is one; anything else is the reference.</summary>
    private static ActivitySizes Sane(ActivitySizes s)
    {
        static double Or(double v, double fallback) => double.IsFinite(v) && v > 0 ? v : fallback;
        return new ActivitySizes(
            Or(s.Chrome, ExpandedChrome),
            Or(s.MinCards, MinCardsHeight),
            Or(s.Folded, FoldedHeight));
    }

    /// <summary>
    /// 61's sizes for a page <paramref name="pageWidth"/> wide. Up to ~1300 px every value sits at
    /// its minimum (the laptop frame); they reach their maximums near 2700. Rounded on purpose —
    /// heights to whole pixels, the type to half a point — so a resize rebuilds the cards only
    /// when something visible changes. <paramref name="textFactor"/> is the launcher's text size.
    /// </summary>
    public static ActivityFluid Fluid(double pageWidth, double textFactor = 1.0)
    {
        var cqw = pageWidth > 0 && !double.IsNaN(pageWidth) && !double.IsInfinity(pageWidth)
            ? pageWidth / 100.0
            : 0;
        var f = textFactor > 0 ? textFactor : 1.0;
        double Type(double k, double min, double max) => HalfPoint(Math.Clamp(cqw * k, min, max) * f);
        double Px(double k, double min, double max) => Math.Round(Math.Clamp(cqw * k, min, max));
        return new ActivityFluid(
            TitleSize: Type(0.62, 14, 17),
            FactLabelSize: Type(0.42, 10, 12),
            FactValueSize: Type(0.55, 12.5, 15),
            // A fixed 44 (61a): the bars are a star row and fill whatever the card has; a minimum
            // that grew with the page only made the card taller than the fifth player's row.
            PeakBarsMin: 44,
            PeakGap: Px(0.3, 5, 8),
            PeakLineSize: Type(0.62, 12.5, 16),
            PeakSubSize: Type(0.48, 11, 12.5),
            // The space above a two-line match row: 6 on a laptop, up to 9 on a big screen (the
            // maintainer's correction to 61, whose rows were one line at a fixed height).
            MatchRowPadding: Px(0.35, 6, 9),
            MatchNameSize: Type(0.55, 12.5, 15),
            MatchSubSize: Type(0.48, 11, 13),
            RankRowHeight: Px(1.35, 26, 40),
            RankNameSize: Type(0.55, 12.5, 15),
            RankEloSize: Type(0.52, 12, 14),
            EmptyTitleSize: Type(0.7, 15, 19),
            EmptyBodySize: Type(0.55, 12.5, 15));
    }

    private static double HalfPoint(double size)
        => Math.Round(size * 2, MidpointRounding.AwayFromZero) / 2;

    // ------------------------------------------------------------------ the chat column (57)

    /// <summary>The chat column below <see cref="ChatWideFrom"/> px of page width (design 57).</summary>
    public const double ChatNarrow = 280;

    /// <summary>The chat column from <see cref="ChatWideFrom"/> px on.</summary>
    public const double ChatWide = 320;

    /// <summary>The folded chat: a 44-px rail (design 57c).</summary>
    public const double ChatRail = 44;

    /// <summary>The page width from which the chat takes <see cref="ChatWide"/>.</summary>
    public const double ChatWideFrom = 1600;

    /// <summary>
    /// The chat column's width (design 57): a fixed 280 below 1600 px of page width and 320 from
    /// there, or the 44-px rail when folded. It used to be a share of the width, so it measured
    /// ~390 px on a laptop and 12 % of a maximised window. An unmeasured page reads as narrow.
    /// </summary>
    public static double ChatWidth(double pageWidth, bool folded)
        => folded ? ChatRail : pageWidth >= ChatWideFrom ? ChatWide : ChatNarrow;
}
