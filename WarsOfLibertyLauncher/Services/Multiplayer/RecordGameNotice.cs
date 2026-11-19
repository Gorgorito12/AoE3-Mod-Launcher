using System;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Whether the host of a competitive room is asked about Record Game before the countdown.
///
/// <para>It used to be asked before EVERY competitive start, on the grounds that the launcher
/// could not tick AoE3's per-match box. It can now: the box the host's lobby opens with is the
/// profile's <c>gamerecordgame</c> (<see cref="GameSettingsStore.EnsureLobbyRecordGame"/>), and
/// the host's value is the game's for every player. A question repeated every match about
/// something already done stops being read, so it is asked only when there is something to
/// say: the launcher could not write the setting, or the last competitive match of this mod left
/// no recording. The player may still silence it for <see cref="SnoozeDays"/> days; a NEW match
/// without a recording ends that silence (the caller clears the snooze when it records one).</para>
/// </summary>
public static class RecordGameNotice
{
    public enum Reason
    {
        /// <summary>Nothing to say: the box is ticked and the last match was recorded.</summary>
        None,

        /// <summary>The setting could not be written (no profile yet, unreadable): the host ticks it.</summary>
        CouldNotTick,

        /// <summary>The last competitive match of this mod produced no recording.</summary>
        LastNotRecorded,
    }

    /// <summary>How long "don't remind me" lasts.</summary>
    public const int SnoozeDays = 7;

    public static Reason Decide(bool? lastMatchHadNoRecording, bool lobbyBoxTicked,
        DateTime? snoozedUntilUtc, DateTime nowUtc)
    {
        var reason = RecordingMemory.ShouldEscalate(lastMatchHadNoRecording) ? Reason.LastNotRecorded
            : !lobbyBoxTicked ? Reason.CouldNotTick
            : Reason.None;
        if (reason != Reason.None && snoozedUntilUtc is { } until && nowUtc < until) return Reason.None;
        return reason;
    }

    /// <summary>The end of a snooze started now.</summary>
    public static DateTime SnoozeUntil(DateTime nowUtc) => nowUtc.AddDays(SnoozeDays);
}
