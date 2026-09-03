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
        // A FIGURE is right-aligned, so a column of them can be compared down the page. The
        // name is not, and neither is the win bar: a bar is drawn from its own left edge and
        // right-aligning it would make every row start somewhere else.
        foreach (var spec in CivTableLayout.All)
        {
            bool isFigure = spec.Column is not (CivColumn.Civ or CivColumn.WinBar);
            Assert.Equal(isFigure, spec.RightAligned);
        }
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
    public void TheMatchupColumnsAreTheCivColumnsMinusTheOnesItHasNoDataFor()
    {
        var civ = CivTableLayout.All;
        var matchup = CivTableLayout.Matchups;

        // Two are dropped: the duration, because the mean length of one pairing over three
        // games is noise, and the win bar, because a matchup row already IS one side's record
        // against the other and a bar of it would draw the same figure twice.
        Assert.DoesNotContain(matchup, c => c.Column == CivColumn.Length);
        Assert.DoesNotContain(matchup, c => c.Column == CivColumn.WinBar);
        Assert.Equal(civ.Count - 2, matchup.Count);

        // Compared BY ROLE rather than by position, and that is the change the win bar forced:
        // it sits in the middle of the civ table, so the two lists no longer share an index for
        // the columns they do share. What still has to hold — and is the whole reason Matchups
        // is derived instead of written out again — is that a column present in both is the
        // same column: same width, same cap, same alignment. Two hand-written lists drifting on
        // one of those numbers reads as a rendering fault rather than as an edit.
        foreach (var spec in matchup)
        {
            var twin = civ.Single(c => c.Column == spec.Column);
            Assert.Equal(twin.FixedWidth, spec.FixedWidth);
            Assert.Equal(twin.MaxWidth, spec.MaxWidth);
            Assert.Equal(twin.RightAligned, spec.RightAligned);
        }

        // And in the same order, so neither table reads back to front.
        var sharedOrder = civ.Where(c => matchup.Any(m => m.Column == c.Column))
                             .Select(c => c.Column);
        Assert.Equal(sharedOrder, matchup.Select(m => m.Column));
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
