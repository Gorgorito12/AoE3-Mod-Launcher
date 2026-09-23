using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What kind of spark an age throws off, if any.</summary>
public enum RankSparkKind { None, Ember, Mote, Star }

/// <summary>One spark, in the badge's own proportions: X/Y are fractions of the badge's width
/// and height (and may fall outside 0..1 — a Sovereign's stars reach past the shield), Size is
/// in pixels at a 36-px badge and scales with it.</summary>
public readonly record struct RankSpark(double X, double Y, double Size, double DurationSeconds, double DelaySeconds, bool EightPoints);

/// <summary>
/// The timings of every light on a rank badge, per age. Read off the design prototype
/// (<c>docs/design_insignias_rango/Prototipo-insignias.html</c>, 43a) — its keyframes and its
/// per-row durations — and kept here, with no WPF, so they can be tested.
///
/// <para><b>The light is slow, and slower the higher the age.</b> The sharp sheen crosses the
/// shield in 46 % of its cycle on Fortress and Industrial, 58 % on Imperial and 70 % on
/// Sovereign, and its cycle lengthens with it.</para>
///
/// <para><b>The number pulses when the sheen crosses the CENTRE, not halfway through its
/// travel.</b> The sheen is a shield-wide band moving from −160 % to +260 % of its own width, so
/// its centre is over the badge's at 1.6 / 4.2 = 38 % of the travel; the easing curve front-loads
/// the motion, so that happens at 12.4 / 15.7 / 18.9 % of the CYCLE.
/// <see cref="SheenCrossesCentreAt"/> re-derives those numbers from the curve, and a test holds
/// the pulse peaks to it — change a curve or a travel and the test says the peak must move.</para>
/// </summary>
public static class RankBadgeTiming
{
    /// <summary>The broad, weak sheen: ease-in-out, over 34 % of its cycle.</summary>
    public const double BroadSheenTravel = 0.34;

    /// <summary>The sharp sheen's easing: <c>cubic-bezier(.33,0,.18,1)</c>.</summary>
    public static readonly (double X1, double Y1, double X2, double Y2) SharpSheenCurve = (0.33, 0, 0.18, 1);

    /// <summary>Plain CSS <c>ease-in-out</c>.</summary>
    public static readonly (double X1, double Y1, double X2, double Y2) EaseInOut = (0.42, 0, 0.58, 1);

    /// <summary>The sharp sheen and the pulse both start this late, so they stay in step.</summary>
    public const double SharpSheenDelaySeconds = 0.45;

    /// <summary>How far the sheen band starts off the badge, and how far past it it ends, in
    /// widths of the band (which is as wide as the badge).</summary>
    public const double SheenFrom = -1.6, SheenTo = 2.6;

    /// <summary>The number grows this much at the peak of its pulse.</summary>
    public const double PulseScale = 1.05;

    /// <summary>Stars are this many from 60 px up; below that the Sovereign throws ten.</summary>
    public const int SovereignStarsLarge = 14;

    /// <summary>Everything that moves on a badge of one age. A layer an age does not have is 0 /
    /// null — never a default the caller might animate anyway.</summary>
    public sealed record Layers(
        double BroadSheenSeconds,
        double BroadSheenAlpha,
        double AuraSeconds,
        double AuraBlur,
        double FacetSeconds,
        double SharpSheenSeconds,
        double SharpSheenTravel,
        double SharpSheenAlpha,
        double[]? PulseKeys,
        double EdgeLightSeconds,
        RankSparkKind Sparks,
        int SparkCount,
        double SparkMinSeconds,
        double SparkMaxSeconds,
        double InnerShineSeconds,
        double HaloSeconds,
        double HaloReverseSeconds)
    {
        public bool HasAny => BroadSheenSeconds > 0;
    }

    private static readonly Layers None = new(0, 0, 0, 0, 0, 0, 0, 0, null, 0, RankSparkKind.None, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// The layers of an age. They accumulate: each age carries the previous one's lights plus
    /// its own. Discovery has none at all — if the first step already glowed, climbing would not
    /// show.
    /// </summary>
    public static Layers For(RankAge age) => age switch
    {
        RankAge.Colonial => None with { BroadSheenSeconds = 10, BroadSheenAlpha = 0.26 },
        RankAge.Fortress => new(8, 0.48, 6.4, 3.5, 5.5, 8.6, 0.46, 0.86,
            new[] { 0.06, 0.124, 0.20 }, 0, RankSparkKind.None, 0, 0, 0, 0, 0, 0),
        RankAge.Industrial => new(8, 0.48, 6.4, 3.5, 5.5, 8.6, 0.46, 0.86,
            new[] { 0.06, 0.124, 0.20 }, 6.2, RankSparkKind.Ember, 5, 3.0, 4.4, 0, 0, 0),
        RankAge.Imperial => new(8.8, 0.62, 5.6, 5.5, 5.5, 9.8, 0.58, 0.86,
            new[] { 0.09, 0.157, 0.25 }, 6.2, RankSparkKind.Mote, 7, 3.3, 4.8, 10.3, 0, 0),
        RankAge.Sovereign => new(9.4, 0.62, 5.2, 5.5, 4.3, 11.5, 0.70, 1.0,
            new[] { 0.11, 0.189, 0.29 }, 6.2, RankSparkKind.Star, 10, 4.3, 6.2, 8.7, 8.0, 12.2),
        _ => None,
    };

    /// <summary>
    /// The time fraction at which a CSS <c>cubic-bezier(x1,y1,x2,y2)</c> reaches
    /// <paramref name="progress"/>. Solved by bisection on the curve's parameter: the curves used
    /// here are monotonic in Y, which is all bisection needs.
    /// </summary>
    public static double TimeForProgress((double X1, double Y1, double X2, double Y2) curve, double progress)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 60; i++)
        {
            var mid = (lo + hi) / 2;
            if (Bezier(curve.Y1, curve.Y2, mid) < progress) lo = mid; else hi = mid;
        }
        return Bezier(curve.X1, curve.X2, (lo + hi) / 2);
    }

    private static double Bezier(double p1, double p2, double s)
        => 3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;

    /// <summary>
    /// The fraction of its cycle at which the sharp sheen's centre is over the badge's centre —
    /// the moment the number has to peak. Null for an age with no sharp sheen.
    /// </summary>
    public static double? SheenCrossesCentreAt(RankAge age)
    {
        var l = For(age);
        if (l.SharpSheenTravel <= 0) return null;
        var progress = (0 - SheenFrom) / (SheenTo - SheenFrom);
        return l.SharpSheenTravel * TimeForProgress(SharpSheenCurve, progress);
    }

    /// <summary>
    /// The sparks of one badge. Everything about them — where, how big, how long, how late — is
    /// drawn from <paramref name="seedKey"/> (a player id or a position) and the age, so two
    /// badges side by side never glitter alike or in step, and the SAME badge draws the same
    /// pattern every time it is rebuilt. The pages that show badges are rebuilt on every payload;
    /// a pattern drawn from a counter would jump on each refresh.
    /// </summary>
    public static IReadOnlyList<RankSpark> Sparks(string seedKey, RankAge age, double badgeWidth)
    {
        var l = For(age);
        var result = new List<RankSpark>();
        if (l.Sparks == RankSparkKind.None) return result;

        var count = l.Sparks == RankSparkKind.Star && badgeWidth >= 60 ? SovereignStarsLarge : l.SparkCount;
        var rng = new Random(StableSeed(seedKey + "|" + age));

        var (xMin, xMax, yMin, yMax, sMin, sMax) = l.Sparks switch
        {
            RankSparkKind.Ember => (0.20, 0.60, 0.40, 0.85, 7.0, 10.0),
            RankSparkKind.Mote => (0.14, 0.84, 0.18, 0.87, 10.0, 14.0),
            _ => (-0.13, 1.05, -0.10, 1.00, 10.0, 20.0),
        };

        for (var i = 0; i < count; i++)
        {
            var duration = l.SparkMinSeconds + rng.NextDouble() * (l.SparkMaxSeconds - l.SparkMinSeconds);
            result.Add(new RankSpark(
                X: xMin + rng.NextDouble() * (xMax - xMin),
                Y: yMin + rng.NextDouble() * (yMax - yMin),
                Size: Math.Round(sMin + rng.NextDouble() * (sMax - sMin)),
                DurationSeconds: Math.Round(duration, 2),
                DelaySeconds: Math.Round(rng.NextDouble() * duration, 2),
                EightPoints: l.Sparks == RankSparkKind.Star && rng.NextDouble() < 0.6));
        }
        return result;
    }

    /// <summary>
    /// FNV-1a over the key's UTF-16 code units. <c>string.GetHashCode</c> is randomised per
    /// process in .NET, so it would give a different pattern on every launch.
    /// </summary>
    public static int StableSeed(string key)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in key)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}
