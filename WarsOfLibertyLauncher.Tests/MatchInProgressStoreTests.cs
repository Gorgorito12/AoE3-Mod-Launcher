using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the file a launcher that DIES mid-match leaves for the next launch.
///
/// <para>THE ONE THAT MATTERS is the round trip of the frozen context: a rehydrated
/// <see cref="MatchContext"/> must be the same facts — host flag, roster, competitiveness,
/// format, names — because the exit handler that consumes it will report and confirm on the
/// strength of them, against a room that no longer exists.</para>
/// </summary>
public class MatchInProgressStoreTests
{
    private static readonly DateTime Launched = new(2026, 9, 12, 18, 10, 0, DateTimeKind.Utc);

    private static MatchContext Duel() => MatchContext.Capture(
        new[] { "rival-id", "host-id" }, "lobby-1", "wol", "host-id", isHost: true,
        Launched, isCompetitive: true,
        new Dictionary<string, string> { ["host-id"] = "Geaf", ["rival-id"] = "Nathan" },
        RoomFormat.OneVOne);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "aoe3ml-tests", Guid.NewGuid().ToString("N"), "match-in-progress.json");

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheContextComesBackFrozenExactlyAsItWasCaptured()
    {
        var path = TempPath();
        var saved = MatchInProgressStore.Describe(Duel(), "wol", 4242, @"C:\Games\WoL\age3y.exe", Launched);
        MatchInProgressStore.Save(saved, path);

        var back = MatchInProgressStore.Load(path);
        Assert.NotNull(back);
        var ctx = back!.Context;

        Assert.True(ctx.IsHost);
        Assert.Equal(new[] { "host-id", "rival-id" }, ctx.Participants);   // sorted, as Capture sorts
        Assert.Equal("lobby-1", ctx.LobbyId);
        Assert.Equal("wol", ctx.ModId);
        Assert.Equal("host-id", ctx.ReporterUserId);
        Assert.Equal(Launched, ctx.StartedAtUtc);
        Assert.True(ctx.IsCompetitive);
        Assert.Equal(RoomFormat.OneVOne, ctx.Format);
        Assert.Equal("Nathan", ctx.InGameNames!["rival-id"]);
        Assert.Equal(4242, back.GamePid);
        Assert.Equal("wol", back.ProfileId);

        // And it can report: the whole point of bringing it back.
        Assert.True(ctx.CanReport(Launched.AddMinutes(20), minSeconds: 180).Ok);

        MatchInProgressStore.Clear(path);
        Assert.Null(MatchInProgressStore.Load(path));
    }

    [Fact]
    public void ADeliberateExitLeavesNothingToResume()
    {
        var path = TempPath();
        MatchInProgressStore.Save(MatchInProgressStore.Describe(Duel(), "wol", 1, null, Launched), path);
        MatchInProgressStore.Clear(path);
        Assert.False(File.Exists(path));
        // Clearing twice is fine — the exit paths do not coordinate.
        MatchInProgressStore.Clear(path);
    }

    [Fact]
    public void AStaleFileIsNotResumed()
    {
        var saved = MatchInProgressStore.Describe(Duel(), "wol", 1, null, Launched);
        Assert.True(MatchInProgressStore.IsResumable(saved, Launched.AddHours(3)));
        // The server refuses a report older than a week; there is nothing to gain by trying.
        Assert.False(MatchInProgressStore.IsResumable(saved, Launched.AddDays(8)));
        // A launch in the future is a clock that cannot be trusted.
        Assert.False(MatchInProgressStore.IsResumable(saved, Launched.AddHours(-1)));
    }

    [Fact]
    public void AShapeNothingCouldReportIsNotResumed()
    {
        var solo = MatchContext.Capture(new[] { "host-id" }, "lobby-1", "wol", "host-id", true, Launched);
        Assert.False(MatchInProgressStore.IsResumable(
            MatchInProgressStore.Describe(solo, "wol", 1, null, Launched), Launched.AddHours(1)));

        var noRoom = MatchContext.Capture(new[] { "a", "b" }, null, "wol", "a", true, Launched);
        Assert.False(MatchInProgressStore.IsResumable(
            MatchInProgressStore.Describe(noRoom, "wol", 1, null, Launched), Launched.AddHours(1)));

        var noProfile = MatchInProgressStore.Describe(Duel(), "", 1, null, Launched);
        Assert.False(MatchInProgressStore.IsResumable(noProfile, Launched.AddHours(1)));

        Assert.False(MatchInProgressStore.IsResumable(null, Launched));
    }

    [Fact]
    public void GarbageOnDiskIsNoMatch_NotAThrow()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Null(MatchInProgressStore.Load(path));
        Assert.Null(MatchInProgressStore.Load(Path.Combine(Path.GetDirectoryName(path)!, "missing.json")));
        MatchInProgressStore.Clear(path);
    }
}
