using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The download limit in Settings (2026-11-18): ONE bucket shared by every download the launcher
/// makes, so two at once share the limit rather than each taking it. The limit is read on every
/// chunk, so a change applies within a fraction of a second; an idle bucket banks no more than
/// <see cref="TokenBucket.BurstSeconds"/> of traffic, so a pause cannot be spent as a spike.
///
/// <para>Throttled: installs, WoL patches, GitHubReleases payloads and delta patches, the
/// user-data seed, translations, Radmin, replays, the in-app self-update. Exempt on purpose: the
/// startup self-update (the player is looking at a splash and nothing else), the granular
/// repair's directory reads (their 30-s bound must keep meaning something), metadata and images.</para>
/// </summary>
internal static class DownloadThrottle
{
    private static int _limitKbps;
    private static readonly TokenBucket Shared = new(() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
        () => Volatile.Read(ref _limitKbps) * 1024.0);

    /// <summary>KB/s; 0 = no limit. Set at start and whenever Settings changes it.</summary>
    internal static int LimitKbps
    {
        get => Volatile.Read(ref _limitKbps);
        set => Volatile.Write(ref _limitKbps, Math.Max(0, value));
    }

    /// <summary>Waits as long as <paramref name="bytes"/> just read must take under the limit.</summary>
    internal static async ValueTask WaitAsync(int bytes, CancellationToken ct)
    {
        var wait = Shared.Take(bytes);
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
    }
}

/// <summary>A token bucket with a clock and a limit read on every call (pure enough to pin).</summary>
internal sealed class TokenBucket
{
    /// <summary>How many seconds of traffic an idle bucket may bank.</summary>
    internal const double BurstSeconds = 0.25;

    private readonly Func<double> _nowSeconds;
    private readonly Func<double> _bytesPerSecond;
    private readonly object _gate = new();
    private double _tokens;
    private double _last = double.NaN;

    internal TokenBucket(Func<double> nowSeconds, Func<double> bytesPerSecond)
    {
        _nowSeconds = nowSeconds;
        _bytesPerSecond = bytesPerSecond;
    }

    /// <summary>Spends <paramref name="bytes"/> and answers how long to wait; zero when unlimited.</summary>
    internal TimeSpan Take(int bytes)
    {
        var rate = _bytesPerSecond();
        lock (_gate)
        {
            var now = _nowSeconds();
            if (rate <= 0)
            {
                _last = now;
                _tokens = 0;
                return TimeSpan.Zero;
            }
            var burst = rate * BurstSeconds;
            if (double.IsNaN(_last)) _tokens = burst;
            else _tokens = Math.Min(burst, _tokens + (now - _last) * rate);
            _last = now;
            _tokens -= bytes;
            return _tokens >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-_tokens / rate);
        }
    }
}
