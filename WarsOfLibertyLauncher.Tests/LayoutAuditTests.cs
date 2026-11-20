using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The testbed's layout probe (Services/LayoutAudit.cs). It must name a text a player cannot read
/// in full and stay quiet about one that fits — a probe that cries wolf on every screen is one
/// nobody reads. The quiet cases are the point as much as the loud ones.
/// </summary>
[Collection("wpf-and-language")]
public class LayoutAuditTests
{
    private static Grid Arrange(Grid root, double width = 400, double height = 200)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return root;
    }

    private static TextBlock Text(string text, double? width = null, TextTrimming trimming = TextTrimming.None)
    {
        var tb = new TextBlock { Text = text, TextTrimming = trimming, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left };
        if (width is double w) tb.Width = w;
        return tb;
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ACutTextIsNamedAndOneThatFitsIsNot()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var panel = new StackPanel();
            panel.Children.Add(Text("This sentence is much longer than forty pixels", 40));
            panel.Children.Add(Text("Fits"));
            root.Children.Add(panel);
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.Contains(findings, f => f.Kind == "CUT" && f.Text.StartsWith("This sentence"));
            Assert.DoesNotContain(findings, f => f.Text == "Fits");
        });
        Assert.Null(error);
    }

    [Fact]
    public void AnEllipsisIsATrimNotACut()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            root.Children.Add(Text("This sentence is much longer than forty pixels", 40, TextTrimming.CharacterEllipsis));
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.Contains(findings, f => f.Kind == "TRIM");
            Assert.DoesNotContain(findings, f => f.Kind == "CUT");
        });
        Assert.Null(error);
    }

    /// <summary>A panel that clips (a fixed-width cell) with its text running past the edge.</summary>
    [Fact]
    public void TextPastAClippingPanelIsOutside()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var cell = new Border { Width = 60, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Text("A label that does not fit the cell at all"));
            cell.Child = line;
            root.Children.Add(cell);
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.Contains(findings, f => f.Kind == "OUTSIDE");
        });
        Assert.Null(error);
    }

    /// <summary>Scrolled-away text is where it should be: the scroll viewer's clip is not a defect.</summary>
    [Fact]
    public void AScrollViewersClipIsLeftAlone()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var list = new StackPanel();
            for (var i = 0; i < 30; i++) list.Children.Add(Text("Row " + i));
            root.Children.Add(new ScrollViewer { Content = list, Height = 80 });
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.DoesNotContain(findings, f => f.Kind == "OUTSIDE");
        });
        Assert.Null(error);
    }

    /// <summary>A scroll viewer that cannot scroll sideways still cuts what runs past its sides.</summary>
    [Fact]
    public void TextPastTheSideOfAVerticalScrollViewerIsOutside()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var list = new StackPanel();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Text("A row far wider than the narrow scroll viewer that holds it"));
            list.Children.Add(line);
            root.Children.Add(new ScrollViewer
            {
                Content = list, Width = 120, Height = 80, HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.Contains(findings, f => f.Kind == "OUTSIDE");
        });
        Assert.Null(error);
    }

    [Fact]
    public void TwoTextsOnOneLineTouchUnderFourAndAreNearUnderEight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Text("Left"));
            line.Children.Add(new TextBlock { Text = "Right", FontSize = 14, Margin = new Thickness(2, 0, 0, 0) });
            line.Children.Add(new TextBlock { Text = "Near", FontSize = 14, Margin = new Thickness(6, 0, 0, 0) });
            line.Children.Add(new TextBlock { Text = "Far", FontSize = 14, Margin = new Thickness(20, 0, 0, 0) });
            root.Children.Add(line);
            var findings = LayoutAudit.Audit(Arrange(root), "test");
            Assert.Contains(findings, f => f.Kind == "TOUCH" && f.Text.Contains("Left") && f.Text.Contains("Right"));
            Assert.Contains(findings, f => f.Kind == "NEAR" && f.Text.Contains("Right") && f.Text.Contains("Near"));
            Assert.DoesNotContain(findings, f => f.Text.Contains("Far"));
        });
        Assert.Null(error);
    }

    /// <summary>An alert card drawn over the page hides what is under it: not two texts colliding.</summary>
    [Fact]
    public void TextBehindACardDrawnOverItIsNotAnOverlap()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            root.Children.Add(Text("A sentence on the page under the card"));
            var card = new Border
            {
                Background = Brushes.Black, Width = 300, Height = 60,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = Text("The alert's own words"),
            };
            root.Children.Add(card);
            Assert.DoesNotContain(LayoutAudit.Audit(Arrange(root), "test"), f => f.Kind == "OVERLAP");
        });
        Assert.Null(error);
    }

    /// <summary>A room chip wider than its line (the chat's room card at 125 % text) trims rather
    /// than being cut at the card's edge: its row used to be a horizontal StackPanel, which
    /// measures the label with infinite width.</summary>
    [Fact]
    public void ARoomChipNarrowerThanItsTextTrims()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var root = new Grid();
            var line = new WrapPanel { Width = 90, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            line.Children.Add(WarsOfLibertyLauncher.Controls.MultiplayerTab.RoomChip("Observadores 1/2 · un texto largo", "MpPrivate", "MpPrivateText"));
            root.Children.Add(line);
            var findings = LayoutAudit.Audit(Arrange(root), "chip");
            Assert.DoesNotContain(findings, f => f.Kind is "CUT" or "OUTSIDE");
            Assert.Contains(findings, f => f.Kind == "TRIM");
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The Library's copies menu shortened every path to a fixed 48 characters, which the block
    /// then cut AGAIN at its end, losing the copy's own folder, the one part that tells two copies
    /// apart. Fitted to the real width (MiddlePathText), the tail survives at any width.
    /// </summary>
    [Fact]
    public void AFittedPathKeepsItsLastFolder()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            const string path = @"C:\Users\Jeison\Documents\GitHub\AoE3-Mod-Launcher\tools\testbed\out\sandbox\Wars of Liberty";
            foreach (var width in new[] { 180.0, 240.0, 320.0 })
            {
                var root = new Grid();
                var block = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas"), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
                WarsOfLibertyLauncher.Controls.MiddlePathText.Bind(block, path);
                root.Children.Add(block);
                Arrange(root, 400, 60);
                Assert.EndsWith("Wars of Liberty", block.Text);
                Assert.DoesNotContain(LayoutAudit.Audit(root, "path"), f => f.Kind is "CUT" or "TRIM");
            }
        });
        Assert.Null(error);
    }

    /// <summary>A surface scaled down to fit a small window (UiScale) is not a page of collisions:
    /// the first version compared unscaled text widths with scaled positions and flagged every row.</summary>
    [Fact]
    public void AScaledSurfaceIsMeasuredInItsOwnUnits()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var line = new StackPanel { Orientation = Orientation.Horizontal, LayoutTransform = new ScaleTransform(0.8, 0.8) };
            line.Children.Add(new TextBlock { Text = "Salas abiertas", FontSize = 14 });
            line.Children.Add(new TextBlock { Text = "7", FontSize = 14, Margin = new Thickness(10, 0, 0, 0) });
            root.Children.Add(line);
            Assert.DoesNotContain(LayoutAudit.Audit(Arrange(root), "scaled"), f => f.Kind is "OVERLAP" or "TOUCH" or "NEAR" or "OUTSIDE");
        });
        Assert.Null(error);
    }

    [Fact]
    public void HiddenTextIsNotAudited()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var root = new Grid();
            var hidden = new Border { Visibility = Visibility.Collapsed, Child = Text("This sentence is much longer than forty pixels", 40) };
            root.Children.Add(hidden);
            Assert.Empty(LayoutAudit.Audit(Arrange(root), "test"));
        });
        Assert.Null(error);
    }
}
