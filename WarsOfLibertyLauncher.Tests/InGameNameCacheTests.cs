using System;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The AoE3 profile name is read off disk once per room, not on every tick — and every rule that
/// keeps that from going wrong is a refusal: a blank read is never remembered and never erases a
/// known name.
/// </summary>
public class InGameNameCacheTests
{
    private sealed class Reader
    {
        public int Reads;
        public string? Next = "Gorgorito";
        public string? Read() { Reads++; return Next; }
    }

    private DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheProfileIsReadOncePerRoom()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader();
        for (var i = 0; i < 50; i++) Assert.Equal("Gorgorito", cache.Get("wol", r.Read));
        Assert.Equal(1, r.Reads);
    }

    [Fact]
    public void AnotherModReadsAgain()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader();
        cache.Get("wol", r.Read);
        r.Next = "gorgorito";
        Assert.Equal("gorgorito", cache.Get("improvement-mod", r.Read));
        Assert.Equal(2, r.Reads);
    }

    [Fact]
    public void ANameThatCannotBeReadIsNotRemembered()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader { Next = null };
        Assert.Null(cache.Get("wol", r.Read));
        _now += InGameNameCache.BlankRetry;
        r.Next = "Gorgorito";
        Assert.Equal("Gorgorito", cache.Get("wol", r.Read));
        Assert.Equal(2, r.Reads);
    }

    [Fact]
    public void ABlankReadIsRetriedAtMostEvery15s()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader { Next = "  " };
        cache.Get("wol", r.Read);
        _now += TimeSpan.FromSeconds(14);
        Assert.Null(cache.Get("wol", r.Read));
        Assert.Equal(1, r.Reads);
        _now += TimeSpan.FromSeconds(1);
        cache.Get("wol", r.Read);
        Assert.Equal(2, r.Reads);
    }

    [Fact]
    public void ARefreshReadsAgain()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader();
        cache.Get("wol", r.Read);
        r.Next = "Renamed";
        Assert.Equal("Renamed", cache.Get("wol", r.Read, refresh: true));
        Assert.Equal(2, r.Reads);
    }

    [Fact]
    public void AFailedRefreshKeepsTheNameAlreadyKnown()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader();
        cache.Get("wol", r.Read);
        r.Next = null;
        Assert.Equal("Gorgorito", cache.Get("wol", r.Read, refresh: true));
        Assert.Equal("Gorgorito", cache.Get("wol", r.Read));
        Assert.Equal(2, r.Reads);
    }

    [Fact]
    public void ForgetMakesTheNextGetRead()
    {
        var cache = new InGameNameCache(() => _now);
        var r = new Reader();
        cache.Get("wol", r.Read);
        cache.Forget();
        cache.Get("wol", r.Read);
        Assert.Equal(2, r.Reads);
    }
}
