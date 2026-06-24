using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="VerifyService.VerifyAgainstManifest"/>: the per-file
/// integrity check behind Verify / granular Repair. Covers the hash pin,
/// damaged/missing classification, size-first behaviour, the translation
/// (localization-invariant) rule, the empty-hashes back-compat gate, and the
/// manifest round-trip of the new <c>FileHashes</c> field.
/// </summary>
public class VerifyServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("verify-test-").FullName;
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

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Writes a file under the install dir and returns its fingerprint.</summary>
    private static FileFingerprint Write(string installDir, string rel, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var full = Path.Combine(installDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return new FileFingerprint(bytes.Length, Sha256Hex(bytes));
    }

    // ---------------- Hash pin ----------------

    [Fact]
    public void Sha256_KnownVector_MatchesIntact()
    {
        // SHA-256("hello") — pins that VerifyService computes the standard digest.
        const string helloSha = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");

        var manifest = new InstallManifest
        {
            FileHashes = new() { ["a.txt"] = new FileFingerprint(5, helloSha) },
        };

        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);

        Assert.Empty(res.MissingItems);
        Assert.Empty(res.CorruptItems);
        Assert.Equal(1, res.TotalFilesChecked);
    }

    // ---------------- Classification ----------------

    [Fact]
    public void Classifies_Missing_Corrupt_And_Healthy()
    {
        var dir = NewTempDir();
        var fpA = Write(dir, "data/a.xml", "alpha");
        var fpB = Write(dir, "data/b.xml", "bravo");
        var fpC = Write(dir, "art/c.ddt", "charlie");

        var manifest = new InstallManifest
        {
            FileHashes = new()
            {
                ["data/a.xml"] = fpA,    // stays healthy
                ["data/b.xml"] = fpB,    // will be corrupted (different bytes, same length)
                ["art/c.ddt"]  = fpC,    // will be deleted
            },
        };

        // Corrupt b.xml (same length, different content) and delete c.ddt.
        File.WriteAllText(Path.Combine(dir, "data", "b.xml"), "BRAVO");
        File.Delete(Path.Combine(dir, "art", "c.ddt"));

        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);

        Assert.Equal(new[] { "art/c.ddt" }, res.MissingItems);
        Assert.Equal(new[] { "data/b.xml" }, res.CorruptItems);
        Assert.DoesNotContain("data/a.xml", res.CorruptItems);
        Assert.DoesNotContain("data/a.xml", res.MissingItems);
    }

    // ---------------- size-first ----------------

    [Fact]
    public void SizeMismatch_IsCorrupt_EvenIfHashWouldMatch()
    {
        var dir = NewTempDir();
        Write(dir, "f.bar", "0123456789");

        // Manifest claims a different size — size check fails before hashing.
        var manifest = new InstallManifest
        {
            FileHashes = new() { ["f.bar"] = new FileFingerprint(999, Sha256Hex(System.Text.Encoding.UTF8.GetBytes("0123456789"))) },
        };

        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);
        Assert.Equal(new[] { "f.bar" }, res.CorruptItems);
    }

    [Fact]
    public void SameSizeSameHash_IsClean()
    {
        var dir = NewTempDir();
        var fp = Write(dir, "f.bar", "0123456789");
        var manifest = new InstallManifest { FileHashes = new() { ["f.bar"] = fp } };

        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);
        Assert.Empty(res.CorruptItems);
        Assert.Empty(res.MissingItems);
        Assert.Equal(1, res.TotalFilesChecked);
    }

    // ---------------- Back-compat gate ----------------

    [Fact]
    public void HasFileHashes_Gate()
    {
        Assert.False(VerifyService.HasFileHashes(null));
        Assert.False(VerifyService.HasFileHashes(new InstallManifest()));
        Assert.True(VerifyService.HasFileHashes(new InstallManifest
        {
            FileHashes = new() { ["x"] = new FileFingerprint(1, "ab") },
        }));
    }

    // ---------------- Translation (localization-invariant) ----------------

    [Fact]
    public void CoveredFile_TranslatedLive_VerifiedAgainstSnapshot_NotCorrupt()
    {
        var dir = NewTempDir();
        const string rel = "data/stringtabley.xml";

        // Canonical English bytes → this is what the manifest fingerprints.
        var canonical = "ENGLISH-STRINGS";
        var fp = Write(dir, rel, canonical);

        // The live file is later overwritten by a Spanish translation...
        File.WriteAllText(Path.Combine(dir, "data", "stringtabley.xml"), "CADENAS-EN-ESPAÑOL-MAS-LARGAS");
        // ...but the canonical snapshot holds the original English bytes.
        var originals = Path.Combine(dir, TranslationService.TranslationsFolderName,
            TranslationService.OriginalsFolderName);
        Directory.CreateDirectory(originals);
        File.WriteAllText(Path.Combine(originals, "stringtabley.xml"), canonical);

        var manifest = new InstallManifest { FileHashes = new() { [rel] = fp } };

        var res = VerifyService.VerifyAgainstManifest(
            dir, manifest, coveredFiles: new[] { @"data\stringtabley.xml" });

        Assert.Empty(res.CorruptItems);   // translated live file must NOT be flagged
        Assert.Empty(res.MissingItems);
        Assert.Equal(1, res.TotalFilesChecked);
    }

    [Fact]
    public void CoveredFile_NoSnapshot_IsSkipped_NotCorrupt()
    {
        var dir = NewTempDir();
        const string rel = "data/stringtabley.xml";
        var fp = Write(dir, rel, "ENGLISH");

        // Live file translated, NO _originals snapshot present.
        File.WriteAllText(Path.Combine(dir, "data", "stringtabley.xml"), "ESPAÑOL");

        var manifest = new InstallManifest { FileHashes = new() { [rel] = fp } };

        var res = VerifyService.VerifyAgainstManifest(
            dir, manifest, coveredFiles: new[] { @"data\stringtabley.xml" });

        Assert.Empty(res.CorruptItems);     // can't compare → skipped, not flagged
        Assert.Empty(res.MissingItems);     // existence still confirmed
        Assert.Equal(0, res.TotalFilesChecked);   // skipped → not counted as verified
    }

    // ---------------- delete.lst consistency (capture → strip → manifest) ----------------

    [Fact]
    public void PruneMissingHashes_DropsStrippedDeleteList_NoFalseMissing()
    {
        var dir = NewTempDir();
        // A real overlay file on disk...
        var fpA = Write(dir, "data/a.xml", "alpha");
        // ...plus a fingerprint the copy captured for delete.lst, which the
        // strip pass deleted from disk (never written here).
        var captured = new Dictionary<string, FileFingerprint>
        {
            ["data/a.xml"] = fpA,
            ["delete.lst"] = new FileFingerprint(3, "abc"),
        };

        var pruned = NativeInstallService.PruneMissingHashes(dir, captured);

        // delete.lst is gone (not on disk); the real file survives.
        Assert.True(pruned.ContainsKey("data/a.xml"));
        Assert.False(pruned.ContainsKey("delete.lst"));

        // And Verify against the pruned manifest must NOT flag delete.lst missing.
        var manifest = new InstallManifest { FileHashes = pruned };
        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);
        Assert.Empty(res.MissingItems);
        Assert.Empty(res.CorruptItems);
    }

    // ---------------- Manifest round-trip ----------------

    [Fact]
    public void Manifest_RoundTrips_FileHashes()
    {
        var dir = NewTempDir();
        var manifest = new InstallManifest
        {
            InstallPath = dir,
            FileHashes = new()
            {
                ["data/a.xml"] = new FileFingerprint(123, "deadbeef"),
                ["art/b.ddt"] = new FileFingerprint(456, "cafe"),
            },
        };
        manifest.Save();

        var loaded = InstallManifest.TryLoad(dir);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FileHashes.Count);
        Assert.Equal(123, loaded.FileHashes["data/a.xml"].Size);
        Assert.Equal("deadbeef", loaded.FileHashes["data/a.xml"].Sha256);
        Assert.Equal(456, loaded.FileHashes["art/b.ddt"].Size);
        Assert.Equal("cafe", loaded.FileHashes["art/b.ddt"].Sha256);

        // EngineFileHashes round-trips too.
        manifest.EngineFileHashes["RockallDLL.dll"] = new FileFingerprint(7, "abcd");
        manifest.Save();
        var loaded2 = InstallManifest.TryLoad(dir);
        Assert.Equal(7, loaded2!.EngineFileHashes["RockallDLL.dll"].Size);
        Assert.Equal("abcd", loaded2.EngineFileHashes["RockallDLL.dll"].Sha256);
    }

    // ---------------- Parallelism determinism ----------------

    [Fact]
    public void Parallel_ManyFiles_OrderedDeterministicOutput()
    {
        var dir = NewTempDir();
        var hashes = new Dictionary<string, FileFingerprint>();
        for (int i = 0; i < 200; i++)
            hashes[$"data/f{i:D3}.xml"] = Write(dir, $"data/f{i:D3}.xml", $"content-{i}");

        // Corrupt three (different bytes, same length) and delete two.
        foreach (var i in new[] { 10, 50, 150 })
            File.WriteAllText(Path.Combine(dir, "data", $"f{i:D3}.xml"),
                new string('Z', System.Text.Encoding.UTF8.GetByteCount($"content-{i}")));
        foreach (var i in new[] { 3, 199 })
            File.Delete(Path.Combine(dir, "data", $"f{i:D3}.xml"));

        var manifest = new InstallManifest { FileHashes = hashes };
        var res = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);

        Assert.Equal(new[] { "data/f003.xml", "data/f199.xml" }, res.MissingItems);
        Assert.Equal(new[] { "data/f010.xml", "data/f050.xml", "data/f150.xml" }, res.CorruptItems);
        // Output is sorted Ordinal regardless of parallel completion order.
        Assert.Equal(res.MissingItems.OrderBy(x => x, StringComparer.Ordinal), res.MissingItems);
        Assert.Equal(res.CorruptItems.OrderBy(x => x, StringComparer.Ordinal), res.CorruptItems);
    }

    // ---------------- Engine verification (separate from overlay) ----------------

    [Fact]
    public void Engine_DamagedFlagged_AndNotInOverlayVerify()
    {
        var dir = NewTempDir();
        var rockall = Write(dir, "RockallDLL.dll", "engine-bytes");
        Write(dir, "data/x.xml", "overlay-ok");

        var manifest = new InstallManifest
        {
            FileHashes = new() { ["data/x.xml"] = Write(dir, "data/x.xml", "overlay-ok") },
            EngineFileHashes = new() { ["RockallDLL.dll"] = rockall },
        };

        // Corrupt the engine DLL.
        File.WriteAllText(Path.Combine(dir, "RockallDLL.dll"), "CORRUPT-ENGINE");

        var engine = VerifyService.VerifyEngineFiles(dir, manifest, coveredFiles: null);
        Assert.Equal(new[] { "RockallDLL.dll" }, engine);

        // The overlay pass must NOT see the engine file (it's a separate map),
        // so it can never be routed into the granular payload re-copy.
        var overlay = VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null);
        Assert.Empty(overlay.CorruptItems);
        Assert.DoesNotContain("RockallDLL.dll", overlay.CorruptItems);
    }

    [Fact]
    public void HasEngineHashes_Gate()
    {
        Assert.False(VerifyService.HasEngineHashes(null));
        Assert.False(VerifyService.HasEngineHashes(new InstallManifest()));
        Assert.True(VerifyService.HasEngineHashes(new InstallManifest
        {
            EngineFileHashes = new() { ["RockallDLL.dll"] = new FileFingerprint(1, "ab") },
        }));
    }

    // ---------------- Progress bytes ----------------

    [Fact]
    public void Progress_BytesTotal_EqualsSumOfSizes()
    {
        var dir = NewTempDir();
        var hashes = new Dictionary<string, FileFingerprint>
        {
            ["a.txt"] = Write(dir, "a.txt", "12345"),       // 5
            ["b.txt"] = Write(dir, "b.txt", "1234567890"),  // 10
        };
        long expected = hashes.Values.Sum(f => f.Size);

        long seenTotal = 0;
        var probe = new LastProgress(p => seenTotal = p.BytesTotal);
        var manifest = new InstallManifest { FileHashes = hashes };
        VerifyService.VerifyAgainstManifest(dir, manifest, coveredFiles: null, probe);

        Assert.Equal(expected, seenTotal);
    }

    private sealed class LastProgress : IProgress<VerifyService.VerifyProgress>
    {
        private readonly Action<VerifyService.VerifyProgress> _on;
        public LastProgress(Action<VerifyService.VerifyProgress> on) => _on = on;
        public void Report(VerifyService.VerifyProgress value) => _on(value);
    }

    // ---------------- Unexpected files (diagnostic) ----------------

    [Fact]
    public void FindUnexpectedFiles_DetectsExtra_IgnoresArtifacts()
    {
        var dir = NewTempDir();
        Write(dir, "data/x.xml", "tracked");
        Write(dir, "data/PLANTED.tmp", "leftover");          // unexpected
        Write(dir, "install-manifest.json", "{}");            // launcher artifact
        Write(dir, "translations/_originals/stringtabley.xml", "EN"); // artifact (translations/)

        var manifest = new InstallManifest
        {
            Files = new() { "data/x.xml" },
            FileHashes = new() { ["data/x.xml"] = new FileFingerprint(1, "ab") },
        };

        var extras = VerifyService.FindUnexpectedFiles(dir, manifest);

        Assert.Contains("data/PLANTED.tmp", extras);
        Assert.DoesNotContain("install-manifest.json", extras);
        Assert.DoesNotContain("translations/_originals/stringtabley.xml", extras);
        Assert.DoesNotContain("data/x.xml", extras);
    }

    // ---------------- Recapture (post-patch refresh) ----------------

    [Fact]
    public void RecaptureHashes_RefreshesTouched_CoveredViaSnapshot()
    {
        var dir = NewTempDir();
        // Overlay file the patch touched.
        Write(dir, "data/x.xml", "patched-content");
        // Covered file: live is translated, snapshot holds canonical English.
        Write(dir, "data/stringtabley.xml", "ESPAÑOL");
        var enFp = Write(dir, "translations/_originals/stringtabley.xml", "ENGLISH");
        // An engine file (always recaptured).
        Write(dir, "RockallDLL.dll", "engine-v2");

        var (overlay, engine) = NativeInstallService.RecaptureHashes(
            installPath: dir,
            touchedRelPaths: new[] { "data/x.xml", "data/stringtabley.xml" },
            overlayRelPaths: new[] { "data/x.xml", "data/stringtabley.xml" },
            coveredFiles: new[] { @"data\stringtabley.xml" });

        // Touched overlay file captured from disk.
        Assert.True(overlay.ContainsKey("data/x.xml"));
        // Covered overlay file captured from the ENGLISH snapshot, not the live ES file.
        Assert.Equal(enFp.Sha256, overlay["data/stringtabley.xml"].Sha256);
        // Engine map recomputed (curated DLLs, hash-if-present).
        Assert.True(engine.ContainsKey("RockallDLL.dll"));
        // stringtabley is overlay-OWNED here → it must be in overlay ONLY, never
        // duplicated into the engine map (the data-file-overlap fix).
        Assert.False(engine.ContainsKey("data/stringtabley.xml"));
    }

    [Fact]
    public void RecaptureHashes_EngineDataFile_WhenNotOverlay_GoesToEngine()
    {
        var dir = NewTempDir();
        Write(dir, "data/protoy.xml", "proto-bytes");  // present, NOT declared overlay

        var (overlay, engine) = NativeInstallService.RecaptureHashes(
            installPath: dir,
            touchedRelPaths: new[] { "data/protoy.xml" },
            overlayRelPaths: Array.Empty<string>(),     // overlay-on-vanilla: data is base
            coveredFiles: null);

        Assert.False(overlay.ContainsKey("data/protoy.xml"));
        Assert.True(engine.ContainsKey("data/protoy.xml"));   // base → engine bucket
    }
}
