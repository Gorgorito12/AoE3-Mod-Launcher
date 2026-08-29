namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Whether the last competitive match produced a recording — the one fact that turns the
/// pre-match Record Game reminder from a refrain into a statement.
///
/// <para><b>Why the reminder needs this at all.</b> The launcher cannot tick AoE3's per-match
/// "Record Game" box and cannot see whether the player did: writing <c>optionrecordgame</c> into
/// the profile does not move it (measured twice) and <c>+RecordGame</c> as a launch argument does
/// nothing (tested twice, once by playing a whole LAN game). So the confirmation before a
/// competitive start is a nudge, never a guarantee — and a nudge that reads identically every
/// time stops being read. What the launcher CAN do is notice afterwards that no recording
/// appeared, and say so the next time.</para>
///
/// <para>Pure and free of WPF so the rules below can be tested, which matters because the
/// expensive mistake here is not failing to warn — it is warning the wrong person about the
/// wrong thing.</para>
/// </summary>
public static class RecordingMemory
{
    /// <summary>
    /// What to remember about the match that just ended, or <c>null</c> to leave the previous
    /// memory alone.
    /// </summary>
    /// <param name="competitive">
    /// Whether the room put rating on this match. Only a competitive match asked the player to
    /// tick the box, so only a competitive match can hold it against them.
    /// </param>
    /// <param name="reportable">
    /// Whether this was a real host-side match — the same question the report asks, passed in by
    /// the caller so the two cannot disagree about it. A two-minute solo launch says nothing
    /// about anybody's habits.
    /// </param>
    /// <param name="recordingFound">
    /// Whether a recording for this match was found at all.
    ///
    /// <para><b>A recording that exists but never finished writing its ending counts as FOUND.</b>
    /// That player did tick the box; what went wrong was closing AoE3 without returning to the
    /// main menu. Escalating the Record Game warning at them would send them to fix the one thing
    /// that was not broken — and the end-of-match card already tells them the real cause.</para>
    /// </param>
    public static bool? Evaluate(bool competitive, bool reportable, bool recordingFound)
    {
        // Not a competitive host match: we learned nothing, so we must not overwrite what we
        // knew. Returning false here would quietly clear a real warning the next time somebody
        // played a casual game in between.
        if (!competitive || !reportable) return null;
        return !recordingFound;
    }

    /// <summary>
    /// Whether the pre-match confirmation should lead with the fact instead of the reminder.
    ///
    /// <para>Only an explicit "the last one did not record". <c>null</c> — never played one, or
    /// nothing conclusive since — gets the ordinary wording: accusing somebody of forgetting
    /// something we never saw them forget is worse than saying nothing.</para>
    /// </summary>
    public static bool ShouldEscalate(bool? lastMatchHadNoRecording)
        => lastMatchHadNoRecording == true;
}
