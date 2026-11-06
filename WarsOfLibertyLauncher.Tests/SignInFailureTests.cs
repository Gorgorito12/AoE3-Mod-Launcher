using System;
using System.Net.Http;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>What the sign-in window makes of a failure and of the link (design 97).</summary>
public class SignInFailureTests
{
    private static LobbyApiException Api(int status, string code) => new(status, code, "m", null);

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheServersWayOfSayingExpiredIsAMissingHandle()
    {
        Assert.Equal(SignInFailureKind.Expired, SignInFailure.Classify(Api(404, "not_found"), waitingForBrowser: true));
        Assert.Equal(SignInFailureKind.Expired, SignInFailure.Classify(Api(408, "device_flow_timeout"), true));
        Assert.Equal(SignInFailureKind.Expired, SignInFailure.Classify(Api(400, "expired_token"), true));
    }

    [Fact]
    public void A404WhileAskingForTheLinkIsNotAnExpiredLink()
        => Assert.Equal(SignInFailureKind.Refused, SignInFailure.Classify(Api(404, "not_found"), false));

    [Fact]
    public void AnAnswerIsARefusalAndNoAnswerIsNot()
    {
        Assert.Equal(SignInFailureKind.Refused, SignInFailure.Classify(Api(500, "internal"), true));
        Assert.Equal(SignInFailureKind.Unreachable, SignInFailure.Classify(new HttpRequestException("x"), true));
        Assert.Equal(SignInFailureKind.Unreachable, SignInFailure.Classify(new TimeoutException(), false));
    }

    [Fact]
    public void TheDetailKeepsEveryInnerException()
    {
        var ex = new HttpRequestException("outer", new InvalidOperationException("inner"));
        Assert.Equal("HttpRequestException: outer || InvalidOperationException: inner", SignInFailure.Describe(ex));
    }

    [Theory]
    [InlineData("https://discord.com/oauth2/authorize?a=b", "https://discord.com", "/oauth2/authorize?a=b")]
    [InlineData("https://discord.com", "https://discord.com", "")]
    [InlineData("not a link", "", "not a link")]
    public void TheLinkSplitsAfterItsHost(string url, string host, string rest)
        => Assert.Equal((host, rest), SignInFailure.SplitLink(url));

    [Theory]
    [InlineData(900, 15)]
    [InlineData(61, 2)]
    public void TheDurationIsInWholeMinutes(int seconds, int minutes)
        => Assert.Equal(minutes, SignInFailure.LinkMinutes(seconds));

    [Fact]
    public void NoDurationFromTheServerMeansNoNumber()
        => Assert.Null(SignInFailure.LinkMinutes(0));
}
