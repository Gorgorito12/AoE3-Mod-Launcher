using System;
using System.Text;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the log may say about a session token. The point is that a bundle can tell the three
/// causes of "your session ended" apart — a lifetime that ran out, a signing key that changed, a
/// PC clock that is behind — WITHOUT the token ever being written down.
/// </summary>
public class SessionTokenInfoTests
{
    private const long Now = 1_800_000_000;

    /// <summary>A JWT shaped like the server's: header.payload.signature, base64url, no padding.</summary>
    private static string Jwt(long iat, long exp, string sub = "user-123", string du = "gorgorito")
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}."
            + $"{B64($"{{\"sub\":\"{sub}\",\"du\":\"{du}\",\"iat\":{iat},\"exp\":{exp}}}")}.c2lnbmF0dXJl";
    }

    [Fact]
    public void TheClaimsAreReadOffTheRealShape()
    {
        var claims = SessionTokenInfo.TryReadClaims(Jwt(Now - 3600, Now + 86_400));
        Assert.NotNull(claims);
        Assert.Equal(Now - 3600, claims!.Value.IssuedAt);
        Assert.Equal(Now + 86_400, claims.Value.ExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.!!!.c")]
    [InlineData("eyJ4IjoxfQ..sig")]
    public void AnythingElseReadsAsNothing(string? token)
        => Assert.Null(SessionTokenInfo.TryReadClaims(token));

    [Fact]
    public void APayloadWithoutAnExpiryReadsAsNothing()
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"x\",\"iat\":5}")).TrimEnd('=');
        Assert.Null(SessionTokenInfo.TryReadClaims($"h.{b64}.s"));
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the line that names a token never contains it, nor the user id or
    /// name inside its payload — only a fingerprint and the two timestamps.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheLogLineNeverCarriesTheTokenOrWhoItBelongsTo()
    {
        var token = Jwt(Now - 60, Now + 7 * 86_400, sub: "uuid-secret", du: "secret-name");
        var line = SessionTokenInfo.Describe(token, Now);

        Assert.DoesNotContain(token, line);
        Assert.DoesNotContain(token.Split('.')[1], line);
        Assert.DoesNotContain("uuid-secret", line);
        Assert.DoesNotContain("secret-name", line);
        Assert.Contains("#" + SessionTokenInfo.Fingerprint(token), line);
        Assert.Contains("UTC", line);
    }

    [Fact]
    public void TheFingerprintIsStableShortAndTellsTokensApart()
    {
        var a = SessionTokenInfo.Fingerprint("token-A");
        Assert.Equal(a, SessionTokenInfo.Fingerprint("token-A"));
        Assert.NotEqual(a, SessionTokenInfo.Fingerprint("token-B"));
        Assert.Equal(8, a.Length);
        Assert.Equal("none", SessionTokenInfo.Fingerprint(null));
        Assert.Equal("none", SessionTokenInfo.Fingerprint(""));
    }

    [Fact]
    public void ALifetimeThatRanOutIsExpired()
    {
        var claims = new SessionTokenInfo.Claims(Now - 8 * 86_400, Now - 3600);
        Assert.Equal(SessionTokenInfo.RejectionCause.Expired,
            SessionTokenInfo.ClassifyRejection(Now, claims, serverNowUnix: Now + 2));
    }

    [Fact]
    public void ExpiredOnlyByTheServersClockMeansThisPcIsBehind()
    {
        // Valid for another 10 minutes here; the server's clock is 15 minutes ahead.
        var claims = new SessionTokenInfo.Claims(Now - 86_400, Now + 600);
        var cause = SessionTokenInfo.ClassifyRejection(Now, claims, serverNowUnix: Now + 900);
        Assert.Equal(SessionTokenInfo.RejectionCause.ExpiredByServerClock, cause);
        Assert.Contains("15m", SessionTokenInfo.Explain(cause, Now, Now + 900));
        Assert.Contains("behind", SessionTokenInfo.Explain(cause, Now, Now + 900));
    }

    /// <summary>Valid by both clocks and still refused: the key changed. The incident that
    /// signed every launcher out at once looks exactly like this.</summary>
    [Fact]
    public void ValidByBothClocksAndRefusedIsASigningKeyProblem()
    {
        var claims = new SessionTokenInfo.Claims(Now - 86_400, Now + 5 * 86_400);
        var cause = SessionTokenInfo.ClassifyRejection(Now, claims, serverNowUnix: Now + 1);
        Assert.Equal(SessionTokenInfo.RejectionCause.NotExpired, cause);
        Assert.Contains("signing key", SessionTokenInfo.Explain(cause, Now, Now + 1));

        // No server clock at all (the global chat's refusal has no response to read): the same.
        Assert.Equal(SessionTokenInfo.RejectionCause.NotExpired,
            SessionTokenInfo.ClassifyRejection(Now, claims, serverNowUnix: null));
    }

    [Fact]
    public void AnUnreadableTokenSaysNothingRatherThanGuessing()
        => Assert.Equal(SessionTokenInfo.RejectionCause.Unknown,
            SessionTokenInfo.ClassifyRejection(Now, null, Now));

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(45 * 60, "45m")]
    [InlineData(2 * 3600 + 5 * 60, "2h 05m")]
    [InlineData(4 * 86_400 + 13 * 3600, "4d 13h")]
    [InlineData(-90, "1m")]
    public void SpansReadTheWayAPersonReadsThem(long seconds, string expected)
        => Assert.Equal(expected, SessionTokenInfo.Span(seconds));

    [Fact]
    public void TheExpirySaysWhetherItIsAheadOrBehind()
    {
        Assert.StartsWith("expires ", SessionTokenInfo.DescribeExpiry(Now, Now + 3600));
        Assert.Contains("(in 1h 00m)", SessionTokenInfo.DescribeExpiry(Now, Now + 3600));
        Assert.StartsWith("expired ", SessionTokenInfo.DescribeExpiry(Now, Now - 7200));
        Assert.Contains("2h 00m ago", SessionTokenInfo.DescribeExpiry(Now, Now - 7200));
        Assert.Equal("no expiry recorded", SessionTokenInfo.DescribeExpiry(Now, 0));
    }
}
