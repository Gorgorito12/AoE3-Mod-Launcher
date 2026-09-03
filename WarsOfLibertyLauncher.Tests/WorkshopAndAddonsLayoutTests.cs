using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the two screens the third redesign pass rebuilt: the Workshop list and
/// detail panel (6a) and the addons tab of the mod properties dialog (5d).
///
/// <para><b>What these tests can and cannot see.</b> They load the real XAML and walk
/// the real visual tree, so a missing resource key, a style that resolves to nothing
/// and a control that never got built all fail here. They cannot see two panels drawn
/// on top of each other — that is what opening the launcher is for, and it is how the
/// overlap in the settings window got through a suite that asserted only Visibility.
/// Anything below that is a claim about STRUCTURE, never about appearance.</para>
/// </summary>
public class WorkshopAndAddonsLayoutTests
{
    [Fact]
    public void TheWorkshopHeaderKeepsTwoHeadingLevelsAndTheCountRidesWithTheSort()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var browser = new ModsBrowser();
            Force(browser);

            // The four heading levels the handoff cuts to two: the title and its
            // subtitle stay in the tree (their code-behind still fills them) and are
            // collapsed, so a future reader finds them rather than re-inventing them.
            var title = Find<TextBlock>(browser, "HeaderTitle");
            Assert.False(IsEffectivelyVisible(title),
                "the Workshop title is a heading level the handoff removed");

            // The count is IN the filter row, beside the sort control — not a third
            // heading of its own above the list.
            var summary = Find<TextBlock>(browser, "ListSummary");
            var sort = Find<ComboBox>(browser, "SortBox");
            Assert.True(IsAncestorOf(CommonAncestor(summary, sort), summary));
            Assert.True(ReferenceEquals(summary.Parent, sort.Parent),
                "the count and the sort control should share one cluster");

            // Both sub-tabs and all five filter chips survive: the chips lost weight,
            // not behaviour, and TheWorkshopFiltersRowFitsAtTheNarrowestWindow pins
            // that there are five of them.
            foreach (var name in new[] { "SubTabMyMods", "SubTabCatalog" })
                Assert.NotNull(Find<Button>(browser, name));
            foreach (var name in new[]
                     {
                         "FilterAll", "FilterInstalled", "FilterNotInstalled",
                         "FilterUpdates", "FilterCompatible",
                     })
                Assert.NotNull(Find<Button>(browser, name));
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheWorkshopDetailPanelCarriesTheIdentityLineAndTheDetailsTable()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var browser = new ModsBrowser();
            Force(browser);

            // autor · versión under the name, and DETAILS as a table — the two pieces
            // that replaced the metadata grid.
            Assert.NotNull(Find<TextBlock>(browser, "DetailIdentityLine"));
            Assert.NotNull(Find<TextBlock>(browser, "DetailMetaTitle"));
            Assert.NotNull(Find<StackPanel>(browser, "DetailMetaTable"));

            // The community links have their own style now. Sharing ModLinkPill would
            // have moved the support links in half a dozen unrelated dialogs.
            var link = browser.TryFindResource("WsCommunityLink") as Style;
            Assert.True(link != null, "WsCommunityLink is missing from ModsBrowser.xaml");
            var probe = new Button { Style = link };
            probe.Measure(new Size(400, 200));
            Assert.True(probe.Foreground != null, "WsCommunityLink resolved no Foreground");
            Assert.Equal(new Thickness(0), probe.BorderThickness);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheWorkshopUsesTheOneBluePaletteAndNoneOfTheGoldSurfaceKeys()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var browser = new ModsBrowser();
            Force(browser);

            // The tab used to be painted in the gold-theme surface keys while the two
            // settings windows beside it had moved to the blue set, so the same
            // application read as two. This asserts the ROOT surface, which is the one
            // that decides whether the rest can look like it belongs.
            var root = FirstDescendant<Grid>(browser);
            Assert.True(root != null, "the Workshop has no root Grid");
            var app = Application.Current!.Resources;
            Assert.Equal(
                ((SolidColorBrush)app["MpAppBg"]).Color,
                ((SolidColorBrush)root!.Background).Color);
        });
        Assert.Null(error);
    }

    [Fact]
    public void EveryStringTheTwoRedesignedScreensAddedResolvesInBothLanguages()
    {
        // A key that resolves to its own name is the shape a typo takes: the UI shows
        // "AddonBadgeCosmetic" instead of COSMÉTICO and nothing throws.
        var keys = new[]
        {
            "ModsBrowserBadgeBase", "ModsBrowserDetailMetaTitle",
            "ModsBrowserInstallTypeIsolated", "ModsBrowserInstallTypeOverlay",
            "ModsBrowserUpdateMechAutomatic", "ModsBrowserUpdateMechExternal",
            "ModsBrowserUpdateMechManual",
            "AddonsGroupCatalog", "AddonsGroupCatalogHint",
            "AddonsGroupImported", "AddonsGroupImportedHint",
            "AddonBadgeActive", "AddonBadgeCosmetic", "AddonBadgeMultiplayerRisk",
            "AddonBadgeBlocked", "AddonBadgeInstaller",
            "AddonFileCount", "AddonXmbCount", "AddonDataCount",
            "AddonInstallerNote", "AddonEnableAnyway", "AddonsFooterNote",
        };

        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { "en", "es" })
            {
                Strings.SetLanguage(lang);
                foreach (var key in keys)
                {
                    var value = Strings.Get(key);
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{key} is empty in {lang}");
                    Assert.False(value == key, $"{key} has no {lang} translation");
                }
            }

            // The three that take a figure must actually carry the placeholder, or the
            // count silently disappears from a line that exists to state it.
            Strings.SetLanguage("es");
            foreach (var key in new[] { "AddonFileCount", "AddonXmbCount", "AddonDataCount" })
                Assert.Contains("{0}", Strings.Get(key));
        }
        finally
        {
            Strings.SetLanguage(previous);
        }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static void Force(FrameworkElement el)
    {
        el.Measure(new Size(1400, 900));
        el.Arrange(new Rect(0, 0, 1400, 900));
        el.UpdateLayout();
    }

    private static T Find<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        var found = root.FindName(name) as T;
        Assert.True(found != null, $"{name} is missing from the tree");
        return found!;
    }

    private static bool IsEffectivelyVisible(FrameworkElement el)
    {
        for (DependencyObject? d = el; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static bool IsAncestorOf(DependencyObject? ancestor, DependencyObject? child)
    {
        for (var d = child; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private static DependencyObject? CommonAncestor(DependencyObject a, DependencyObject b)
    {
        for (DependencyObject? x = a; x is not null; x = VisualTreeHelper.GetParent(x))
            if (IsAncestorOf(x, b)) return x;
        return null;
    }

    private static T? FirstDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var deeper = FirstDescendant<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static Exception? RunOnStaThread(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the STA thread did not finish");
        return captured;
    }

    private static void EnsureResources()
    {
        // Shared holder, not a local guard: Application.Current goes null when the STA
        // thread that created it exits while WPF's one-per-AppDomain guard does not
        // reset, so the second class to run would throw. See TestApplication.
        var app = TestApplication.Ensure();
        if (app.Resources.MergedDictionaries.Count > 0) return;
        foreach (var name in new[] { "Tokens", "Colors", "Text", "Chrome", "Buttons", "Inputs", "Controls" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/Aoe3ModLauncher;component/Styles/{name}.xaml",
                    UriKind.Absolute),
            });
        }
        app.Resources["FontSizeCaption"] = 13.0;
        app.Resources["FontSizeBody"] = 14.0;
        app.Resources["FontSizeBodyStrong"] = 15.0;
        app.Resources["FontSizeSubtitle"] = 16.0;
        app.Resources["FontSizeTitle"] = 18.0;
        app.Resources["FontSizeHeading"] = 24.0;
        app.Resources["FontSizeDisplay"] = 34.0;
        app.Resources["DisplayFont"] = new FontFamily("Cambria, Georgia");
        app.Resources["BodyFont"] = new FontFamily("Segoe UI, Tahoma");
        app.Resources["MonoFont"] = new FontFamily("Consolas, Courier New");
    }
}
