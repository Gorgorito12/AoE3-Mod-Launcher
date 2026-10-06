using System;
using System.Globalization;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Decides, one second at a time, whether THIS PC draws too slowly for the launcher's moving
/// lights — and, once it does, says so in one line and never changes its mind for the session.
/// The wiring lives in <see cref="DiagnosticLog"/>, which acts on the answer by turning every
/// light off (<c>RankBadge.SetReducedEffects</c>); this half is pure so the rule can be tested.
///
/// <para><b>Why it exists.</b> A player's laptop with hardware rendering (WPF tier 2), 1366×768
/// and its memory at 90 % paid about 300 ms for EVERY frame. The badges' light was already capped
/// at 30 frames a second and stopped in the background, and the launcher was still unusable on
/// screen: two or three of those frames a second leave the UI thread no time for anything else.
/// No fixed list of "weak" machines could have caught that one, so the cost is measured.</para>
///
/// <para><b>The signature is FEW BUT EXPENSIVE frames</b>, and both halves are required. A fast PC
/// can spend 400 ms of a second drawing too — dragging the window's edge lays out and redraws at
/// the refresh rate — but in many cheap frames; that is a resize, not a weak machine, and turning
/// a fast PC's lights off for the session because somebody resized the window would be a loss for
/// nothing. Only seconds the player can see count: a hidden window's frames say nothing about what
/// the player is waiting for.</para>
/// </summary>
internal sealed class EffectsGovernor
{
    /// <summary>Milliseconds of a second spent drawing before that second counts as slow.</summary>
    internal const long SlowRenderMsPerSecond = 400;

    /// <summary>...and the average frame must cost at least this much: fewer than ~16 frames a
    /// second is a machine that cannot keep up, many cheaper ones are a burst of work.</summary>
    internal const long SlowFrameMs = 60;

    /// <summary>Slow seconds in a row before the lights go off: one heavy page build is not a
    /// weak machine.</summary>
    internal const int SustainSeconds = 3;

    /// <summary>WPF's rendering tier below which the lights never start: without full hardware
    /// acceleration every frame is paid for by the CPU.</summary>
    internal const int MinimumTier = 2;

    private int _slowSeconds;
    private long _slowMs;
    private long _slowRenders;

    /// <summary>Whether the lights have been turned off. Never goes back.</summary>
    public bool Tripped { get; private set; }

    /// <summary>Whether one second, on its own, counts as slow.</summary>
    internal static bool IsSlow(LayoutStormDetector.Second second)
        => second.Watched
           && second.Renders > 0
           && second.RenderMs >= SlowRenderMsPerSecond
           && second.RenderMs / second.Renders >= SlowFrameMs;

    /// <summary>
    /// Feeds one second. Returns the line to log the moment the lights go off, null otherwise.
    /// <paramref name="badgesAnimating"/> is only for the line.
    /// </summary>
    public string? Observe(LayoutStormDetector.Second second, long badgesAnimating)
    {
        if (Tripped) return null;
        if (!IsSlow(second))
        {
            _slowSeconds = 0;
            _slowMs = _slowRenders = 0;
            return null;
        }

        _slowSeconds++;
        _slowMs += second.RenderMs;
        _slowRenders += second.Renders;
        if (_slowSeconds < SustainSeconds) return null;

        Tripped = true;
        var inv = CultureInfo.InvariantCulture;
        return string.Format(inv,
            "EFFECTS REDUCED: drawing took {0} ms of every second ({1} ms a frame) for {2} s with {3} badges animating — lights off for this session",
            _slowMs / _slowSeconds, _slowMs / Math.Max(1, _slowRenders), _slowSeconds, badgesAnimating);
    }

    /// <summary>
    /// Called once at start with WPF's rendering tier. Returns the line to log when the tier alone
    /// is reason enough to keep the lights off, null otherwise.
    /// </summary>
    public string? ObserveRenderTier(int tier)
    {
        if (Tripped || tier >= MinimumTier) return null;
        Tripped = true;
        return string.Format(CultureInfo.InvariantCulture,
            "EFFECTS REDUCED: WPF rendering tier {0}, no full hardware acceleration — lights off for this session",
            tier);
    }
}
