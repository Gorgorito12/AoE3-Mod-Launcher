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
///
/// <para><b>A storm with NO layout pass and a flood of size changes is a layout that never
/// completes</b>, and the line says so (<see cref="IsNonConverging"/>). WPF's layout loop gives up
/// after 153 rounds without raising <c>LayoutUpdated</c> — which is what this counts as a pass —
/// and tries again on the next frame, so a size-change handler that changes the size it reacts to
/// reads as "0/s layout passes" beside thousands of size changes. That was the v1.0.15 storm
/// (<c>InlineFlagFit</c> collapsing flags in rows a <c>FitStackPanel</c> laid out at their own
/// width), and the line that reported it said "0/s layout passes" and nothing about why; read
/// as "no layout work", it pointed away from the cause. The verdict also names the busiest
/// SINGLE element, because the grouped list adds up every instance under one name and cannot
/// say whether one element is changing size hundreds of times a second.</para>
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

    /// <summary>Size changes per redraw, over a storm with no layout pass at all, from which the
    /// layout is called non-converging. A frame that settles changes an element's size about
    /// once; one that never settles changes the same few elements up to 153 times each, so the
    /// two are far apart.</summary>
    internal const int NonConvergingSizeChangesPerRedraw = 50;

    /// <summary>One second of observation. <see cref="Watched"/>: the launcher is the foreground
    /// application and its window is on screen. <see cref="BusiestElement"/> is the single element
    /// that changed size most often this second, and <see cref="BusiestElementChanges"/> how
    /// often — counted BEFORE the elements are grouped by name.</summary>
    internal readonly record struct Second(
        int LayoutPasses,
        int Renders,
        long RenderMs,
        IReadOnlyDictionary<string, int> SizeChanges,
        int AnimatedRenders = 0,
        bool Watched = true,
        string? BusiestElement = null,
        int BusiestElementChanges = 0);

    /// <summary>Whether one second, on its own, counts towards a storm.</summary>
    internal static bool IsHot(Second second)
        => second.LayoutPasses >= PassesPerSecond
           || second.Renders >= (second.Watched ? RendersPerSecondWatched : RendersPerSecondUnwatched)
           || second.RenderMs >= SlowRenderMsPerSecond;

    /// <summary>
    /// Whether a storm is a layout that never completes: redraws, not one finished layout pass,
    /// and at least <see cref="NonConvergingSizeChangesPerRedraw"/> size changes per redraw. A
    /// storm of redraws alone — animations, a debugger's overlay — changes no size and is not
    /// this.
    /// </summary>
    internal static bool IsNonConverging(long layoutPasses, long renders, long sizeChanges)
        => layoutPasses == 0 && renders > 0
           && sizeChanges >= renders * NonConvergingSizeChangesPerRedraw;

    private int _hotSeconds;
    private bool _reportedThisStorm;
    private DateTime _lastReport = DateTime.MinValue;
    private long _passes, _renders, _animated, _renderMs;
    private int _unwatchedSeconds;
    private readonly Dictionary<string, int> _sizes = new(StringComparer.Ordinal);
    private string? _busiest;
    private int _busiestChanges;
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
        if (second.BusiestElement != null && second.BusiestElementChanges > _busiestChanges)
        {
            _busiest = second.BusiestElement;
            _busiestChanges = second.BusiestElementChanges;
        }

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
        var sb = new StringBuilder("LAYOUT STORM  ");
        long sizeChanges = 0;
        foreach (var n in _sizes.Values) sizeChanges += n;
        if (IsNonConverging(_passes, _renders, sizeChanges))
        {
            // Said first, because "0/s layout passes" on its own reads as "no layout work at all".
            sb.Append(inv, $"NON-CONVERGING: no layout pass ever completed, {sizeChanges / _renders} size changes a redraw ");
            sb.Append("(WPF gives up after 153 passes and starts again on the next frame");
            if (_busiest != null)
                sb.Append(inv, $"; busiest single element {_busiest}, {_busiestChanges} size changes in one second");
            sb.Append(") — ");
        }
        sb.Append(inv, $"{_passes / s}/s layout passes, {_renders / s}/s redraws ");
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
        _busiest = null;
        _busiestChanges = 0;
        _countersAtStart = null;
    }
}
