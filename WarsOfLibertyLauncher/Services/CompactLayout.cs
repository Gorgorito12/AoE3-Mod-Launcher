namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Whether the launcher draws its COMPACT layout — the one design handoff turn 36
/// (<c>docs/design_handoff_salas_laptop</c>) built for laptop screens: one header row instead of
/// two, 52-px room rows, the community activity folded to a 44-px strip.
///
/// <para><b>Why the window decides it, not the screen.</b> The problem the handoff measured is
/// that a ~1280×720 window spent almost all its height on headers and left the room list about
/// 120 px. That is a property of the WINDOW: a restored window on a large monitor has the same
/// problem as a maximised one on a laptop.</para>
///
/// <para><b>Only SPATIAL choices hang off this.</b> Everything functional the handoff changed
/// (the room code in the search box, the Connected dropdown, the per-seat bars) applies at every
/// size, by the maintainer's decision: otherwise maximising a window on a desktop would move
/// controls from one place to another.</para>
///
/// <para>Pure and WPF-free so the thresholds and the hysteresis are pinned by
/// <c>CompactLayoutTests</c> rather than argued about.</para>
/// </summary>
public static class CompactLayout
{
    /// <summary>Below this window width (DIP) the layout is compact. The handoff's own number.</summary>
    public const double MaxCompactWidth = 1500;

    /// <summary>Below this window height (DIP) the layout is compact. The handoff's own number.</summary>
    public const double MaxCompactHeight = 900;

    /// <summary>
    /// How far past a threshold the window has to go before a compact layout turns wide again.
    ///
    /// <para>Switching layouts never changes the window's size, so there is no feedback loop to
    /// break — this only stops the header flipping between one row and two on every pixel of a
    /// slow drag along the edge.</para>
    /// </summary>
    public const double Hysteresis = 8;

    /// <summary>
    /// Whether a window of <paramref name="width"/>×<paramref name="height"/> DIP is compact,
    /// given whether it was compact a moment ago.
    ///
    /// <para>A size that is not positive (or is NaN) means the window has not been laid out yet,
    /// and keeps the previous answer rather than inventing one: flipping to wide for one frame and
    /// back is a visible flash of the other layout.</para>
    /// </summary>
    public static bool IsCompact(double width, double height, bool wasCompact)
    {
        if (!(width > 0) || !(height > 0)) return wasCompact;

        return wasCompact
            ? width < MaxCompactWidth + Hysteresis || height < MaxCompactHeight + Hysteresis
            : width < MaxCompactWidth || height < MaxCompactHeight;
    }
}
