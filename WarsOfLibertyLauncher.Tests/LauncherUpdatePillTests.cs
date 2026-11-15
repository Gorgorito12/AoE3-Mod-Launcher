using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using System.Xml.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The launcher-update pill (design 110, docs/design_14nov): the one solid, gold piece of the
/// title bar. MainWindow's XAML is parsed by no other test, so this one lifts the pill out of it
/// and builds it on its own, with the launcher's real resources.
/// </summary>
[Collection("wpf-and-language")]
public class LauncherUpdatePillTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static Button BuildPill()
    {
        var doc = XDocument.Load(UiThreadAttributionTests.LauncherFile("MainWindow.xaml"));
        var pill = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "LauncherUpdatePill");
        var copy = new XElement(pill);
        // What only MainWindow can resolve: its event handler and the shell namespace's flag.
        copy.Attributes().Where(a => a.Name.LocalName is "Click" or "WindowChrome.IsHitTestVisibleInChrome"
                                     || a.Name.LocalName == "Name").Remove();
        copy.SetAttributeValue("Visibility", "Visible");
        copy.Add(new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName));
        return (Button)XamlReader.Parse(copy.ToString());
    }

    private static Path Arrow(Button pill)
    {
        pill.ApplyTemplate();
        return (Path)pill.Template.FindName("PillArrow", pill);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The arrow is DRAWN — the character "↑" was the old pill — and the
    /// "ready" state draws another one. The face is the gold gradient whatever the mod is.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheArrowIsDrawnAndTheFaceIsAlwaysGold()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var pill = BuildPill();
            pill.Content = "Update launcher";
            var arrow = Arrow(pill);
            Assert.NotNull(arrow);
            var update = arrow.Data.ToString();

            pill.Tag = "ready";
            pill.ApplyTemplate();
            Assert.NotEqual(update, Arrow(pill).Data.ToString());

            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            pill.Arrange(new Rect(pill.DesiredSize));
            pill.UpdateLayout();
            var face = (Border)pill.Template.FindName("PillFace", pill);
            Assert.Same(Application.Current.FindResource("UiGoldPillFill"), face.Background);
            Assert.Equal(30, face.ActualHeight, 1);

            // No glyph anywhere in the pill: every TextBlock is the label.
            foreach (var text in Descendants(pill).OfType<TextBlock>())
                Assert.DoesNotContain(text.Text, new[] { "↑", "↻" });
        });
        Assert.Null(error);
    }

    /// <summary>The halo is siblings of the face, so no Effect ever sits over the label.</summary>
    [Fact]
    public void NoEffectSitsOverTheLabel()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var pill = BuildPill();
            pill.Content = "Update launcher";
            pill.ApplyTemplate();
            var face = (Border)pill.Template.FindName("PillFace", pill);
            for (DependencyObject? e = face; e != null; e = VisualTreeHelper.GetParent(e))
                if (e is UIElement u) Assert.Null(u.Effect);
            foreach (var e in Descendants(face).OfType<UIElement>()) Assert.Null(e.Effect);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The breath stops with the effects reduced, under the pointer and with nobody looking, and is
    /// a finite animation on a timer — never a clock left running.
    /// </summary>
    [Fact]
    public void TheBreathHoldsStillWithReducedEffectsAndUnderThePointer()
    {
        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("MainWindow.xaml.cs"));
        var refresh = src[src.IndexOf("private void RefreshPillPulse()", StringComparison.Ordinal)..];
        refresh = refresh[..refresh.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("!LauncherUpdatePill.IsMouseOver", refresh);
        Assert.Contains("!WarsOfLibertyLauncher.Controls.RankBadge.ReducedEffects", refresh);

        var breathe = src[src.IndexOf("private void BreatheLauncherUpdatePill()", StringComparison.Ordinal)..];
        breathe = breathe[..breathe.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("FillBehavior.Stop", breathe);
        Assert.DoesNotContain("RepeatBehavior", breathe);
        Assert.True(MainWindow.PillBreathPeriod > MainWindow.PillBreathLength);
    }

    /// <summary>
    /// The "ready" state reaches the template: its trigger swaps the arrow on Tag="ready", and the
    /// code that set the state wrote a glyph into Tag instead, so "Restart to update" kept the
    /// upward arrow (found on the capture of the ready state).
    /// </summary>
    [Fact]
    public void TheReadyStateSetsTheTagTheTemplateReads()
    {
        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("MainWindow.xaml.cs"));
        var apply = src[src.IndexOf("private void ApplyLauncherUpdatePillText(", StringComparison.Ordinal)..];
        apply = apply[..apply.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("LauncherUpdatePill.Tag = ready ? \"ready\" : null;", apply);
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
