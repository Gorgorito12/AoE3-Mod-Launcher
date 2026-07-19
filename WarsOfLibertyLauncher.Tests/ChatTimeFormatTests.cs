using System;
using System.Globalization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="ChatTimeFormat"/> — the global-chat header stamp that now
/// shows the DATE, not just the time, so old messages don't read as recent.
/// A fixed "today", a fixed "yesterday" word and the English culture keep every
/// case deterministic (no machine clock / OS locale dependence).
/// </summary>
public class ChatTimeFormatTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly DateTime Today = new(2026, 7, 17);   // date-only

    [Fact]
    public void Today_ShowsTimeOnly()
    {
        var s = ChatTimeFormat.Format(new DateTime(2026, 7, 17, 10, 32, 0), Today, "Yesterday", En);
        Assert.Equal("10:32", s);
    }

    [Fact]
    public void Yesterday_ShowsYesterdayWordAndTime()
    {
        var s = ChatTimeFormat.Format(new DateTime(2026, 7, 16, 19, 3, 0), Today, "Yesterday", En);
        Assert.Equal("Yesterday 19:03", s);
    }

    [Fact]
    public void OlderSameYear_ShowsDayMonthAndTime()
    {
        var s = ChatTimeFormat.Format(new DateTime(2026, 3, 15, 19, 3, 0), Today, "Yesterday", En);
        Assert.Equal("15 Mar 19:03", s);
    }

    [Fact]
    public void OlderDifferentYear_IncludesYear()
    {
        var s = ChatTimeFormat.Format(new DateTime(2025, 7, 15, 19, 3, 0), Today, "Yesterday", En);
        Assert.Equal("15 Jul 2025 19:03", s);
    }

    [Fact]
    public void SpanishCulture_UsesSpanishMonth()
    {
        var es = CultureInfo.GetCultureInfo("es");
        var s = ChatTimeFormat.Format(new DateTime(2026, 3, 15, 19, 3, 0), Today, "Ayer", es);
        // es abbreviated March is "mar" (with or without a trailing period,
        // depending on the ICU/.NET version) — assert the stable parts.
        Assert.StartsWith("15 mar", s);
        Assert.EndsWith("19:03", s);
    }

    [Fact]
    public void FormatFull_HasFullDateAndTime()
    {
        var s = ChatTimeFormat.FormatFull(new DateTime(2026, 7, 15, 19, 3, 0), En);
        Assert.Contains("2026", s);
        Assert.Contains("19:03", s);
    }
}
