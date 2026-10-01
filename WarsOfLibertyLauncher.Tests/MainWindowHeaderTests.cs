using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The compact header (design handoff turns 38-39) is the SAME three bars as the wide one, only
/// lower. Pinned from the XAML and the source — MainWindow is never constructed by a test.
/// </summary>
public class MainWindowHeaderTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// THE ONE THAT MATTERS. Turn 36 moved the tabs, the Connected capsule and the account block
    /// into the title bar through host Borders, and WORKSHOP ended up under the Update pill. The
    /// handoff that replaced it says a small window shows the same blocks as a big one: the nav
    /// row keeps all three, in the XAML, and nothing re-parents them.
    /// </summary>
    [Fact]
    public void TheNavRowKeepsTheTabsTheCapsuleAndTheAccountBlock()
    {
        var doc = XDocument.Load(RepoFile("MainWindow.xaml"));
        var nav = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MainNav");
        foreach (var name in new[] { "TopTabBar", "TopTabPlay", "TopTabMods", "TopTabMultiplayer", "ConnectionChip", "AccountButton" })
            Assert.True(nav.Descendants().Any(e => (string?)e.Attribute(X + "Name") == name),
                $"{name} has left the nav row.");

        // No host Borders to move them into, and no seam for a nav row that collapses.
        var names = doc.Descendants().Select(e => (string?)e.Attribute(X + "Name")).Where(n => n != null).ToHashSet();
        foreach (var gone in new[] { "TitleTabsHost", "TitleConnectionHost", "TitleAccountHost", "TitleBarSeam", "AccountEloChip" })
            Assert.DoesNotContain(gone, names);

        var code = File.ReadAllText(RepoFile("MainWindow.Compact.cs"));
        Assert.DoesNotContain("MoveChild", code);
        Assert.DoesNotContain("MainNav.Visibility", code);
    }

    [Fact]
    public void TheCompactBarsAreTheHandoffsHeights()
    {
        Assert.Equal(34, Token("TitleBarHeightMainCompact"));
        Assert.Equal(42, Token("MainNavHeightCompact"));
        Assert.Equal(44, Token("TitleBarButtonWidthMainCompact"));
        Assert.Equal(44, Token("MpSubBarHeightCompact"));
    }

    [Fact]
    public void TheCaptionHeightAndTheBarReadTheSameKey()
    {
        // App.MainTitleBarHeightKey is the one answer; both the window chrome and the bar use it.
        Assert.Equal("TitleBarHeightMainCompact", App.MainTitleBarHeightKey(compact: true));
        Assert.Equal("TitleBarHeightMain", App.MainTitleBarHeightKey(compact: false));

        var code = File.ReadAllText(RepoFile("MainWindow.Compact.cs"));
        Assert.Contains("App.MainTitleBarHeightKey(compact)", code);
        Assert.Contains("App.SyncMainCaptionHeight(this, compact)", code);
    }

    private static double Token(string key)
    {
        var doc = XDocument.Load(RepoFile("Styles/Tokens.xaml"));
        var token = doc.Descendants().Single(e => (string?)e.Attribute(X + "Key") == key);
        return double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture);
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
}
