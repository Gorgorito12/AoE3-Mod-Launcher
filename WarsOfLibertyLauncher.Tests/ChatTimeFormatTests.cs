using System;
using System.Globalization;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="ChatTimeFormat"/> — the global chat's day divider and the full
/// timestamp behind a message's hover tooltip. A fixed "today", fixed words and the
/// English culture keep every case deterministic (no machine clock / OS locale
/// dependence).
///
/// <para>These used to pin <c>Format</c>, the per-message stamp that carried the date
/// on every line. The design handoff replaced that with a centred divider, so the
/// rules moved to <see cref="ChatTimeFormat.DateLabel"/> and the cases moved with
/// them — same today / yesterday / same-year / older-year boundaries, now asserted
/// where they are actually reachable.</para>
/// </summary>
public class ChatTimeFormatTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private static readonly DateTime Today = new(2026, 7, 17);   // date-only

    private static string Label(DateTime day, CultureInfo? culture = null)
        => ChatTimeFormat.DateLabel(day, Today, "Today", "Yesterday", culture ?? En);

    [Fact]
    public void Today_IsNamed_NotDated()
    {
        // A date on the messages you are reading right now is noise, and in a live
        // chat it is the common case.
        Assert.Equal("Today", Label(new DateTime(2026, 7, 17, 10, 32, 0)));
    }

    [Fact]
    public void Yesterday_IsNamedToo()
    {
        Assert.Equal("Yesterday", Label(new DateTime(2026, 7, 16, 19, 3, 0)));
    }

    [Fact]
    public void OlderSameYear_ShowsDayAndMonth_WithoutTheYear()
    {
        Assert.Equal("15 Mar", Label(new DateTime(2026, 3, 15, 19, 3, 0)));
    }

    [Fact]
    public void OlderDifferentYear_IncludesTheYear()
    {
        // Without this a message from last March and one from this March would carry
        // the same divider.
        Assert.Equal("15 Jul 2025", Label(new DateTime(2025, 7, 15, 19, 3, 0)));
    }

    [Fact]
    public void TheBoundaryIsTheDay_NotTheElapsedTime()
    {
        // One minute past midnight is "today"; one minute before it is "yesterday",
        // however few minutes separate them.
        Assert.Equal("Today", Label(new DateTime(2026, 7, 17, 0, 1, 0)));
        Assert.Equal("Yesterday", Label(new DateTime(2026, 7, 16, 23, 59, 0)));
    }

    [Fact]
    public void SpanishCulture_UsesSpanishMonth()
    {
        var es = CultureInfo.GetCultureInfo("es");
        // es abbreviated March is "mar" (with or without a trailing period,
        // depending on the ICU/.NET version) — assert the stable part.
        Assert.StartsWith("15 mar", Label(new DateTime(2026, 3, 15, 19, 3, 0), es));
    }

    [Fact]
    public void FormatFull_HasFullDateAndTime()
    {
        // Still reachable: it is the tooltip on a message's timestamp, which is where
        // the precise moment went when the visible stamp became a bare time.
        var s = ChatTimeFormat.FormatFull(new DateTime(2026, 7, 15, 19, 3, 0), En);
        Assert.Contains("2026", s);
        Assert.Contains("19:03", s);
    }
}
