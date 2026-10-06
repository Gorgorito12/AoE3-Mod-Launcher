using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Work the launcher used to do while starting, on the UI thread, for things nobody could see.
/// A player's bundle put the window on screen 3.4 s after the constructor finished; 1.1 s of it
/// was a row of mod cards inside a panel that is always collapsed and a Workshop list rebuilt
/// off-screen — twice each before the window showed, and again right after.
/// </summary>
public class StartupWorkTests
{
    /// <summary>
    /// The detection is shared for a few seconds: the constructor, the install check, the update
    /// check and the other mods' scan each ran the whole probe — every fixed drive, the Steam
    /// libraries, the registry — within the same moment.
    /// </summary>
    [Fact]
    public void AoE3DetectionIsProbedOnceForCallersInTheSameMoment()
    {
        AoE3Detector.Invalidate();
        var first = AoE3Detector.FindAll();
        var probes = AoE3Detector.ProbeCount;
        var second = AoE3Detector.FindAll();

        Assert.Equal(probes, AoE3Detector.ProbeCount);
        Assert.Equal(first, second);
        // Each caller owns its list: one that sorts or trims it cannot change another's.
        Assert.NotSame(first, second);

        AoE3Detector.Invalidate();
        AoE3Detector.FindAll();
        Assert.True(AoE3Detector.ProbeCount > probes);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the start. The cards strip lives inside <c>LegacyPlayContent</c>,
    /// collapsed for good, and the Workshop is painted only while it is on screen. Read off the
    /// source because neither can be observed without the whole main window.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NothingInvisibleIsBuiltWhileStarting()
    {
        var source = File.ReadAllText(RepoFile("MainWindow.xaml.cs"));

        var cards = Body(source, "private void RefreshModCards()");
        Assert.DoesNotContain("ModCardsPanel", cards);
        Assert.Contains("EnsureModAssetsAsync", cards); // the one thing the cards did that is needed

        var browser = Body(source, "private void RefreshModsBrowser()");
        var visibleCheck = browser.IndexOf("ModsBrowserView.IsVisible", StringComparison.Ordinal);
        var populate = browser.IndexOf("ModsBrowserView.Populate", StringComparison.Ordinal);
        Assert.True(visibleCheck >= 0, "the Workshop must check it is on screen");
        Assert.True(populate > visibleCheck, "and check it BEFORE painting");

        // The window's own first activation must not repeat the startup asset pass.
        Assert.Contains("private DateTime _lastFocusRevalidateUtc = DateTime.UtcNow;", source);
    }

    // ── helpers ──

    /// <summary>The text of a method, from its signature to the closing brace at its indent.</summary>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, $"end of '{signature}' not found");
        return source[start..end];
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
