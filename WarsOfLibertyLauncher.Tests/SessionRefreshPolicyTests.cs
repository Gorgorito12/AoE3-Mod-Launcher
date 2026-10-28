using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// When the launcher asks to renew its seven-day session token, and when it warns before a room.
/// The renewal is the cure for "your session ended" every seven days; the brakes keep it to one
/// small request every few days, and off a server that does not have the route yet.
/// </summary>
public class SessionRefreshPolicyTests
{
    private const long Now = 1_800_000_000;
    private const long Day = 86_400;
    private const long Hour = 3600;

    [Fact]
    public void AFreshTokenIsLeftAlone()
        => Assert.False(SessionRefreshPolicy.IsDue(Now, Now + 6 * Day, lastAttemptUnix: 0, unsupportedUntilUnix: 0));

    [Fact]
    public void ATokenWithUnderThreeDaysLeftIsRenewed()
        => Assert.True(SessionRefreshPolicy.IsDue(Now, Now + 3 * Day - 1, 0, 0));

    /// <summary>An expired token is still tried: a server with a grace rescues it, and one without
    /// answers 401 — the same sign-out the next authenticated call would have caused.</summary>
    [Fact]
    public void AnExpiredTokenIsStillTried()
        => Assert.True(SessionRefreshPolicy.IsDue(Now, Now - Hour, 0, 0));

    [Fact]
    public void NoSessionMeansNothingToRenew()
        => Assert.False(SessionRefreshPolicy.IsDue(Now, 0, 0, 0));

    [Fact]
    public void AFailedAttemptIsNotRepeatedForHalfAnHour()
    {
        Assert.False(SessionRefreshPolicy.IsDue(Now, Now + Hour, lastAttemptUnix: Now - 29 * 60, unsupportedUntilUnix: 0));
        Assert.True(SessionRefreshPolicy.IsDue(Now, Now + Hour, lastAttemptUnix: Now - 31 * 60, unsupportedUntilUnix: 0));
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a server without the route is not asked again on every tick — and it is
    /// asked again later, because a backend can be updated while a tray launcher runs for days.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AServerWithoutTheRouteIsLeftAloneForAWhileNotForever()
    {
        Assert.False(SessionRefreshPolicy.IsDue(Now, Now + Hour, 0, unsupportedUntilUnix: Now + 1));
        Assert.True(SessionRefreshPolicy.IsDue(Now, Now + Hour, 0, unsupportedUntilUnix: Now));
        Assert.Equal(6, SessionRefreshPolicy.RetryUnsupportedAfter.TotalHours);
    }

    [Fact]
    public void ARoomIsWarnedAboutOnlyInTheLastSixHours()
    {
        Assert.False(SessionRefreshPolicy.ShouldWarnBeforeRoom(Now, Now + 6 * Hour));
        Assert.True(SessionRefreshPolicy.ShouldWarnBeforeRoom(Now, Now + 6 * Hour - 1));
        Assert.True(SessionRefreshPolicy.ShouldWarnBeforeRoom(Now, Now - 10));
        Assert.False(SessionRefreshPolicy.ShouldWarnBeforeRoom(Now, 0));
    }

    /// <summary>Renewal must start well before the warning could ever fire, or a launcher talking to a
    /// server that renews would still warn people.</summary>
    [Fact]
    public void RenewalStartsLongBeforeTheWarning()
        => Assert.True(SessionRefreshPolicy.RefreshWhenLeft > SessionRefreshPolicy.WarnBeforeRoomWhenLeft * 4);
}
