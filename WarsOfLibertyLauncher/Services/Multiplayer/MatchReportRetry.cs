using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Which failed match reports may be sent again, and what a 403 to one means.
///
/// <para><b>Why.</b> The host's <c>POST /matches</c> was a single request: any failure lost the
/// match for good, because the host then sends <c>game_ended</c>, the room reopens, and nothing
/// can found the match from the players' readings any more. A backend restart at the end of a long
/// match (nginx answers 502 while it is down), its write breaker (503), a dropped connection — each
/// cost a rated match.</para>
///
/// <para><b>The refusals are the point.</b> A report is retried ONLY when the server certainly did
/// not store it: it said so (502 from the proxy, 503, a 429 whose window ends before the next
/// attempt) or the request never reached it (no connection, no name resolution, no TLS session).
/// A timeout or a response that broke off half-way is AMBIGUOUS — the server may have stored the
/// match — and a report with no recording carries no fingerprint the server could deduplicate it
/// by, so a retry there could put one game in everybody's history twice. Those are left exactly as
/// they were. So is every 4xx: the server read the report and refused it for a reason a second copy
/// cannot change.</para>
///
/// <para>The delays are charged to the same 30-second clock that holds the room after the game
/// (<see cref="RoomMatchState.ResultGraceSeconds"/>), beside <see cref="ReplayRetryLadder"/> — a
/// test pins the sum.</para>
/// </summary>
internal static class MatchReportRetry
{
    /// <summary>The wait before the second and the third attempt.</summary>
    internal static readonly TimeSpan[] Delays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

    /// <summary>The most the retries can add to the post-game chain, in milliseconds.</summary>
    internal static int MaxAddedDelayMs
    {
        get
        {
            var sum = 0;
            foreach (var d in Delays) sum += (int)d.TotalMilliseconds;
            return sum;
        }
    }

    /// <summary>Whether a failed report may be sent again after <paramref name="nextDelay"/>.</summary>
    internal static bool ShouldRetry(Exception error, TimeSpan nextDelay)
    {
        if (error is LobbyApiException api)
            return ShouldRetry(api.Status, null, callerCancelled: false, RetryAfterSeconds(api.Details), nextDelay);
        return ShouldRetry(null, error, callerCancelled: error is OperationCanceledException && !IsTimeout(error),
            retryAfterSeconds: null, nextDelay);
    }

    /// <summary>
    /// The rule itself. <paramref name="httpStatus"/> is null when no response came back at all;
    /// <paramref name="retryAfterSeconds"/> is the server's <c>details.retry_after</c> on a 429.
    /// </summary>
    internal static bool ShouldRetry(
        int? httpStatus, Exception? error, bool callerCancelled, int? retryAfterSeconds, TimeSpan nextDelay)
    {
        if (callerCancelled) return false;

        if (httpStatus is int status)
        {
            return status switch
            {
                // The proxy could not reach the backend (restarting), or the backend refused the
                // write before doing it (its circuit breaker). Neither stored anything.
                502 or 503 => true,
                // The rate limit's window. Only worth it when it ends before the next attempt; a
                // longer wait would outlast the room's hold, and the day bucket never ends in time.
                429 => retryAfterSeconds is int wait && wait >= 0 && wait <= nextDelay.TotalSeconds,
                _ => false,
            };
        }

        // No response: retried only when the request never left this machine.
        for (var e = error; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException http)
                return http.HttpRequestError is HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError
                    or HttpRequestError.SecureConnectionError;
        }
        return false;
    }

    /// <summary>
    /// A 403 <c>forbidden</c> to a report means the host role moved to another player during the
    /// match (the server takes reports from the CURRENT host only). That player reports it; our
    /// own reading still has a place — as a confirmation. A 403 <c>user_banned</c> is not this.
    /// </summary>
    internal static bool HostRoleMovedAway(int status, string? code)
        => status == 403 && string.Equals(code, "forbidden", StringComparison.Ordinal);

    /// <summary>The server's <c>details.retry_after</c>, in seconds, when it sent one.</summary>
    internal static int? RetryAfterSeconds(IReadOnlyDictionary<string, object?>? details)
    {
        if (details == null || !details.TryGetValue("retry_after", out var raw) || raw == null) return null;
        switch (raw)
        {
            case JsonElement { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i): return i;
            case JsonElement { ValueKind: JsonValueKind.Number } n when n.TryGetDouble(out var d): return (int)Math.Ceiling(d);
            case JsonElement { ValueKind: JsonValueKind.String } s
                when int.TryParse(s.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p): return p;
            case int i: return i;
            case long l: return (int)Math.Min(l, int.MaxValue);
            case double d: return (int)Math.Ceiling(d);
            case string str when int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p): return p;
            default: return null;
        }
    }

    /// <summary>One phrase for the log line that announces a retry.</summary>
    internal static string Describe(Exception error) => error switch
    {
        LobbyApiException api => $"HTTP {api.Status} {api.Code}",
        HttpRequestException http => $"{http.HttpRequestError}: {http.Message}",
        _ => error.Message,
    };

    /// <summary>HttpClient's own timeout surfaces as a cancellation carrying a TimeoutException.</summary>
    private static bool IsTimeout(Exception error)
    {
        for (var e = error.InnerException; e != null; e = e.InnerException)
            if (e is TimeoutException) return true;
        return false;
    }
}
