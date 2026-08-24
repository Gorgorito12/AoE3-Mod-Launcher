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
    /// The label for the chat's centred day divider: "TODAY" / "YESTERDAY" / "7 AGO".
    ///
    /// <para>This is what remains of the old per-message <c>Format</c>, which stamped the
    /// date onto every line. The design handoff replaced that with a centred divider, so
    /// the date logic lives here and the message stamp became a bare time. Kept in this
    /// service rather than inlined in the UI so the boundaries — when a day stops being
    /// "yesterday", when the year starts being worth printing — stay testable off the
    /// STA thread, the same reason the rest of this class exists.</para>
    ///
    /// <para>Words for today and yesterday rather than a date, because a date on the
    /// messages you are reading right now is noise — and in a live chat that is the
    /// common case. The reference only ever shows an older day, so it is silent on these
    /// two; naming them is the reading that keeps the divider useful.</para>
    /// </summary>
    public static string DateLabel(
        DateTime day, DateTime today, string todayWord, string yesterdayWord, CultureInfo culture)
    {
        var date = day.Date;
        var todayDate = today.Date;

        if (date == todayDate) return todayWord;
        if (date == todayDate.AddDays(-1)) return yesterdayWord;

        return date.Year == todayDate.Year
            ? day.ToString("d MMM", culture)
            : day.ToString("d MMM yyyy", culture);
    }

    /// <summary>Full date + time for the hover tooltip, e.g. "Monday 15 Jul 2026, 19:03".</summary>
    public static string FormatFull(DateTime local, CultureInfo culture)
        => local.ToString("dddd d MMM yyyy, HH:mm", culture);
}
