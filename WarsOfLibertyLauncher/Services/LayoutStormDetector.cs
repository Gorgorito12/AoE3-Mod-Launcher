using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Decides, one second at a time, whether the UI thread is caught in a STORM — layout passes or
/// redraws that keep coming with nothing worth drawing — and writes the one line that names what
/// was busy. The wiring that feeds it lives in <see cref="DiagnosticLog"/>; this half is pure so
/// the rule can be tested.
///
/// <para><b>Why it exists.</b> Two players' bundles showed the UI thread blocked 80-99 % of the
/// time, by redraws of ~300 ms each, back to back, for hours — with the launcher in the tray. The
/// existing stall watch could say THAT and never WHAT, and it only records a redraw that takes
/// 250 ms or more, so on a fast PC the very same storm cost a few milliseconds a frame and left
/// no trace at all. This counts frames and layout passes instead of timing them, which reads the
/// same on every machine. It is what found the cause: rank-badge animation clocks that were
/// detached but never stopped (see <c>RankBadge.Stop</c>).</para>
///
/// <para><b>What counts.</b> An idle launcher runs no layout pass and draws no frame, so
/// sustained layout passes are always a storm — something asks for another pass every time the
/// last one finishes. Redraws are judged by whether anybody can SEE them: on screen, the rank
/// badges legitimately animate at <c>RankBadge.FrameRate</c>, so only a rate well above that is
/// a storm; hidden, minimized or behind another program, every frame is drawn for nobody.</para>
///
/// <para><b>And FEW BUT EXPENSIVE frames count too</b> (<see cref="SlowRenderMsPerSecond"/>). The
/// first version counted only how many: a weak laptop drawing two or three frames a second at
/// ~300 ms each — the UI thread busy 101 s out of 104 — never crossed a single threshold, and the
/// bundle sent to report exactly that carried no storm line at all.</para>
/// </summary>
internal sealed class LayoutStormDetector
{
    /// <summary>Layout passes per second that count as a storm, watched or not.</summary>
    internal const int PassesPerSecond = 20;

    /// <summary>Redraws per second that count as a storm when nobody can see the window.</summary>
    internal const int RendersPerSecondUnwatched = 20;

    /// <summary>Redraws per second that count as a storm on screen: three times the badges'
    /// frame-rate cap, i.e. something drawing at the monitor's refresh rate.</summary>
    internal const int RendersPerSecondWatched = 90;

    /// <summary>Milliseconds of a second spent drawing that count as a storm whatever the frame
    /// count, watched or not: half the UI thread's time gone to redraws.</summary>
    internal const long SlowRenderMsPerSecond = 500;

    /// <summary>How many hot seconds in a row before it is reported: a resize or a tab switch
    /// legitimately runs a burst, and that is not what this is for.</summary>
    internal const int SustainSeconds = 3;

    /// <summary>At most one storm line per this long, however long the storm lasts.</summary>
    internal static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    /// <summary>How many of the busiest elements the line names.</summary>
    internal const int TopElements = 8;

    /// <summary>One second of observation. <see cref="Watched"/>: the launcher is the foreground
    /// application and its window is on screen.</summary>
    internal readonly record struct Second(
        int LayoutPasses,
        int Renders,
        long RenderMs,
        IReadOnlyDictionary<string, int> SizeChanges,
        int AnimatedRenders = 0,
        bool Watched = true);

    /// <summary>Whether one second, on its own, counts towards a storm.</summary>
    internal static bool IsHot(Second second)
        => second.LayoutPasses >= PassesPerSecond
           || second.Renders >= (second.Watched ? RendersPerSecondWatched : RendersPerSecondUnwatched)
           || second.RenderMs >= SlowRenderMsPerSecond;

    private int _hotSeconds;
    private bool _reportedThisStorm;
    private DateTime _lastReport = DateTime.MinValue;
    private long _passes, _renders, _animated, _renderMs;
    private int _unwatchedSeconds;
    private readonly Dictionary<string, int> _sizes = new(StringComparer.Ordinal);
    private Dictionary<string, long>? _countersAtStart;

    /// <summary>
    /// Feeds one second. Returns the line to log, or null. <paramref name="counters"/> is the
    /// current snapshot of <see cref="PerfCounters"/>; the line reports how much each moved
    /// during the storm, so the snapshot is remembered when one begins.
    /// </summary>
    public string? Observe(DateTime now, Second second,
        IReadOnlyDictionary<string, long> counters,
        IReadOnlyDictionary<string, long> gauges,
        string context)
    {
        if (!IsHot(second))
        {
            var over = _reportedThisStorm ? $"LAYOUT STORM over after {_hotSeconds} s" : null;
            Clear();
            return over;
        }

        if (_hotSeconds == 0) _countersAtStart = new Dictionary<string, long>(counters);
        _hotSeconds++;
        if (!second.Watched) _unwatchedSeconds++;
        _passes += second.LayoutPasses;
        _renders += second.Renders;
        _animated += second.AnimatedRenders;
        _renderMs += second.RenderMs;
        foreach (var kv in second.SizeChanges)
            _sizes[kv.Key] = _sizes.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;

        if (_hotSeconds < SustainSeconds) return null;
        if (now - _lastReport < ReportInterval) return null;

        _lastReport = now;
        _reportedThisStorm = true;
        return Format(counters, gauges, context);
    }

    private string Format(IReadOnlyDictionary<string, long> counters,
        IReadOnlyDictionary<string, long> gauges, string context)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = _hotSeconds;
        var sb = new StringBuilder();
        sb.Append(inv, $"LAYOUT STORM  {_passes / s}/s layout passes, {_renders / s}/s redraws ");
        sb.Append(inv, $"({_animated / s}/s driven by animations), {_renderMs / s} ms/s drawing");
        if (_renders > 0) sb.Append(inv, $" ({_renderMs / _renders} ms a frame)");
        if (_renderMs / s >= SlowRenderMsPerSecond) sb.Append(" — slow frames");
        sb.Append(inv, $", for {s} s");
        sb.Append(_unwatchedSeconds == s ? ", nobody watching"
            : _unwatchedSeconds == 0 ? ", on screen"
            : $", nobody watching for {_unwatchedSeconds.ToString(inv)} s of it");

        var top = _sizes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(TopElements).ToList();
        sb.Append(" — size changes: ");
        sb.Append(top.Count == 0 ? "none" : string.Join(", ", top.Select(kv => $"{kv.Key}×{kv.Value}")));

        var moved = counters
            .Select(kv => (kv.Key, Delta: kv.Value - (_countersAtStart != null && _countersAtStart.TryGetValue(kv.Key, out var v) ? v : 0)))
            .Where(x => x.Delta > 0)
            .OrderByDescending(x => x.Delta).ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        sb.Append(" — counters: ");
        sb.Append(moved.Count == 0 ? "none" : string.Join(", ", moved.Select(x => $"{x.Key} +{x.Delta}")));

        var levels = gauges.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        if (levels.Count > 0)
            sb.Append(" — now: ").Append(string.Join(", ", levels.Select(kv => $"{kv.Key}={kv.Value}")));

        if (!string.IsNullOrEmpty(context)) sb.Append(" — ").Append(context);
        return sb.ToString();
    }

    private void Clear()
    {
        _hotSeconds = 0;
        _unwatchedSeconds = 0;
        _reportedThisStorm = false;
        _passes = _renders = _animated = _renderMs = 0;
        _sizes.Clear();
        _countersAtStart = null;
    }
}
