using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RecordingMemory"/> — what the launcher is allowed to remember about whether
/// the last competitive match was recorded, and when it may hold that against the player.
///
/// <para><b>Why this needs testing at all.</b> The launcher cannot tick AoE3's per-match Record
/// Game box and cannot see whether the player did, so the reminder before a competitive start is
/// a nudge. This is what lets the NEXT one lead with a fact instead. The expensive mistake is not
/// failing to warn — it is warning the wrong person about the wrong thing, which is exactly what
/// the refusals below prevent.</para>
/// </summary>
public class RecordingMemoryTests
{
    [Fact]
    public void ACompetitiveMatchWithNoRecordingIsRemembered()
        => Assert.True(RecordingMemory.Evaluate(
            competitive: true, reportable: true, recordingFound: false));

    /// <summary>
    /// The escalation must not stick. Once a match records properly the memory is cleared, or the
    /// player would be told off before every match for the rest of time.
    /// </summary>
    [Fact]
    public void ARecordedMatchClearsIt()
        => Assert.False(RecordingMemory.Evaluate(
            competitive: true, reportable: true, recordingFound: true));

    /// <summary>
    /// <b>The one that decides whether the advice is right.</b> A recording that exists but never
    /// finished writing its ending counts as FOUND, so it does not escalate: that player did tick
    /// the box, and what went wrong was closing AoE3 before returning to the main menu. Telling
    /// them to remember Record Game would send them to fix the one thing that was not broken.
    /// (The end-of-match card already names the real cause.)
    /// </summary>
    [Fact]
    public void ARecordingWithNoEndingStillCountsAsRecorded()
    {
        // The caller passes recordingFound: true for it — a file was found and read.
        Assert.False(RecordingMemory.Evaluate(
            competitive: true, reportable: true, recordingFound: true));
        Assert.False(RecordingMemory.ShouldEscalate(false));
    }

    /// <summary>
    /// A casual match teaches nothing: nobody was asked to tick anything. Returning "recorded"
    /// here would quietly clear a warning that had been earned, just because somebody played one
    /// friendly game in between.
    /// </summary>
    [Theory]
    [InlineData(false, true)]    // casual
    [InlineData(true, false)]    // competitive, but not a real host-side match
    [InlineData(false, false)]
    public void NothingIsLearnedFromAMatchThatNeverAskedForARecording(bool competitive, bool reportable)
    {
        Assert.Null(RecordingMemory.Evaluate(competitive, reportable, recordingFound: false));
        Assert.Null(RecordingMemory.Evaluate(competitive, reportable, recordingFound: true));
    }

    /// <summary>
    /// Only an explicit "the last one did not record" escalates. Never having played one is not
    /// evidence, and accusing somebody of forgetting something we never saw them forget is worse
    /// than saying nothing.
    /// </summary>
    [Fact]
    public void OnlyRealEvidenceEscalates()
    {
        Assert.True(RecordingMemory.ShouldEscalate(true));
        Assert.False(RecordingMemory.ShouldEscalate(false));
        Assert.False(RecordingMemory.ShouldEscalate(null));
    }

    /// <summary>
    /// The round trip a player actually makes: forget the box, get told, remember it next time,
    /// and stop being told.
    /// </summary>
    [Fact]
    public void ForgettingThenRememberingEndsTheWarning()
    {
        var afterForgetting = RecordingMemory.Evaluate(true, true, recordingFound: false);
        Assert.True(RecordingMemory.ShouldEscalate(afterForgetting));

        var afterRemembering = RecordingMemory.Evaluate(true, true, recordingFound: true);
        Assert.False(RecordingMemory.ShouldEscalate(afterRemembering));
    }
}
