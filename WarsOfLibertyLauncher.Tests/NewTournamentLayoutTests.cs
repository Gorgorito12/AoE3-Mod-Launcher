using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Design 108 (docs/design_14nov): no text of the new-tournament window is cut. The case it was made
/// for: in English, "First come, first served" grew bold when chosen and ran under its box's rim.
/// </summary>
[Collection("wpf-and-language")]
public class NewTournamentLayoutTests
{
    private static CreateTournamentDialog Laid(string format = "1v1")
    {
        DialogXamlTests.EnsureResources();
        var dlg = new CreateTournamentDialog(privateSupported: true);
        if (format != "1v1") dlg.PickFormat(format);
        var root = (FrameworkElement)dlg.Content;
        root.Measure(new Size(780, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(780, root.DesiredSize.Height)));
        root.UpdateLayout();
        return dlg;
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static System.Collections.Generic.IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock t) yield return t;
            foreach (var d in Texts(child)) yield return d;
        }
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Choosing changes the fill and the rim, never the weight: every label of
    /// every choice measures the same chosen or not, in both languages.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void THE_ONE_THAT_MATTERS_ChoosingNeverChangesTheWeight(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var dlg = Laid();
                foreach (var row in new[] { dlg.EntryRow, dlg.VisibilityRow })
                {
                    var before = Texts(row).Select(t => (t.Text, t.FontWeight)).ToList();
                    Click(row.Buttons.Last());
                    row.UpdateLayout();
                    Assert.Equal(before, Texts(row).Select(t => (t.Text, t.FontWeight)).ToList());
                }
                foreach (var seg in new[] { dlg.FormatRow, dlg.CapacityRow })
                {
                    var weights = seg.Buttons.Select(b => b.FontWeight).Distinct().ToList();
                    Assert.Single(weights);
                    Click(seg.Buttons.First());
                    Assert.Equal(weights, seg.Buttons.Select(b => b.FontWeight).Distinct().ToList());
                }
                dlg.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Every option's text fits inside its row: the row grows with it instead of cutting
    /// it (no fixed height, wrapped lines). English is the language that failed.</summary>
    [Theory]
    [InlineData("en", "1v1")]
    [InlineData("en", "3v3")]
    [InlineData("es", "2v2")]
    public void EveryChoiceHoldsItsWholeText(string language, string format)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var dlg = Laid(format);
                var rows = dlg.EntryRow.Buttons.Concat(dlg.VisibilityRow.Buttons)
                    .Concat(dlg.FormatRow.Buttons).Concat(dlg.CapacityRow.Buttons);
                if (format != "1v1") rows = rows.Concat(dlg.SourceRows.Buttons);
                foreach (var button in rows)
                {
                    Assert.True(double.IsNaN(button.Height), "a choice has a fixed height");
                    foreach (var text in Texts(button))
                    {
                        var at = text.TransformToAncestor(button).TransformBounds(new Rect(text.RenderSize));
                        Assert.True(at.Right <= button.ActualWidth + 0.5 && at.Bottom <= button.ActualHeight + 0.5,
                            $"'{text.Text}' runs past its box ({at} in {button.ActualWidth}x{button.ActualHeight})");
                        // Measured on its own at the width it was given, the text is no taller than drawn.
                        text.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
                        Assert.True(text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom <= text.ActualHeight + 0.5, $"'{text.Text}' is cut: {text.DesiredSize} vs {text.ActualWidth}x{text.ActualHeight}, button {button.ActualHeight} min {button.MinHeight}");
                    }
                }
                dlg.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>The two cards stand at the same height, and the team card exists only for teams.</summary>
    [Fact]
    public void TheTwoCardsAreTheSameHeightAndTeamsAddTheirCard()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var dlg = Laid();
            Assert.Equal(dlg.WhatCard.ActualHeight, dlg.WhoCard.ActualHeight, 1);
            Assert.Equal(Visibility.Collapsed, dlg.TeamSourceBlock.Visibility);
            Assert.Equal(780, dlg.Width);
            dlg.Close();

            var teams = Laid("3v3");
            Assert.Equal(Visibility.Visible, teams.TeamSourceBlock.Visibility);
            Assert.Equal(3, teams.SourceRows.Buttons.Count());
            var heights = teams.SourceRows.Buttons.Select(b => Math.Round(b.ActualHeight)).Distinct().ToList();
            Assert.Single(heights);
            teams.Close();
        });
        Assert.Null(error);
    }
}
