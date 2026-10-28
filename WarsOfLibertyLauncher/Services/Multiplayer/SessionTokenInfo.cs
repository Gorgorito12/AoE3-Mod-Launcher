using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What a diagnostic bundle needs to know about a session token — without the token.
///
/// <para><b>Why it exists.</b> "Your multiplayer session ended" has three causes behind it and the
/// log could tell none of them apart: the token's lifetime ran out (the common one — the server
/// mints seven days and nothing renewed it), the server's signing key changed (every token dies at
/// once), or this PC's clock is behind the server's near the end of a token's life. All three come
/// back as the same 401. The token's own expiry, read beside both clocks, separates them.</para>
///
/// <para><b>Never the token.</b> The log carries a fingerprint — a hash prefix, enough to tell "the
/// same token" from "a different one" across lines and processes. Of the payload only the two
/// timestamps are read; <c>sub</c> and <c>du</c> are never touched.</para>
///
/// <para>The payload is read WITHOUT verifying the signature. The launcher cannot (the key is the
/// server's) and does not need to: nothing here decides anything, it only describes.</para>
/// </summary>
internal static class SessionTokenInfo
{
    /// <summary>The two timestamps a session token carries, in Unix seconds. 0 = absent.</summary>
    internal readonly record struct Claims(long IssuedAt, long ExpiresAt);

    /// <summary>Why the server refused a token, as far as the two clocks can tell.</summary>
    internal enum RejectionCause
    {
        /// <summary>The token is not readable, so nothing can be said.</summary>
        Unknown,
        /// <summary>Its expiry has passed by this PC's clock — the lifetime ran out.</summary>
        Expired,
        /// <summary>Still valid by this PC's clock, expired by the server's: the PC clock is behind.</summary>
        ExpiredByServerClock,
        /// <summary>Valid by both clocks: the server no longer accepts the signature.</summary>
        NotExpired,
    }

    /// <summary>The first eight hex digits of the token's SHA-256, or <c>none</c>.</summary>
    internal static string Fingerprint(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "none";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>The token's <c>iat</c> and <c>exp</c>, or null when it is not a readable JWT.</summary>
    internal static Claims? TryReadClaims(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[1].Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expiresAt)) return null;
            var issuedAt = root.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var i) ? i : 0;
            return new Claims(issuedAt, expiresAt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Why a rejected token was refused. <paramref name="serverNowUnix"/> is the server's clock from
    /// the response's <c>Date</c> header, when there was one.
    /// </summary>
    internal static RejectionCause ClassifyRejection(long nowUnix, Claims? claims, long? serverNowUnix)
    {
        if (claims is not { ExpiresAt: > 0 } c) return RejectionCause.Unknown;
        if (c.ExpiresAt <= nowUnix) return RejectionCause.Expired;
        if (serverNowUnix is long server && c.ExpiresAt <= server) return RejectionCause.ExpiredByServerClock;
        return RejectionCause.NotExpired;
    }

    /// <summary>The cause in words, for the log.</summary>
    internal static string Explain(RejectionCause cause, long nowUnix, long? serverNowUnix) => cause switch
    {
        RejectionCause.Expired => "the token's lifetime ran out (nothing renewed it)",
        RejectionCause.ExpiredByServerClock =>
            $"expired by the server's clock only — this PC's clock is {Span(Math.Abs((serverNowUnix ?? nowUnix) - nowUnix))} behind",
        RejectionCause.NotExpired =>
            "the token has not expired by either clock — the server no longer accepts its signature "
            + "(its signing key changed, or the token came from another server)",
        _ => "the token could not be read",
    };

    /// <summary>One line for the log: fingerprint, when it was issued and when it expires.</summary>
    internal static string Describe(string? token, long nowUnix)
    {
        var fingerprint = Fingerprint(token);
        if (TryReadClaims(token) is not { } c) return $"token #{fingerprint} (unreadable)";
        var issued = c.IssuedAt > 0 ? $"issued {FormatUtc(c.IssuedAt)}, " : "";
        return $"token #{fingerprint}, {issued}{DescribeExpiry(nowUnix, c.ExpiresAt)}";
    }

    /// <summary>"expires 2026-11-03 21:14 UTC (in 5d 3h)" / "expired … (2h 05m ago)".</summary>
    internal static string DescribeExpiry(long nowUnix, long expiresAtUnix)
    {
        if (expiresAtUnix <= 0) return "no expiry recorded";
        var at = FormatUtc(expiresAtUnix);
        var delta = expiresAtUnix - nowUnix;
        return delta >= 0 ? $"expires {at} (in {Span(delta)})" : $"expired {at} ({Span(-delta)} ago)";
    }

    /// <summary>Unix seconds as an absolute UTC time — the log's own lines carry no date.</summary>
    internal static string FormatUtc(long unixSeconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return $"@{unixSeconds}";
        }
    }

    /// <summary>A duration the way a person reads it: "4d 13h", "2h 05m", "45m", "30s".</summary>
    internal static string Span(long seconds)
    {
        if (seconds < 0) seconds = -seconds;
        var t = TimeSpan.FromSeconds(Math.Min(seconds, (long)TimeSpan.MaxValue.TotalSeconds));
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes:00}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m";
        return $"{t.Seconds}s";
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
