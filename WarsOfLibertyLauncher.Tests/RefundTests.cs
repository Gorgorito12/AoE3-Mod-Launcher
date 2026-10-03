using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the refund (design 55n): the sentence, the bell said once per refund, the profile banner
/// over the affected mode's card until "Got it" — and every kind of notification keeping a glyph
/// of its own in the bell, which is how a new kind silently turns into the plain bell.
/// </summary>
[Collection("wpf-and-language")]
public class RefundTests
{
    private static RefundNotice Refund(string id, string mode, int points, bool seen = false) => new()
    {
        RefundId = id, Mode = mode, Points = points, Matches = 1,
        RatingBefore = 1500, RatingAfter = 1500 + points, Seen = seen,
    };

    [Fact]
    public void OnlyTheUnseenRefundsOfThatLadderAreShown_AndTheyAreSummed()
    {
        var refunds = new[]
        {
            Refund("a", "default", 20), Refund("b", "default", 14), Refund("c", "team", 9),
            Refund("d", "default", 50, seen: true),
        };
        var solo = RefundView.Unseen(refunds, team: false);
        Assert.Equal(new[] { "a", "b" }, solo.Select(r => r.RefundId));
        Assert.Equal(new[] { "c" }, RefundView.Unseen(refunds, team: true).Select(r => r.RefundId));
        Assert.Empty(RefundView.Unseen(null, team: false));

        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEs);
        try
        {
            Assert.Equal("Recuperaste 34 puntos: un rival contra el que perdiste fue sancionado por hacer trampas.",
                RefundView.Text(solo));
            Assert.StartsWith("Recuperaste 1 punto:", RefundView.Text(1));
        }
        finally { Strings.SetLanguage(previous); }
    }

    /// <summary>The standing carries the refund every session until it is dismissed; the bell says it once.</summary>
    [Fact]
    public void TheBellSaysARefundOnce()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config, persist: () => { });
        Assert.True(center.RaiseRatingRefund("refund-1", "Puntos devueltos", "Recuperaste 34 puntos"));
        Assert.False(center.RaiseRatingRefund("refund-1", "Puntos devueltos", "Recuperaste 34 puntos"));
        Assert.True(center.RaiseRatingRefund("refund-2", "Puntos devueltos", "Recuperaste 9 puntos"));
        Assert.False(center.RaiseRatingRefund("", "x", "y"));
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.RatingRefund));
        Assert.All(center.Items, i => Assert.Equal("", i.ModId));
    }

    /// <summary>
    /// The banner sits over the affected mode's card, says the sentence, and "Got it" takes it
    /// away — in the preview without asking any server.
    /// </summary>
    [Fact]
    public void TheBannerSitsOverItsModeAndGotItDismissesIt()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var body = new StackPanel();
                var tab = new MultiplayerTab { ProfileBodyOverride = body };
                var sample = EloDemoData.Profile();
                sample.Standing.Refunds = new List<RefundNotice> { EloDemoData.Refund() };
                tab.ShowDemoEloProfile(sample);

                var banner = Walk(body).OfType<Border>().Single(b => Equals(b.Tag, MultiplayerTab.RefundBannerTag));
                Assert.Contains(AllText(banner), t => t.StartsWith("Recuperaste 34 puntos:"));

                // Over the 1v1 card: the banner and that card share one stack, banner first.
                var stack = (StackPanel)LogicalTreeHelper.GetParent(banner);
                Assert.Same(banner, stack.Children[0]);
                Assert.Equal(MultiplayerTab.ModeCardTag, ((FrameworkElement)stack.Children[1]).Tag);

                var ok = Walk(banner).OfType<Button>().Single();
                Assert.Equal("Entendido", ok.Content);
                ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.DoesNotContain(Walk(body).OfType<Border>(), b => Equals(b.Tag, MultiplayerTab.RefundBannerTag));
                Assert.All(sample.Standing.Refunds!, r => Assert.True(r.Seen));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Every kind has a glyph of its own in the bell's fallback block — the one a mod-less item
    /// shows. A kind with no trigger falls back to the plain bell in silence, which would make
    /// "points refunded" read as an ordinary update.
    /// </summary>
    [Fact]
    public void EveryKindHasItsOwnGlyphInTheBell()
    {
        var xaml = File.ReadAllText(RepoFile("MainWindow.xaml"));
        foreach (var kind in Enum.GetNames<NotificationKind>())
        {
            var count = CountOf(xaml, $"<DataTrigger Binding=\"{{Binding Kind}}\" Value=\"{kind}\">");
            Assert.True(count >= 1, $"no bell glyph for {kind}");
        }
        Assert.Equal(2, CountOf(xaml, "Value=\"RatingRefund\">"));
    }

    private static int CountOf(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }

    private static List<string> AllText(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
