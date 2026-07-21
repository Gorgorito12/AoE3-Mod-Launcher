using System;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RoomAgeFormat"/> — the "how long has this room been open"
/// formatter. Two things worth pinning: the SQLite-UTC parse (a naive parse reads
/// the zone-less timestamp as LOCAL and the counter drifts by the host's offset),
/// and the compact duration buckets.
/// </summary>
public class RoomAgeFormatTests
{
    [Fact]
    public void ParseCreatedUtc_SqliteSpaceFormat_IsUtc()
    {
        var dt = RoomAgeFormat.ParseCreatedUtc("2026-07-18 14:22:00");
        Assert.NotNull(dt);
        Assert.Equal(DateTimeKind.Utc, dt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 7, 18, 14, 22, 0, DateTimeKind.Utc), dt.Value);
    }

    [Fact]
    public void ParseCreatedUtc_IsoZ_MatchesSpaceFormat()
    {
        var space = RoomAgeFormat.ParseCreatedUtc("2026-07-18 14:22:00");
        var iso = RoomAgeFormat.ParseCreatedUtc("2026-07-18T14:22:00Z");
        Assert.Equal(space, iso);
    }

    [Fact]
    public void ParseCreatedUtc_EmptyOrGarbage_IsNull()
    {
        Assert.Null(RoomAgeFormat.ParseCreatedUtc(""));
        Assert.Null(RoomAgeFormat.ParseCreatedUtc("   "));
        Assert.Null(RoomAgeFormat.ParseCreatedUtc(null));
        Assert.Null(RoomAgeFormat.ParseCreatedUtc("not a date"));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(45, "45 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(300, "5 min")]
    [InlineData(3599, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(4800, "1 h 20 min")]
    [InlineData(7200, "2 h")]
    [InlineData(86400, "1 d")]
    [InlineData(97200, "1 d 3 h")]
    public void Compact_Buckets(int seconds, string expected)
    {
        Assert.Equal(expected, RoomAgeFormat.Compact(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Compact_Negative_ClampsToZero()
    {
        Assert.Equal("0 s", RoomAgeFormat.Compact(TimeSpan.FromSeconds(-30)));
    }

    // ---- Coarse (the "last played" formatter) ----
    // Single unit on purpose: "1 d 3 h" is useful for a live room, noise for
    // "you played this yesterday".

    [Theory]
    [InlineData(0, "1 min")]        // just now must never read "0 min"
    [InlineData(5, "1 min")]
    [InlineData(59, "1 min")]
    [InlineData(60, "1 min")]
    [InlineData(300, "5 min")]
    [InlineData(3599, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(4800, "1 h")]       // no second unit, unlike Compact's "1 h 20 min"
    [InlineData(7200, "2 h")]
    [InlineData(86399, "23 h")]
    [InlineData(86400, "1 d")]
    [InlineData(97200, "1 d")]      // Compact would say "1 d 3 h"
    [InlineData(259200, "3 d")]
    public void Coarse_Buckets(int seconds, string expected)
    {
        Assert.Equal(expected, RoomAgeFormat.Coarse(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Coarse_Negative_ClampsToZero()
    {
        // A clock skew that puts "last played" in the future must not print a
        // negative age.
        Assert.Equal("1 min", RoomAgeFormat.Coarse(TimeSpan.FromSeconds(-30)));
    }
}
