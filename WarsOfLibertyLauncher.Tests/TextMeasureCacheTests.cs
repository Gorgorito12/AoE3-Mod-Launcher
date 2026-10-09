using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The text measurements behind the hover reveal and the inline flags: taken in the block's OWN
/// formatting mode, and taken once per content rather than on every SizeChanged and Loaded.
/// </summary>
[Collection("wpf-and-language")]
public class TextMeasureCacheTests
{
    private const string Sentence = "Gommiustan beat Aluclown · ESOC Fertile Crescent · 24 min · COMPETITIVE 1v1";

    private static TextBlock Block(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static double Measured(TextBlock tb, TextFormattingMode mode)
        => RevealText.MeasureOne(tb.Text, tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch,
            tb.FontSize, tb.FlowDirection, VisualTreeHelper.GetDpi(tb).PixelsPerDip, mode);

    /// <summary>
    /// THE ONE THAT MATTERS: what MeasureOne answers is what the block draws, in each mode. It
    /// measured in Ideal while the launcher draws in Display, so the cut, the reveal and the
    /// hidden flags were all decided a few pixels off.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_MeasureOneAgreesWithTheBlockItMeasures()
    {
        var error = StaTestThread.Run(() =>
        {
            foreach (var mode in new[] { TextFormattingMode.Display, TextFormattingMode.Ideal })
            {
                var tb = Block(Sentence);
                RevealText.SetEnabled(tb, false);
                var host = new Border { UseLayoutRounding = true, Child = tb };
                TextOptions.SetTextFormattingMode(host, mode);
                host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var drawn = tb.DesiredSize.Width;
                var dip = 1.0 / VisualTreeHelper.GetDpi(tb).PixelsPerDip;
                Assert.True(Math.Abs(Measured(tb, mode) - drawn) <= dip + 0.01,
                    $"{mode}: measured {Measured(tb, mode):0.##} against a drawn {drawn:0.##}");
            }

            // Not vacuous: the two modes really do disagree about this sentence by more than a
            // pixel and a half, so measuring in the wrong one decides a cut wrongly.
            var probe = Block(Sentence);
            Assert.True(Math.Abs(Measured(probe, TextFormattingMode.Display) - Measured(probe, TextFormattingMode.Ideal)) > 1.5,
                "Display and Ideal agree on this sentence, so the test proves nothing");
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    private static TextBlock FlaggedLine()
    {
        var line = Block("");
        line.Text = null;
        foreach (var name in new[] { "Geaf_Argento", "UnstoppableStreletsy", "El Taita", "Kaiser" })
        {
            line.Inlines.Add(new InlineUIContainer(new Border
            {
                Width = 18, Height = 12, Margin = new Thickness(0, 0, 6, 0), Background = Brushes.SteelBlue,
            }));
            line.Inlines.Add(new Run(name + "  "));
        }
        return line;
    }

    private static void Lay(FrameworkElement host, double width)
    {
        host.Measure(new Size(width, 40));
        host.Arrange(new Rect(0, 0, width, 40));
        host.UpdateLayout();
    }

    [Fact]
    public void ALoadedAfterTheSizeChangeDoesNotMeasureAgain()
    {
        var error = StaTestThread.Run(() =>
        {
            var line = FlaggedLine();
            RevealText.SetEnabled(line, false);
            var host = new Border { UseLayoutRounding = true, Child = line };
            Lay(host, 200);

            InlineFlagFit.Apply(line);
            var measured = PerfCounters.CountersSnapshot().GetValueOrDefault("InlineFlagFit.Measure");
            InlineFlagFit.Apply(line);   // a Loaded after the SizeChanged, same width
            Assert.Equal(measured, PerfCounters.CountersSnapshot().GetValueOrDefault("InlineFlagFit.Measure"));
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    [Fact]
    public void AWidthChangeIsDecidedFromTheCachedWidths()
    {
        var error = StaTestThread.Run(() =>
        {
            var line = FlaggedLine();
            RevealText.SetEnabled(line, false);
            var host = new Border { UseLayoutRounding = true, Child = line };
            Lay(host, 200);
            InlineFlagFit.Apply(line);
            var measured = PerfCounters.CountersSnapshot().GetValueOrDefault("InlineFlagFit.Measure");

            Lay(host, 600);
            InlineFlagFit.Apply(line);
            Assert.Equal(measured, PerfCounters.CountersSnapshot().GetValueOrDefault("InlineFlagFit.Measure"));
            // And it really re-decided: at 600 every flag fits again.
            foreach (var inline in line.Inlines)
                if (inline is InlineUIContainer { Child: FrameworkElement p })
                    Assert.Equal(Visibility.Visible, p.Visibility);
        }, TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    // ---------------------------------------------------------------- the reveal, by reference

    private static (TextBlock Text, Border Card) RevealedCard(double width)
    {
        var text = Block("Mapa mas jugado: ESOC Fertile Crescent");
        var card = new Border
        {
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            Child = text,
        };
        Lay(card, width);
        text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return (text, card);
    }

    [Fact]
    public void ALoadedAfterLayoutKeepsTheSameReveal()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (text, _) = RevealedCard(90);
            var armed = Assert.IsType<ToolTip>(text.ToolTip);
            text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Same(armed, text.ToolTip);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AResizeThatKeepsTheLineCutKeepsTheSameReveal()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (text, card) = RevealedCard(90);
            var armed = Assert.IsType<ToolTip>(text.ToolTip);
            Lay(card, 110);
            Assert.True(text.ActualWidth > 95, "the resize did not happen");
            Assert.Same(armed, text.ToolTip);

            // And a resize that lets it fit takes the reveal away.
            Lay(card, 600);
            Assert.Null(text.ToolTip);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AMoveUnderAnotherBackdropRebuildsTheReveal()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var (text, card) = RevealedCard(90);
            var armed = Assert.IsType<ToolTip>(text.ToolTip);

            card.Child = null;
            var other = new Border
            {
                Background = (Brush)Application.Current.FindResource("MpAppBg"),
                Child = text,
            };
            Lay(other, 90);
            text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var rebuilt = Assert.IsType<ToolTip>(text.ToolTip);
            Assert.NotSame(armed, rebuilt);
            Assert.Same(Application.Current.FindResource("MpAppBg"), rebuilt.Background);
        });
        Assert.Null(error);
    }

    /// <summary>The rank guide's name column is measured the way it is drawn.</summary>
    [Fact]
    public void TheRankGuideMeasuresInDisplayMode()
    {
        var src = System.IO.File.ReadAllText(UiThreadAttributionTests.LauncherFile("Controls/RankGuideCard.cs"));
        Assert.Contains("TextFormattingMode.Display", src);
        Assert.DoesNotContain("pixelsPerDip: 1.0", src);
    }
}
