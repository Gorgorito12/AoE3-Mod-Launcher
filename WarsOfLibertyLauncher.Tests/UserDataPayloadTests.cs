using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="UserDataPayloadService"/> — the seed that lays a mod's folder skeleton into
/// <c>Documents\My Games\&lt;mod&gt;</c> so mods like <i>Knights and Barbarians</i> will start.
///
/// <para><b>Almost every test here is a refusal, and that is the point.</b> The destination is
/// the player's own folder: their saved games, home-city decks, profile and hotkeys. A seed that
/// could overwrite something there, or that recorded a file it did not create, would destroy data
/// while the launcher reported a perfectly successful install — the failure would be silent both
/// times, at write and at uninstall.</para>
/// </summary>
public class UserDataPayloadTests
{
    private const string Folder = "Knights and Barbarians";

    private static UserDataPayloadService.SeedPlan Plan(
        IEnumerable<string> entries,
        IEnumerable<string>? existingFiles = null,
        IEnumerable<string>? existingDirs = null)
    {
        var files = new HashSet<string>(existingFiles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(existingDirs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return UserDataPayloadService.Plan(
            new List<string>(entries), Folder, files.Contains, dirs.Contains);
    }

    // ------------------------------------------------------------------ the invariant

    /// <summary>
    /// THE invariant. A file the player already has is never queued, so it can never be
    /// overwritten — whatever the payload happens to contain.
    /// </summary>
    [Fact]
    public void AFileThePlayerAlreadyHasIsNeverQueued()
    {
        var plan = Plan(
            new[] { "Users3/NewProfile3.xml", "Savegame/sp_Jerusalem_homecity.xml" },
            existingFiles: new[] { "Users3/NewProfile3.xml" });

        Assert.DoesNotContain(plan.Files, f => f.RelativePath == "Users3/NewProfile3.xml");
        Assert.Contains(plan.Files, f => f.RelativePath == "Savegame/sp_Jerusalem_homecity.xml");
        Assert.Equal(1, plan.Skipped);
    }

    /// <summary>
    /// The second half of the same invariant: a file that was skipped is not recorded as ours,
    /// so uninstall can never reach it. Only what <see cref="UserDataPayloadService.Plan"/>
    /// queues is ever written, and only what is written is ever recorded.
    /// </summary>
    [Fact]
    public void ASkippedFileIsNotSomethingWeCouldEverDelete()
    {
        var plan = Plan(
            new[] { "Users3/NewProfile3.xml" },
            existingFiles: new[] { "Users3/NewProfile3.xml" });

        Assert.Empty(plan.Files);
    }

    // ------------------------------------------------------------------ refusals

    /// <summary>
    /// An entry climbing out of the folder is refused. Without this, one
    /// <c>..\Age of Empires 3\Users3\</c> entry would write a mod's files straight into the
    /// player's BASE GAME profile.
    /// </summary>
    [Fact]
    public void AnEntryThatEscapesTheFolderIsRefused()
    {
        Assert.Null(UserDataPayloadService.NormalizeEntry(@"..\Age of Empires 3\Users3\x.xml", ""));
        Assert.Null(UserDataPayloadService.NormalizeEntry("../../etc/passwd", ""));
        Assert.Empty(Plan(new[] { @"..\Age of Empires 3\Users3\x.xml" }).Files);
    }

    /// <summary>A rooted entry names somewhere else entirely and is refused.</summary>
    [Fact]
    public void ARootedEntryIsRefused()
    {
        Assert.Null(UserDataPayloadService.NormalizeEntry(@"C:\Windows\system32\x.dll", ""));
        Assert.Null(UserDataPayloadService.NormalizeEntry("/etc/passwd", ""));
    }

    /// <summary>Empty and whitespace entries are not paths.</summary>
    [Fact]
    public void AnEmptyEntryIsRefused()
    {
        Assert.Null(UserDataPayloadService.NormalizeEntry(null, ""));
        Assert.Null(UserDataPayloadService.NormalizeEntry("   ", ""));
    }

    // ------------------------------------------------------------------ shape

    /// <summary>
    /// The skeleton is the whole reason this feature exists: the mod falls back to the stock maps
    /// when these folders are missing, and most of them contain no files at all.
    /// </summary>
    [Fact]
    public void EmptySkeletonDirectoriesAreStillCreated()
    {
        var plan = Plan(new[] { "AI3/", "RM3/", "RM3/groupings/", "Screenshots/" });

        Assert.Contains("AI3", plan.Dirs);
        Assert.Contains("RM3", plan.Dirs);
        Assert.Contains("RM3/groupings", plan.Dirs);
        Assert.Contains("Screenshots", plan.Dirs);
        Assert.Empty(plan.Files);
    }

    /// <summary>Parents come before children, or creation fails on the first nested folder.</summary>
    [Fact]
    public void DirectoriesAreOrderedParentsFirst()
    {
        var plan = Plan(new[] { "RM3/groupings/x.xml" });

        Assert.Equal("RM3", plan.Dirs[0]);
        Assert.Equal("RM3/groupings", plan.Dirs[1]);
    }

    /// <summary>A directory that is already there is not re-created and not recorded.</summary>
    [Fact]
    public void ADirectoryThatAlreadyExistsIsNotRecorded()
    {
        var plan = Plan(new[] { "AI3/", "RM3/" }, existingDirs: new[] { "AI3" });

        Assert.DoesNotContain("AI3", plan.Dirs);
        Assert.Contains("RM3", plan.Dirs);
    }

    /// <summary>The zip may be rooted at the mod's folder name; that wrapper is stripped.</summary>
    [Fact]
    public void AWrapperFolderNamedAfterTheModIsStripped()
    {
        var plan = Plan(new[]
        {
            "Knights and Barbarians/Users3/NewProfile3.xml",
            "Knights and Barbarians/AI3/",
        });

        Assert.Contains(plan.Files, f => f.RelativePath == "Users3/NewProfile3.xml");
        Assert.Contains("AI3", plan.Dirs);
    }

    /// <summary>…and it may equally be rooted at the contents, with nothing to strip.</summary>
    [Fact]
    public void AZipRootedAtTheContentsNeedsNoStripping()
    {
        var plan = Plan(new[] { "Users3/NewProfile3.xml", "AI3/" });

        Assert.Contains(plan.Files, f => f.RelativePath == "Users3/NewProfile3.xml");
    }

    /// <summary>
    /// The reason this does NOT reuse <c>NativeInstallService.ResolvePayloadPrefix</c>, which
    /// strips any single shared top-level folder: a zip holding only <c>Users3\…</c> would have
    /// <c>Users3</c> stripped and the profile written loose in the root, where the game would
    /// never look for it. Only the folder the payload is FOR is ever stripped.
    /// </summary>
    [Fact]
    public void ASingleContentFolderIsNotMistakenForAWrapper()
    {
        var plan = Plan(new[] { "Users3/NewProfile3.xml", "Users3/LastProfile3.dat" });

        Assert.Contains(plan.Files, f => f.RelativePath == "Users3/NewProfile3.xml");
        Assert.DoesNotContain(plan.Files, f => f.RelativePath == "NewProfile3.xml");
    }

    /// <summary>Backslash separators are what a Windows-built zip actually contains.</summary>
    [Fact]
    public void BackslashSeparatedEntriesAreUnderstood()
    {
        var plan = Plan(new[] { @"Knights and Barbarians\Users3\NewProfile3.xml" });

        Assert.Contains(plan.Files, f => f.RelativePath == "Users3/NewProfile3.xml");
    }

    // ------------------------------------------------------------------ removal

    private static FileFingerprint Fp(long size, string sha) => new(size, sha);

    /// <summary>
    /// THE second invariant. The game rewrites its profile on every run, so a player who has
    /// launched the mod once owns that file — uninstall must leave it alone even when they asked
    /// for the cleanup.
    /// </summary>
    [Fact]
    public void RemovalSkipsAFileThePlayerHasChanged()
    {
        var recorded = new Dictionary<string, FileFingerprint>
        {
            ["Users3/NewProfile3.xml"] = Fp(100, "aaa"),
            ["Savegame/sp_Cairo_homecity.xml"] = Fp(200, "bbb"),
        };

        var take = UserDataPayloadService.SelectForRemoval(recorded, rel =>
            rel == "Users3/NewProfile3.xml"
                ? Fp(140, "changed")          // played with: theirs now
                : Fp(200, "bbb"));            // untouched: still ours

        Assert.Equal(new[] { "Savegame/sp_Cairo_homecity.xml" }, take);
    }

    /// <summary>A same-size but different-content file is still changed; size alone is not proof.</summary>
    [Fact]
    public void RemovalComparesContentNotJustSize()
    {
        var recorded = new Dictionary<string, FileFingerprint> { ["a.xml"] = Fp(100, "aaa") };

        Assert.Empty(UserDataPayloadService.SelectForRemoval(recorded, _ => Fp(100, "different")));
    }

    /// <summary>A file already gone is simply not ours to remove.</summary>
    [Fact]
    public void RemovalIgnoresAFileThatIsAlreadyGone()
    {
        var recorded = new Dictionary<string, FileFingerprint> { ["a.xml"] = Fp(100, "aaa") };

        Assert.Empty(UserDataPayloadService.SelectForRemoval(recorded, _ => null));
    }

    /// <summary>
    /// A record with no hash cannot be proven ours, so it is kept. Fail towards leaving the
    /// player's folder alone.
    /// </summary>
    [Fact]
    public void RemovalKeepsAnUnverifiableRecord()
    {
        var recorded = new Dictionary<string, FileFingerprint> { ["a.xml"] = Fp(100, "") };

        Assert.Empty(UserDataPayloadService.SelectForRemoval(recorded, _ => Fp(100, "")));
    }

    /// <summary>Nothing recorded, nothing removed — the case for every mod without a payload.</summary>
    [Fact]
    public void NothingRecordedMeansNothingRemoved()
    {
        Assert.Empty(UserDataPayloadService.SelectForRemoval(
            new Dictionary<string, FileFingerprint>(), _ => Fp(1, "x")));
    }

    // ------------------------------------------------------------------ the hash Seed relies on

    /// <summary>
    /// <c>Seed</c> records a fingerprint per file it writes, and it must do that SYNCHRONOUSLY:
    /// it used to call <c>ComputeSha256Async(...).GetAwaiter().GetResult()</c>, which deadlocked a
    /// real install at 95 % — the async version awaits without <c>ConfigureAwait(false)</c>, so its
    /// continuation needs the very WPF SynchronizationContext the blocking call was holding.
    ///
    /// <para><b>This test cannot reproduce that</b> — a test host has no SynchronizationContext, so
    /// the old call returned instantly here and 2,193 tests passed straight over the bug. What it
    /// CAN pin is the property that lets <c>Seed</c> stay synchronous at all: the two must agree,
    /// or uninstall (which compares a recorded digest against a freshly computed one) would refuse
    /// to remove a file the launcher genuinely wrote.</para>
    /// </summary>
    [Fact]
    public async Task TheSynchronousHashAgreesWithTheAsyncOne()
    {
        var path = Path.Combine(Path.GetTempPath(), "aoe3ml-hash-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Bigger than the 1 MiB stream buffer, so this exercises more than one read.
            var bytes = new byte[(1024 * 1024) + 4321];
            new Random(1234).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            Assert.Equal(await HashService.ComputeSha256Async(path), HashService.ComputeSha256(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>A missing file is an empty digest in both, so neither can be mistaken for a hash.</summary>
    [Fact]
    public async Task BothHashesAgreeThatAMissingFileHasNoDigest()
    {
        var absent = Path.Combine(Path.GetTempPath(), "aoe3ml-absent-" + Guid.NewGuid().ToString("N"));

        Assert.Equal("", HashService.ComputeSha256(absent));
        Assert.Equal("", await HashService.ComputeSha256Async(absent));
    }

    // ------------------------------------------------------------------ finding the seed asset

    private static DeltaChainPlanner.ReleaseSnapshot Rel(
        string tag, bool prerelease, params string[] assetNames)
        => new(tag, prerelease, assetNames
            .Select(n => new DeltaChainPlanner.ReleaseAssetSnapshot(n, 1, $"https://x/{tag}/{n}"))
            .ToList());

    private static UserDataPayloadService.SeedAssetPick? Pick(
        IEnumerable<DeltaChainPlanner.ReleaseSnapshot> graph,
        string installing,
        string? approved = "1.0")
        => UserDataPayloadService.SelectSeedAsset(
            graph.ToList(), installing, approved, "userdata.zip");

    /// <summary>
    /// The no-op case, and the one that must never regress: when the release being installed
    /// carries the asset, nothing else is consulted. Every other test here is about a modder who
    /// forgot; this one is about the modder who did not.
    /// </summary>
    [Fact]
    public void TheReleaseBeingInstalledWinsAndNothingElseIsConsulted()
    {
        var pick = Pick(new[]
        {
            Rel("1.2", false, "payload.zip", "userdata.zip"),
            Rel("1.0", false, "payload.zip", "userdata.zip"),
        }, installing: "1.2");

        Assert.Equal("1.2", pick!.Value.Tag);
    }

    /// <summary>
    /// The case this feature exists for: the author shipped 1.2 with only the payload. Without a
    /// fallback every install made from 1.2 onwards gets no skeleton, and the mod does not start.
    /// </summary>
    [Fact]
    public void AReleaseMissingTheSeedFallsBackToTheApprovedTag()
    {
        var pick = Pick(new[]
        {
            Rel("1.2", false, "payload.zip"),
            Rel("1.1", false, "payload.zip", "userdata.zip"),
            Rel("1.0", false, "payload.zip", "userdata.zip"),
        }, installing: "1.2", approved: "1.0");

        Assert.Equal("1.0", pick!.Value.Tag);
    }

    /// <summary>
    /// With neither the installing release nor the approved tag carrying it — the approved tag
    /// having been re-tagged away, which is ordinary housekeeping for a modder — the newest
    /// release that has it is used rather than giving up.
    /// </summary>
    [Fact]
    public void WithNeitherTagCarryingItTheNewestReleaseThatHasItIsUsed()
    {
        var pick = Pick(new[]
        {
            Rel("1.3", false, "payload.zip"),
            Rel("1.2", false, "payload.zip", "userdata.zip"),
            Rel("1.1", false, "payload.zip", "userdata.zip"),
        }, installing: "1.3", approved: "gone");

        Assert.Equal("1.2", pick!.Value.Tag);
    }

    /// <summary>
    /// A prerelease's seed should not be handed to a stable install while a stable one exists —
    /// so the newer prerelease loses to the older stable release.
    /// </summary>
    [Fact]
    public void AStableReleaseIsPreferredOverANewerPrerelease()
    {
        var pick = Pick(new[]
        {
            Rel("2.0", false, "payload.zip"),
            Rel("1.9-rc1", true, "userdata.zip"),
            Rel("1.8", false, "userdata.zip"),
        }, installing: "2.0", approved: "gone");

        Assert.Equal("1.8", pick!.Value.Tag);
    }

    /// <summary>…but a prerelease still beats a mod that will not start.</summary>
    [Fact]
    public void APrereleaseIsUsedWhenNoStableReleaseCarriesIt()
    {
        var pick = Pick(new[]
        {
            Rel("2.0", false, "payload.zip"),
            Rel("1.9-rc1", true, "userdata.zip"),
        }, installing: "2.0", approved: "gone");

        Assert.Equal("1.9-rc1", pick!.Value.Tag);
    }

    /// <summary>
    /// No release carries it at all — the mod simply does not ship one, or its name is misspelled
    /// in the catalog. The caller must land on AssetMissing exactly as it did before the fallback
    /// existed, so the answer has to stay null rather than degrade to "some other asset".
    /// </summary>
    [Fact]
    public void NoReleaseCarryingItStaysUnresolved()
    {
        Assert.Null(Pick(new[]
        {
            Rel("1.2", false, "payload.zip"),
            Rel("1.1", false, "payload.zip", "patch-1.0-to-1.1.zip"),
        }, installing: "1.2"));
    }

    /// <summary>
    /// ListReleaseGraphAsync returns an EMPTY list on any failure rather than throwing — offline,
    /// rate-limited, or a repo that has vanished. That must read as "not found", never as a crash
    /// on the install path, since the whole seed is best-effort.
    /// </summary>
    [Fact]
    public void AnEmptyGraphIsUnresolvedRatherThanAnError()
    {
        Assert.Null(Pick(Array.Empty<DeltaChainPlanner.ReleaseSnapshot>(), installing: "1.2"));
    }

    /// <summary>GitHub asset names preserve case; the catalog's spelling need not match it.</summary>
    [Fact]
    public void TheAssetNameMatchesCaseInsensitively()
    {
        var pick = Pick(new[] { Rel("1.2", false, "UserData.ZIP") }, installing: "1.2");

        Assert.Equal("1.2", pick!.Value.Tag);
    }

    /// <summary>The url comes from the release actually picked, not from the one being installed.</summary>
    [Fact]
    public void TheUrlComesFromTheReleaseThatWasPicked()
    {
        var pick = Pick(new[]
        {
            Rel("1.2", false, "payload.zip"),
            Rel("1.0", false, "userdata.zip"),
        }, installing: "1.2", approved: "1.0");

        Assert.Equal("https://x/1.0/userdata.zip", pick!.Value.Url);
    }

    // ------------------------------------------------------------------ catalog projection

    private static ModCatalogEntry Entry(string? userDataFolder, string? payload)
        => new()
        {
            Manifest = new ModCatalogManifest
            {
                Id = "test-mod",
                DisplayName = "Test Mod",
                UserDataFolder = userDataFolder,
                Install = new ModCatalogInstall
                {
                    Type = "IsolatedFolder",
                    UserDataPayload = payload,
                },
                Update = new ModCatalogUpdate { Mechanism = "GitHubReleases" },
            },
        };

    /// <summary>The ordinary case: a declared folder and a declared asset both come through.</summary>
    [Fact]
    public void ADeclaredPayloadIsProjected()
    {
        var profile = ModRegistry.ProjectToProfile(Entry("Knights and Barbarians", "userdata.zip"));

        Assert.Equal("userdata.zip", profile.UserDataPayload);
    }

    /// <summary>
    /// A payload with no declared folder is DROPPED. UserDataService can discover a folder for a
    /// mod that named none, which is fine for reading it (backups, diagnostics) and not fine for
    /// writing into it: a wrong guess would drop one mod's files into another mod's save folder.
    /// The destination has to be declared.
    /// </summary>
    [Fact]
    public void APayloadWithNoDeclaredFolderIsDropped()
    {
        Assert.Equal("", ModRegistry.ProjectToProfile(Entry(null, "userdata.zip")).UserDataPayload);
        Assert.Equal("", ModRegistry.ProjectToProfile(Entry("   ", "userdata.zip")).UserDataPayload);
    }

    /// <summary>Declaring neither is the case for almost every mod, and stays empty.</summary>
    [Fact]
    public void NoPayloadDeclaredStaysEmpty()
    {
        Assert.Equal("", ModRegistry.ProjectToProfile(Entry("Some Folder", null)).UserDataPayload);
    }
}
