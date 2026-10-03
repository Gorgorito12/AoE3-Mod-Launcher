using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The win streak 🔥 (design 55): wins in a row on one ladder, cut by a loss or by 14 days without
/// a rated match. The SERVER counts it (<c>streak_current</c>, <c>streak_ended_at</c>); this class
/// only decides how it is drawn.
/// </summary>
public static class StreakView
{
    /// <summary>The pill 🔥N appears from this many wins in a row.</summary>
    public const int FlameFrom = 3;

    public enum Look
    {
        /// <summary>No streak: "—".</summary>
        None,
        /// <summary>One or two wins: the number in white, no flame.</summary>
        Plain,
        /// <summary>Three or more: the 🔥N pill.</summary>
        Flame,
    }

    public static Look LookOf(int current)
        => current >= FlameFrom ? Look.Flame : current > 0 ? Look.Plain : Look.None;

    /// <summary>Whether a ladder row carries the pill (only from three).</summary>
    public static bool ShowsPill(int current) => current >= FlameFrom;

    /// <summary>
    /// The date the current streak ended for lack of play, when that is what happened: the
    /// server sends it only then (a streak cut by a loss has none — the loss is in the History).
    /// </summary>
    public static DateTimeOffset? ExpiredOn(string? streakEndedAt)
        => DateTimeOffset.TryParse(streakEndedAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
