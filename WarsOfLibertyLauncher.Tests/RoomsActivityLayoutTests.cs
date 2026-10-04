using System;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// How the Rooms page shares its left column (designs 57b and 61): first the rooms — at least four
/// rows, all of them when fewer, or the empty notice — then the community block, ANCHORED AT THE
/// BOTTOM: about a third of the column when it fits, shrunk toward its minimum when it does not,
/// folded to its header line when even that does not, and the cards OVER the list when the player
/// asked for them. The rooms row is always the star — the block is always at the bottom.
///
/// <para>What these replace: 60 put the block straight under a list that shrank to its notice, so
/// on a big screen everything bunched up at the top over a huge gap; and on a laptop its two-line
/// data strip left the rooms a single row.</para>
/// </summary>
public class RoomsActivityLayoutTests
{
    // 57a's laptop: a compact row is 54 + 6, the card's chrome (title, column headings, padding)
    // about 70.
    private const double Chrome = 70;
    private const double Row = 54;
    private const double Gap = RoomsActivityLayout.Gap;
    private static readonly double Four = RoomsActivityLayout.RoomsMinHeight(Chrome, 8, Row);
    private static readonly ActivitySizes Reference = ActivitySizes.Reference;

    /// <summary>Four rows, or all of them when there are fewer — and never more than four.</summary>
    [Theory]
    [InlineData(0, 70)]
    [InlineData(1, 70 + 60)]
    [InlineData(3, 70 + 3 * 60)]
    [InlineData(4, 70 + 4 * 60)]
    [InlineData(9, 70 + 4 * 60)]
    public void TheRoomsKeepFourRowsOrAllOfThemWhenFewer(int rows, double expected)
        => Assert.Equal(expected, RoomsActivityLayout.RoomsMinHeight(Chrome, rows, Row));

    /// <summary>
    /// With no gap given, the plan uses the tab's narrow G (<see cref="PageSpacing"/>); 61's 10 gave
    /// way to the maintainer's one-gap rule.
    /// </summary>
    [Fact]
    public void TheDefaultGapIsTheNarrowPageGap() => Assert.Equal(PageSpacing.Narrow, RoomsActivityLayout.Gap);

    /// <summary>
    /// THE ONE THAT MATTERS for the maintainer's "the limit should be the 5th best player": the cards
    /// are their height — the Ranking card with five rows — on a laptop column AND on a tall one. A
    /// tall column gives the rest to the rooms, never a band of nothing under the fifth player.
    /// </summary>
    [Theory]
    [InlineData(682)]
    [InlineData(900)]
    [InlineData(1274)]
    [InlineData(2000)]
    public void THE_ONE_THAT_MATTERS_TheCardsEndAtTheFifthPlayerOnAnyColumn(double column)
    {
        var plan = RoomsActivityLayout.Plan(column, Four, roomsEmpty: false, hasActivity: true, choice: null);
        Assert.Equal(RoomsActivityMode.Fixed, plan.Mode);
        Assert.Equal(Reference.MinCards, plan.CardsHeight);

        var empty = RoomsActivityLayout.Plan(column, 130, roomsEmpty: true, hasActivity: true, choice: null);
        Assert.Equal(Reference.MinCards, empty.CardsHeight);
    }

    /// <summary>The measured height decides, and one pixel short of it the block folds.</summary>
    [Fact]
    public void OnePixelShortOfTheCardsHeightFolds()
    {
        var sizes = new ActivitySizes(62, 150, 50);
        var roomsMin = 640.0;
        var exact = roomsMin + Gap + sizes.Chrome + sizes.MinCards;
        var plan = RoomsActivityLayout.Plan(exact, roomsMin, false, true, null, sizes);
        Assert.Equal(RoomsActivityMode.Fixed, plan.Mode);
        Assert.Equal(150, plan.CardsHeight);

        var tight = RoomsActivityLayout.Plan(exact - 1, roomsMin, false, true, null, sizes);
        Assert.Equal(RoomsActivityMode.Folded, tight.Mode);
        Assert.Equal(0, tight.CardsHeight);
    }

    /// <summary>
    /// On a short laptop column with eight rooms the block folds: the rooms come first, and their
    /// four rows plus the folded header line fit.
    /// </summary>
    [Fact]
    public void OnAShortLaptopColumnTheRoomsKeepFourRowsAndTheBlockFolds()
    {
        var column = 520.0;
        var plan = RoomsActivityLayout.Plan(column, Four, roomsEmpty: false, hasActivity: true, choice: null);
        Assert.Equal(RoomsActivityMode.Folded, plan.Mode);
        Assert.True(Four + Gap + RoomsActivityLayout.FoldedHeight <= column);
    }

    /// <summary>The block's MEASURED minimum decides, not the reference: a taller minimum folds the same column.</summary>
    [Fact]
    public void TheMeasuredMinimumDecides()
    {
        var column = Four + Gap + Reference.Chrome + Reference.MinCards;
        Assert.Equal(RoomsActivityMode.Fixed, RoomsActivityLayout.Plan(column, Four, false, true, null, Reference).Mode);
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Plan(column, Four, false, true, null, Reference with { MinCards = Reference.MinCards + 40 }).Mode);
    }

    /// <summary>An explicit "Hide" is obeyed however much room there is.</summary>
    [Fact]
    public void AnExplicitHideIsObeyed()
    {
        var plan = RoomsActivityLayout.Plan(2000, Four, false, true, choice: false);
        Assert.Equal(RoomsActivityMode.Folded, plan.Mode);
        Assert.Equal(0, plan.CardsHeight);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for 57a's "Show activity opens the cards on top of the list": the
    /// player asked for the cards and even the minimum does not fit, so they are laid OVER the list
    /// at their minimum — never taken from the rooms' four rows.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AShownPanelThatDoesNotFitOpensOverTheList()
    {
        var plan = RoomsActivityLayout.Plan(520, Four, false, true, choice: true);
        Assert.Equal(RoomsActivityMode.Overlay, plan.Mode);
        Assert.Equal(Reference.MinCards, plan.CardsHeight);
        Assert.Equal(RoomsActivityMode.Fixed, RoomsActivityLayout.Plan(1200, Four, false, true, choice: true).Mode);
    }

    /// <summary>
    /// The cards only lie over the list when its row can hold them WHOLE — the column less the
    /// FOLDED block under it, as measured; never over an empty list, whose notice and "Create
    /// room" are the thing to see.
    /// </summary>
    [Fact]
    public void TheCardsNeverLieOverARowTooShortToHoldThem()
    {
        // A column too short for the block beside four rooms, but tall enough for the overlay.
        var justEnough = Reference.MinCards + RoomsActivityLayout.OverlayChrome + Gap + Reference.Folded;
        var big = 2000.0; // rooms that take everything, so only the overlay is in question
        Assert.Equal(RoomsActivityMode.Overlay,
            RoomsActivityLayout.Plan(justEnough, big, false, true, choice: true).Mode);
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Plan(justEnough - 1, big, false, true, choice: true).Mode);

        // A folded block that measures taller leaves less for the cards above it.
        var taller = Reference with { Folded = Reference.Folded + 30 };
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Plan(justEnough, big, false, true, choice: true, taller).Mode);

        // An empty list never takes them.
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Plan(justEnough, big, roomsEmpty: true, true, choice: true).Mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void NothingToShow_NoBlock(bool? choice)
        => Assert.Equal(RoomsActivityMode.None,
            RoomsActivityLayout.Plan(1000, Four, false, hasActivity: false, choice).Mode);

    /// <summary>
    /// Before the first layout the column has no height. That must not read as "too short" and
    /// fold a block the player never hid.
    /// </summary>
    [Fact]
    public void BeforeLayout_ADefaultChoiceDoesNotFold()
    {
        Assert.Equal(RoomsActivityMode.Fixed, RoomsActivityLayout.Plan(0, Four, false, true, null).Mode);
        Assert.Equal(RoomsActivityMode.Folded, RoomsActivityLayout.Plan(0, Four, false, true, false).Mode);
    }

    /// <summary>A measurement is only taken when it is one; anything else is the reference.</summary>
    [Fact]
    public void ANonsenseMeasurementFallsBackToTheReference()
    {
        var nonsense = new ActivitySizes(double.NaN, 0, -3);
        Assert.Equal(RoomsActivityLayout.Plan(1274, 130, true, true, null),
            RoomsActivityLayout.Plan(1274, 130, true, true, null, nonsense));
    }

    // ---- 61's page-following sizes ----

    /// <summary>
    /// Before anything is measured the cards' height is the Ranking card's five rows at the page's
    /// row height — the fifth player is the limit — never below the laptop's 186.
    /// </summary>
    [Theory]
    [InlineData(1300, 1.0)]
    [InlineData(2560, 1.0)]
    [InlineData(2560, 1.25)]
    public void TheEstimateIsTheRankingsFiveRows(double width, double text)
    {
        var f = RoomsActivityLayout.Fluid(width, text);
        var five = RoomsActivityLayout.RankingCardChrome + 5 * f.RankRowHeight + 4 * RoomsActivityLayout.RankRowSpacing;
        Assert.Equal(Math.Max(RoomsActivityLayout.MinCardsHeight, Math.Ceiling(five)), RoomsActivityLayout.EstimateMinCards(f));
    }

    /// <summary>On 61a's laptop frame every size sits at its minimum: 6 px over a match, 26-px ranks; the bars' minimum is 44.</summary>
    [Fact]
    public void OnALaptopEverySizeIsItsMinimum()
    {
        var f = RoomsActivityLayout.Fluid(1300);
        Assert.Equal(14, f.TitleSize);
        Assert.Equal(10, f.FactLabelSize);
        Assert.Equal(12.5, f.FactValueSize);
        Assert.Equal(44, f.PeakBarsMin);
        Assert.Equal(6, f.MatchRowPadding);
        Assert.Equal(26, f.RankRowHeight);
    }

    /// <summary>On 61b's big frame they have grown, but never past their maximums.</summary>
    [Fact]
    public void OnABigScreenTheSizesGrowAndStopAtTheirMaximums()
    {
        var f = RoomsActivityLayout.Fluid(2560);
        Assert.Equal(16, f.TitleSize);        // .62 · 25.6 = 15.9 → 16
        Assert.Equal(9, f.MatchRowPadding);   // .35 · 25.6 = 8.96 → 9
        Assert.Equal(35, f.RankRowHeight);    // 1.35 · 25.6 = 34.6 → 35
        Assert.Equal(44, f.PeakBarsMin);      // fixed: the bars fill the card, the fifth player sets its height

        var huge = RoomsActivityLayout.Fluid(10000);
        Assert.Equal(17, huge.TitleSize);
        Assert.Equal(9, huge.MatchRowPadding);
        Assert.Equal(40, huge.RankRowHeight);
        Assert.Equal(44, huge.PeakBarsMin);
    }

    /// <summary>The type follows the launcher's text size; the heights are minimums and do not.</summary>
    [Fact]
    public void TheTypeFollowsTheTextSize()
    {
        var f = RoomsActivityLayout.Fluid(1300, 1.2);
        Assert.Equal(17, f.TitleSize);          // 14 · 1.2 = 16.8 → 17
        Assert.Equal(15, f.FactValueSize);      // 12.5 · 1.2
        Assert.Equal(6, f.MatchRowPadding);
        Assert.Equal(RoomsActivityLayout.Fluid(0), RoomsActivityLayout.Fluid(double.NaN));
    }

    // ---- the chat column (57) ----

    /// <summary>280 below 1600 px of page width, 320 from there, the 44-px rail when folded.</summary>
    [Theory]
    [InlineData(0, false, 280)]
    [InlineData(1366, false, 280)]
    [InlineData(1599.5, false, 280)]
    [InlineData(1600, false, 320)]
    [InlineData(2560, false, 320)]
    [InlineData(1366, true, 44)]
    [InlineData(2560, true, 44)]
    public void TheChatIs280Or320OrTheRail(double page, bool folded, double expected)
        => Assert.Equal(expected, RoomsActivityLayout.ChatWidth(page, folded));

    // ---- FitStackPanel ----

    /// <summary>
    /// THE ONE THAT MATTERS for the cards: a list never shows a row cut in half. A row that does
    /// not fit WHOLE is not shown at all.
    /// </summary>
    [Fact]
    public void AListShowsOnlyRowsThatFitWhole()
    {
        double[] rows = { 50, 50, 50, 50 };
        Assert.Equal(3, FitStackPanel.CountThatFit(rows, spacing: 0, available: 175));
        Assert.Equal(2, FitStackPanel.CountThatFit(rows, spacing: 10, available: 165));
        Assert.Equal(4, FitStackPanel.CountThatFit(rows, spacing: 2, available: 206));
    }

    [Fact]
    public void LayoutRoundingDoesNotCostAnExactFit()
        => Assert.Equal(2, FitStackPanel.CountThatFit(new double[] { 50, 50 }, 0, 99.8));

    [Fact]
    public void ACollapsedChildIsSkippedAndNeverCounted()
        => Assert.Equal(2, FitStackPanel.CountThatFit(new double[] { 50, -1, 50 }, 10, 110));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void NoRoomShowsNothing(double available)
        => Assert.Equal(0, FitStackPanel.CountThatFit(new double[] { 10 }, 0, available));
}
