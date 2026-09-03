using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The record of what a player's decks held when a match ended.
///
/// <para>It exists because the deck file is MUTABLE: the recording names the home city file and
/// nothing else about the deck, so opening a July match would otherwise show September's cards.
/// The rules below are the ones that decide whether a stored snapshot really belongs to the
/// match being looked at — and the refusals are the point, because a snapshot matched to the
/// wrong game is a confident, wrong answer.</para>
/// </summary>
public class DeckSnapshotStoreTests : IDisposable
{
    private readonly List<string> _temp = new();

    public void Dispose()
    {
        foreach (var dir in _temp)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static DeckSnapshotEntry Entry(string modId, DateTime capturedUtc, params string[] blobs)
    {
        var entry = new DeckSnapshotEntry { ModId = modId, CapturedUtc = capturedUtc };
        for (var i = 0; i < blobs.Length; i++) entry.Files["file" + i + ".xml"] = blobs[i];
        return entry;
    }

    private static readonly DateTime Noon = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ the debounce

    /// <summary>
    /// A multiplayer game reaches its end through two paths — the dashboard's exit monitor and
    /// the lobby's own watcher — and without this one match would leave two entries.
    /// </summary>
    [Fact]
    public void TheSecondReportOfTheSameMatchDoesNotCaptureAgain()
    {
        var entries = new[] { Entry("wol", Noon) };

        Assert.False(DeckSnapshotStore.ShouldCapture(entries, "wol", Noon.AddSeconds(3)));
        Assert.False(DeckSnapshotStore.ShouldCapture(entries, "wol", Noon.AddSeconds(-3)));
    }

    [Fact]
    public void ALaterMatchIsCapturedNormally() =>
        Assert.True(DeckSnapshotStore.ShouldCapture(
            new[] { Entry("wol", Noon) },
            "wol",
            Noon.AddSeconds(DeckSnapshotStore.DebounceSeconds + 1)));

    /// <summary>Two mods can be played minutes apart; one must not swallow the other's snapshot.</summary>
    [Fact]
    public void AnotherModsMatchIsNeverTheSameMatch() =>
        Assert.True(DeckSnapshotStore.ShouldCapture(
            new[] { Entry("wol", Noon) }, "improvement-mod", Noon.AddSeconds(3)));

    [Fact]
    public void WithNothingStoredYetEverythingIsCaptured() =>
        Assert.True(DeckSnapshotStore.ShouldCapture(Array.Empty<DeckSnapshotEntry>(), "wol", Noon));

    // ------------------------------------------------------------------ pairing

    /// <summary>
    /// The snapshot and the recording are both written when the match ends, seconds apart — so
    /// the nearest one wins, and "nearest" is what tells two matches of the same evening apart.
    /// </summary>
    [Fact]
    public void TheNearestSnapshotInTimeIsTheMatch()
    {
        var entries = new[]
        {
            Entry("wol", Noon, "aaa"),
            Entry("wol", Noon.AddMinutes(30), "bbb"),
        };

        Assert.Equal("bbb",
            DeckSnapshotStore.NearestTo(entries, "wol", Noon.AddMinutes(31))!.Files.Values.Single());
        Assert.Equal("aaa",
            DeckSnapshotStore.NearestTo(entries, "wol", Noon.AddSeconds(20))!.Files.Values.Single());
    }

    /// <summary>
    /// <b>The refusal that matters.</b> A match whose snapshot was never taken — every match
    /// played before this existed, and any the launcher did not see end — must come back with
    /// NOTHING rather than with the nearest unrelated deck.
    /// </summary>
    [Fact]
    public void AMatchWithNoSnapshotOfItsOwnGetsNone()
    {
        var entries = new[] { Entry("wol", Noon, "aaa") };

        Assert.Null(DeckSnapshotStore.NearestTo(
            entries, "wol", Noon + DeckSnapshotStore.MatchWindow + TimeSpan.FromMinutes(1)));
        Assert.Null(DeckSnapshotStore.NearestTo(entries, "improvement-mod", Noon));
        Assert.Null(DeckSnapshotStore.NearestTo(Array.Empty<DeckSnapshotEntry>(), "wol", Noon));
    }

    // ------------------------------------------------------------------ purge

    [Fact]
    public void OnlyTheNewestMatchesAreKept()
    {
        var entries = Enumerable.Range(0, 10)
            .Select(i => Entry("wol", Noon.AddMinutes(i), "blob" + i)).ToList();

        var kept = DeckSnapshotStore.Trim(entries, 3);

        Assert.Equal(3, kept.Count);
        Assert.Equal(new[] { "blob9", "blob8", "blob7" }, kept.Select(e => e.Files.Values.Single()));
    }

    /// <summary>
    /// <b>Content addressing is what makes this subtle.</b> One stored file can belong to forty
    /// matches, so it dies only when the LAST of them is trimmed — deleting per entry would take
    /// the deck of every match that still points at it.
    /// </summary>
    [Fact]
    public void AStoredFileSurvivesWhileAnyMatchStillPointsAtIt()
    {
        var kept = new[] { Entry("wol", Noon, "shared"), Entry("wol", Noon.AddHours(1), "shared") };

        Assert.Empty(DeckSnapshotStore.UnreferencedBlobs(new[] { "shared" }, kept));
        Assert.Equal(new[] { "orphan" },
            DeckSnapshotStore.UnreferencedBlobs(new[] { "shared", "orphan" }, kept));
    }

    // ------------------------------------------------------------------ round trip

    /// <summary>
    /// Capturing twice with an unchanged deck must not write the file twice — the whole reason
    /// the store is keyed by content rather than by match.
    /// </summary>
    [Fact]
    public void CapturingAnUnchangedDeckTwiceStoresOneCopy()
    {
        var userData = NewUserData();
        var modId = "snapshot-test-" + Guid.NewGuid().ToString("N")[..8];

        var first = new DateTime(2026, 9, 2, 20, 0, 0, DateTimeKind.Utc);
        DeckSnapshotStore.Capture(userData, modId, first);
        DeckSnapshotStore.Capture(userData, modId, first.AddHours(1));

        var mine = DeckSnapshotStore.ReadIndex()
            .Where(e => e.ModId == modId).OrderBy(e => e.CapturedUtc).ToList();

        Assert.Equal(2, mine.Count);
        Assert.Equal(mine[0].Files["sp_Beijing_homecity.xml"], mine[1].Files["sp_Beijing_homecity.xml"]);

        // And it reads back as the deck it was, named by the file the recording would name.
        var decks = DeckSnapshotStore.Read(modId, first.AddMinutes(1), "sp_Beijing_homecity.xml");

        Assert.NotNull(decks);
        var profile = Assert.Single(decks!);
        Assert.Equal("Beijing", profile.CityName);
        Assert.Equal(new[] { "YPHCExpandedTradingPost", "HCShipWoodCrates3" },
            profile.Decks.Single().Cards.Select(c => c.InternalName));
    }

    [Fact]
    public void AFolderWithNoHomeCityFilesLeavesNothingBehind()
    {
        var dir = Directory.CreateTempSubdirectory("wol-snap-empty-").FullName;
        _temp.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "Savegame"));

        var modId = "snapshot-empty-" + Guid.NewGuid().ToString("N")[..8];
        DeckSnapshotStore.Capture(dir, modId, DateTime.UtcNow);

        Assert.DoesNotContain(DeckSnapshotStore.ReadIndex(), e => e.ModId == modId);
    }

    [Fact]
    public void NothingToCaptureFromIsNotAnError()
    {
        var error = Record.Exception(() =>
        {
            DeckSnapshotStore.Capture(null, "wol", DateTime.UtcNow);
            DeckSnapshotStore.Capture("   ", "wol", DateTime.UtcNow);
            DeckSnapshotStore.Capture(@"C:\no\such\folder", "wol", DateTime.UtcNow);
            DeckSnapshotStore.Capture("x", null, DateTime.UtcNow);
        });

        Assert.Null(error);
        Assert.Null(DeckSnapshotStore.Read(null, DateTime.UtcNow, null));
    }

    /// <summary>A home city folder shaped like the game's, UTF-16 with a BOM as the game writes it.</summary>
    private string NewUserData()
    {
        var dir = Directory.CreateTempSubdirectory("wol-snap-test-").FullName;
        _temp.Add(dir);

        var savegame = Path.Combine(dir, "Savegame");
        Directory.CreateDirectory(savegame);

        File.WriteAllText(
            Path.Combine(savegame, "sp_Beijing_homecity.xml"),
            """
            <savedhomecity version ="2">
              <civ>Chinese</civ>
              <name>Beijing</name>
              <level>16</level>
              <decks>
                <deck>
                  <name>Static Deck</name>
                  <gameid>4</gameid>
                  <cards>
                    <card dbid ="4128">YPHCExpandedTradingPost</card>
                    <card dbid ="2212">HCShipWoodCrates3</card>
                  </cards>
                </deck>
              </decks>
            </savedhomecity>
            """,
            System.Text.Encoding.Unicode);

        return dir;
    }
}
