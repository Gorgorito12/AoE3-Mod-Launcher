namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the launcher can honestly say about recording during a match.</summary>
public enum RecordingState
{
    /// <summary>The launcher asked for recording and the mod's profile carries it.</summary>
    Requested,

    /// <summary>The player turned recording off. Nothing will be written.</summary>
    Off,

    /// <summary>Recording is wanted but this mod's profile has not been written yet.</summary>
    Unknown,

    /// <summary>
    /// The player's AoE3 profile name cannot be read, so even a perfect recording could
    /// not be tied to this match.
    ///
    /// <para>A different axis from the other three — nothing to do with recording — but
    /// the same answer to the question the cell actually asks, which is whether this
    /// match is going to count. It takes precedence: telling somebody to tick Record
    /// Game while the launcher cannot identify them is sending them to fix the wrong
    /// thing.</para>
    /// </summary>
    ProfileUnreadable,
}

/// <summary>
/// The in-game panel's RECORDING cell.
///
/// <para><b>Why this is a "requested", never an "active".</b> The design asks for a green
/// "activa" read from the recording plan. The plan answers a narrower question — whether
/// the launcher should write <c>optionrecordgame</c> into this mod's profile — and it is
/// measured, in <c>.claude/rules/multiplayer.md</c>, that the profile setting does NOT
/// drive recording in multiplayer: AoE3's per-match "Record Game" checkbox does, it comes
/// up unticked every time, and the launcher cannot read it. Both candidate ways to set it
/// automatically were tried and both failed.</para>
///
/// <para>So a green "active" would be a claim the launcher cannot support, in the one
/// place where being wrong costs the player their rating: they would read "active", not
/// tick the box, and the match would count for nobody. The cell keeps the reference's
/// position, size and colours; only the wording drops from a statement about the game to
/// a statement about the launcher's own setting, which is all that is knowable.</para>
/// </summary>
public static class RecordingIndicator
{
    /// <summary>
    /// Classify from the launcher-wide preference and this mod's per-mod marker.
    /// </summary>
    /// <param name="enableGameRecording">The launcher-wide preference.</param>
    /// <param name="appliedForThisMod">
    /// The mod's <c>GameRecordingApplied</c> marker: null when its profile has never been
    /// written, which is a legitimate "not yet" — the game creates the profile on its
    /// first run — rather than a failure.
    /// </param>
    /// <param name="canIdentifyPlayer">
    /// Whether the player's own AoE3 profile name could be read. Passed in as a plain
    /// bool rather than looked up here, because reading it touches disk and this cell is
    /// repainted on a timer — the caller resolves it once when the match starts.
    /// </param>
    public static RecordingState Classify(
        bool enableGameRecording, bool? appliedForThisMod, bool canIdentifyPlayer = true)
    {
        // First, because it outranks the rest: with no readable profile name the result
        // cannot be read no matter what the recording does, so leading with anything
        // about recording would point the player at the wrong thing.
        if (!canIdentifyPlayer) return RecordingState.ProfileUnreadable;

        // The player's own choice wins outright, and it is knowable with certainty whether
        // or not the profile was ever reached.
        if (!enableGameRecording) return RecordingState.Off;
        return appliedForThisMod == true ? RecordingState.Requested : RecordingState.Unknown;
    }
}
