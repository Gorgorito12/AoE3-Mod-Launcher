using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Rooms strip is about the community; the Statistics page is about one mod.
///
/// <para><b>They shared one payload, and the scope came from the wrong page.</b>
/// <c>RefreshActivityStripAsync</c> asked for community stats with <c>StatsModId()</c> — the mod
/// picked on the STATISTICS subtab. So choosing a mod nobody plays there emptied the
/// "Community activity" strip on ROOMS: the peak-hours card vanished, the totals read
/// "0 matches (30 d)", and the recent matches fell back to the legacy one-line form.</para>
///
/// <para>And it was wrong before anybody touched a picker: <c>StatsModId()</c> falls back to the
/// mod being played, so a panel headed "Community activity" had always been reporting a single
/// mod's numbers.</para>
/// </summary>
public class StatsScopeTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The strip's fetch carries no mod scope at all.
    ///
    /// <para>Read off the source because the defect is one argument on one call, and reaching
    /// it at runtime would mean a signed-in session and a live server. The argument IS the bug,
    /// and it is the thing that must not come back.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheRoomsStripIsNotScopedToTheStatisticsPicker()
    {
        var body = MethodBody("private async Task RefreshActivityStripAsync()");

        Assert.DoesNotContain("StatsModId()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StatsMode()", body, StringComparison.Ordinal);
        Assert.Contains("GetCommunityStatsAsync(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the other half, which is how a lazy fix would go wrong: unplug the scope instead of
    /// separating it, and the Statistics page starts showing every mod's figures under a mod
    /// picker.
    /// </summary>
    [Fact]
    public void TheStatisticsPageStillAsksAboutItsOwnMod()
    {
        var body = MethodBody("private async Task RefreshStatsCommunityAsync()");
        Assert.Contains("StatsModId()", body, StringComparison.Ordinal);
        Assert.Contains("StatsMode()", body, StringComparison.Ordinal);

        // And it is actually kicked, or the page would draw from a payload nobody fetches.
        Assert.Contains("RefreshStatsCommunityAsync()",
            MethodBody("private void RefreshStatsForMod()"), StringComparison.Ordinal);
    }

    /// <summary>The CODE of one method, from its signature to the next member.</summary>
    private static string MethodBody(string signature)
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' is gone; this guard has to move with it.");

        var end = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;

        // COMMENTS FIRST, or this reads its own prose: the comment explaining why the fetch no
        // longer passes StatsModId() contains the words "StatsModId()", and the first version
        // of this test failed on that sentence rather than on any code. AccountChipTests learned
        // the same lesson on the word "return".
        return string.Join("\n", source[start..end].Split('\n')
            .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : line));
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
        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// A window that draws its own shape does not get the launcher's dialog chrome.
///
/// <para><b>The bug.</b> <c>App</c> hangs its chrome on every window without a native title bar:
/// a <c>WindowChrome</c> with a 44 px caption, the maximize fix, and DWM rounded corners. The
/// desktop toast window is <c>WindowStyle.None</c> too, but it is also the ONE window in the
/// repository with <c>AllowsTransparency = true</c> — every dialog sets it to False by hand — and
/// it is transparent precisely because the toast cards bring their own rounded corners and
/// shadow. Chrome plus DWM rounding on a layered window paints a bordered rectangle around and
/// beside the cards, which is what a user reported.</para>
/// </summary>
public class WindowChromeScopeTests
{
    /// <summary>
    /// Chrome is for chromeless OPAQUE windows. Not for one that asked for transparency, and
    /// not for one that kept the system title bar.
    /// </summary>
    [Theory]
    // The ten dialogs: no title bar of their own, opaque, so they want the launcher's.
    [InlineData(System.Windows.WindowStyle.None, false, true)]
    // The desktop toast window: its shape is its own.
    [InlineData(System.Windows.WindowStyle.None, true, false)]
    // Anything still wearing the system frame was never in scope.
    [InlineData(System.Windows.WindowStyle.SingleBorderWindow, false, false)]
    [InlineData(System.Windows.WindowStyle.SingleBorderWindow, true, false)]
    public void ATransparentWindowKeepsItsOwnShape(
        System.Windows.WindowStyle style, bool allowsTransparency, bool expected)
    {
        Assert.Equal(expected, App.ShouldApplyLauncherChrome(style, allowsTransparency));
    }

    /// <summary>
    /// And the hook actually asks it.
    ///
    /// <para>The rule can be perfectly stated and never consulted: the defect was a bare
    /// <c>if (w.WindowStyle == WindowStyle.None)</c> at the call site, which is what this
    /// reads for. Source, because the hook runs on a real window's Loaded event against live
    /// HWNDs and there is nothing to observe from a test.</para>
    /// </summary>
    [Fact]
    public void TheWindowHookAsksTheRule()
    {
        var source = File.ReadAllText(RepoFile("App.xaml.cs"));

        Assert.Contains("ShouldApplyLauncherChrome(w.WindowStyle, w.AllowsTransparency)",
            source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (w.WindowStyle == WindowStyle.None)",
            source, StringComparison.Ordinal);
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
        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}
