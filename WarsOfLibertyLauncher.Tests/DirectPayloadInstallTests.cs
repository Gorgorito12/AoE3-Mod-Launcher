using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Both this class and <see cref="PayloadIntegrityTests"/> drive the real
/// <see cref="NativeInstallService.ExtractPayloadAsync"/>, which extracts into the ONE
/// shared <c>%TEMP%\WarsOfLibertyLauncher\native-install\extracted</c> folder and wipes it
/// on entry. xUnit runs each test class as its own collection in parallel by default, so
/// without sharing a collection the two would race and wipe each other's output.
/// </summary>
[CollectionDefinition("payload-extract", DisableParallelization = true)]
public class PayloadExtractCollection { }

/// <summary>
/// Tests for the DIRECT payload install — <see cref="NativeInstallService.ExtractPayloadToDestinationAsync"/>
/// and its wrapper rule <see cref="NativeInstallService.ResolvePayloadPrefix"/> — which
/// extracts a mod's payload straight into the install folder instead of staging a full
/// loose copy of it in <c>%TEMP%</c> first.
///
/// <para>The centrepiece is <see cref="DirectExtraction_LandsTheSameFilesAsTheStagedPath"/>.
/// The two paths must agree on WHERE every file lands, and they decide that with different
/// machinery: the staged path inspects the extracted FOLDER
/// (<see cref="NativeInstallService.NormalizePayloadRoot"/>), the direct path reads the
/// zip's ENTRY NAMES. If they ever disagree, a wrapped payload lands one level too deep and
/// the mod silently does nothing — and for a mod on <c>GitHubReleases</c> the next update's
/// <c>ApplyUpdateDeletions</c> would read every previously-shipped file as "no longer
/// shipped" and delete it. Comparing them directly is the only way to know they agree.</para>
/// </summary>
[Collection("payload-extract")]
public class DirectPayloadInstallTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-direct-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Builds a zip. An entry ending in '/' is a DIRECTORY entry (empty
    /// <c>ZipArchiveEntry.Name</c>) — both extraction paths skip those, and the fact that
    /// they never reach disk is exactly what the wrapper rules have to agree about.
    /// </summary>
    private string MakeZip(string name, params string[] entries)
    {
        var zipPath = Path.Combine(NewTempDir(), name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            var entry = zip.CreateEntry(e);
            if (e.EndsWith("/", StringComparison.Ordinal)) continue;
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write($"content of {e}");
        }
        return zipPath;
    }

    private static string Sha256Of(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    // ------------------------------------------------------------------
    // ResolvePayloadPrefix — the entry-name twin of NormalizePayloadRoot.
    // These mirror PayloadRootTests case for case.
    // ------------------------------------------------------------------

    [Fact]
    public void FlatPayload_HasNoWrapperPrefix()
    {
        Assert.Equal("", NativeInstallService.ResolvePayloadPrefix(
            new[] { "data/protoy.xml", "art/thing.ddt", "Sound/x.wav" }));
    }

    [Fact]
    public void SingleWrapper_IsStripped()
    {
        Assert.Equal("Knights and Barbarians/", NativeInstallService.ResolvePayloadPrefix(
            new[] { "Knights and Barbarians/data/protoy.xml", "Knights and Barbarians/art/thing.ddt" }));
    }

    [Fact]
    public void WrapperPlusLooseFile_IsNotAWrapper()
    {
        Assert.Equal("", NativeInstallService.ResolvePayloadPrefix(
            new[] { "data/protoy.xml", "readme.txt" }));
    }

    [Fact]
    public void DoubleWrapper_DescendsUntilRealContent()
    {
        Assert.Equal("outer/inner/", NativeInstallService.ResolvePayloadPrefix(
            new[] { "outer/inner/data/protoy.xml", "outer/inner/art/thing.ddt" }));
    }

    [Fact]
    public void EmptyPayload_HasNoPrefix()
    {
        Assert.Equal("", NativeInstallService.ResolvePayloadPrefix(Array.Empty<string>()));
    }

    // ------------------------------------------------------------------
    // The differential test.
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> PayloadShapes() => new List<object[]>
    {
        // Flat — the WoL / Improvement Mod shape. Must be an outright no-op.
        new object[] { "flat", new[] { "data/protoy.xml", "art/thing.ddt", "Sound/x.wav" } },
        // One wrapper folder around everything.
        new object[] { "wrapped", new[] { "W/data/protoy.xml", "W/art/thing.ddt" } },
        // A wrapper that also drops a file at the root — not a pure wrapper.
        new object[] { "wrapper-plus-loose", new[] { "W/data/protoy.xml", "readme.txt" } },
        // Doubly wrapped.
        new object[] { "double-wrapped", new[] { "o/i/data/protoy.xml", "o/i/art/thing.ddt" } },
        // Explicit directory entries alongside the files.
        new object[] { "with-dir-entries", new[] { "W/", "W/data/", "W/data/protoy.xml", "W/art/thing.ddt" } },
        // THE divergence case: an explicit EMPTY directory entry. It never reaches disk
        // (the extract loop skips directory entries before creating anything), so the
        // folder rule sees ONE top-level name and strips the wrapper. An entry rule that
        // counted every entry would see two and strip nothing.
        new object[] { "empty-dir-entry", new[] { "EmptyDir/", "W/data/protoy.xml", "W/art/thing.ddt" } },
        // Backslash separators inside entry names.
        new object[] { "backslashes", new[] { @"data\protoy.xml", @"art\thing.ddt" } },
        // "./"-prefixed entry names.
        new object[] { "dot-slash", new[] { "./data/protoy.xml", "./art/thing.ddt" } },
        // A single top-level folder holding a single folder holding files: both rules
        // descend all the way to the level with real content.
        new object[] { "deep-single-chain", new[] { "a/b/c/protoy.xml" } },
        // A payload whose ONLY top-level folder is data\ has that folder stripped as if
        // it were a wrapper, so protoy.xml lands at the install root. Surprising, and
        // pinned here precisely because it is only defensible while the staged path does
        // exactly the same — which is what this test proves. (AddonPaths.StripCommonRoot
        // vetoes game-root names for this reason; NormalizePayloadRoot deliberately does
        // not, and the two payload paths must agree with each other before either agrees
        // with taste.) A real payload — WoL, Improvement Mod — ships several top-level
        // folders, so this never fires in practice.
        new object[] { "lone-game-folder", new[] { "data/protoy.xml" } },
    };

    /// <summary>
    /// For every payload shape, the direct extraction must put the files at exactly the
    /// same relative paths the staged path does. Anything else means a wrapped payload
    /// installs to the wrong depth for one of the two mechanisms.
    /// </summary>
    [Theory]
    [MemberData(nameof(PayloadShapes))]
    public async Task DirectExtraction_LandsTheSameFilesAsTheStagedPath(string label, string[] entries)
    {
        var zip = MakeZip(label + ".zip", entries);
        var svc = new NativeInstallService();

        // Staged: extract to %TEMP%, then take each written file relative to the resolved
        // payload root — which is what the overlay copy uses as its source.
        var staged = await svc.ExtractPayloadAsync(zip, null, null, default);
        var stagedRel = staged.Written
            .Select(w => Path.GetRelativePath(staged.Root, w).Replace('\\', '/'))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Direct: extract into a fresh destination and read the capture.
        var dest = NewTempDir();
        var capture = await svc.ExtractPayloadToDestinationAsync(zip, dest, null, null, default);
        var directRel = capture.AllFiles
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(stagedRel, directRel);

        // And the files are really there, at those paths.
        foreach (var rel in directRel)
            Assert.True(File.Exists(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar))),
                $"[{label}] missing on disk: {rel}");
    }

    // ------------------------------------------------------------------
    // Capture semantics: classification, hashes, duplicates, zip-slip.
    // ------------------------------------------------------------------

    /// <summary>
    /// Net-new vs base-shadowing is decided by whether the file was already on disk when
    /// the payload landed — on a fresh install that means "already in the AoE3 clone".
    /// This is the same seam <see cref="OverlayDeletionTests"/> pins for the staged copy.
    /// </summary>
    [Fact]
    public async Task Capture_ClassifiesNetNewAgainstWhatTheCloneLeft()
    {
        var dest = NewTempDir();
        // Stand in for the cloned base game.
        var baseFile = Path.Combine(dest, "data", "base.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFile)!);
        File.WriteAllText(baseFile, "the base game's copy");

        // Several top-level folders — the real WoL / Improvement Mod shape. With only
        // data\ at the top the wrapper rule would (correctly, and exactly like the staged
        // path) strip it, which is a different thing to test; see "lone-game-folder".
        var zip = MakeZip("payload.zip",
            "data/base.xml", "data/mod_a.xml", "data/mod_b.xml", "art/thing.ddt");
        var capture = await new NativeInstallService()
            .ExtractPayloadToDestinationAsync(zip, dest, null, null, default);

        Assert.Contains("data/mod_a.xml", capture.FreshOnDisk);
        Assert.Contains("data/mod_b.xml", capture.FreshOnDisk);
        Assert.DoesNotContain("data/base.xml", capture.FreshOnDisk);   // shadowed the base
        Assert.Equal(4, capture.AllFiles.Count);

        // The payload's version won.
        Assert.Equal("content of data/base.xml", File.ReadAllText(baseFile));
    }

    /// <summary>
    /// The fingerprints are computed while writing, in one pass. They have to describe the
    /// bytes that actually landed, or Verify calls a healthy file corrupt forever and
    /// Repair re-downloads gigabytes to "fix" it.
    /// </summary>
    [Fact]
    public async Task Capture_HashesMatchTheBytesOnDisk()
    {
        var dest = NewTempDir();
        var zip = MakeZip("payload.zip", "data/protoy.xml", "AI3/wolai.upl");

        var capture = await new NativeInstallService()
            .ExtractPayloadToDestinationAsync(zip, dest, null, null, default);

        Assert.Equal(2, capture.Hashes.Count);
        foreach (var (rel, fp) in capture.Hashes)
        {
            var full = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(Sha256Of(full), fp.Sha256);
            Assert.Equal(new FileInfo(full).Length, fp.Size);
        }
    }

    /// <summary>
    /// A zip may legally carry the same path twice. The staged path enumerates the DISK
    /// and so reports it once; recording it twice here would put it twice in the
    /// manifest's overlay list and twice in OverlayNetNew.
    /// </summary>
    [Fact]
    public async Task Capture_RecordsADuplicatedEntryOnlyOnce()
    {
        var dest = NewTempDir();
        var zipPath = Path.Combine(NewTempDir(), "dupes.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "data/protoy.xml", "data/protoy.xml", "art/thing.ddt" })
            {
                using var s = zip.CreateEntry(name).Open();
                using var w = new StreamWriter(s);
                w.Write("x");
            }
        }

        var capture = await new NativeInstallService()
            .ExtractPayloadToDestinationAsync(zipPath, dest, null, null, default);

        Assert.Equal(2, capture.AllFiles.Count);
        Assert.Single(capture.AllFiles, p => p == "data/protoy.xml");
    }

    /// <summary>
    /// The zip-slip guard matters more here than in the staged path: an entry that escapes
    /// would write outside the player's game folder, not outside a throwaway temp dir.
    /// </summary>
    [Fact]
    public async Task Capture_RejectsAnEntryThatWouldEscapeTheInstallFolder()
    {
        var parent = NewTempDir();
        var dest = Path.Combine(parent, "install");
        Directory.CreateDirectory(dest);
        var outside = Path.Combine(parent, "escaped.txt");

        var zip = MakeZip("evil.zip", "data/protoy.xml", "art/thing.ddt", "../escaped.txt");
        var capture = await new NativeInstallService()
            .ExtractPayloadToDestinationAsync(zip, dest, null, null, default);

        Assert.False(File.Exists(outside));
        Assert.Equal(2, capture.AllFiles.Count);
        Assert.Contains("data/protoy.xml", capture.AllFiles);
        // The escaping entry must also be invisible to the wrapper rule — if it counted,
        // it would look like a loose file at the root and change where everything lands.
        Assert.DoesNotContain(capture.AllFiles, p => p.Contains("escaped"));
    }

    /// <summary>
    /// The no-op that matters: a healthy payload must extract without the integrity guard
    /// firing. If this fails, the guard is aborting real installs — worse than the problem
    /// it exists to catch.
    /// </summary>
    [Fact]
    public async Task IntactPayload_ExtractsWithoutTrippingTheIntegrityGuard()
    {
        var dest = NewTempDir();
        var zip = MakeZip("payload.zip",
            "data/protoy.xml", "data/techtreey.xml", "AI3/wolai.upl", "art/zulushield/x.ddt");

        var capture = await new NativeInstallService()
            .ExtractPayloadToDestinationAsync(zip, dest, null, null, default);

        Assert.Equal(4, capture.AllFiles.Count);
        Assert.Equal(4, capture.FreshOnDisk.Count);
    }

    /// <summary>
    /// Opening the archive IS the corruption check the direct path runs before the caller
    /// spends minutes cloning AoE3.
    /// </summary>
    [Fact]
    public void ValidatePayloadZip_AcceptsAGoodZipAndRejectsGarbage()
    {
        var good = MakeZip("good.zip", "data/protoy.xml");
        NativeInstallService.ValidatePayloadZip(good);   // must not throw

        var bad = Path.Combine(NewTempDir(), "bad.zip");
        File.WriteAllBytes(bad, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02 });

        Assert.ThrowsAny<Exception>(() => NativeInstallService.ValidatePayloadZip(bad));
    }
}
