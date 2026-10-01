using System;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which pictures embedded in a trimmed line stay visible (<see cref="InlineFlagFit"/>).
///
/// <para>A community match row puts each player's flag before the name. When a four-player
/// match did not fit, WPF cut the names and went on drawing the flags past the "…", so a cut
/// name was followed by the NEXT player's flag (reported with a screenshot).</para>
/// </summary>
public class InlineFlagFitTests
{
    private const double Flag = 18; // 14 px chip + 4 px gap

    [Fact]
    public void ALineThatFitsShowsEveryFlag()
        => Assert.All(
            InlineFlagFit.VisibleObjects(new[] { (true, Flag), (false, 60.0), (true, Flag), (false, 40.0) }, 200, 10),
            Assert.True);

    /// <summary>
    /// THE ONE THAT MATTERS: the flags BEFORE the ellipsis stay, the ones after it go — "Kaise…"
    /// must not be followed by the next player's flag.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_FlagsPastTheCutAreHidden()
    {
        // flag 0-18, text 18-78, flag 78-96, text 96-176, flag 176-194, text 194-284.
        var items = new[]
        {
            (true, Flag), (false, 60.0), (true, Flag), (false, 80.0), (true, Flag), (false, 90.0),
        };
        var shown = InlineFlagFit.VisibleObjects(items, available: 200, ellipsisWidth: 10);

        Assert.True(shown[0]);
        Assert.True(shown[2]);
        Assert.False(shown[4]); // ends at 194, past the cut at 190
        // Text is never the question.
        Assert.True(shown[1] && shown[3] && shown[5]);
    }

    /// <summary>Half a flag beside "…" reads as damage, so a flag the cut runs through goes too.</summary>
    [Fact]
    public void AFlagTheCutRunsThroughIsHidden()
    {
        var items = new[] { (false, 180.0), (true, Flag), (false, 50.0) };
        var shown = InlineFlagFit.VisibleObjects(items, available: 200, ellipsisWidth: 12);
        Assert.False(shown[1]); // 180-198 against a cut at 188
    }

    /// <summary>No width yet (before the first layout) decides nothing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void NoWidthHidesNothing(double available)
        => Assert.All(InlineFlagFit.VisibleObjects(new[] { (false, 500.0), (true, Flag) }, available, 10), Assert.True);

    /// <summary>
    /// The decision is made on NOMINAL widths, so a flag that is currently hidden still counts
    /// what it would take — otherwise hiding it would free the room that shows it again, and
    /// the line would flip back and forth on every layout pass.
    /// </summary>
    [Fact]
    public void AHiddenFlagStillCountsItsWidth()
    {
        var error = StaTestThread.Run(() =>
        {
            var chip = new Border { Width = 14, Height = 10, Margin = new Thickness(0, 0, 4, 0) };
            Assert.Equal(18, RevealText.NominalWidth(chip));
            chip.Visibility = Visibility.Collapsed;
            Assert.Equal(18, RevealText.NominalWidth(chip));
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }
}
