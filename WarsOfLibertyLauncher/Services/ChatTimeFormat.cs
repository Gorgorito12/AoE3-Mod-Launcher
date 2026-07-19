using System;
using System.Globalization;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Formats a chat message timestamp for the global chat header — the DATE is
/// shown, not just the time, so old messages don't read as recent (and the
/// midnight wrap-around, where an evening message looks "later" than a morning
/// one, stops being confusing).
///
/// Pure + WPF-free ON PURPOSE so it can be unit-tested off the UI thread
/// (<c>ChatTimeFormatTests</c>) — same rationale as <see cref="PathDisplay"/>:
/// it must NOT live as a static on <c>MultiplayerTab</c>, whose static brush
/// fields throw on an STA-less test thread. The caller supplies "today", the
/// localized "yesterday" word and the culture, so the core stays deterministic.
/// </summary>
public static class ChatTimeFormat
{
    /// <summary>
    /// Compact header stamp: today → <c>HH:mm</c>; yesterday →
    /// <c>{yesterdayWord} HH:mm</c>; same year → <c>d MMM HH:mm</c>; otherwise
    /// <c>d MMM yyyy HH:mm</c>. Month names come from <paramref name="culture"/>.
    /// </summary>
    /// <param name="local">The message time in LOCAL time.</param>
    /// <param name="today">Local <see cref="DateTime.Today"/> (date-only).</param>
    public static string Format(DateTime local, DateTime today, string yesterdayWord, CultureInfo culture)
    {
        var time = local.ToString("HH:mm", culture);
        var date = local.Date;
        var todayDate = today.Date;

        if (date == todayDate) return time;
        if (date == todayDate.AddDays(-1)) return $"{yesterdayWord} {time}";

        var datePart = date.Year == todayDate.Year
            ? local.ToString("d MMM", culture)
            : local.ToString("d MMM yyyy", culture);
        return $"{datePart} {time}";
    }

    /// <summary>Full date + time for the hover tooltip, e.g. "Monday 15 Jul 2026, 19:03".</summary>
    public static string FormatFull(DateTime local, CultureInfo culture)
        => local.ToString("dddd d MMM yyyy, HH:mm", culture);
}
