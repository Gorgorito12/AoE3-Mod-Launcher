using System;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The download limit, on a fake clock. Two downloads share ONE rate, an idle bucket banks no more
/// than the burst, and "no limit" never waits.
/// </summary>
public class DownloadThrottleTests
{
    private sealed class Clock { public double Now; }

    [Fact]
    public void NoLimitNeverWaits()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(() => clock.Now, () => 0);
        Assert.Equal(TimeSpan.Zero, bucket.Take(10_000_000));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TwoStreamsShareOneRate()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(() => clock.Now, () => 1000);
        // The burst (250 bytes) is spent first; after that every byte costs 1 ms, whoever reads it.
        Assert.Equal(TimeSpan.Zero, bucket.Take(250));
        var a = bucket.Take(500);
        var b = bucket.Take(500);
        Assert.Equal(0.5, a.TotalSeconds, 3);
        Assert.Equal(1.0, b.TotalSeconds, 3);
    }

    [Fact]
    public void AnIdleBucketBanksNoMoreThanTheBurst()
    {
        var clock = new Clock();
        var bucket = new TokenBucket(() => clock.Now, () => 1000);
        bucket.Take(0);
        clock.Now += 3600;
        Assert.Equal(TimeSpan.Zero, bucket.Take(250));
        Assert.True(bucket.Take(100) > TimeSpan.Zero);
    }

    [Fact]
    public void AChangedLimitAppliesOnTheNextChunk()
    {
        var clock = new Clock();
        double rate = 0;
        var bucket = new TokenBucket(() => clock.Now, () => rate);
        Assert.Equal(TimeSpan.Zero, bucket.Take(1_000_000));
        rate = 1000;
        bucket.Take(250);
        Assert.True(bucket.Take(1000) >= TimeSpan.FromSeconds(0.99));
    }
}
