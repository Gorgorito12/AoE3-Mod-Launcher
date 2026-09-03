using System;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The civilization table's column definition — the one place its header and its rows agree on
/// where a column is. A drift between the two misaligns every row, and it is a break no compile
/// step can see and no screenshot on a wide monitor shows.
/// </summary>
public class CivTableLayoutTests
{
    /// <summary>
    /// Every column the enum declares is in the table, and exactly once. A value added later and
    /// forgotten here would simply never be drawn.
    /// </summary>
    [Fact]
    public void EveryColumnIsInTheTableExactlyOnce()
    {
        var declared = Enum.GetValues<CivColumn>();
        var laid = CivTableLayout.All.Select(c => c.Column).ToList();

        Assert.Equal(declared.Length, laid.Count);
        Assert.Equal(declared.Length, laid.Distinct().Count());
        foreach (var column in declared) Assert.Contains(column, laid);
    }

    /// <summary>
    /// <b>Exactly one flexible column</b>, and it is the NAME. Two would split the surplus and
    /// neither would reach its cap; none would leave the table a fixed width in a window that is
    /// not, stranding it against the left edge.
    ///
    /// <para>It is the name and not a count for a reason worth keeping: the ladder gives its
    /// surplus to RATING because that cell carries the comparative bar, so widening it lengthens
    /// a piece of data. This table has no bar and its numbers are short and fixed, so the only
    /// thing worth growing is the name.</para>
    /// </summary>
    [Fact]
    public void OnlyTheNameColumnIsFlexible()
    {
        var flexible = CivTableLayout.All.Where(c => c.FixedWidth == null).ToList();

        var only = Assert.Single(flexible);
        Assert.Equal(CivColumn.Civ, only.Column);
        Assert.NotNull(only.MaxWidth);
    }

    /// <summary>
    /// Every header key resolves. A missing one renders as the key itself — the build stays green
    /// and the table grows a column headed "MpCivColPlayed".
    /// </summary>
    [Fact]
    public void EveryHeaderKeyExists()
    {
        foreach (var column in Enum.GetValues<CivColumn>())
        {
            var key = CivTableLayout.HeaderKey(column);
            Assert.False(string.IsNullOrEmpty(key));
            Assert.NotEqual(key, Strings.Get(key));
        }
    }

    /// <summary>
    /// The counts are right-aligned and the name is not — a column of numbers that does not line
    /// up on its last digit cannot be read down the page.
    /// </summary>
    [Fact]
    public void TheNumbersAreRightAlignedAndTheNameIsNot()
    {
        foreach (var spec in CivTableLayout.All)
            Assert.Equal(spec.Column != CivColumn.Civ, spec.RightAligned);
    }

    [Fact]
    public void TheGapIsARealGap() => Assert.True(CivTableLayout.ColumnGap > 0);

    /// <summary>
    /// The matchup table's columns are DERIVED from the civ table's, not written out again.
    ///
    /// <para>The two tables are stacked on the same page, so their columns have to line up. With
    /// two hand-written lists a later tweak to one width misaligns them by a few pixels — which
    /// reads as a rendering fault rather than as an edit somebody made, and nothing would fail.</para>
    /// </summary>
    [Fact]
    public void TheMatchupColumnsAreTheCivColumnsMinusTheDuration()
    {
        var civ = CivTableLayout.All;
        var matchup = CivTableLayout.Matchups;

        Assert.Equal(civ.Count - 1, matchup.Count);
        Assert.DoesNotContain(matchup, c => c.Column == CivColumn.Length);

        // Same order, same widths, same alignment — one by one, because "the same count" would
        // pass over a list that had been re-typed with a different number in it.
        for (var i = 0; i < matchup.Count; i++)
        {
            Assert.Equal(civ[i].Column, matchup[i].Column);
            Assert.Equal(civ[i].FixedWidth, matchup[i].FixedWidth);
            Assert.Equal(civ[i].MaxWidth, matchup[i].MaxWidth);
            Assert.Equal(civ[i].RightAligned, matchup[i].RightAligned);
        }
    }

    /// <summary>
    /// Only the first header differs: that cell holds a PAIR, and "CIVILIZATION" over
    /// "Chinese vs Ottomans" would be describing half of it.
    /// </summary>
    [Fact]
    public void OnlyTheFirstMatchupHeaderDiffersFromTheCivTable()
    {
        Assert.NotEqual(CivTableLayout.HeaderKey(CivColumn.Civ),
                        CivTableLayout.MatchupHeaderKey(CivColumn.Civ));

        foreach (var column in new[] { CivColumn.Played, CivColumn.Record, CivColumn.Percent })
        {
            Assert.Equal(CivTableLayout.HeaderKey(column),
                         CivTableLayout.MatchupHeaderKey(column));
        }

        // Every key it can hand out has to resolve, or a header renders as its own key name.
        foreach (var spec in CivTableLayout.Matchups)
        {
            var key = CivTableLayout.MatchupHeaderKey(spec.Column);
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.NotEqual(key, Strings.Get(key));
        }
    }
}
