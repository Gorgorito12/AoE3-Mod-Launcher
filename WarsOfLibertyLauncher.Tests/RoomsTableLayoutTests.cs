using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RoomsTableLayout"/> — the one definition of the rooms table's columns, and the
/// rule for which survive as the window narrows.
///
/// Two things matter here. The drop ORDER, because giving up the wrong column would take away the
/// reason the table exists; and the fact that the header and the rows now read the SAME list,
/// which is what keeps them aligned. Before this they were two sets of literals kept in step by a
/// comment asking the next reader to remember.
/// </summary>
public class RoomsTableLayoutTests
{
    /// <summary>Comfortably more than the sum of every comfort width.</summary>
    private const double Wide = 2000;

    /// <summary>About what the table gets in the smallest window the launcher allows.</summary>
    private const double Narrow = 480;

    [Fact]
    public void WithRoomToSpare_EveryColumnIsShown()
    {
        var kept = RoomsTableLayout.Resolve(Wide).Select(c => c.Column).ToList();

        Assert.Equal(RoomsTableLayout.All.Select(c => c.Column), kept);
        Assert.Empty(RoomsTableLayout.Hidden(Wide));
    }

    [Fact]
    public void ColumnsAreGivenUpLeastUsefulFirst()
    {
        // Walk the width down and record the order things disappear in.
        var order = new System.Collections.Generic.List<RoomColumn>();
        RoomColumn[] previous = RoomsTableLayout.Resolve(Wide).Select(c => c.Column).ToArray();

        for (var width = Wide; width >= 200; width -= 10)
        {
            var now = RoomsTableLayout.Resolve(width).Select(c => c.Column).ToArray();
            order.AddRange(previous.Except(now));
            previous = now;
        }

        Assert.Equal(new[] { RoomColumn.Ping, RoomColumn.Host, RoomColumn.Mod }, order);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(900)]
    [InlineData(700)]
    [InlineData(480)]
    [InlineData(200)]
    [InlineData(0)]
    public void TheReasonForTheTableSurvivesEveryWidth(double width)
    {
        // Which room, whether there is space in it, and the way in. Lose any of these and the
        // table stops being worth showing at all.
        var kept = RoomsTableLayout.Resolve(width).Select(c => c.Column).ToList();

        Assert.Contains(RoomColumn.Room, kept);
        Assert.Contains(RoomColumn.Players, kept);
        Assert.Contains(RoomColumn.Action, kept);
    }

    [Fact]
    public void ADroppedColumnIsReportedSoItsValueCanMoveToTheSubtitle()
    {
        // Nothing is meant to be LOST when a column goes — the row builder folds these into the
        // room's second line, so the pairing has to stay exact.
        var kept = RoomsTableLayout.Resolve(Narrow).Select(c => c.Column).ToHashSet();
        var hidden = RoomsTableLayout.Hidden(Narrow);

        Assert.NotEmpty(hidden);
        Assert.All(hidden, c => Assert.DoesNotContain(c, kept));
        Assert.Equal(RoomsTableLayout.All.Count, kept.Count + hidden.Count);
    }

    [Fact]
    public void HiddenColumnsComeBackInDisplayOrder()
    {
        // They are read out as a sentence in the subtitle, so the order has to be the one the
        // table itself uses, not the order they happened to be dropped in.
        var hidden = RoomsTableLayout.Hidden(Narrow);
        var expected = RoomsTableLayout.All.Select(c => c.Column).Where(hidden.Contains);

        Assert.Equal(expected, hidden);
    }

    [Fact]
    public void SameColumns_IsWhatLetsAResizeSkipRebuildingEveryRow()
    {
        var a = RoomsTableLayout.Resolve(1200);
        var b = RoomsTableLayout.Resolve(1180);   // different width, same set

        Assert.True(RoomsTableLayout.SameColumns(a, b));
        Assert.False(RoomsTableLayout.SameColumns(a, RoomsTableLayout.Resolve(Narrow)));
    }

    [Fact]
    public void EveryColumnIsDeclaredExactlyOnce()
    {
        var declared = RoomsTableLayout.All.Select(c => c.Column).ToList();

        Assert.Equal(declared.Count, declared.Distinct().Count());
        Assert.Equal(System.Enum.GetValues<RoomColumn>().Length, declared.Count);
    }
}
