using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What a rating is allowed to look like. As everywhere in this corner of the code,
/// the REFUSALS are the point: the server hands every new player 1500, and the whole
/// job of these two functions is to keep that number from being painted as though
/// someone had earned it.
/// </summary>
public class RatingDisplayTests
{
    [Fact]
    public void ADeltaAlwaysCarriesItsSign()
    {
        // "12" and "+12" read as different claims about the same match.
        Assert.Equal("+12", RatingDisplay.FormatDelta(12));
        Assert.Equal("-12", RatingDisplay.FormatDelta(-12));
    }

    [Fact]
    public void AnUnknownDeltaIsNothing_NotZero()
    {
        // The case that matters. A match stored without being rated, or reported to a
        // backend too old to answer, has no delta — and writing "+0" there would claim
        // the game was played for nothing, which is a different statement from not
        // knowing what it did.
        Assert.Null(RatingDisplay.FormatDelta(null));
    }

    [Fact]
    public void AZeroDeltaIsStillShown()
    {
        // The mirror of the case above, and the reason the two must not be collapsed:
        // when both ends of the change are known, "it moved nothing" is a fact worth
        // stating. Don't "tidy" this into null.
        Assert.Equal("+0", RatingDisplay.FormatDelta(0));
    }

    [Fact]
    public void ASettledRatingIsShown()
    {
        Assert.True(RatingDisplay.ShouldShow(1712, 78));
    }

    [Fact]
    public void AProvisionalRatingIsWithheld()
    {
        // A brand-new player: the server's default 1500 at the maximum deviation. This
        // is exactly the number that must never appear beside a name in the roster.
        Assert.False(RatingDisplay.ShouldShow(1500, 350));
    }

    [Fact]
    public void TheProvisionalBoundaryMatchesTheBackend()
    {
        // MatchOutcomeView.IsProvisional compares with a strict >, and the leaderboard
        // query filters with rd <= 110, so exactly 110 counts as settled on both sides.
        // If these two ever disagree, a player is ranked in the table and blank in the
        // room at the same instant.
        Assert.True(RatingDisplay.ShouldShow(1600, MatchOutcomeView.ProvisionalRd));
        Assert.False(RatingDisplay.ShouldShow(1600, MatchOutcomeView.ProvisionalRd + 0.1));
    }

    [Fact]
    public void HalfAnAnswerIsNoAnswer()
    {
        // A rating with no deviation comes from a backend that only tells half the
        // story — and the missing half is precisely what decides whether the number
        // means anything yet. In that doubt, nothing is shown.
        Assert.False(RatingDisplay.ShouldShow(1712, null));
        Assert.False(RatingDisplay.ShouldShow(null, 78));
        Assert.False(RatingDisplay.ShouldShow(null, null));
    }
}
