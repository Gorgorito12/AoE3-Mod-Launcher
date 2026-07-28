using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the win rate shown on the profile.
///
/// <para><b>The case that matters is the denominator.</b> A match is stored with
/// <c>result = 0.5</c> whenever its outcome could not be read — no recording, a team game, a
/// skirmish, or any match reported before the launcher could read one — and on a real account
/// that is most of them. Dividing wins by games PLAYED would tell someone who won 3 of their 4
/// decided games that they win 3&#160;% of the time.</para>
///
/// <para>The other half is refusing to answer: no decided games means no rate, never a 0&#160;%.
/// That is the same rule as the History badge, which shows nothing rather than "Draw".</para>
/// </summary>
public class PlayerStandingTests
{
    [Fact]
    public void RateIgnoresGamesWhoseResultWasNeverKnown()
    {
        // 3 wins, 1 loss, and 36 matches nobody could rule on: 75%, not 8%.
        Assert.Equal(75, PlayerStanding.WinPercent(wins: 3, losses: 1));
        Assert.Equal(4, PlayerStanding.DecidedGames(wins: 3, losses: 1));
    }

    [Fact]
    public void NoDecidedGamesGivesNoRateAtAll()
    {
        // The player has played — every match simply had no readable result. Showing 0%
        // would read as "loses every game", which is the opposite of "we don't know".
        Assert.Null(PlayerStanding.WinPercent(0, 0));
    }

    [Fact]
    public void AnOlderBackendLooksExactlyLikeNoDecidedGames()
    {
        // wins/losses are absent from the JSON before this feature shipped, so they
        // deserialize to 0 — and land on the same silence with no special case.
        Assert.Null(PlayerStanding.WinPercent(0, 0));
    }

    [Theory]
    [InlineData(1, 0, 100)]
    [InlineData(0, 1, 0)]      // a real 0%: he lost the one game that was decided
    [InlineData(1, 1, 50)]
    [InlineData(1, 2, 33)]
    [InlineData(2, 1, 67)]     // rounds away from zero, so 66.67 → 67
    public void RoundsToWholePercents(int wins, int losses, int expected)
        => Assert.Equal(expected, PlayerStanding.WinPercent(wins, losses));

    [Fact]
    public void NegativeCountsCannotProduceANonsenseRate()
    {
        // These arrive over the wire. A negative denominator or a rate above 100 would be
        // worse than useless on screen.
        Assert.Equal(100, PlayerStanding.WinPercent(3, -1));
        Assert.Null(PlayerStanding.WinPercent(-1, -1));
        Assert.Equal(0, PlayerStanding.DecidedGames(-5, -5));
    }
}
