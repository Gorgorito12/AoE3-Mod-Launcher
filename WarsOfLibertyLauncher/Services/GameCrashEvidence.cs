using System;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Services;

/// <summary>What the player's OWN recording says about how the match ended.</summary>
public enum RecordingOutcome
{
    /// <summary>The recording could not be found or read, so it says nothing either way.</summary>
    Unknown,
    /// <summary>The outcome trailer is there: the game wrote its ending.</summary>
    Present,
    /// <summary>The recording is there and the trailer is not: the process died before it
    /// could write one.</summary>
    Absent,
}

/// <summary>A Windows Application Error 1000 event for the game, as the event log recorded it.</summary>
public sealed record CrashEvent(string Module, string ExceptionCode, DateTime TimeUtc, int? Pid);

/// <summary>
/// Everything the launcher could observe about HOW the game closed, and the one derived fact
/// the server acts on.
///
/// <para><b>Why this exists.</b> "The game closed with no ending in the recording" is produced
/// identically by a crash and by <c>taskkill</c>, and the ladder used to treat both as the loss
/// the opponent's recording names. What a player cannot fabricate with a click is what a real
/// crash leaves in the operating system: an Application Error 1000 event naming the executable
/// and the faulting module, and an NTSTATUS exception code as the process's exit code. A
/// terminated process leaves neither. The four signals travel to the server with
/// <c>game_exit_evidence</c>; the server re-derives <see cref="CrashVerified"/> itself from
/// the same rule (<c>src/elo/crashEvidence.ts</c>) and never trusts the flag — this copy is for
/// the log and the card.</para>
///
/// <para><b>It only ever VOIDS a match, never flips it.</b> The crashed player does not win; the
/// match stops counting for both, at most once per player per day. That bound, and the fact
/// that forging the event takes injecting a real fault into the process, is what makes an
/// automatic rule defensible where a claimed "it crashed" never was.</para>
/// </summary>
public sealed record GameExitEvidence(
    int? ExitCode,
    RecordingOutcome Recording,
    bool StoppedByUser,
    CrashEvent? Event)
{
    /// <summary>The launcher's copy of the server's rule — see <see cref="GameCrashEvidence.Verify"/>.</summary>
    public bool CrashVerified => GameCrashEvidence.Verify(this);
}

/// <summary>The pure half: the verdict from four signals, with no I/O.</summary>
public static class GameCrashEvidence
{
    /// <summary>
    /// NTSTATUS failure: severity bits 11, i.e. 0xC0000000 and above, read UNSIGNED — .NET hands
    /// the code back as a signed int, so 0xC0000005 arrives as −1073741819.
    ///
    /// <para><b>0xFFFFFFFF is excluded by name.</b> It sits above the threshold but it is not a
    /// status the kernel ever raises: it is the −1 that <c>Process.Kill()</c> — the launcher's
    /// own Stop, and several tools — passes to <c>TerminateProcess</c>. The one exit code a
    /// killed game reliably carries must not read as a crash. <c>taskkill</c> and Task Manager
    /// exit with 1.</para>
    /// </summary>
    public static bool IsNtstatusFailure(int exitCode)
    {
        var u = unchecked((uint)exitCode);
        return u >= 0xC0000000u && u != 0xFFFFFFFFu;
    }

    /// <summary>
    /// All four, in the order they matter: the event proves a crash happened; the missing
    /// ending proves the crash is what ended the match (a game that crashed on the victory
    /// screen decided nothing); the user not having stopped it rules out the launcher's own
    /// kill; and the exit code may be unknown, because the elevated launch path has no handle to
    /// read it from.
    /// </summary>
    public static bool Verify(GameExitEvidence e)
    {
        if (e.Event == null) return false;
        if (e.Recording != RecordingOutcome.Absent) return false;
        if (e.StoppedByUser) return false;
        if (e.ExitCode is int code && !IsNtstatusFailure(code)) return false;
        return true;
    }

    /// <summary>
    /// The recording outcome as the replay search reports it. Decided means the trailer was
    /// read; <see cref="LocalReadFailure.RecordingNoOutcome"/> is precisely "file there, no
    /// ending"; ambiguous means a trailer WAS found but could not be interpreted — the game
    /// still wrote one, so a crash cannot be what ended the match. Everything else — no file,
    /// unreadable, somebody else's — says nothing about how the game closed.
    /// </summary>
    public static RecordingOutcome OutcomeOf(bool resultDecided, LocalReadFailure failure)
    {
        if (resultDecided) return RecordingOutcome.Present;
        return failure switch
        {
            LocalReadFailure.RecordingNoOutcome => RecordingOutcome.Absent,
            LocalReadFailure.RecordingAmbiguous => RecordingOutcome.Present,
            _ => RecordingOutcome.Unknown,
        };
    }

    /// <summary>The word the wire carries.</summary>
    public static string WireName(RecordingOutcome outcome) => outcome switch
    {
        RecordingOutcome.Present => "present",
        RecordingOutcome.Absent => "absent",
        _ => "unknown",
    };
}
