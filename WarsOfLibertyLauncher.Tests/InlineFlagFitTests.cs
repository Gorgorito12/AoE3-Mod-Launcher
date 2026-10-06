using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
    // A picture's width for the pure cases below. Any value does; the match rows' real chip is
    // 18 px plus its 6-px gap, i.e. 24 (see AHiddenFlagStillCountsItsWidth).
    private const double Flag = 18;

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
    /// The decision is made on NOMINAL widths, so a flag that is not shown still counts what it
    /// takes — whichever way it is not shown. <see cref="InlineFlagFit"/> writes Hidden, which
    /// keeps the flag's width on the line; Collapsed (what it wrote from v1.0.15 to v1.0.15f)
    /// frees it.
    /// </summary>
    [Fact]
    public void AHiddenFlagStillCountsItsWidth()
    {
        var error = StaTestThread.Run(() =>
        {
            // The match rows' real chip (design 58b): 18 x 12, 6 px before the name.
            var chip = new Border
            {
                Width = WarsOfLibertyLauncher.Controls.MultiplayerTab.MatchFlagWidth,
                Height = WarsOfLibertyLauncher.Controls.MultiplayerTab.MatchFlagHeight,
                Margin = new Thickness(0, 0, WarsOfLibertyLauncher.Controls.MultiplayerTab.MatchFlagGap, 0),
            };
            Assert.Equal(24, RevealText.NominalWidth(chip));
            chip.Visibility = Visibility.Hidden;
            Assert.Equal(24, RevealText.NominalWidth(chip));
            chip.Visibility = Visibility.Collapsed;
            Assert.Equal(24, RevealText.NominalWidth(chip));
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the Rooms-page storm (v1.0.15 to v1.0.15f): deciding which flags
    /// to hide must never change the LAYOUT of the line it decides for.
    ///
    /// <para><see cref="InlineFlagFit.Apply"/> runs from the line's own <c>SizeChanged</c>. A
    /// picture switched to <c>Collapsed</c> takes its width off the line: the TextBlock's measure
    /// goes invalid and its trimmed width moves. In a match row a <c>FitStackPanel</c> had no room
    /// for, that width WAS the row's width (a 0x0 arrange slot is inflated to the row's own
    /// DesiredSize), so the row resized, <c>SizeChanged</c> ran Apply again, and the answer flipped
    /// back. WPF gave up after 153 passes a frame — "0/s layout passes" and ~250-ms frames, for
    /// hours, on a player's laptop. <c>Hidden</c> only stops drawing the picture: the line keeps
    /// its width, so nothing Apply does can feed back into the size it reacts to.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_HidingAFlagNeverInvalidatesTheLine()
    {
        var error = StaTestThread.Run(() =>
        {
            var line = new TextBlock
            {
                FontSize = 12.5,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            foreach (var name in new[] { "Geaf_Argento", "UnstoppableStreletsy", "El Taita", "Kaiser" })
            {
                line.Inlines.Add(new InlineUIContainer(new Border
                {
                    Width = MultiplayerTab.MatchFlagWidth,
                    Height = MultiplayerTab.MatchFlagHeight,
                    Margin = new Thickness(0, 0, MultiplayerTab.MatchFlagGap, 0),
                    Background = Brushes.SteelBlue,
                })
                {
                    BaselineAlignment = BaselineAlignment.Center,
                });
                line.Inlines.Add(new Run(name + "  "));
            }

            // The launcher's own text settings (App.OnAnyWindowLoaded): layout rounding and
            // Display formatting, which is what the player's window ran under.
            var host = new Border { UseLayoutRounding = true, Child = line };
            TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
            host.Measure(new Size(200, 40));
            host.Arrange(new Rect(0, 0, 200, 40));
            host.UpdateLayout();
            var before = line.DesiredSize;

            InlineFlagFit.Apply(line);

            var pictures = line.Inlines.OfType<InlineUIContainer>()
                .Select(c => (FrameworkElement)c.Child).ToList();
            // Not vacuous: four flags and four names do not fit 200 px, so the later flags go.
            Assert.Contains(pictures, p => p.Visibility != Visibility.Visible);
            Assert.True(line.IsMeasureValid,
                "hiding a flag invalidated its line's measure; from a SizeChanged handler that is a layout loop");
            host.UpdateLayout();
            Assert.Equal(before, line.DesiredSize);
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }
}
