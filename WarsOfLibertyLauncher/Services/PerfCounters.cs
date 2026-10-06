using System.Collections.Concurrent;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Named counters the layout-storm watch prints beside its verdict, so a line in a player's
/// diagnostic bundle says WHICH piece of the launcher was busy, not only that something was.
///
/// <para><b>Counters</b> only ever grow (how many times a method ran); the watch reports how much
/// each one moved during the storm. <b>Gauges</b> are a current level (how many badges are
/// animating right now) and are reported as they stand.</para>
///
/// <para>Cheap by design: one dictionary update per call, safe from any thread. Never put one on
/// a path that runs per pixel or per glyph — per method call is the granularity it is for.</para>
/// </summary>
internal static class PerfCounters
{
    private static readonly ConcurrentDictionary<string, long> s_counters = new();
    private static readonly ConcurrentDictionary<string, long> s_gauges = new();

    /// <summary>Counts one more run of <paramref name="name"/>.</summary>
    public static void Increment(string name) => s_counters.AddOrUpdate(name, 1, static (_, v) => v + 1);

    /// <summary>Moves the gauge <paramref name="name"/> by <paramref name="delta"/>.</summary>
    public static void AddGauge(string name, long delta) => s_gauges.AddOrUpdate(name, delta, (_, v) => v + delta);

    public static Dictionary<string, long> CountersSnapshot() => new(s_counters);

    public static Dictionary<string, long> GaugesSnapshot() => new(s_gauges);
}
