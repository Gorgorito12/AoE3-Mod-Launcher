using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RankingTableLayout"/> — the one definition of the Clasificación table's
/// columns, and the rule that turns a rating into the bar beside it.
///
/// <para>The columns used to be a list of literals written twice, in the header builder and in
/// the row builder, kept in step by a comment in each asking the next reader to remember. The
/// two drifting apart misaligns every row in the table, and it is a break no compile can see
/// and no screenshot on a wide monitor shows.</para>
/// </summary>
public class RankingTableLayoutTests
{
    [Fact]
    public void EveryColumnIsDefinedExactlyOnce()
    {
        var columns = RankingTableLayout.All.Select(c => c.Column).ToList();
        Assert.Equal(columns.Count, columns.Distinct().Count());

        // Every value of the enum has a spec. A column added to the enum and forgotten here
        // would simply never be drawn, silently.
        foreach (RankingColumn column in System.Enum.GetValues<RankingColumn>())
            Assert.Contains(column, columns);
    }

    /// <summary>
    /// THE DECISION THAT MAKES A FULL-WIDTH TABLE READABLE: the surplus goes to RATING, and
    /// PLAYER is capped.
    ///
    /// <para>The page fills the window, so something has to absorb the extra width. With
    /// PLAYER flexible — the obvious reading of the handoff's fixed mockup — a 2000-px window
    /// puts the name hard left and its rating about 1500 px away, which is the complaint the
    /// whole rebuild started from. RATING's cell holds the comparative bar, so giving it the
    /// surplus lengthens a piece of data and literally draws the line between the name and its
    /// number.</para>
    /// </summary>
    [Fact]
    public void TheSurplusGoesToTheRatingBar_NotToThePlayersName()
    {
        var player = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Player);
        var rating = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Rating);

        Assert.Null(rating.FixedWidth);
        Assert.Null(rating.MaxWidth);

        Assert.Null(player.FixedWidth);
        Assert.True(player.MaxWidth is > 0,
            "PLAYER must be capped, or it takes the surplus and strands the rating.");
    }

    /// <summary>
    /// Exactly TWO columns are flexible, and the other four are fixed — the four that hold
    /// numbers, which have to line up under their headings down the whole table.
    /// </summary>
    [Fact]
    public void OnlyPlayerAndRatingStretch()
    {
        var flexible = RankingTableLayout.All
            .Where(c => c.FixedWidth == null)
            .Select(c => c.Column)
            .ToList();

        Assert.Equal(new[] { RankingColumn.Player, RankingColumn.Rating }, flexible);
    }

    /// <summary>
    /// The cap is generous enough that a narrow window never trips it: at the 900-px minimum
    /// the two flexible columns share about 289 px each, well under it. A cap that bound at
    /// normal widths would be a fixed column wearing a disguise.
    /// </summary>
    [Fact]
    public void ThePlayerCapDoesNotBindOnASmallWindow()
    {
        var fixedTotal = RankingTableLayout.All.Sum(c => c.FixedWidth ?? 0)
                       + RankingTableLayout.ColumnGap * (RankingTableLayout.All.Count - 1);

        // The narrowest window, less the tab's own side margins and the card's padding.
        const double narrowest = 900 - 28 - 28;
        var perFlexibleColumn = (narrowest - fixedTotal) / 2;

        var player = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Player);
        Assert.True(perFlexibleColumn < player.MaxWidth,
            $"at the narrowest window each flexible column gets {perFlexibleColumn:F0} px, "
            + $"which already meets the {player.MaxWidth} cap — so the cap is binding at "
            + "normal sizes and PLAYER is effectively a fixed column.");
    }

    /// <summary>
    /// The four data columns are right-aligned and the two identifying ones are not. A ragged
    /// right edge on numbers of different lengths is the reason a table like this is hard to
    /// read down, which is the whole complaint the redesign started from.
    /// </summary>
    [Fact]
    public void TheNumbersAreRightAligned()
    {
        foreach (var spec in RankingTableLayout.All)
        {
            var expected = spec.Column is RankingColumn.Decided
                                       or RankingColumn.Record
                                       or RankingColumn.Percent;
            Assert.Equal(expected, spec.RightAligned);
        }
        Assert.False(RankingTableLayout.All.First(c => c.Column == RankingColumn.Rating).RightAligned);
    }

    /// <summary>Every column has a heading, or it ships with a blank one nobody notices.</summary>
    [Fact]
    public void EveryColumnHasAHeaderKey()
    {
        foreach (var spec in RankingTableLayout.All)
            Assert.False(string.IsNullOrWhiteSpace(RankingTableLayout.HeaderKey(spec.Column)));
    }

    // ------------------------------------------------------------------ the bar

    /// <summary>
    /// THE CASE THE BAR EXISTS FOR. Ratings cluster in a narrow band, so measuring from ZERO
    /// would put every bar within a few percent of full and the column would be a row of
    /// identical stripes. Measured from the bottom of the table, the same four ratings spread
    /// out across it.
    /// </summary>
    [Fact]
    public void TheBarIsMeasuredFromTheTablesFloor_NotFromZero()
    {
        const double top = 1604;
        const double bottom = 1383;

        var first = RankingTableLayout.BarFraction(top, bottom, top);
        var middle = RankingTableLayout.BarFraction(1488, bottom, top);
        var last = RankingTableLayout.BarFraction(bottom, bottom, top);

        Assert.Equal(1.0, first);
        Assert.Equal(RankingTableLayout.MinBarFraction, last);

        // Measured from zero, 1488/1604 would be 0.93 — indistinguishable from the leader.
        Assert.InRange(middle, 0.4, 0.6);
    }

    /// <summary>
    /// The bottom row keeps a stub of bar. An empty one reads as "no rating", which is a
    /// different claim, and one the table cannot make about somebody who qualified for it.
    /// </summary>
    [Fact]
    public void TheLastPlaceStillHasABar()
    {
        Assert.True(RankingTableLayout.BarFraction(1000, 1000, 2000) > 0);
        Assert.Equal(RankingTableLayout.MinBarFraction,
                     RankingTableLayout.BarFraction(1000, 1000, 2000));
    }

    /// <summary>
    /// Degenerate tables do not divide by zero. One player, or a table where everybody is
    /// level, gives every bar the same full length — which is true.
    /// </summary>
    [Theory]
    [InlineData(1500, 1500, 1500)]
    [InlineData(1500, 1500, 1499)]
    public void ATableWithNoSpreadFillsEveryBar(double rating, double lowest, double highest)
    {
        Assert.Equal(1.0, RankingTableLayout.BarFraction(rating, lowest, highest));
    }

    /// <summary>Nothing may overflow its track.</summary>
    [Fact]
    public void TheBarNeverLeavesItsTrack()
    {
        foreach (var rating in new[] { 0.0, 900, 1383, 1500, 1604, 5000 })
        {
            var f = RankingTableLayout.BarFraction(rating, 1383, 1604);
            Assert.InRange(f, RankingTableLayout.MinBarFraction, 1.0);
        }
    }

    // --------------------------------------------------------------- the colour

    /// <summary>
    /// The three bands, at their boundaries. Colour is the only reason the percentage column
    /// earns its width — a table of bare percentages is read a row at a time.
    /// </summary>
    [Theory]
    [InlineData(100, "MpOkTextAlt")]
    [InlineData(62, "MpOkTextAlt")]
    [InlineData(50, "MpOkTextAlt")]
    [InlineData(49, "MpCaution")]
    [InlineData(30, "MpCaution")]
    [InlineData(29, "MpDestructiveText")]
    [InlineData(0, "MpDestructiveText")]
    public void ThePercentageIsColouredByBand(int percent, string expected)
    {
        Assert.Equal(expected, RankingTableLayout.PercentBrushKey(percent));
    }
}
