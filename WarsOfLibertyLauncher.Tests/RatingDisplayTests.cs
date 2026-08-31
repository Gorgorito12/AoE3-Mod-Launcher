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

    /// <summary>
    /// Who reads "unrated" instead of the 1500 everybody starts from.
    ///
    /// <para>This NARROWS <c>ShouldShow</c>, which was itself a reversal, so both turns are
    /// pinned rather than only the latest. The earlier one is right that a shared starting
    /// number claims nothing about anyone — and that is exactly why it says nothing useful
    /// either, printing the same 1500 beside a name that earned it and one that did not.</para>
    /// </summary>
    [Theory]
    // The game count is exact when it travels — GET /matches/elo sends it.
    [InlineData(null, 0, true)]
    [InlineData(null, 1, false)]
    [InlineData(null, 40, false)]
    // The room roster has only the deviation, so the untouched default stands in for it.
    [InlineData(350.0, null, true)]
    [InlineData(349.9, null, true)]   // survives the JSON round trip
    [InlineData(230.0, null, false)]  // three rated matches in: provisional, but PLAYED
    [InlineData(80.0, null, false)]
    // The count wins when both are present: it answers the question directly.
    [InlineData(350.0, 5, false)]
    [InlineData(80.0, 0, true)]
    public void IsUnrated_ReadsWhicheverSignalTheSurfaceCarries(
        double? rd, int? gamesPlayed, bool expected)
    {
        Assert.Equal(expected, RatingDisplay.IsUnrated(rd, gamesPlayed));
    }

    /// <summary>
    /// <b>The refusal, and the reason this is a separate test.</b> A backend older than these
    /// fields sends neither, and turning that silence into "unrated" would be inventing a
    /// claim about the player — the same mistake <c>ShouldShow</c> refuses to make about a
    /// null rating. Not knowing keeps painting the number, exactly as before.
    /// </summary>
    [Fact]
    public void KnowingNothingIsNotTheSameAsUnrated()
    {
        Assert.False(RatingDisplay.IsUnrated(rd: null, gamesPlayed: null));
    }

    /// <summary>
    /// A player who really is on 1500 with matches behind them keeps the number. This is the
    /// case the whole change would break if it keyed off the RATING instead of the evidence.
    /// </summary>
    [Fact]
    public void ARatedPlayerSittingExactlyOn1500IsNotUnrated()
    {
        Assert.True(RatingDisplay.ShouldShow(1500));
        Assert.False(RatingDisplay.IsUnrated(rd: 95, gamesPlayed: 12));
    }
}
