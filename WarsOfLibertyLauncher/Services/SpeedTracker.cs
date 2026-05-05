using System;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Computes download speed and ETA from a stream of byte-count samples.
/// Uses an exponential moving average to smooth out noise without lagging
/// behind real network condition changes.
/// </summary>
public class SpeedTracker
{
    private const double SmoothingAlpha = 0.3;
    private const double MinSampleIntervalSeconds = 0.5;

    private DateTime _lastSampleTime;
    private long _lastBytes;
    private double _emaBytesPerSecond;
    private bool _hasFirstSample;

    /// <summary>Smoothed bytes-per-second. Zero until at least two samples have arrived.</summary>
    public double BytesPerSecond { get; private set; }

    public void Reset()
    {
        _lastSampleTime = default;
        _lastBytes = 0;
        _emaBytesPerSecond = 0;
        BytesPerSecond = 0;
        _hasFirstSample = false;
    }

    /// <summary>Feed in the latest cumulative byte count.</summary>
    public void Sample(long currentBytes)
    {
        var now = DateTime.UtcNow;

        if (!_hasFirstSample)
        {
            _lastSampleTime = now;
            _lastBytes = currentBytes;
            _hasFirstSample = true;
            return;
        }

        var elapsed = (now - _lastSampleTime).TotalSeconds;
        if (elapsed < MinSampleIntervalSeconds) return;

        var delta = currentBytes - _lastBytes;
        if (delta < 0) delta = 0;       // happens if a fresh patch resets the counter

        var instant = delta / elapsed;
        _emaBytesPerSecond = _emaBytesPerSecond == 0
            ? instant
            : SmoothingAlpha * instant + (1 - SmoothingAlpha) * _emaBytesPerSecond;

        BytesPerSecond = _emaBytesPerSecond;
        _lastSampleTime = now;
        _lastBytes = currentBytes;
    }

    /// <summary>Estimate seconds remaining to download <paramref name="bytesRemaining"/>.</summary>
    public TimeSpan? EstimateTimeRemaining(long bytesRemaining)
    {
        if (BytesPerSecond < 1024) return null;     // not enough data yet
        var seconds = bytesRemaining / BytesPerSecond;
        if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return null;
        return TimeSpan.FromSeconds(seconds);
    }
}
