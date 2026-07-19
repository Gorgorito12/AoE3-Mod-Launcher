using System;
using System.Globalization;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Formats how long a lobby has been open, from the backend's <c>created_at</c>.
///
/// Pure + WPF-free ON PURPOSE so it's unit-testable off the UI thread
/// (<c>RoomAgeFormatTests</c>) — same rationale as <see cref="ChatTimeFormat"/> /
/// <see cref="PathDisplay"/>. Two traps it handles: (1) SQLite's
/// <c>datetime('now')</c> yields <c>'YYYY-MM-DD HH:MM:SS'</c> in UTC with NO zone
/// marker, which a naive parse reads as LOCAL and drifts by the host's offset (the
/// exact bug the Discord backend's <c>normaliseSqliteTimestamp</c> guards); (2) the
/// compact duration units (s/min/h/d) read the same in EN and ES, so it needs no
/// culture — only the surrounding "open for {0}" / "abierta hace {0}" wrapper is
/// localized by the caller.
/// </summary>
public static class RoomAgeFormat
{
    /// <summary>
    /// Parse the backend <c>created_at</c> as UTC. Accepts the SQLite
    /// <c>'YYYY-MM-DD HH:MM:SS'</c> (space, no zone → assumed UTC) and ISO
    /// <c>'...T...Z'</c> forms. Returns null for empty/garbage input.
    /// </summary>
    public static DateTime? ParseCreatedUtc(string? createdAt)
    {
        if (string.IsNullOrWhiteSpace(createdAt)) return null;
        if (DateTimeOffset.TryParse(
                createdAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.UtcDateTime;
        }
        return null;
    }

    /// <summary>
    /// Compact elapsed duration: <c>"45 s"</c> / <c>"5 min"</c> /
    /// <c>"1 h 20 min"</c> / <c>"2 h"</c> / <c>"1 d 3 h"</c>. Never negative
    /// (clamped to 0). Units are shared EN/ES, so no culture is needed.
    /// </summary>
    public static string Compact(TimeSpan elapsed)
    {
        long s = (long)elapsed.TotalSeconds;
        if (s < 0) s = 0;
        if (s < 60) return $"{s} s";
        if (s < 3600) return $"{s / 60} min";
        if (s < 86400)
        {
            long h = s / 3600;
            long m = (s % 3600) / 60;
            return m > 0 ? $"{h} h {m} min" : $"{h} h";
        }
        long days = s / 86400;
        long hrs = (s % 86400) / 3600;
        return hrs > 0 ? $"{days} d {hrs} h" : $"{days} d";
    }
}
