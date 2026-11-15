using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Outside the Library nothing follows the mod open there (2026-11-14): the Multiplayer tab's
/// underline and the quick match's button wore Wars of Liberty's red while the bar beside them
/// had another mod chosen. The tab underline takes the mod's colour in the Library only, and the
/// quick match is the launcher's blue.
/// </summary>
public class OutsideLibraryColourTests
{
    private static string Style(string xaml, string key)
    {
        int start = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, key);
        int end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheQuickMatchButtonReadsNoModColour()
    {
        var xaml = File.ReadAllText(UiThreadAttributionTests.LauncherFile(Path.Combine("Styles", "Buttons.xaml")));
        var style = Style(xaml, "QcButtonPrimary");
        Assert.DoesNotMatch(new Regex(@"\{DynamicResource Mod[A-Z]"), style);
        Assert.Contains("MpAction", style);

        var card = File.ReadAllText(UiThreadAttributionTests.LauncherFile(Path.Combine("Controls", "MultiplayerTab.QuickCard.cs")));
        Assert.DoesNotContain("\"ModAccent", card);
    }

    [Fact]
    public void TheTabUnderlineIsTheModsColourInTheLibraryOnly()
    {
        var xaml = File.ReadAllText(UiThreadAttributionTests.LauncherFile(Path.Combine("Styles", "Buttons.xaml")));
        var style = Style(xaml, "NavTabButton");
        Assert.DoesNotContain("{DynamicResource ModAccent}", style);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{DynamicResource MpAction}\"/>", style);

        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("MainWindow.xaml.cs"));
        var swap = src[src.IndexOf("private void SwitchTopTab(TopTab tab)", StringComparison.Ordinal)..];
        swap = swap[..swap.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("var underline = tab == TopTab.Play ? \"ModAccent\" : \"MpAction\";", swap);
    }
}
