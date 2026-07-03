using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the optional GitHubReleases incremental delta-patch feature
/// (<see cref="DeltaPatchService"/>): the pure diff/select/eligibility/pre-verify logic and a
/// generator round-trip. The download/apply path is integration (Windows smoke) — these pin the
/// robustness guards that must never regress: pre-verify rejects a diverged/mislabeled base, the
/// diff is exact, and the external-hosted gate holds.
/// </summary>
public class DeltaPatchTests : IDisposable
{
    private readonly List<string> _temp = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("delta-test-").FullName;
        _temp.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _temp)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------- ComputeDiff

    [Fact]
    public void ComputeDiff_ClassifiesAddedChangedDeleted()
    {
        var oldH = new Dictionary<string, string>
        {
            ["data/keep.xml"] = "aaaa",
            ["data/change.xml"] = "bbbb",
            ["data/gone.xml"] = "cccc",
        };
        var newH = new Dictionary<string, string>
        {
            ["data/keep.xml"] = "aaaa",       // unchanged → not in patch
            ["data/change.xml"] = "b2b2",      // changed
            ["data/new.xml"] = "dddd",         // added
        };

        var (changed, deleted) = DeltaPatchService.ComputeDiff(oldH, newH);

        Assert.Equal(new[] { "data/change.xml", "data/new.xml" }, changed.Select(c => c.Path).ToArray());
        Assert.Equal(new[] { "data/gone.xml" }, deleted.ToArray());

        var chg = changed.Single(c => c.Path == "data/change.xml");
        Assert.Equal("bbbb", chg.FromSha256);
        Assert.Equal("b2b2", chg.Sha256);

        var add = changed.Single(c => c.Path == "data/new.xml");
        Assert.Null(add.FromSha256);          // an addition has no prior hash
        Assert.Equal("dddd", add.Sha256);
    }

    // ---------------------------------------------------------------- SelectPatch

    [Fact]
    public void SelectPatch_MatchesFromAndToTag_ElseNull()
    {
        var a = new DeltaPatchService.DeltaPatchDescriptor { FromTag = "v1.0", ToTag = "v1.1" };
        var b = new DeltaPatchService.DeltaPatchDescriptor { FromTag = "v0.9", ToTag = "v1.1" };
        var set = new[] { a, b };

        Assert.Same(a, DeltaPatchService.SelectPatch(set, "v1.0", "v1.1"));
        Assert.Same(b, DeltaPatchService.SelectPatch(set, "v0.9", "v1.1"));
        Assert.Same(a, DeltaPatchService.SelectPatch(set, "V1.0", "V1.1"));   // case-insensitive
        Assert.Null(DeltaPatchService.SelectPatch(set, "v0.5", "v1.1"));       // version skip → full
        Assert.Null(DeltaPatchService.SelectPatch(set, "v1.0", "v2.0"));       // wrong target
        Assert.Null(DeltaPatchService.SelectPatch(set, "", "v1.1"));
    }

    // ---------------------------------------------------------------- IsEligible

    [Fact]
    public void IsEligible_RequiresGitHubReleasesOptInAndNotExternal()
    {
        ModProfile Gh(bool delta, string external = "") => new()
        {
            Id = "m",
            UpdateMechanism = ModUpdateMechanism.GitHubReleases,
            GitHubReleases = new GitHubReleasesSettings
            {
                SourceRepo = "o/r",
                ApprovedReleaseTag = "v1",
                DeltaPatches = delta,
                ExternalAssetUrlTemplate = external,
            },
        };

        Assert.True(DeltaPatchService.IsEligible(Gh(delta: true)));
        Assert.False(DeltaPatchService.IsEligible(Gh(delta: false)));                        // opt-in off
        Assert.False(DeltaPatchService.IsEligible(Gh(delta: true, external: "https://x/{tag}.zip"))); // external-hosted
        Assert.False(DeltaPatchService.IsEligible(new ModProfile { Id = "m", UpdateMechanism = ModUpdateMechanism.WolPatcher }));
        Assert.False(DeltaPatchService.IsEligible(null));
    }

    // ---------------------------------------------------------------- PreVerify

    private static InstallManifest ManifestWith(params (string path, string sha)[] files)
    {
        var m = new InstallManifest();
        foreach (var (p, s) in files)
            m.FileHashes[p] = new FileFingerprint(1, s);
        return m;
    }

    [Fact]
    public void PreVerify_StrongPath_AcceptsMatchingFromHash_RejectsMismatch()
    {
        var manifest = ManifestWith(("data/protoy.xml", "aaaa"));

        var ok = new DeltaPatchService.DeltaPatchDescriptor
        {
            FromTag = "v1", ToTag = "v2",
            Changed = { new() { Path = "data/protoy.xml", FromSha256 = "aaaa", Sha256 = "bbbb" } },
        };
        Assert.True(DeltaPatchService.PreVerify(@"C:\nope", manifest, ok, null));

        var diverged = new DeltaPatchService.DeltaPatchDescriptor
        {
            FromTag = "v1", ToTag = "v2",
            Changed = { new() { Path = "data/protoy.xml", FromSha256 = "ZZZZ", Sha256 = "bbbb" } },
        };
        Assert.False(DeltaPatchService.PreVerify(@"C:\nope", manifest, diverged, null));
    }

    [Fact]
    public void PreVerify_Addition_OkWhenAbsentFromManifest_RejectsInconsistentFromHash()
    {
        var manifest = ManifestWith(("data/protoy.xml", "aaaa"));

        // A genuine addition: blank fromSha256, path not in the manifest → allowed.
        var addition = new DeltaPatchService.DeltaPatchDescriptor
        {
            FromTag = "v1", ToTag = "v2",
            Changed = { new() { Path = "data/new.xml", FromSha256 = null, Sha256 = "dddd" } },
        };
        Assert.True(DeltaPatchService.PreVerify(@"C:\nope", manifest, addition, null));

        // Inconsistent: a non-blank fromSha256 for a file the manifest doesn't know → reject.
        var inconsistent = new DeltaPatchService.DeltaPatchDescriptor
        {
            FromTag = "v1", ToTag = "v2",
            Changed = { new() { Path = "data/new.xml", FromSha256 = "eeee", Sha256 = "dddd" } },
        };
        Assert.False(DeltaPatchService.PreVerify(@"C:\nope", manifest, inconsistent, null));
    }

    [Fact]
    public void PreVerify_DegradedPath_VerifiesLiveFileAgainstManifest()
    {
        var install = NewTempDir();
        var rel = "data/live.xml";
        var full = Path.Combine(install, "data", "live.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "hello");
        var liveHash = VerifyService.ComputeFingerprintOf(full).Sha256;

        // No fromSha256 in the descriptor → PreVerify hashes the live file vs the manifest.
        var manifestOk = ManifestWith((rel, liveHash));
        var desc = new DeltaPatchService.DeltaPatchDescriptor
        {
            FromTag = "v1", ToTag = "v2",
            Changed = { new() { Path = rel, FromSha256 = null, Sha256 = "newhash" } },
        };
        Assert.True(DeltaPatchService.PreVerify(install, manifestOk, desc, null));

        // Manifest records a different hash than what's on disk (diverged install) → reject.
        var manifestBad = ManifestWith((rel, "somethingelse"));
        Assert.False(DeltaPatchService.PreVerify(install, manifestBad, desc, null));
    }

    // ---------------------------------------------------------------- generator round-trip

    [Fact]
    public async Task GeneratePatchAsync_ProducesPatchWithOnlyChangedFiles_AndDescriptor()
    {
        var oldDir = NewTempDir();
        var newDir = NewTempDir();
        var outDir = NewTempDir();

        // OLD overlay: keep, change, gone.
        Write(oldDir, "data/keep.xml", "same");
        Write(oldDir, "data/change.xml", "old-bytes");
        Write(oldDir, "data/gone.xml", "removed");
        // NEW overlay: keep (identical), change (new bytes), new (added). gone is dropped.
        Write(newDir, "data/keep.xml", "same");
        Write(newDir, "data/change.xml", "new-bytes");
        Write(newDir, "data/new.xml", "added");

        var oldZip = Path.Combine(outDir, "old.zip");
        var newZip = Path.Combine(outDir, "new.zip");
        ZipFile.CreateFromDirectory(oldDir, oldZip);
        ZipFile.CreateFromDirectory(newDir, newZip);

        var result = await DeltaPatchService.GeneratePatchAsync(oldZip, newZip, "v1.0", "v1.1", outDir);

        Assert.Equal(2, result.ChangedCount);     // change.xml + new.xml
        Assert.Equal(1, result.DeletedCount);      // gone.xml
        Assert.True(File.Exists(result.PatchZipPath));
        Assert.True(File.Exists(result.PatchJsonPath));

        // The patch zip carries ONLY the changed/added files, not the unchanged one.
        using var zip = ZipFile.OpenRead(result.PatchZipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "data/change.xml", "data/new.xml" }, names);
    }

    private static void Write(string root, string rel, string content)
    {
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ---------------------------------------------------------------- apply (integration)

    private string ShaOf(string content)
    {
        var t = Path.Combine(NewTempDir(), "c.bin");
        File.WriteAllText(t, content);
        return VerifyService.ComputeFingerprintOf(t).Sha256;
    }

    /// <summary>
    /// End-to-end apply: lay down a v1.0 install (with an ACTIVE translation on a covered file),
    /// generate a real patch to v1.1, apply it, and assert the file tree + manifest are what a full
    /// update would produce — AND that the <c>_originals</c> snapshot for an UNCHANGED covered file
    /// stays canonical English (the exact regression that would break version detection + the
    /// multiplayer fingerprint for translated users). This is the path build/smoke never exercise.
    /// </summary>
    [Fact]
    public async Task ApplyGitHubDelta_MatchesFullResult_AndKeepsOriginalsCanonicalForTranslatedUser()
    {
        var install = NewTempDir();

        // v1.0 overlay on disk (canonical bytes).
        Write(install, "data/protoy.xml", "proto-v1");          // non-covered, will change
        Write(install, "data/stringtabley.xml", "EN-strings");  // covered, UNCHANGED across versions
        Write(install, "art/old.ddt", "old-art");               // net-new, dropped in v1.1

        // Active translation: _originals holds canonical, live covered file is TRANSLATED.
        var originals = Path.Combine(install, "translations", "_originals");
        Directory.CreateDirectory(originals);
        File.Copy(Path.Combine(install, "data", "stringtabley.xml"),
                  Path.Combine(originals, "stringtabley.xml"));
        File.WriteAllText(Path.Combine(install, "data", "stringtabley.xml"), "ES-strings");

        // v1.0 manifest (FileHashes are canonical / pre-translation).
        new InstallManifest
        {
            ModId = "m", Version = "v1.0", InstallPath = install,
            OverlayFiles = new() { "data/protoy.xml", "data/stringtabley.xml", "art/old.ddt" },
            OverlayNetNew = new() { "art/old.ddt" },
            FileHashes = new()
            {
                ["data/protoy.xml"] = new FileFingerprint(8, ShaOf("proto-v1")),
                ["data/stringtabley.xml"] = new FileFingerprint(10, ShaOf("EN-strings")),
                ["art/old.ddt"] = new FileFingerprint(7, ShaOf("old-art")),
            },
        }.Save();

        // Build a real patch from two canonical overlay zips (v1.0 -> v1.1).
        var oldOverlay = NewTempDir();
        Write(oldOverlay, "data/protoy.xml", "proto-v1");
        Write(oldOverlay, "data/stringtabley.xml", "EN-strings");
        Write(oldOverlay, "art/old.ddt", "old-art");
        var newOverlay = NewTempDir();
        Write(newOverlay, "data/protoy.xml", "proto-v2");         // changed
        Write(newOverlay, "data/stringtabley.xml", "EN-strings"); // unchanged (covered)
        Write(newOverlay, "art/new.ddt", "new-art");             // added

        var work = NewTempDir();
        var oldZip = Path.Combine(work, "old.zip");
        var newZip = Path.Combine(work, "new.zip");
        ZipFile.CreateFromDirectory(oldOverlay, oldZip);
        ZipFile.CreateFromDirectory(newOverlay, newZip);
        var gen = await DeltaPatchService.GeneratePatchAsync(oldZip, newZip, "v1.0", "v1.1", work);

        var descriptor = System.Text.Json.JsonSerializer.Deserialize<DeltaPatchService.DeltaPatchDescriptor>(
            await File.ReadAllTextAsync(gen.PatchJsonPath))!;
        var prepared = new DeltaPatchService.PreparedPatch(descriptor, gen.PatchZipPath);

        var profile = new ModProfile
        {
            Id = "m",
            UpdateMechanism = ModUpdateMechanism.GitHubReleases,
            Translations = new TranslationsSettings { CoveredFiles = { "data\\stringtabley.xml" } },
        };

        var ok = await new NativeInstallService().ApplyGitHubDeltaAsync(
            profile, "v1.1", prepared, install, null, null, default);
        Assert.True(ok);

        // Files match a full v1.1 install.
        Assert.Equal("proto-v2", File.ReadAllText(Path.Combine(install, "data", "protoy.xml")));
        Assert.Equal("new-art", File.ReadAllText(Path.Combine(install, "art", "new.ddt")));
        Assert.False(File.Exists(Path.Combine(install, "art", "old.ddt")));   // net-new, auto-deleted

        // Manifest re-stamped correctly.
        var m2 = InstallManifest.TryLoad(install)!;
        Assert.Equal("v1.1", m2.Version);
        Assert.Contains("data/protoy.xml", m2.OverlayFiles);
        Assert.Contains("art/new.ddt", m2.OverlayFiles);
        Assert.DoesNotContain("art/old.ddt", m2.OverlayFiles);
        Assert.Equal(ShaOf("proto-v2"), m2.FileHashes["data/protoy.xml"].Sha256);
        Assert.Equal(ShaOf("new-art"), m2.FileHashes["art/new.ddt"].Sha256);
        Assert.Equal(ShaOf("EN-strings"), m2.FileHashes["data/stringtabley.xml"].Sha256);  // canonical

        // THE regression guard: the snapshot for the unchanged covered file must be canonical
        // English, NOT the translated bytes (else version detection + MP fingerprint break).
        Assert.Equal("EN-strings", File.ReadAllText(Path.Combine(originals, "stringtabley.xml")));
        // The delta restored canonical on disk too (the update tail re-applies the translation).
        Assert.Equal("EN-strings", File.ReadAllText(Path.Combine(install, "data", "stringtabley.xml")));
    }
}
