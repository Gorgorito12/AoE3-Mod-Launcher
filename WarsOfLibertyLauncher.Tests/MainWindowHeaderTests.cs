using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The compact header (design handoff turn 36) moves the main tabs, the Connected capsule and
/// the account block INTO the title bar's caption region. Two things about that are invisible
/// when broken and are therefore pinned from the XAML itself — MainWindow is never constructed
/// by a test.
/// </summary>
public class MainWindowHeaderTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Shell = "clr-namespace:System.Windows.Shell;assembly=PresentationFramework";

    /// <summary>
    /// THE ONE THAT MATTERS. <c>IsHitTestVisibleInChrome</c> does not inherit into a
    /// ContentControl's Content, so a control that can sit in the caption region must carry it
    /// itself — or a click on it starts a window drag. That is exactly how the nav tabs once
    /// went dead, and the symptom ("they work right after opening the brand menu") looked
    /// nothing like a hit-test bug.
    /// </summary>
    [Theory]
    [InlineData("TopTabPlay")]
    [InlineData("TopTabMods")]
    [InlineData("TopTabMultiplayer")]
    [InlineData("ConnectionChip")]
    [InlineData("AccountButton")]
    public void EveryControlThatCanEnterTheCaptionRegionIsHitTestableThere(string name)
    {
        var doc = XDocument.Load(RepoFile("MainWindow.xaml"));
        var element = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
        var flag = element.Attributes()
            .SingleOrDefault(a => a.Name.LocalName == "WindowChrome.IsHitTestVisibleInChrome"
                                  && a.Name.NamespaceName == Shell);
        Assert.True(flag != null && flag.Value == "True",
            $"{name} can sit in the compact title bar and has no IsHitTestVisibleInChrome of its own: "
            + "clicking it would drag the window.");
    }

    [Theory]
    [InlineData("TitleBarHeightMainCompact")]
    [InlineData("TitleBarButtonWidthMainCompact")]
    public void TheCompactHeadersGeometryIsWholePixelsAtCommonScales(string key)
    {
        var doc = XDocument.Load(RepoFile("Styles/Tokens.xaml"));
        var token = doc.Descendants().Single(e => (string?)e.Attribute(X + "Key") == key);
        var value = double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var scale in new[] { 1.25, 1.5, 2.0 })
            Assert.Equal(Math.Round(value * scale), value * scale);
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
