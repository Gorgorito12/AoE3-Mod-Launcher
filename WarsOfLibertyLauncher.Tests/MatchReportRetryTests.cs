using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which failed match reports are sent again. A single request used to decide it, so a backend
/// restart at the end of a long match lost the match for good.
///
/// <para><b>The REFUSALS are the point.</b> A retry is only safe when the server certainly did not
/// store the report: a report with no recording carries no fingerprint the server could deduplicate
/// a second copy by, so retrying an AMBIGUOUS failure could put one game in everybody's history twice.
/// Every 4xx was read and refused, and a second copy cannot change that.</para>
/// </summary>
public class MatchReportRetryTests
{
    private static readonly TimeSpan Next = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    public void ARefusalBeforeTheWriteIsRetried(int status)
        => Assert.True(MatchReportRetry.ShouldRetry(status, null, false, null, Next));

    [Theory]
    [InlineData(400)]   // a malformed report — the same bytes are malformed again
    [InlineData(401)]   // the session is dead; a retry cannot revive it
    [InlineData(403)]   // the host role moved — handled as a confirmation instead
    [InlineData(404)]   // the room is gone
    [InlineData(409)]
    [InlineData(426)]   // launcher too old
    [InlineData(500)]   // the server failed somewhere — perhaps after storing it
    [InlineData(504)]   // the proxy gave up waiting — the backend may have stored it
    public void THE_ONE_THAT_MATTERS_AnythingTheServerMayHaveStoredOrRefusedIsNotRetried(int status)
        => Assert.False(MatchReportRetry.ShouldRetry(status, null, false, null, Next));

    [Fact]
    public void ARateLimitIsRetriedOnlyWhenItsWindowEndsBeforeTheNextAttempt()
    {
        Assert.True(MatchReportRetry.ShouldRetry(429, null, false, retryAfterSeconds: 3, Next));
        Assert.True(MatchReportRetry.ShouldRetry(429, null, false, retryAfterSeconds: 5, Next));
        Assert.False(MatchReportRetry.ShouldRetry(429, null, false, retryAfterSeconds: 40, Next));
        Assert.False(MatchReportRetry.ShouldRetry(429, null, false, retryAfterSeconds: null, Next));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public void ARequestThatNeverLeftIsRetried(HttpRequestError kind)
        => Assert.True(MatchReportRetry.ShouldRetry(
            new HttpRequestException(kind, "no route"), Next));

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.Unknown)]
    public void AResponseThatBrokeOffIsAmbiguousAndNotRetried(HttpRequestError kind)
        => Assert.False(MatchReportRetry.ShouldRetry(
            new HttpRequestException(kind, "broke off"), Next));

    [Fact]
    public void ATimeoutIsAmbiguousAndNotRetried()
        => Assert.False(MatchReportRetry.ShouldRetry(
            new TaskCanceledException("timed out", new TimeoutException()), Next));

    [Fact]
    public void ACancelIsNeverRetried()
    {
        Assert.False(MatchReportRetry.ShouldRetry(new OperationCanceledException(), Next));
        Assert.False(MatchReportRetry.ShouldRetry(502, null, callerCancelled: true, null, Next));
    }

    [Fact]
    public void TheRetryAfterIsReadFromTheServersDetails()
    {
        using var doc = JsonDocument.Parse("{\"retry_after\":4}");
        var details = new Dictionary<string, object?> { ["retry_after"] = doc.RootElement.GetProperty("retry_after").Clone() };
        Assert.Equal(4, MatchReportRetry.RetryAfterSeconds(details));
        Assert.True(MatchReportRetry.ShouldRetry(new LobbyApiException(429, "rate_limited", "slow down", details), Next));

        Assert.Null(MatchReportRetry.RetryAfterSeconds(null));
        Assert.Null(MatchReportRetry.RetryAfterSeconds(new Dictionary<string, object?>()));
    }

    [Fact]
    public void A403ForbiddenMeansTheHostRoleMovedAndNothingElseDoes()
    {
        Assert.True(MatchReportRetry.HostRoleMovedAway(403, "forbidden"));
        Assert.False(MatchReportRetry.HostRoleMovedAway(403, "user_banned"));
        Assert.False(MatchReportRetry.HostRoleMovedAway(401, "unauthorized"));
        Assert.False(MatchReportRetry.HostRoleMovedAway(404, "forbidden"));
    }

    /// <summary>
    /// The retries are charged to the same clock that holds the room after a match: the recording
    /// ladder (both halves) plus every retry delay must fit inside it, or the Leave button opens
    /// while the result is still being sent.
    /// </summary>
    [Fact]
    public void TheRetriesFitInsideTheResultHoldBesideTheRecordingLadder()
    {
        var ladderMs = ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.PreReport(true))
                       + ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.Continuation(true));
        Assert.True(ladderMs + MatchReportRetry.MaxAddedDelayMs < RoomMatchState.ResultGraceSeconds * 1000,
            $"ladder {ladderMs} ms + retries {MatchReportRetry.MaxAddedDelayMs} ms do not fit in "
            + $"{RoomMatchState.ResultGraceSeconds} s");
    }
}
