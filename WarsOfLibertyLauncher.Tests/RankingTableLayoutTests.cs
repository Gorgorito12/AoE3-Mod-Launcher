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

    // ------------------------------------------------- the bar against the order

    /// <summary>
    /// One row of the live table: what the server sends, and what the ladder is ordered by.
    /// </summary>
    private sealed record Player(string Name, double Rating, double Rd);

    /// <summary>
    /// The live table the bug was reported from, in the order the server returned it. Ratings
    /// are the real ones; the deviations are the only shape consistent with that order — a
    /// player with two decided matches carries a far wider one than a player with thirty-five.
    ///
    /// <para>Note the third and fourth rows: 1643 above 1720. Any fixture whose ratings already
    /// descend would let a bar drawn from the rating pass this file, which is exactly how the
    /// defect survived.</para>
    /// </summary>
    private static readonly Player[] AsTheServerSentThem =
    {
        new("Geaf_Argento", 1571, 90),   // 35 decided
        new("Aluclown",     1519, 78),   // 44 decided
        new("NathanR06",    1643, 170),  //  5 decided
        new("AleReis",      1720, 215),  //  2 decided
        new("Gommiustan",   1626, 172),  //  8 decided
        new("alexari2040",  1662, 260),  //  1 decided
    };

    /// <summary>
    /// THE ONE THAT MATTERS. The table is ordered by the conservative rating, so the bar beside
    /// each rating must be drawn from THAT and not from the rating — otherwise the longest bar
    /// on the page sits in fourth place and the table reads as mismeasured, which is precisely
    /// what was reported.
    ///
    /// <para>This is the bug written down: fed the raw rating it fails on the real data, and no
    /// amount of restating the intent in a doc comment made it pass. Both of this class's own
    /// comments already claimed the bar "makes the order legible" while it was wired to the one
    /// number that does not descend.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheBarNeverContradictsTheOrder()
    {
        var values = AsTheServerSentThem
            .Select(p => RankingTableLayout.ConservativeRating(p.Rating, p.Rd))
            .ToList();

        var lowest = values.Min();
        var highest = values.Max();

        var bars = values.Select(v => RankingTableLayout.BarFraction(v, lowest, highest)).ToList();

        for (var i = 1; i < bars.Count; i++)
        {
            Assert.True(
                bars[i] <= bars[i - 1] + 1e-9,
                $"row {i} ({AsTheServerSentThem[i].Name}) draws a longer bar than the row above it "
                + $"({AsTheServerSentThem[i - 1].Name}): {bars[i]:P1} vs {bars[i - 1]:P1}. The bar must "
                + "be fed the conservative rating, which is what the table is ordered by.");
        }
    }

    /// <summary>
    /// The half of the same defect that is invisible in a monotonicity check: the leader must
    /// own the full bar. Drawn from the rating, the full bar went to whoever had the highest
    /// number regardless of where the table had placed him.
    /// </summary>
    [Fact]
    public void TheFullBarBelongsToTheTopOfTheTable()
    {
        var values = AsTheServerSentThem
            .Select(p => RankingTableLayout.ConservativeRating(p.Rating, p.Rd))
            .ToList();

        Assert.Equal(1.0, RankingTableLayout.BarFraction(values[0], values.Min(), values.Max()));

        // And the player with the biggest RATING is not the one who gets it.
        var loudest = AsTheServerSentThem.OrderByDescending(p => p.Rating).First();
        Assert.Equal("AleReis", loudest.Name);
        Assert.NotEqual("AleReis", AsTheServerSentThem[0].Name);
    }

    /// <summary>
    /// The launcher's arithmetic is the backend's. `LADDER_ORDER_BY` is
    /// <c>(e.rating - 2 * e.rd) DESC</c> and `conservativeRating` is the same expression in JS;
    /// this is the third copy and the one that decides what a player SEES, so it is pinned
    /// against the very fixture the backend's own `ladder.test.ts` uses. If the two ever
    /// disagree the bars stop matching the order again, and nothing else would notice.
    /// </summary>
    [Fact]
    public void ConservativeRatingIsTheSameExpressionTheServerOrdersBy()
    {
        // The live table from src/stats/ladder.test.ts, ratings and deviations verbatim.
        var live = new[]
        {
            new Player("Gommiustan",   1626.34, 248.23),
            new Player("Aluclown",     1603.58, 125.29),
            new Player("Geaf_Argento", 1509.62, 132.88),
            new Player("Gorgorito12",  1383.36, 286.93),
        };

        Assert.Equal(
            new[] { "Aluclown", "Geaf_Argento", "Gommiustan", "Gorgorito12" },
            live.OrderByDescending(p => RankingTableLayout.ConservativeRating(p.Rating, p.Rd))
                .Select(p => p.Name)
                .ToArray());

        // Same rating, more doubt, lower place — the property the whole ordering rests on.
        Assert.True(
            RankingTableLayout.ConservativeRating(1600, 60)
            > RankingTableLayout.ConservativeRating(1600, 300));

        // The coefficient is 2, not 1: at one deviation Gommiustan moves up a place.
        Assert.Equal(1600 - 2 * 60, RankingTableLayout.ConservativeRating(1600, 60));
    }

    /// <summary>
    /// A backend that never sent <c>rd</c> deserialises it as 0, and the bar then collapses to
    /// the rating — byte-for-byte today's behaviour, and the correct bar for a server that is
    /// ordering by the rating for the same reason.
    /// </summary>
    [Fact]
    public void WithNoDeviationTheBarIsTheRating()
    {
        Assert.Equal(1500, RankingTableLayout.ConservativeRating(1500, 0));
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
