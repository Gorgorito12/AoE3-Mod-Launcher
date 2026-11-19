using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Dragging the bracket to move it (2026-11-18). A press arms only when there is something to
/// scroll and never on a scroll bar; nothing moves until the pointer has gone
/// <see cref="Threshold"/> pixels, so a short press still picks the card under it; past that the
/// scroller takes the pointer and the card's click never fires. The zoom is a layout transform, so
/// a drag moves the same distance on screen at any zoom. Pure, pinned by <c>BracketPanTests</c>.
/// </summary>
public static class BracketPan
{
    /// <summary>How far the pointer must move, in DIPs, before a press becomes a drag.</summary>
    public const double Threshold = 5;

    /// <summary>Whether a press may start a drag at all.</summary>
    public static bool MayPan(double extentWidth, double extentHeight, double viewportWidth, double viewportHeight, bool onScrollBar)
        => !onScrollBar && (extentWidth > viewportWidth + 0.5 || extentHeight > viewportHeight + 0.5);

    /// <summary>Whether this movement since the press is a drag.</summary>
    public static bool Starts(double dx, double dy)
        => Math.Abs(dx) >= Threshold || Math.Abs(dy) >= Threshold;

    /// <summary>The offsets that keep the sheet under the pointer: it moves WITH the hand.</summary>
    public static (double X, double Y) Offsets(double startX, double startY, double dx, double dy,
        double maxX, double maxY)
        => (Math.Clamp(startX - dx, 0, Math.Max(0, maxX)), Math.Clamp(startY - dy, 0, Math.Max(0, maxY)));
}
