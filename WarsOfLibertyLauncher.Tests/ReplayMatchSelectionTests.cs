using System;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for picking the recording that belongs to the match that just ended.
///
/// <para><b>The scenario these defend against was found on a real disk, not imagined.</b>
/// Replays other players send you are kept in the same <c>Savegame\</c> folder, because
/// that is where the game looks for them, and their timestamp is when they were copied.
/// The maintainer's Wars of Liberty folder held two of someone else's games stamped
/// eleven minutes NEWER than his own — so "take the newest file written after the match
/// started" selected a stranger's recording, whose players were two people he had never
/// played against. Reported, that would have rated two strangers for a game they were
/// not in.</para>
/// </summary>
public class ReplayMatchSelectionTests
{
    // ---------------- the rule: is this recording ours? ----------------

    private static ReplayParserService.ReplayHeader Header(
        params (int Slot, string Name, bool Human)[] players)
        => new(
            GameVersion: "age3y.exe 6.0108.0321.0137",
            GameName: "test",
            MapName: "amazonia",
            MapPool: "amazonia",
            PlayerCount: players.Length,
            Players: players
                .Select(p => new ReplayParserService.ReplayPlayer(
                    p.Slot, p.Name, 1, 0,
                    p.Human ? ReplayParserService.SlotTypeHuman : 1u))
                .ToList());

    private static readonly ReplayParserService.ReplayHeader Ours =
        Header((1, "Gorgorito", true), (2, "Rival", true));

    /// <summary>The real shape of the file that nearly got picked.</summary>
    private static readonly ReplayParserService.ReplayHeader SomeoneElses =
        Header((1, "El Taita", true), (2, "CodeFender", true));

    [Fact]
    public void AcceptsARecordingTheHostPlayedIn()
        => Assert.True(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 2));

    [Fact]
    public void RejectsSomeoneElsesRecording()
    {
        // The check that carries the weight: another player's replay simply does not
        // contain the host, however new the file is.
        Assert.False(ReplayParserService.LooksLikeThisMatch(SomeoneElses, "Gorgorito", 2));
    }

    [Fact]
    public void RejectsAGameWithADifferentNumberOfPeople()
    {
        // Right host, wrong game — he was in a 1v1, this recording has four.
        var teamGame = Header(
            (1, "Gorgorito", true), (2, "A", true), (3, "B", true), (4, "C", true));

        Assert.False(ReplayParserService.LooksLikeThisMatch(teamGame, "Gorgorito", 2));
    }

    [Fact]
    public void RejectsWhenSomeoneElseRecordedIt()
    {
        // The host is in the game but did not record it, so this is a copy of the same
        // match from the opponent's machine — not what this launcher just produced.
        Assert.False(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 2, recorderSlot: 2));
        Assert.True(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 2, recorderSlot: 1));
    }

    [Fact]
    public void IgnoresTheRecorderCheckWhenTheTrailerHadNone()
    {
        // A recording without a trailer is already headed for Ambiguous; rejecting it
        // here too would throw away the map and civilizations for nothing.
        Assert.True(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 2, recorderSlot: -1));
    }

    [Fact]
    public void MatchesTheHostNameLoosely()
    {
        // It comes from an AoE3 profile the player typed, not from an id.
        Assert.True(ReplayParserService.LooksLikeThisMatch(Ours, "  gorgorito  ", 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnUnknownHostConfirmsNothing(string? host)
        => Assert.False(ReplayParserService.LooksLikeThisMatch(Ours, host!, 2));

    [Fact]
    public void RejectsANullHeader()
        => Assert.False(ReplayParserService.LooksLikeThisMatch(null, "Gorgorito", 2));

    /// <summary>
    /// A head count of zero means the room's roster was lost, which is the moment there is LEAST
    /// to go on — so it confirms nothing, exactly like a blank host name.
    ///
    /// <para>This assertion used to run the other way: zero skipped the check and a recording
    /// naming the host was accepted on two checks instead of three. The caller announces the
    /// recording through its own path when it only wants to name the file, so failing here costs
    /// nothing but a result nobody could stand behind.</para>
    /// </summary>
    [Fact]
    public void AHumanCountOfZeroConfirmsNothing()
    {
        Assert.False(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 0));
        Assert.False(ReplayParserService.LooksLikeThisMatch(SomeoneElses, "Gorgorito", 0));
        Assert.False(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", -1));
    }

    // ---------------- the walk over candidates ----------------

    private static ReplayUploadService.CandidateVerdict Verdict(bool ours) =>
        ours ? ReplayUploadService.CandidateVerdict.Match : ReplayUploadService.CandidateVerdict.NotOurs;

    [Fact]
    public void WalksPastANewerRecordingThatIsNotVerdict()
    {
        // Exactly the disk that prompted this: a downloaded replay copied in after the
        // match started, sitting newer than the real one.
        using var dir = new TempDir();
        var theirs = dir.Write("Code vs Taita 1.age3Yrec", DateTime.UtcNow);
        var mine = dir.Write("Record Game 1.age3Yrec", DateTime.UtcNow.AddMinutes(-2));

        var picked = ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10),
            f => Verdict(f.Name == Path.GetFileName(mine)));

        Assert.NotNull(picked.File);
        Assert.Equal(Path.GetFileName(mine), picked.File!.Name);
        Assert.NotEqual(Path.GetFileName(theirs), picked.File.Name);
    }

    [Fact]
    public void ReturnsNullWhenNoCandidateIsVerdict()
    {
        // Better no result than a stranger's. The caller reports a draw.
        using var dir = new TempDir();
        dir.Write("someone else.age3Yrec", DateTime.UtcNow);

        Assert.Null(ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10), _ => Verdict(false)).File);
    }

    [Fact]
    public void IgnoresRecordingsWrittenBeforeTheMatch()
    {
        using var dir = new TempDir();
        dir.Write("old.age3Yrec", DateTime.UtcNow.AddHours(-3));

        Assert.Null(ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10), _ => Verdict(true)).File);
    }

    [Fact]
    public void OneUnreadableCandidateDoesNotEndTheWalk()
    {
        // A file still being flushed, or locked, must not hide the one behind it.
        using var dir = new TempDir();
        dir.Write("locked.age3Yrec", DateTime.UtcNow);
        var mine = dir.Write("mine.age3Yrec", DateTime.UtcNow.AddMinutes(-1));

        var picked = ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10),
            f => f.Name == "locked.age3Yrec"
                ? throw new IOException("in use")
                : Verdict(f.Name == Path.GetFileName(mine)));

        Assert.Equal(Path.GetFileName(mine), picked.File?.Name);
    }

    [Fact]
    public void StopsAfterTheCap()
    {
        // A folder can hold hundreds and each candidate costs an inflate, so the walk is
        // bounded: the right file is among the newest few or it is not there.
        using var dir = new TempDir();
        var now = DateTime.UtcNow;
        for (var i = 0; i < ReplayUploadService.MaxCandidatesExamined + 3; i++)
            dir.Write($"r{i}.age3Yrec", now.AddSeconds(-i));

        var examined = 0;
        ReplayUploadService.FindMatchReplay(
            dir.Path, now.AddMinutes(-10), _ => { examined++; return Verdict(false); });

        Assert.Equal(ReplayUploadService.MaxCandidatesExamined, examined);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the retry. The search runs the instant the game process dies, so
    /// the recording we want is often still being flushed. Unreadable files used to consume the
    /// budget of five, which meant a handful of them could hide the real recording behind a
    /// quota that had already been spent on nothing — and the match went down as a draw.
    /// </summary>
    [Fact]
    public void AnUnreadableCandidateDoesNotConsumeTheBudget()
    {
        using var dir = new TempDir();
        var now = DateTime.UtcNow;
        for (var i = 0; i < ReplayUploadService.MaxCandidatesExamined; i++)
            dir.Write($"flushing{i}.age3Yrec", now.AddSeconds(-i));
        var mine = dir.Write("mine.age3Yrec", now.AddMinutes(-1));

        var picked = ReplayUploadService.FindMatchReplay(
            dir.Path, now.AddMinutes(-10),
            f => f.Name.StartsWith("flushing", StringComparison.Ordinal)
                ? ReplayUploadService.CandidateVerdict.Unreadable
                : Verdict(f.Name == Path.GetFileName(mine)));

        Assert.Equal(Path.GetFileName(mine), picked.File?.Name);
        Assert.Equal(ReplayUploadService.MaxCandidatesExamined, picked.Unreadable);
    }

    /// <summary>A folder full of unreadable files still has to stop somewhere.</summary>
    [Fact]
    public void StopsAtTheHardCeilingWhenNothingIsReadable()
    {
        using var dir = new TempDir();
        var now = DateTime.UtcNow;
        for (var i = 0; i < ReplayUploadService.MaxCandidatesOpened + 5; i++)
            dir.Write($"r{i}.age3Yrec", now.AddSeconds(-i));

        var opened = 0;
        var search = ReplayUploadService.FindMatchReplay(
            dir.Path, now.AddMinutes(-10),
            _ => { opened++; return ReplayUploadService.CandidateVerdict.Unreadable; });

        Assert.Null(search.File);
        Assert.Equal(ReplayUploadService.MaxCandidatesOpened, opened);
    }

    /// <summary>
    /// Retrying buys something in exactly two cases, and "a file that isn't ours" is not one of
    /// them: a recording that parsed cleanly and belongs to another game will parse identically
    /// in three seconds.
    /// </summary>
    [Fact]
    public void ShouldRetry_WhenSomethingWasUnreadable_OrNothingWasThereYet()
    {
        var unreadable = new ReplayUploadService.ReplaySearch(null, Parsed: 2, Unreadable: 1);
        var justNotOurs = new ReplayUploadService.ReplaySearch(null, Parsed: 3, Unreadable: 0);
        // Nothing newer than the launch existed yet — which is what a recording written a moment
        // AFTER the process exits looks like. This case used to get zero retries, so the match
        // reported all-draws at once and the file that arrived a second later was never seen.
        var nothingYet = new ReplayUploadService.ReplaySearch(null, Parsed: 0, Unreadable: 0);

        Assert.True(ReplayUploadService.ShouldRetry(unreadable, attempt: 0, maxAttempts: 4));
        Assert.True(ReplayUploadService.ShouldRetry(nothingYet, attempt: 0, maxAttempts: 4));
        Assert.False(ReplayUploadService.ShouldRetry(justNotOurs, attempt: 0, maxAttempts: 4));
    }

    /// <summary>
    /// <b>The case that matters most in this file.</b> The retries above are only affordable
    /// because they run BEHIND the report — and that is worth nothing if a match whose recording
    /// is right there still pays for them. A found recording must stop the ladder dead, at
    /// attempt 0, so the common path is as fast as it ever was.
    /// </summary>
    [Fact]
    public void AFoundRecordingNeverWaits()
    {
        using var dir = new TempDir();
        var found = new ReplayUploadService.ReplaySearch(
            new FileInfo(dir.Write("ours.age3Yrec", DateTime.UtcNow)), Parsed: 1, Unreadable: 0);

        Assert.False(ReplayUploadService.ShouldRetry(found, attempt: 0, maxAttempts: 4));
    }

    /// <summary>
    /// The match's own window ORDERS candidates and must never REJECT one.
    ///
    /// <para>A file written long after the game closed — a replay the player renamed to send it
    /// over Discord, which is exactly what happened in the incident this came from — should rank
    /// below the match's own recording. But the recording is finished as the game closes and its
    /// timestamp keeps moving while the retries run, so a ceiling that REJECTED would start
    /// discarding legitimate recordings: the very symptom this area exists to fix.</para>
    /// </summary>
    [Fact]
    public void TheWindowOrdersCandidates_ButNeverDiscardsThem()
    {
        using var dir = new TempDir();
        var started = DateTime.UtcNow.AddMinutes(-30);
        var exited = DateTime.UtcNow.AddMinutes(-10);

        // Newest on disk, but written long after the game closed.
        dir.Write("copied-later.age3Yrec", exited.AddMinutes(5));
        dir.Write("the-match.age3Yrec", exited.AddSeconds(-3));

        var seen = new System.Collections.Generic.List<string>();
        var search = ReplayUploadService.FindMatchReplay(
            dir.Path, started,
            f => { seen.Add(f.Name); return Verdict(false); },
            preferBeforeUtc: exited.AddMinutes(1));

        // In-window first...
        Assert.Equal("the-match.age3Yrec", seen[0]);
        // ...and the out-of-window one is still examined, not dropped.
        Assert.Contains("copied-later.age3Yrec", seen);
        Assert.Null(search.File);
    }

    [Fact]
    public void ShouldRetry_StopsOnceFoundOrOutOfAttempts()
    {
        using var dir = new TempDir();
        var found = new ReplayUploadService.ReplaySearch(
            new FileInfo(dir.Write("x.age3Yrec", DateTime.UtcNow)), Parsed: 1, Unreadable: 3);
        var stillUnreadable = new ReplayUploadService.ReplaySearch(null, Parsed: 0, Unreadable: 2);

        Assert.False(ReplayUploadService.ShouldRetry(found, attempt: 0, maxAttempts: 4));
        Assert.False(ReplayUploadService.ShouldRetry(stillUnreadable, attempt: 3, maxAttempts: 4));
        Assert.True(ReplayUploadService.ShouldRetry(stillUnreadable, attempt: 2, maxAttempts: 4));
    }

    [Fact]
    public void MissingFolderIsNullNotAThrow()
        => Assert.Null(ReplayUploadService.FindMatchReplay(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()),
            DateTime.UtcNow.AddMinutes(-10), _ => Verdict(true)).File);

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rec-sel-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(System.IO.Path.Combine(Path, "Savegame"));

        public string Write(string name, DateTime writtenUtc)
        {
            var full = System.IO.Path.Combine(Path, "Savegame", name);
            File.WriteAllBytes(full, new byte[] { 0x6C, 0x33, 0x33, 0x74 });
            File.SetLastWriteTimeUtc(full, writtenUtc);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
