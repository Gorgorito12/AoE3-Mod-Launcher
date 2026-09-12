using System;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins what the launcher calls a crash of the game — and, above all, what it does NOT.
///
/// <para>The verdict travels to the server, which re-derives it from the same four signals
/// (<c>src/elo/crashEvidence.ts</c>) and voids the match on it, at most once per player per
/// day. A signal read too generously here is a dodge with a new name: kill the process, keep
/// the rating. So the REJECTIONS are the point.</para>
/// </summary>
public class GameCrashEvidenceTests
{
    private const int AccessViolation = unchecked((int)0xC0000005);

    private static CrashEvent Event(int? pid = 4242) =>
        new("KERNELBASE.dll", "c0000005", new DateTime(2026, 9, 12, 18, 20, 0, DateTimeKind.Utc), pid);

    [Fact]
    public void ARealCrash_IsVerified()
    {
        var e = new GameExitEvidence(AccessViolation, RecordingOutcome.Absent, StoppedByUser: false, Event());
        Assert.True(e.CrashVerified);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ATaskkillIsNotACrash()
    {
        // Exit code 1, no ending, and — the part that decides — no Application Error event:
        // TerminateProcess leaves none.
        var e = new GameExitEvidence(1, RecordingOutcome.Absent, StoppedByUser: false, Event: null);
        Assert.False(e.CrashVerified);
    }

    [Fact]
    public void AnAltF4IsNotACrash()
    {
        var e = new GameExitEvidence(0, RecordingOutcome.Present, StoppedByUser: false, Event: null);
        Assert.False(e.CrashVerified);
    }

    [Fact]
    public void TheStopButtonIsNeverACrash()
    {
        // Every other signal says crash; the launcher killed it, so it is not one.
        var e = new GameExitEvidence(AccessViolation, RecordingOutcome.Absent, StoppedByUser: true, Event());
        Assert.False(e.CrashVerified);
    }

    [Fact]
    public void ACrashWithTheEndingWritten_DidNotEndTheMatch()
    {
        var e = new GameExitEvidence(AccessViolation, RecordingOutcome.Present, StoppedByUser: false, Event());
        Assert.False(e.CrashVerified);
        var unknown = new GameExitEvidence(AccessViolation, RecordingOutcome.Unknown, StoppedByUser: false, Event());
        Assert.False(unknown.CrashVerified);
    }

    [Fact]
    public void AnUnknownExitCodeDoesNotBlockAVerifiedEvent()
    {
        // The elevated launch path has no handle; the event alone is the strong signal.
        var e = new GameExitEvidence(null, RecordingOutcome.Absent, StoppedByUser: false, Event(pid: null));
        Assert.True(e.CrashVerified);
    }

    [Fact]
    public void ANormalExitCodeWithAnEvent_IsSomeOtherRun()
    {
        var e = new GameExitEvidence(0, RecordingOutcome.Absent, StoppedByUser: false, Event());
        Assert.False(e.CrashVerified);
    }

    [Theory]
    [InlineData(unchecked((int)0xC0000005), true)]
    [InlineData(unchecked((int)0xC000001D), true)]
    [InlineData(unchecked((int)0xC00000FD), true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, false)]              // 0xFFFFFFFF: taskkill, not an NTSTATUS
    [InlineData(0x40000000, false)]      // informational severity
    public void NtstatusFailureIsReadUnsigned(int code, bool failure)
        => Assert.Equal(failure, GameCrashEvidence.IsNtstatusFailure(code));

    [Fact]
    public void TheRecordingOutcomeFollowsTheReplaySearch()
    {
        Assert.Equal(RecordingOutcome.Present, GameCrashEvidence.OutcomeOf(true, LocalReadFailure.None));
        Assert.Equal(RecordingOutcome.Absent, GameCrashEvidence.OutcomeOf(false, LocalReadFailure.RecordingNoOutcome));
        // A trailer that was found but could not be read is still a trailer: the game wrote
        // its ending, so a crash cannot be what ended the match.
        Assert.Equal(RecordingOutcome.Present, GameCrashEvidence.OutcomeOf(false, LocalReadFailure.RecordingAmbiguous));
        Assert.Equal(RecordingOutcome.Unknown, GameCrashEvidence.OutcomeOf(false, LocalReadFailure.NoRecordingFound));
        Assert.Equal(RecordingOutcome.Unknown, GameCrashEvidence.OutcomeOf(false, LocalReadFailure.RecordingNotOurs));
    }

    // ---------------------------------------------------------------- the event log parser

    private static string Event1000Xml(string exe, string time, string pidHex, string module = "KERNELBASE.dll", string code = "c0000005") =>
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>"
        + "<System><Provider Name='Application Error'/><EventID>1000</EventID>"
        + $"<TimeCreated SystemTime='{time}'/></System>"
        + "<EventData>"
        + $"<Data>{exe}</Data><Data>1.0.0.0</Data><Data>4a1b2c3d</Data>"
        + $"<Data>{module}</Data><Data>10.0.26100.1</Data><Data>5b3c4d5e</Data>"
        + $"<Data>{code}</Data><Data>000000000003f1a2</Data><Data>{pidHex}</Data>"
        + "<Data>0x1dc1a2b3c4d5e6f</Data><Data>C:\\Games\\age3y.exe</Data><Data>C:\\Windows\\KERNELBASE.dll</Data>"
        + "<Data>b7d6c5e4-0000-0000-0000-000000000000</Data>"
        + "</EventData></Event>";

    private static readonly DateTime From = new(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void TheParserFindsTheGamesOwnCrash()
    {
        var xml = Event1000Xml("age3y.exe", "2026-09-12T18:20:00.0000000Z", "1092");   // 0x1092 = 4242
        var found = WindowsCrashEventLog.Parse(xml, "age3y.exe", 4242, From, To);
        Assert.NotNull(found);
        Assert.Equal("KERNELBASE.dll", found!.Module);
        Assert.Equal("c0000005", found.ExceptionCode);
        Assert.Equal(4242, found.Pid);
    }

    [Fact]
    public void AnotherProgramsCrashIsNotOurs()
    {
        var xml = Event1000Xml("discord.exe", "2026-09-12T18:20:00.0000000Z", "1092");
        Assert.Null(WindowsCrashEventLog.Parse(xml, "age3y.exe", 4242, From, To));
    }

    [Fact]
    public void ACrashOutsideTheMatchWindowIsNotThisMatch()
    {
        var yesterday = Event1000Xml("age3y.exe", "2026-09-11T18:20:00.0000000Z", "1092");
        Assert.Null(WindowsCrashEventLog.Parse(yesterday, "age3y.exe", 4242, From, To));
    }

    [Fact]
    public void ADifferentPidOfTheSameGameIsNotThisMatch()
    {
        var xml = Event1000Xml("age3y.exe", "2026-09-12T18:20:00.0000000Z", "0ABC");
        Assert.Null(WindowsCrashEventLog.Parse(xml, "age3y.exe", 4242, From, To));
    }

    [Fact]
    public void WithNoPidOfOurOwnTheTimeWindowAndTheNameDecide()
    {
        // The elevated launch path knows no pid; the event's pid is then not held against it.
        var xml = Event1000Xml("age3y.exe", "2026-09-12T18:20:00.0000000Z", "0ABC");
        Assert.NotNull(WindowsCrashEventLog.Parse(xml, "AGE3Y.EXE", null, From, To));
    }

    [Fact]
    public void GarbageIsNoEvent()
    {
        Assert.Null(WindowsCrashEventLog.Parse("<Event><broken", "age3y.exe", 4242, From, To));
        Assert.Null(WindowsCrashEventLog.Parse("", "age3y.exe", 4242, From, To));
    }
}
