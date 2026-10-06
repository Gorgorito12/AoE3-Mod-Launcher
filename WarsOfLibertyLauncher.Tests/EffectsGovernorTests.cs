using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// When the launcher decides this PC draws too slowly for its moving lights. A player's laptop
/// with hardware rendering paid ~300 ms for every frame and the launcher was unusable on screen;
/// the rule that turns the lights off has to catch that machine and leave a fast one alone — above
/// all a fast one whose window is being resized, which also spends most of a second drawing.
/// </summary>
public class EffectsGovernorTests
{
    private static readonly IReadOnlyDictionary<string, int> NoSizes = new Dictionary<string, int>();

    /// <summary>A second like the reporter's: two or three frames of ~300 ms each.</summary>
    private static LayoutStormDetector.Second Slow(bool watched = true) => new(0, 2, 620, NoSizes, 2, watched);

    private static LayoutStormDetector.Second Fast() => new(0, 30, 90, NoSizes, 30);

    [Fact]
    public void THE_ONE_THAT_MATTERS_ThreeSlowSecondsInARowTurnTheLightsOff()
    {
        var g = new EffectsGovernor();
        Assert.Null(g.Observe(Slow(), 6));
        Assert.Null(g.Observe(Slow(), 6));
        var line = g.Observe(Slow(), 6);

        Assert.NotNull(line);
        Assert.True(g.Tripped);
        Assert.StartsWith("EFFECTS REDUCED", line);
        Assert.Contains("620 ms of every second", line);
        Assert.Contains("310 ms a frame", line);
        Assert.Contains("6 badges animating", line);
    }

    [Fact]
    public void AFastSecondBreaksTheStreak()
    {
        var g = new EffectsGovernor();
        g.Observe(Slow(), 0);
        g.Observe(Slow(), 0);
        Assert.Null(g.Observe(Fast(), 0));
        Assert.Null(g.Observe(Slow(), 0));
        Assert.Null(g.Observe(Slow(), 0));
        Assert.False(g.Tripped);
        Assert.NotNull(g.Observe(Slow(), 0));
    }

    /// <summary>A hidden window's frames say nothing about what the player is waiting for.</summary>
    [Fact]
    public void SecondsNobodyCanSeeDoNotCount()
    {
        var g = new EffectsGovernor();
        for (var i = 0; i < 10; i++) Assert.Null(g.Observe(Slow(watched: false), 0));
        Assert.False(g.Tripped);
    }

    /// <summary>
    /// The resize: a fast PC dragging the window's edge lays out and redraws at the refresh rate,
    /// and can spend most of a second doing it — in many cheap frames. Turning its lights off for
    /// the session over that would be a loss for nothing.
    /// </summary>
    [Fact]
    public void ManyCheapFramesAreAResizeNotAWeakMachine()
    {
        var g = new EffectsGovernor();
        var resize = new LayoutStormDetector.Second(40, 60, 720, NoSizes, 0, Watched: true);
        Assert.False(EffectsGovernor.IsSlow(resize));
        for (var i = 0; i < 10; i++) Assert.Null(g.Observe(resize, 0));
        Assert.False(g.Tripped);
    }

    [Fact]
    public void WithoutFullHardwareRenderingTheLightsNeverStart()
    {
        Assert.Null(new EffectsGovernor().ObserveRenderTier(2));

        var g = new EffectsGovernor();
        var line = g.ObserveRenderTier(0);
        Assert.NotNull(line);
        Assert.Contains("tier 0", line);
        Assert.True(g.Tripped);
        Assert.NotNull(new EffectsGovernor().ObserveRenderTier(1));
    }

    /// <summary>Once off, off for the session — and one line, not one a second.</summary>
    [Fact]
    public void ItDecidesOnceAndSaysItOnce()
    {
        var g = new EffectsGovernor();
        for (var i = 0; i < 3; i++) g.Observe(Slow(), 0);
        Assert.True(g.Tripped);
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(g.Observe(Slow(), 0));
            Assert.Null(g.Observe(Fast(), 0));
        }
        Assert.Null(g.ObserveRenderTier(0));
        Assert.True(g.Tripped);
    }
}
