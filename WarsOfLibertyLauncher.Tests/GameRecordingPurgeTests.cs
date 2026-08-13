using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="GameRecordingPurge"/> — which recordings the launcher may delete.
///
/// <para>The tests that matter are the ones about what is KEPT. Enabling recording is the
/// launcher's doing, so cleaning up after it is fair; deleting a recording a player renamed and
/// meant to keep is not, and there is no undo. The whole rule is "only files the game named
/// itself", and everything here exists to hold that line.</para>
/// </summary>
public class GameRecordingPurgeTests
{
    private static readonly DateTime Now = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Auto-named recordings, newest first: index 0 is the most recent.</summary>
    private static List<GameRecordingPurge.RecordingFile> Auto(int count, int startAt = 1) =>
        Enumerable.Range(0, count)
            .Select(i => new GameRecordingPurge.RecordingFile(
                $"Record Game {startAt + i}.age3Yrec", Now.AddMinutes(-i)))
            .ToList();

    [Theory]
    [InlineData("Record Game 1.age3Yrec")]
    [InlineData("Record Game 12.age3Yrec")]
    [InlineData("record game 7.age3yrec")]     // the game's casing is not something to rely on
    public void IsAutoNamed_MatchesTheNameTheGameGeneratesItself(string name)
        => Assert.True(GameRecordingPurge.IsAutoNamed(name));

    /// <summary>
    /// THE ONES THAT MATTER. Every one of these is a name a person chose or touched, and a person
    /// who renames a recording is saying they want to keep it.
    /// </summary>
    [Theory]
    [InlineData("Code vs Nathan 2.age3Yrec")]      // a real file on the maintainer's disk
    [InlineData("Record Game.age3Yrec")]           // no number: the game always numbers
    [InlineData("Record Game 1 - copy.age3Yrec")]
    [InlineData("My Record Game 1.age3Yrec")]
    [InlineData("Record Game 1 final.age3Yrec")]
    [InlineData("Record Game 1.age3Ysav")]         // a savegame, not a recording
    [InlineData("Record Game 1.txt")]
    [InlineData("")]
    public void IsAutoNamed_RefusesAnythingWeDidNotName(string name)
        => Assert.False(GameRecordingPurge.IsAutoNamed(name));

    [Fact]
    public void SelectForDeletion_KeepsTheNewestN()
    {
        var doomed = GameRecordingPurge.SelectForDeletion(Auto(15), keepNewest: 10, protectAfterUtc: Now.AddMinutes(1));

        Assert.Equal(5, doomed.Count);
        // The five oldest, which by construction are the five highest-numbered.
        Assert.Contains("Record Game 15.age3Yrec", doomed);
        Assert.DoesNotContain("Record Game 1.age3Yrec", doomed);
    }

    /// <summary>
    /// A renamed recording is neither deleted nor counted against the budget — so keeping twenty
    /// of them never pushes an automatic one out, and none of them is ever touched.
    /// </summary>
    [Fact]
    public void SelectForDeletion_NeverTouchesARenamedRecording()
    {
        var files = Auto(20).ToList();
        files.AddRange(Enumerable.Range(0, 20).Select(i =>
            new GameRecordingPurge.RecordingFile($"Grand final {i}.age3Yrec", Now.AddMinutes(-i))));

        var doomed = GameRecordingPurge.SelectForDeletion(files, keepNewest: 5, protectAfterUtc: Now.AddMinutes(1));

        Assert.Equal(15, doomed.Count);                                   // 20 auto minus the 5 kept
        Assert.All(doomed, n => Assert.StartsWith("Record Game ", n));    // nothing renamed
    }

    /// <summary>
    /// The cleanup runs when a game exits, right beside the code still working out who won from
    /// that very recording. Deleting it would destroy the result on the way to reading it.
    /// </summary>
    [Fact]
    public void SelectForDeletion_NeverTouchesTheMatchWeJustPlayed()
    {
        var files = Auto(15);   // index 0 is the newest, written "now"

        var doomed = GameRecordingPurge.SelectForDeletion(
            files, keepNewest: 0, protectAfterUtc: Now.AddMinutes(-4));

        Assert.DoesNotContain("Record Game 1.age3Yrec", doomed);   // Now
        Assert.DoesNotContain("Record Game 5.age3Yrec", doomed);   // Now - 4 min, on the boundary
        Assert.Contains("Record Game 6.age3Yrec", doomed);         // older than the guard
    }

    [Fact]
    public void SelectForDeletion_DeletesNothingWhenUnderTheBudget()
    {
        Assert.Empty(GameRecordingPurge.SelectForDeletion(Auto(10), 10, Now.AddMinutes(1)));
        Assert.Empty(GameRecordingPurge.SelectForDeletion(
            new List<GameRecordingPurge.RecordingFile>(), 10, Now.AddMinutes(1)));
    }

    /// <summary>
    /// Files copied in one operation share a timestamp to the tick. Without the name tie-break the
    /// survivors would depend on enumeration order, so the same folder could purge differently on
    /// two runs.
    /// </summary>
    [Fact]
    public void SelectForDeletion_IsDeterministicWhenTimestampsTie()
    {
        var tied = Enumerable.Range(1, 6)
            .Select(i => new GameRecordingPurge.RecordingFile($"Record Game {i}.age3Yrec", Now))
            .ToList();

        var first = GameRecordingPurge.SelectForDeletion(tied, 3, Now.AddMinutes(1));
        var again = GameRecordingPurge.SelectForDeletion(
            tied.AsEnumerable().Reverse().ToList(), 3, Now.AddMinutes(1));

        Assert.Equal(first, again);
        Assert.Equal(3, first.Count);
    }

    /// <summary>A budget of zero still protects everything renamed — the rule is the name, not the count.</summary>
    [Fact]
    public void SelectForDeletion_KeepsRenamedFilesEvenWithNoBudgetAtAll()
    {
        var files = new List<GameRecordingPurge.RecordingFile>
        {
            new("Code vs Nathan 2.age3Yrec", Now.AddDays(-400)),
            new("Record Game 1.age3Yrec", Now.AddDays(-400)),
        };

        var doomed = GameRecordingPurge.SelectForDeletion(files, keepNewest: 0, protectAfterUtc: Now);

        Assert.Equal(new[] { "Record Game 1.age3Yrec" }, doomed);
    }
}
