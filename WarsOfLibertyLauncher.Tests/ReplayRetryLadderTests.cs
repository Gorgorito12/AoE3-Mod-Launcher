using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="ReplayRetryLadder"/> — how long the launcher waits for the recording of a
/// match that just ended, and on WHICH side of the match report it waits.
///
/// <para>The competitive ladder was SPLIT around the report rather than duplicated, and the
/// arithmetic that keeps that safe is the whole reason this file exists: the two halves are
/// charged to ONE clock (<see cref="RoomMatchState.ResultGraceSeconds"/> is stamped once, when
/// the result phase leaves <c>None</c>), so a ladder in front of the report PLUS the old one
/// behind it would exceed the ceiling that decides how long a player may be held in the room.
/// None of that is visible in a build, a screenshot or a diff.</para>
/// </summary>
public class ReplayRetryLadderTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The split is a re-partition, never an addition: the pre-report
    /// attempts followed by the continuation's REMAINING rungs are the competitive ladder
    /// exactly as it was before it was split.
    ///
    /// <para>The continuation's own first element is dropped from the comparison because it is
    /// structural — a fresh call must not re-pay a delay the pre-report pass already spent —
    /// which is why it is asserted separately below.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheTwoHalvesAreTheOldCompetitiveLadder()
    {
        var rejoined = ReplayRetryLadder.PreReport(competitive: true)
            .Concat(ReplayRetryLadder.Continuation(competitive: true).Skip(1))
            .ToArray();

        Assert.Equal(ReplayRetryLadder.CompetitiveUnsplit(), rejoined);
    }

    /// <summary>
    /// And therefore the total wait did not move. Stated as its own case because the identity
    /// above could be satisfied by a re-partition that still added a rung.
    /// </summary>
    [Fact]
    public void SplittingItCostsNoExtraWaiting()
    {
        var split = ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.PreReport(true))
            + ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.Continuation(true));

        Assert.Equal(
            ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.CompetitiveUnsplit()),
            split);
    }

    /// <summary>
    /// The ceiling this whole shape exists to respect. Both halves are stamped against the SAME
    /// <see cref="RoomMatchState.ResultGraceSeconds"/> clock, so their sum — not either half —
    /// is what has to fit, with room left over for the work between them.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheWholeCompetitiveWaitFitsInsideTheLeaveHold()
    {
        var totalMs = ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.PreReport(true))
            + ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.Continuation(true));

        Assert.True(
            totalMs < RoomMatchState.ResultGraceSeconds * 1000,
            $"the competitive ladder waits {totalMs} ms against a "
            + $"{RoomMatchState.ResultGraceSeconds}s hold");
    }

    /// <summary>
    /// A casual match's timing did not move by so much as a millisecond, and that is the point:
    /// almost no casual match is recorded (AoE3's per-match box comes up unticked), so waiting
    /// in front of the report would tax the majority for the benefit of nobody.
    /// </summary>
    [Fact]
    public void CasualStillLooksExactlyOnceBeforeReporting()
        => Assert.Equal(new[] { 0 }, ReplayRetryLadder.PreReport(competitive: false));

    /// <summary>...and its continuation is the ladder casual always had, behind the report.</summary>
    [Fact]
    public void TheCasualContinuationIsTheOldCasualLadder()
        => Assert.Equal(
            new[] { 0, 1000, 2500, 5000 },
            ReplayRetryLadder.Continuation(competitive: false));

    /// <summary>
    /// Every continuation opens on zero. It is a fresh call to the search, so charging it the
    /// delay the pre-report pass has already waited would spend that time twice — invisible in
    /// the totals above only because they are computed the same wrong way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AContinuationNeverRePaysADelay(bool competitive)
        => Assert.Equal(0, ReplayRetryLadder.Continuation(competitive)[0]);

    /// <summary>
    /// The pre-report half is short on purpose: enough for a file that is merely still being
    /// flushed, and not so long that a player whose recording is never coming is left staring
    /// at a closed game. Pinned in seconds rather than as a rung count, which is the axis a
    /// person would reason about.
    /// </summary>
    [Fact]
    public void TheCompetitiveReportIsNotHeldForMoreThanAFewSeconds()
        => Assert.True(
            ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.PreReport(true)) <= 5000,
            "the report may wait a moment for the recording, not a coffee break");

    /// <summary>
    /// Competitive waits longer overall, and must. The host has just confirmed Record Game, so
    /// a recording almost certainly exists — the ratio the casual ladder is tuned against is
    /// inverted by construction in that room.
    /// </summary>
    [Fact]
    public void CompetitiveIsThePatientOne()
        => Assert.True(
            ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.CompetitiveUnsplit())
            > ReplayRetryLadder.TotalDelayMs(ReplayRetryLadder.Continuation(false)));

    /// <summary>
    /// Nothing hands back the array it keeps. A caller that sorted or zeroed one in place would
    /// change every later match of the session, on a class whose whole surface is static.
    /// </summary>
    [Fact]
    public void TheLaddersAreCopies()
    {
        var first = ReplayRetryLadder.CompetitiveUnsplit();
        first[0] = 999_999;

        Assert.Equal(0, ReplayRetryLadder.CompetitiveUnsplit()[0]);
        Assert.Equal(0, ReplayRetryLadder.PreReport(true)[0]);
    }
}
