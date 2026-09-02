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
}
