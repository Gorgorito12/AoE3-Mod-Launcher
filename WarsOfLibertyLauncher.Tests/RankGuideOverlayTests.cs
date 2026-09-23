using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rank guide as the player meets it (46a/46b): a layer over the tab, built from the loaded
/// ladder, that every way out actually closes. The card is built in code, so a brush or string
/// key that does not exist throws only when somebody opens it — which is why it is built here.
/// </summary>
[Collection("wpf-and-language")]
public class RankGuideOverlayTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(Strings.LangEn)]
    [InlineData(Strings.LangEs)]
    public void TheGuideOpensOverTheTabWithEveryAge(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var before = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var tab = TabWithLadder();
                tab.ShowRankGuide();
                var overlay = Overlay(tab);
                Assert.NotNull(overlay);

                var all = Walk(overlay!).OfType<FrameworkElement>().ToList();
                var ages = all.Where(e => e.Tag is RankAge && e is Grid { MinHeight: > 0 }).Select(e => (RankAge)e.Tag).ToList();
                Assert.Equal(6, ages.Count);
                foreach (var text in all.OfType<TextBlock>().Select(t => t.Text))
                    Assert.DoesNotContain("MpGuide", text);   // a missing key renders as itself
                Assert.Contains(all, e => Equals(e.Tag, "RankGuideOpenRanking"));
            }
            finally { Strings.SetLanguage(before); }
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheCloseButtonTheScrimAndEscapeAllClose()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithLadder();

            tab.ShowRankGuide();
            var x = (Button)Walk(Overlay(tab)!).OfType<FrameworkElement>().Single(e => Equals(e.Tag, "RankGuideClose"));
            x.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(Overlay(tab));

            tab.ShowRankGuide();
            var scrim = tab.TabRootGrid.Children[tab.TabRootGrid.Children.Count - 3];
            scrim.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            });
            Assert.Null(Overlay(tab));

            tab.ShowRankGuide();
            var outer = Overlay(tab)!;
            var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("t"));
            try
            {
                outer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                });
            }
            finally { source.Dispose(); }
            Assert.Null(Overlay(tab));

            // One guide at a time: opening twice leaves exactly one layer.
            tab.ShowRankGuide();
            tab.ShowRankGuide();
            Assert.Single(tab.TabRootGrid.Children.OfType<FrameworkElement>(), e => Equals(e.Tag, "MpContentOverlay"));
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static MultiplayerTab TabWithLadder()
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab).GetField("_communityStats", Private)!.SetValue(tab, StatsDemoData.Community());
        return tab;
    }

    private static FrameworkElement? Overlay(MultiplayerTab tab)
        => tab.TabRootGrid.Children.OfType<FrameworkElement>().SingleOrDefault(e => Equals(e.Tag, "MpContentOverlay"));

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
