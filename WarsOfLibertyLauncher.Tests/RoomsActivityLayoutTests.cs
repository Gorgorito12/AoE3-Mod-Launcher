using System;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// How the Rooms page splits its left column between the room list and the community panel
/// (design handoff turns 38-40).
///
/// <para>Turn 40 is what these pin now: the open panel is 248 px WHATEVER the room count. Turn
/// 38a let it fill whatever a short list left over, so with no rooms it took the whole column
/// and twelve matches with it. The panel never folds by itself because the window is small —
/// only because it cannot have its 248 px and still leave the list two rows.</para>
/// </summary>
public class RoomsActivityLayoutTests
{
    // A column like the handoff's 1380x860 frame: 716 tall. The rooms block's chrome (section
    // header + column header) is ~64 px and a compact row is 54 + 6.
    private const double Column = 716;
    private const double Chrome = 64;
    private const double Row = 60;
    private static readonly double Minimum = Chrome + 2 * Row;

    /// <summary>
    /// THE ONE THAT MATTERS. Zero, one or eight rooms: the open panel is the same 248-px panel,
    /// and the spare height stays in the list (40a / 40b). There is no mode in which the panel
    /// grows with the space a short list leaves.
    /// </summary>
    [Theory]
    [InlineData(716)]
    [InlineData(1200)]   // a tall window: still 248, the list takes the rest
    public void THE_ONE_THAT_MATTERS_TheOpenPanelIsFixedHoweverMuchRoomThereIs(double column)
        => Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(column, Minimum, choice: null, hasActivity: true));

    /// <summary>An explicit "Hide activity" is obeyed however much room there is.</summary>
    [Fact]
    public void AnExplicitHideIsObeyed()
        => Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Decide(Column, Minimum, choice: false, hasActivity: true));

    /// <summary>
    /// With no choice made, a column too short to give the panel 248 px AND the list two rows
    /// opens folded — the only way the panel folds by itself. One pixel either side decides.
    /// </summary>
    [Fact]
    public void NoChoiceAndAShortColumn_OpensFolded()
    {
        var exact = Minimum + RoomsActivityLayout.Gap + RoomsActivityLayout.ExpandedHeight;
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Decide(exact - 1, Minimum, choice: null, hasActivity: true));
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(exact, Minimum, choice: null, hasActivity: true));
    }

    /// <summary>
    /// And an explicit "Show activity" opens it even there — the player asked; the list scrolls.
    /// </summary>
    [Fact]
    public void AnExplicitShowOpensItEvenInAShortColumn()
    {
        var shortColumn = Minimum + RoomsActivityLayout.Gap + RoomsActivityLayout.ExpandedHeight - 1;
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(shortColumn, Minimum, choice: true, hasActivity: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void NothingToShow_TheListTakesTheWholeColumn(bool? choice)
        => Assert.Equal(RoomsActivityMode.None,
            RoomsActivityLayout.Decide(Column, Minimum, choice, hasActivity: false));

    /// <summary>
    /// Before the first layout the column has no height. That must not read as "too short" and
    /// fold a panel the player never hid.
    /// </summary>
    [Fact]
    public void BeforeLayout_ADefaultChoiceDoesNotFold()
    {
        Assert.Equal(RoomsActivityMode.Fixed, RoomsActivityLayout.Decide(0, Minimum, null, true));
        Assert.Equal(RoomsActivityMode.Folded, RoomsActivityLayout.Decide(0, Minimum, false, true));
    }

    // ---- the open panel's height at larger text ----

    /// <summary>At the reference text size the capped rows fit, and the panel is the handoff's 248.</summary>
    [Fact]
    public void ThePanelIsNeverShorterThan248()
        => Assert.Equal(RoomsActivityLayout.ExpandedHeight,
            RoomsActivityLayout.ExpandedHeightFor((77, 170.4), (70, 158)));

    /// <summary>
    /// THE ONE THAT MATTERS. At 110 % text a match row is ~45 px, so four of them no longer fit
    /// 248 and the card showed three over an empty band (reported). The panel grows by exactly
    /// what the TALLER list needs — its chrome plus its natural height — and no more.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_LargerTextGrowsThePanelByWhatItsRowsNeed()
    {
        Assert.Equal(Math.Ceiling(77.4 + 180.2),
            RoomsActivityLayout.ExpandedHeightFor((77.4, 180.2), (70, 158)));
        Assert.Equal(Math.Ceiling(70 + 230.0),
            RoomsActivityLayout.ExpandedHeightFor((77.4, 180.2), (70, 230)));
    }

    /// <summary>A list that has not been laid out yet (or is folded away) says nothing.</summary>
    [Theory]
    [InlineData(double.NaN, 200)]
    [InlineData(0, 200)]
    [InlineData(-10, 400)]
    [InlineData(77, double.NaN)]
    [InlineData(double.PositiveInfinity, 200)]
    public void AListWithNoUsableMeasureIsIgnored(double chrome, double natural)
        => Assert.Equal(RoomsActivityLayout.ExpandedHeight,
            RoomsActivityLayout.ExpandedHeightFor((chrome, natural)));

    /// <summary>
    /// The open-by-default rule uses the panel's REAL height: a column that fits a 248-px panel
    /// beside two rows of rooms does not fit a taller one, and then it opens folded.
    /// </summary>
    [Fact]
    public void ATallerPanelFoldsAColumnThatOnlyFitsTheShorterOne()
    {
        var column = Minimum + RoomsActivityLayout.Gap + RoomsActivityLayout.ExpandedHeight;
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(column, Minimum, choice: null, hasActivity: true));
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Decide(column, Minimum, choice: null, hasActivity: true, expandedHeight: 270));
        // A smaller figure never shrinks the panel below the handoff's.
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(column, Minimum, choice: null, hasActivity: true, expandedHeight: 100));
    }

    // ---- the folded strip's segments ----

    /// <summary>
    /// Segments are never trimmed: when they do not fit, WHOLE segments leave — first the match
    /// count, then the last match — and the peak hours stay.
    /// </summary>
    [Fact]
    public void TheFoldedStripDropsWholeSegmentsInTheHandoffsOrder()
    {
        double[] widths = { 230, 260, 120 }; // peak, last match, match count
        int[] drop = { 2, 1 };

        Assert.Equal(new[] { true, true, true }, ActivityFit.VisibleSegments(700, widths, drop));
        Assert.Equal(new[] { true, true, false }, ActivityFit.VisibleSegments(500, widths, drop));
        Assert.Equal(new[] { true, false, false }, ActivityFit.VisibleSegments(300, widths, drop));
    }

    /// <summary>A segment the drop order does not name is never removed, even when it overflows.</summary>
    [Fact]
    public void ASegmentOutsideTheDropOrderIsNeverRemoved()
        => Assert.Equal(new[] { true, false, false },
            ActivityFit.VisibleSegments(50, new double[] { 230, 260, 120 }, new[] { 2, 1 }));

    /// <summary>An empty segment (width 0) is not shown, and does not count as one to drop.</summary>
    [Fact]
    public void AnEmptySegmentIsNotShown()
        => Assert.Equal(new[] { true, false, true },
            ActivityFit.VisibleSegments(400, new double[] { 230, 0, 120 }, new[] { 2, 1 }));

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
