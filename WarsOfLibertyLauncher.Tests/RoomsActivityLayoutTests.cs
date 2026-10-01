using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// How the Rooms page splits its left column between the room list and the community panel
/// (design handoff turns 38-39).
///
/// <para>The rule turn 36 got wrong is the one these pin: the split depends on the CONTENT as
/// well as the window. One room leaves the panel everything below it; eight rooms stop the panel
/// at 248 px and the list scrolls. The panel never folds by itself because the window is small —
/// only because it cannot have its 248 px and still leave the list two rows.</para>
/// </summary>
public class RoomsActivityLayoutTests
{
    // A column like the handoff's 1380x860 frame: 716 tall. The rooms block's chrome (section
    // header + column header) is ~64 px and a compact row is 54 + 6.
    private const double Column = 716;
    private const double Chrome = 64;
    private const double Row = 60;
    private static double Natural(int rooms) => Chrome + rooms * Row;
    private static readonly double Minimum = Chrome + 2 * Row;

    [Fact]
    public void FewRooms_TheListMeasuresItsRowsAndThePanelFillsTheRest()
        => Assert.Equal(RoomsActivityMode.Fill,
            RoomsActivityLayout.Decide(Column, Natural(1), Minimum, choice: null, hasActivity: true));

    [Fact]
    public void ManyRooms_ThePanelStopsAt248AndTheListScrolls()
        => Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(Column, Natural(8), Minimum, choice: null, hasActivity: true));

    /// <summary>
    /// The handoff's own threshold: the moment the list would leave the panel less than 248 px,
    /// it is "many rooms". One pixel either side of it decides.
    /// </summary>
    [Fact]
    public void TheSwitchHappensExactlyWhereThePanelWouldGetLessThan248()
    {
        var natural = Column - RoomsActivityLayout.Gap - RoomsActivityLayout.ExpandedHeight;
        Assert.Equal(RoomsActivityMode.Fill,
            RoomsActivityLayout.Decide(Column, natural, Minimum, null, true));
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(Column, natural + 1, Minimum, null, true));
    }

    /// <summary>An explicit "Hide activity" is obeyed however much room there is.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void AnExplicitHideIsObeyedWhateverTheRoomCount(int rooms)
        => Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Decide(Column, Natural(rooms), Minimum, choice: false, hasActivity: true));

    /// <summary>
    /// With no choice made, a column too short to give the panel 248 px AND the list two rows
    /// opens folded — the only way the panel folds by itself.
    /// </summary>
    [Fact]
    public void NoChoiceAndAShortColumn_OpensFolded()
    {
        var shortColumn = Minimum + RoomsActivityLayout.Gap + RoomsActivityLayout.ExpandedHeight - 1;
        Assert.Equal(RoomsActivityMode.Folded,
            RoomsActivityLayout.Decide(shortColumn, Natural(1), Minimum, choice: null, hasActivity: true));
    }

    /// <summary>
    /// And an explicit "Show activity" opens it even there — the player asked; the list scrolls.
    /// </summary>
    [Fact]
    public void AnExplicitShowOpensItEvenInAShortColumn()
    {
        var shortColumn = Minimum + RoomsActivityLayout.Gap + RoomsActivityLayout.ExpandedHeight - 1;
        Assert.Equal(RoomsActivityMode.Fixed,
            RoomsActivityLayout.Decide(shortColumn, Natural(8), Minimum, choice: true, hasActivity: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void NothingToShow_TheListTakesTheWholeColumn(bool? choice)
        => Assert.Equal(RoomsActivityMode.None,
            RoomsActivityLayout.Decide(Column, Natural(1), Minimum, choice, hasActivity: false));

    /// <summary>
    /// Before the first layout the column has no height. That must not read as "too short" and
    /// fold a panel the player never hid.
    /// </summary>
    [Fact]
    public void BeforeLayout_ADefaultChoiceDoesNotFold()
    {
        Assert.Equal(RoomsActivityMode.Fixed, RoomsActivityLayout.Decide(0, 0, Minimum, null, true));
        Assert.Equal(RoomsActivityMode.Folded, RoomsActivityLayout.Decide(0, 0, Minimum, false, true));
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

    [Theory]
    [InlineData(double.NaN, 34)]
    [InlineData(0, 34)]
    [InlineData(150, 34)]   // the 248-px panel's card
    [InlineData(540, 59)]   // the handoff's few-rooms frame (~60)
    public void ThePeakBarsAre34TallAndGrowWithTheirCard(double cardHeight, double expected)
        => Assert.Equal(expected, ActivityFit.PeakBarsHeight(cardHeight));

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
