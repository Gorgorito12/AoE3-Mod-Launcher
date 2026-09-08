using System;
using System.Collections.Generic;
using System.Globalization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// How long the server said to wait, out of a <c>rate_limited</c> refusal.
///
/// <para><b>Why this exists.</b> The refusal arrived as the server's own English sentence —
/// "Too many requests — slow down." — and that reads as a wall rather than as a wait. It was
/// reported from a tournament: a few registrations and withdrawals in a row hit the ceiling,
/// and the next thing refused was CANCELLING the tournament, which comes out of the same
/// bucket. With no number on screen there was no way to tell "wait twenty seconds" from
/// "this is broken now".</para>
///
/// <para>The number is in the payload (<c>retry_after</c>, seconds) and in the
/// <c>Retry-After</c> header; the launcher was throwing both away. Pure and tiny so the
/// parsing is pinned by a test rather than by the one screen that shows it.</para>
/// </summary>
public static class RateLimitNotice
{
    /// <summary>
    /// The seconds to wait, or null when the server did not say — an older backend, or a
    /// refusal that is not about a rate limit. Null means the caller words it without a
    /// number rather than inventing one.
    ///
    /// <para>The value survives being a JSON number or a string: it reaches here through
    /// <c>System.Text.Json</c> as a <c>JsonElement</c> in a
    /// <c>Dictionary&lt;string, object?&gt;</c>, and which of the two it is depends on how the
    /// server happened to serialise it.</para>
    /// </summary>
    public static int? Seconds(IReadOnlyDictionary<string, object?>? details)
    {
        if (details == null || !details.TryGetValue("retry_after", out var raw) || raw == null)
            return null;

        double value;
        switch (raw)
        {
            case int i: value = i; break;
            case long l: value = l; break;
            case double d: value = d; break;
            case System.Text.Json.JsonElement el:
                if (el.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    if (!el.TryGetDouble(out value)) return null;
                }
                else if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (!double.TryParse(el.GetString(), NumberStyles.Any,
                                         CultureInfo.InvariantCulture, out value)) return null;
                }
                else return null;
                break;
            case string s:
                if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return null;
                break;
            default: return null;
        }

        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return null;

        // Rounded UP: telling somebody to wait 12 seconds when 12.4 remain sends them back
        // into the same refusal. Capped at an hour, because a number in the thousands is a
        // server that has stopped making sense and a sentence nobody can act on.
        var seconds = (int)Math.Ceiling(value);
        return seconds > 3600 ? null : seconds;
    }
}
