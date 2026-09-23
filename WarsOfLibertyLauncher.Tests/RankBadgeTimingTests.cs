using System;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RankBadgeTiming"/>: the light of a rank badge. None of this can be seen in a
/// build or a screenshot — a pulse that fires a beat early, or two badges glittering in step,
/// look perfectly fine in a still.
/// </summary>
public class RankBadgeTimingTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The number has to peak exactly as the sheen's centre passes the
    /// badge's — not halfway through the sheen's travel, which the easing curve puts much later.
    /// The peaks are the measured ones from the design; re-derived here from the curve and the
    /// travel, so changing either makes this say the peak has to move with it.
    /// </summary>
    [Theory]
    [InlineData(RankAge.Fortress, 0.124)]
    [InlineData(RankAge.Industrial, 0.124)]
    [InlineData(RankAge.Imperial, 0.157)]
    [InlineData(RankAge.Sovereign, 0.189)]
    public void THE_ONE_THAT_MATTERS_TheNumberPeaksAsTheSheenCrossesTheCentre(RankAge age, double measuredPeak)
    {
        var crossing = RankBadgeTiming.SheenCrossesCentreAt(age);
        Assert.NotNull(crossing);
        Assert.InRange(crossing!.Value, measuredPeak - 0.004, measuredPeak + 0.004);

        var keys = RankBadgeTiming.For(age).PulseKeys!;
        Assert.InRange(keys[1], crossing.Value - 0.004, crossing.Value + 0.004);
        Assert.True(keys[0] < keys[1] && keys[1] < keys[2], "the pulse rises, peaks, then settles");
    }

    /// <summary>The light is slow, and slower the higher the age: travel and cycle both grow.</summary>
    [Fact]
    public void TheLightGetsSlowerAsTheAgeRises()
    {
        var ages = new[] { RankAge.Fortress, RankAge.Industrial, RankAge.Imperial, RankAge.Sovereign };
        for (var i = 1; i < ages.Length; i++)
        {
            var lower = RankBadgeTiming.For(ages[i - 1]);
            var higher = RankBadgeTiming.For(ages[i]);
            Assert.True(higher.SharpSheenTravel >= lower.SharpSheenTravel, $"{ages[i]} travels faster than {ages[i - 1]}");
            Assert.True(higher.SharpSheenSeconds >= lower.SharpSheenSeconds, $"{ages[i]} cycles faster than {ages[i - 1]}");
        }
    }

    /// <summary>Discovery has no light at all; Colonial has only its one slow sheet.</summary>
    [Fact]
    public void DiscoveryIsDarkAndColonialHasOneSheen()
    {
        Assert.False(RankBadgeTiming.For(RankAge.Discovery).HasAny);
        Assert.Null(RankBadgeTiming.SheenCrossesCentreAt(RankAge.Discovery));

        var colonial = RankBadgeTiming.For(RankAge.Colonial);
        Assert.True(colonial.HasAny);
        Assert.Equal(0, colonial.SharpSheenSeconds);
        Assert.Equal(0, colonial.AuraSeconds);
        Assert.Equal(RankSparkKind.None, colonial.Sparks);
    }

    /// <summary>Only Industrial and up throw sparks, and each age its own kind and count.</summary>
    [Theory]
    [InlineData(RankAge.Industrial, 24, 5)]
    [InlineData(RankAge.Imperial, 24, 7)]
    [InlineData(RankAge.Sovereign, 28, 10)]
    [InlineData(RankAge.Sovereign, 72, 14)]
    [InlineData(RankAge.Fortress, 24, 0)]
    [InlineData(RankAge.Colonial, 24, 0)]
    public void EachAgeThrowsItsOwnNumberOfSparks(RankAge age, double width, int expected)
        => Assert.Equal(expected, RankBadgeTiming.Sparks("user-1", age, width).Count);

    /// <summary>
    /// The same badge draws the same pattern every time it is rebuilt — the pages that show
    /// badges are rebuilt on every payload, and a pattern drawn from a counter would jump.
    /// </summary>
    [Fact]
    public void TheSameSeedDrawsTheSamePattern()
    {
        var a = RankBadgeTiming.Sparks("user-42", RankAge.Imperial, 24);
        var b = RankBadgeTiming.Sparks("user-42", RankAge.Imperial, 24);
        Assert.Equal(a, b);
    }

    /// <summary>Two players side by side never glitter alike or in step.</summary>
    [Fact]
    public void TwoPlayersNeverShareAPattern()
    {
        var a = RankBadgeTiming.Sparks("user-42", RankAge.Imperial, 24);
        var b = RankBadgeTiming.Sparks("user-43", RankAge.Imperial, 24);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a.Select(s => s.DelaySeconds), b.Select(s => s.DelaySeconds));
    }

    /// <summary>
    /// The seed is FNV-1a, not <c>string.GetHashCode</c>, which .NET randomises per process —
    /// that would give every launch a different pattern. Pinned to a known value.
    /// </summary>
    [Fact]
    public void TheSeedIsStableAcrossProcesses()
    {
        Assert.Equal(unchecked((int)0x811C9DC5), RankBadgeTiming.StableSeed(""));
        Assert.Equal(unchecked((int)0xE40C292C), RankBadgeTiming.StableSeed("a"));
    }

    /// <summary>Every spark stays inside the ranges the design draws them in.</summary>
    [Theory]
    [InlineData(RankAge.Industrial)]
    [InlineData(RankAge.Imperial)]
    [InlineData(RankAge.Sovereign)]
    public void SparksStayInsideTheirRanges(RankAge age)
    {
        var light = RankBadgeTiming.For(age);
        foreach (var seed in Enumerable.Range(0, 40).Select(i => "user-" + i))
        {
            foreach (var s in RankBadgeTiming.Sparks(seed, age, 28))
            {
                Assert.InRange(s.DurationSeconds, light.SparkMinSeconds - 0.01, light.SparkMaxSeconds + 0.01);
                Assert.InRange(s.DelaySeconds, 0, s.DurationSeconds);
                Assert.InRange(s.X, -0.13, 1.05);
                Assert.InRange(s.Y, -0.10, 1.00);
                Assert.InRange(s.Size, 7, 20);
            }
        }
    }

    /// <summary>The curve solver agrees with a known point: ease-in-out is symmetric.</summary>
    [Fact]
    public void TheCurveSolverIsRight()
        => Assert.InRange(RankBadgeTiming.TimeForProgress(RankBadgeTiming.EaseInOut, 0.5), 0.499, 0.501);
}
