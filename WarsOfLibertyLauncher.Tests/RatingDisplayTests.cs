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
    public void ARatingIsShownWheneverThereIsOne()
    {
        // The provisional gate is gone, deliberately. It withheld the server's starting
        // 1500 so a placeholder could not pass as earned skill — but EVERY player who has
        // not played is on exactly 1500, so a number everybody starts from, shown to
        // everybody, claims nothing about anyone. What the gate actually did was leave
        // the rating blank across the whole app, which read as broken.
        Assert.True(RatingDisplay.ShouldShow(1712));
        Assert.True(RatingDisplay.ShouldShow(1500));
    }

    [Fact]
    public void NoRatingAtAllStillShowsNothing()
    {
        // THE refusal that survives, and the one that was always the point. A null is not
        // somebody's 1500 — it is not knowing, which is exactly the state the app was in
        // the day the backend went down and every rating fetch came back 502. Painting a
        // number there would be the real invention.
        Assert.False(RatingDisplay.ShouldShow(null));
    }
}
