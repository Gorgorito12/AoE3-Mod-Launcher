using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The decision half of the layout-storm watch. It writes the one line a player's bundle carries
/// when the UI thread stops resting, so the rules that matter are the quiet ones: a busy second is
/// not a storm, a storm is reported once a minute and not once a second, and badges animating on
/// screen are not a storm at all.
/// </summary>
public class LayoutStormDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, long> NoCounters = new Dictionary<string, long>();
    private static readonly IReadOnlyDictionary<string, int> NoSizes = new Dictionary<string, int>();

    private static LayoutStormDetector.Second Hot(bool watched = false, IReadOnlyDictionary<string, int>? sizes = null)
        => new(0, 72, 15, sizes ?? NoSizes, 72, watched);

    private static LayoutStormDetector.Second Quiet() => new(0, 0, 0, NoSizes);

    [Fact]
    public void AQuietLauncherWritesNothing()
    {
        var d = new LayoutStormDetector();
        for (var s = 0; s < 120; s++)
            Assert.Null(d.Observe(T0.AddSeconds(s), Quiet(), NoCounters, NoCounters, ""));
    }

    [Fact]
    public void ABurstIsNotAStorm_ThreeSecondsInARowIs()
    {
        var d = new LayoutStormDetector();
        Assert.Null(d.Observe(T0, Hot(), NoCounters, NoCounters, "ctx"));
        Assert.Null(d.Observe(T0.AddSeconds(1), Hot(), NoCounters, NoCounters, "ctx"));
        Assert.Null(d.Observe(T0.AddSeconds(2), Quiet(), NoCounters, NoCounters, "ctx"));
        Assert.Null(d.Observe(T0.AddSeconds(3), Hot(), NoCounters, NoCounters, "ctx"));
        Assert.Null(d.Observe(T0.AddSeconds(4), Hot(), NoCounters, NoCounters, "ctx"));
        var line = d.Observe(T0.AddSeconds(5), Hot(), NoCounters, NoCounters, "window hidden");
        Assert.NotNull(line);
        Assert.StartsWith("LAYOUT STORM", line);
        Assert.Contains("72/s redraws (72/s driven by animations)", line);
        Assert.Contains("nobody watching", line);
        Assert.Contains("window hidden", line);
    }

    /// <summary>A storm that lasts an hour is one line a minute and one line when it ends — not
    /// 3,600, which is what timing every second would have written into a 1.7 MB log.</summary>
    [Fact]
    public void ALongStormIsOneLineAMinuteAndOneWhenItEnds()
    {
        var d = new LayoutStormDetector();
        var lines = new List<string>();
        for (var s = 0; s < 150; s++)
            if (d.Observe(T0.AddSeconds(s), Hot(), NoCounters, NoCounters, "") is { } l) lines.Add(l);
        Assert.Equal(3, lines.Count); // at 3 s, 63 s and 123 s
        var over = d.Observe(T0.AddSeconds(150), Quiet(), NoCounters, NoCounters, "");
        Assert.Equal("LAYOUT STORM over after 150 s", over);
        Assert.Null(d.Observe(T0.AddSeconds(151), Quiet(), NoCounters, NoCounters, ""));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the threshold. On screen the badges legitimately animate at their
    /// capped frame rate, so the same redraw rate that is a storm in the tray is not one in front
    /// of the player — or the watch would cry wolf every minute the Rooms page is open.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_RedrawsCountOnlyWhenNobodyCanSeeThemOrWhenFarAboveTheCap()
    {
        Assert.False(LayoutStormDetector.IsHot(new(0, 60, 10, NoSizes, 60, Watched: true)));
        Assert.True(LayoutStormDetector.IsHot(new(0, 60, 10, NoSizes, 60, Watched: false)));
        Assert.True(LayoutStormDetector.IsHot(new(0, 120, 10, NoSizes, 120, Watched: true)));
        // Layout passes are a storm whether or not anybody watches: an idle page runs none.
        Assert.True(LayoutStormDetector.IsHot(new(25, 0, 0, NoSizes, 0, Watched: true)));
        Assert.False(LayoutStormDetector.IsHot(new(5, 10, 0, NoSizes, 0, Watched: false)));
    }

    /// <summary>
    /// The case the first version could not see: a weak laptop drawing two or three frames a
    /// second at ~300 ms each — its UI thread busy 101 s out of 104 — crossed no threshold, because
    /// they were all about HOW MANY. The bundle sent to report exactly that carried no storm line.
    /// </summary>
    [Fact]
    public void FewButExpensiveFramesAreAStorm()
    {
        var slow = new LayoutStormDetector.Second(0, 2, 620, NoSizes, 0, Watched: true);
        Assert.True(LayoutStormDetector.IsHot(slow));
        // ...while a page animating normally on screen is not.
        Assert.False(LayoutStormDetector.IsHot(new(0, 60, 300, NoSizes, 60, Watched: true)));

        var d = new LayoutStormDetector();
        Assert.Null(d.Observe(T0, slow, NoCounters, NoCounters, ""));
        Assert.Null(d.Observe(T0.AddSeconds(1), slow, NoCounters, NoCounters, ""));
        var line = d.Observe(T0.AddSeconds(2), slow, NoCounters, NoCounters, "");
        Assert.NotNull(line);
        Assert.Contains("620 ms/s drawing (310 ms a frame) — slow frames", line);
    }

    [Fact]
    public void TheLineNamesTheBusiestElementsAndHowFarEachCounterMoved()
    {
        var d = new LayoutStormDetector();
        var sizes = Enumerable.Range(1, 12).ToDictionary(i => $"Border#B{i:00}", i => i);
        var gauges = new Dictionary<string, long> { ["badges animating"] = 6, ["zero"] = 0 };
        string? line = null;
        for (var s = 0; s < 3; s++)
        {
            var counters = new Dictionary<string, long> { ["ApplyActivityLayout"] = 10 + s * 5, ["Idle"] = 4 };
            line = d.Observe(T0.AddSeconds(s), Hot(sizes: sizes), counters, gauges, "");
        }
        Assert.NotNull(line);
        Assert.Contains("Border#B12×36", line); // 12 a second for 3 seconds, busiest first
        Assert.DoesNotContain("Border#B04", line); // only the top eight are named
        Assert.Contains("ApplyActivityLayout +10", line);
        Assert.DoesNotContain("Idle", line); // a counter that did not move is not news
        Assert.Contains("badges animating=6", line);
        Assert.DoesNotContain("zero=", line);
    }

    /// <summary>
    /// A second launch used to rotate the RUNNING launcher's log on its way to forwarding "show
    /// yourself" and exiting — so the player who double-clicks the .exe because the launcher
    /// "will not open" destroyed the very log a bundle is for. The rotation has to come after the
    /// single-instance decision, and so do the two redirect self-heals, which would otherwise pull
    /// a folder out from under a game the running launcher has open.
    /// </summary>
    [Fact]
    public void TheLogIsRotatedOnlyByThePrimaryInstance()
    {
        var source = File.ReadAllText(RepoFile("App.xaml.cs"));
        var start = source.IndexOf("protected override void OnStartup(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var decision = source.IndexOf("if (!primary)", start, StringComparison.Ordinal);
        Assert.True(decision > start, "the single-instance decision was not found in OnStartup");
        foreach (var call in new[]
                 {
                     "Services.DiagnosticLog.Reset();",
                     "Services.AoE3UserDataRedirect.EnsureDefault()",
                     "Services.AoE3SetupPathRedirect.EnsureDefault()",
                 })
        {
            var at = source.IndexOf(call, start, StringComparison.Ordinal);
            Assert.True(at > decision, $"{call} must run after the single-instance decision");
        }
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
