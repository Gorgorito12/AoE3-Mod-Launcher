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
    /// THE ONE THAT MATTERS for reading the v1.0.15 bundle: a layout that never completes. WPF
    /// gives up after 153 passes without raising LayoutUpdated, so the line said "0/s layout
    /// passes" beside thousands of size changes on a few ~250-ms frames — and read on its own,
    /// "0/s layout passes" pointed away from the cause. It now says what it is, first, and names
    /// the single element that kept changing size.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ALayoutThatNeverCompletesIsCalledNonConverging()
    {
        var d = new LayoutStormDetector();
        var sizes = new Dictionary<string, int>
        {
            ["TextBlock@ActivityRecentList"] = 1400, ["Grid@ActivityRecentList"] = 700,
        };
        var second = new LayoutStormDetector.Second(0, 4, 1000, sizes, 0, Watched: true,
            BusiestElement: "TextBlock@ActivityRecentList", BusiestElementChanges: 612);
        string? line = null;
        for (var s = 0; s < 3; s++) line = d.Observe(T0.AddSeconds(s), second, NoCounters, NoCounters, "");

        Assert.NotNull(line);
        Assert.StartsWith("LAYOUT STORM  NON-CONVERGING", line);
        Assert.Contains("525 size changes a redraw", line); // (1400 + 700) × 3 s / 12 redraws
        Assert.Contains("busiest single element TextBlock@ActivityRecentList, 612 size changes in one second", line);
        // The usual figures still follow it.
        Assert.Contains("0/s layout passes, 4/s redraws", line);
    }

    /// <summary>
    /// The verdict is a majority of the storm's SECONDS, not the storm as a whole. It used to need
    /// zero passes over every hot second together, so one completed layout — often the ordinary
    /// first pass of the second the loop began — hid NON-CONVERGING for a storm that then ran for
    /// hours, and the line fell back to "0/s layout passes", which is integer division and never
    /// proved there were none.
    /// </summary>
    [Fact]
    public void ACompletedPassAtTheStormsStartDoesNotHideNonConverging()
    {
        var d = new LayoutStormDetector();
        var sizes = new Dictionary<string, int> { ["TextBlock@ActivityRecentList"] = 2100 };
        var firstSecond = new LayoutStormDetector.Second(1, 4, 1000, sizes, 0, Watched: true);
        var looping = new LayoutStormDetector.Second(0, 4, 1000, sizes, 0, Watched: true);

        Assert.Null(d.Observe(T0, firstSecond, NoCounters, NoCounters, ""));
        Assert.Null(d.Observe(T0.AddSeconds(1), looping, NoCounters, NoCounters, ""));
        var line = d.Observe(T0.AddSeconds(2), looping, NoCounters, NoCounters, "");

        Assert.NotNull(line);
        Assert.StartsWith("LAYOUT STORM  NON-CONVERGING", line);
        Assert.Contains("no layout pass completed in 2 of 3 s", line);
        Assert.Contains("525 size changes a redraw", line); // 2 × 2100 / 8 redraws
    }

    /// <summary>...but a storm whose seconds mostly DID complete their layout is not called it.</summary>
    [Fact]
    public void AStormThatMostlyCompletesIsNotCalledNonConverging()
    {
        var d = new LayoutStormDetector();
        var sizes = new Dictionary<string, int> { ["Grid"] = 2100 };
        var completing = new LayoutStormDetector.Second(30, 4, 1000, sizes, 0, Watched: true);
        var looping = new LayoutStormDetector.Second(0, 4, 1000, sizes, 0, Watched: true);
        string? line = null;
        foreach (var (second, i) in new[] { looping, completing, completing, completing }.Select((x, i) => (x, i)))
            line = d.Observe(T0.AddSeconds(i), second, NoCounters, NoCounters, "") ?? line;

        Assert.NotNull(line);
        Assert.DoesNotContain("NON-CONVERGING", line);
    }

    /// <summary>
    /// ...and only that. Redraws that change no size — animations, a debugger's overlay — and a
    /// layout that does complete, however busy, are storms of another kind: calling them
    /// non-converging would make the word mean nothing.
    /// </summary>
    [Fact]
    public void ARedrawStormOrABusyLayoutIsNotCalledNonConverging()
    {
        var renderOnly = new LayoutStormDetector();
        string? line = null;
        for (var s = 0; s < 3; s++) line = renderOnly.Observe(T0.AddSeconds(s), Hot(), NoCounters, NoCounters, "");
        Assert.NotNull(line);
        Assert.DoesNotContain("NON-CONVERGING", line);

        var completing = new LayoutStormDetector();
        var busy = new LayoutStormDetector.Second(40, 40, 600, new Dictionary<string, int> { ["Grid"] = 8000 },
            0, Watched: true, BusiestElement: "Grid", BusiestElementChanges: 200);
        line = null;
        for (var s = 0; s < 3; s++) line = completing.Observe(T0.AddSeconds(s), busy, NoCounters, NoCounters, "");
        Assert.NotNull(line);
        Assert.DoesNotContain("NON-CONVERGING", line);
    }

    [Theory]
    [InlineData(0, 4, 200, true)]    // 50 a redraw and no pass: the threshold itself
    [InlineData(0, 4, 199, false)]   // just under it
    [InlineData(1, 4, 5000, false)]  // a pass completed: it converges, however slowly
    [InlineData(0, 0, 5000, false)]  // no redraw: nothing was attempted
    [InlineData(0, 72, 0, false)]    // redraws alone: animations
    public void NonConvergingNeedsNoPassARedrawAndAFloodOfSizeChanges(long passes, long renders, long sizes, bool expected)
        => Assert.Equal(expected, LayoutStormDetector.IsNonConverging(passes, renders, sizes));

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

    /// <summary>
    /// The context (window state, focused element, WPF's clocks by reflection) is built only for a
    /// line that is WRITTEN — it used to be built every hot second, about 59 times a minute for
    /// nothing, on the UI thread the storm was already saturating.
    /// </summary>
    [Fact]
    public void TheContextIsBuiltOnlyForALineThatIsWritten()
    {
        var d = new LayoutStormDetector();
        var built = 0;
        string Context() { built++; return "ctx"; }
        IReadOnlyDictionary<string, long> Counters() => NoCounters;

        for (var s = 0; s < 10; s++) Assert.Null(d.Observe(T0.AddSeconds(s), Quiet(), Counters, NoCounters, Context));
        Assert.Equal(0, built);

        Assert.Null(d.Observe(T0.AddSeconds(10), Hot(), Counters, NoCounters, Context));
        Assert.Null(d.Observe(T0.AddSeconds(11), Hot(), Counters, NoCounters, Context));
        Assert.Equal(0, built);

        var line = d.Observe(T0.AddSeconds(12), Hot(), Counters, NoCounters, Context);
        Assert.NotNull(line);
        Assert.Contains("ctx", line);
        Assert.Equal(1, built);

        for (var s = 13; s < 72; s++) Assert.Null(d.Observe(T0.AddSeconds(s), Hot(), Counters, NoCounters, Context));
        Assert.Equal(1, built);
        Assert.NotNull(d.Observe(T0.AddSeconds(72), Hot(), Counters, NoCounters, Context));
        Assert.Equal(2, built);
    }

    /// <summary>The counters are read for the storm's baseline and for the line, never per second.</summary>
    [Fact]
    public void TheCountersAreReadOnlyWhileHot()
    {
        var d = new LayoutStormDetector();
        var reads = 0;
        var counters = new Dictionary<string, long> { ["ApplyActivityLayout"] = 10 };
        IReadOnlyDictionary<string, long> Counters() { reads++; return counters; }

        for (var s = 0; s < 5; s++) d.Observe(T0.AddSeconds(s), Quiet(), Counters, NoCounters, () => "");
        Assert.Equal(0, reads);

        d.Observe(T0.AddSeconds(5), Hot(), Counters, NoCounters, () => "");
        Assert.Equal(1, reads);
        d.Observe(T0.AddSeconds(6), Hot(), Counters, NoCounters, () => "");
        Assert.Equal(1, reads);

        counters["ApplyActivityLayout"] = 25;
        var line = d.Observe(T0.AddSeconds(7), Hot(), Counters, NoCounters, () => "");
        Assert.Equal(2, reads);
        Assert.Contains("ApplyActivityLayout +15", line);

        for (var s = 8; s < 30; s++) d.Observe(T0.AddSeconds(s), Hot(), Counters, NoCounters, () => "");
        Assert.Equal(2, reads);
    }

    /// <summary>The live tick hands the detector the lazy form, and keeps the gauges eager.</summary>
    [Fact]
    public void TheLiveTickPassesTheContextLazily()
    {
        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("Services/DiagnosticLog.cs"));
        Assert.Contains("s_storm.Observe(DateTime.UtcNow, second, () => PerfCounters.CountersSnapshot(), gauges, BuildContext)", src);
        Assert.Contains("var gauges = PerfCounters.GaugesSnapshot();", src);
    }
}
