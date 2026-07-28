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

    [Fact]
    public void AHumanCountOfZeroSkipsThatCheck()
    {
        // The caller passes 0 when it doesn't know how many were in the room; the host
        // check still has to hold.
        Assert.True(ReplayParserService.LooksLikeThisMatch(Ours, "Gorgorito", 0));
        Assert.False(ReplayParserService.LooksLikeThisMatch(SomeoneElses, "Gorgorito", 0));
    }

    // ---------------- the walk over candidates ----------------

    [Fact]
    public void WalksPastANewerRecordingThatIsNotOurs()
    {
        // Exactly the disk that prompted this: a downloaded replay copied in after the
        // match started, sitting newer than the real one.
        using var dir = new TempDir();
        var theirs = dir.Write("Code vs Taita 1.age3Yrec", DateTime.UtcNow);
        var mine = dir.Write("Record Game 1.age3Yrec", DateTime.UtcNow.AddMinutes(-2));

        var picked = ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10),
            f => f.Name == Path.GetFileName(mine));

        Assert.NotNull(picked);
        Assert.Equal(Path.GetFileName(mine), picked!.Name);
        Assert.NotEqual(Path.GetFileName(theirs), picked.Name);
    }

    [Fact]
    public void ReturnsNullWhenNoCandidateIsOurs()
    {
        // Better no result than a stranger's. The caller reports a draw.
        using var dir = new TempDir();
        dir.Write("someone else.age3Yrec", DateTime.UtcNow);

        Assert.Null(ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10), _ => false));
    }

    [Fact]
    public void IgnoresRecordingsWrittenBeforeTheMatch()
    {
        using var dir = new TempDir();
        dir.Write("old.age3Yrec", DateTime.UtcNow.AddHours(-3));

        Assert.Null(ReplayUploadService.FindMatchReplay(
            dir.Path, DateTime.UtcNow.AddMinutes(-10), _ => true));
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
                : f.Name == Path.GetFileName(mine));

        Assert.Equal(Path.GetFileName(mine), picked?.Name);
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
            dir.Path, now.AddMinutes(-10), _ => { examined++; return false; });

        Assert.Equal(ReplayUploadService.MaxCandidatesExamined, examined);
    }

    [Fact]
    public void MissingFolderIsNullNotAThrow()
        => Assert.Null(ReplayUploadService.FindMatchReplay(
            Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()),
            DateTime.UtcNow.AddMinutes(-10), _ => true));

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
