using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Regression tests for <see cref="ModHashService"/>'s LOCALIZATION-INVARIANT
/// fingerprint. The canonical bug: applying a community translation overwrites
/// <c>data\stringtabley.xml</c> — one of the three probed files — so a translated
/// and an English install on the same build produced different
/// <see cref="ModFingerprint.CombinedHash"/>es and the lobby gate wrongly rejected
/// the join. String tables don't affect the simulation, so the fix hashes the
/// English snapshot (<c>translations\_originals\</c>) for covered files instead of
/// the live file. These tests pin that the fix matches translated vs English
/// installs WITHOUT masking a real simulation difference (protoy.xml).
/// </summary>
public class ModHashServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-hash-test-").FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static ModProfile WolProfile() => new()
    {
        Id = "wol",
        Translations = new TranslationsSettings { CoveredFiles = { @"data\stringtabley.xml" } },
    };

    private static void Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Lays down an install: the three live probe files, and OPTIONALLY the English
    /// snapshot for stringtabley under <c>translations\_originals\</c>.
    /// </summary>
    private string MakeInstall(string protoy, string techtree, string stringtableLive, string? stringtableSnapshot)
    {
        var root = NewTempDir();
        Write(root, @"data\protoy.xml", protoy);
        Write(root, @"data\techtreey.xml", techtree);
        Write(root, @"data\stringtabley.xml", stringtableLive);
        if (stringtableSnapshot != null)
            Write(root, @"translations\_originals\stringtabley.xml", stringtableSnapshot);
        return root;
    }

    [Fact]
    public async Task TranslatedAndEnglish_SameSnapshot_ProduceSameCombinedHash()
    {
        var profile = WolProfile();
        // English peer: the live stringtabley IS the English baseline (snapshot == live).
        var english = MakeInstall("PROTO", "TECH", "ENGLISH_STRINGS", stringtableSnapshot: "ENGLISH_STRINGS");
        // Translated peer: the live stringtabley is Spanish, but the _originals snapshot
        // holds the SAME English baseline — so the fingerprint hashes the snapshot.
        var spanish = MakeInstall("PROTO", "TECH", "SPANISH_STRINGS", stringtableSnapshot: "ENGLISH_STRINGS");

        var fpEn = await ModHashService.FingerprintAsync(profile, english);
        var fpEs = await ModHashService.FingerprintAsync(profile, spanish);

        Assert.Equal(fpEn.CombinedHash, fpEs.CombinedHash);
    }

    [Fact]
    public async Task DifferentProto_ProduceDifferentCombinedHash()
    {
        var profile = WolProfile();
        // protoy.xml differs (a REAL out-of-sync trigger); everything else identical.
        // protoy has no snapshot, so it's always hashed live → must still mismatch.
        var a = MakeInstall("PROTO_A", "TECH", "ENGLISH_STRINGS", stringtableSnapshot: "ENGLISH_STRINGS");
        var b = MakeInstall("PROTO_B", "TECH", "ENGLISH_STRINGS", stringtableSnapshot: "ENGLISH_STRINGS");

        var fpA = await ModHashService.FingerprintAsync(profile, a);
        var fpB = await ModHashService.FingerprintAsync(profile, b);

        Assert.NotEqual(fpA.CombinedHash, fpB.CombinedHash);
    }

    [Fact]
    public async Task NoSnapshot_DifferentLiveStringtable_ProduceDifferentCombinedHash()
    {
        var profile = WolProfile();
        // With no _originals snapshot, ResolveHashableFile falls back to the live file,
        // so different live stringtables must still differ (no masking without a snapshot).
        var a = MakeInstall("PROTO", "TECH", "ENGLISH_STRINGS", stringtableSnapshot: null);
        var b = MakeInstall("PROTO", "TECH", "SPANISH_STRINGS", stringtableSnapshot: null);

        var fpA = await ModHashService.FingerprintAsync(profile, a);
        var fpB = await ModHashService.FingerprintAsync(profile, b);

        Assert.NotEqual(fpA.CombinedHash, fpB.CombinedHash);
    }

    // -- Per-profile probe files ----------------------------------------------
    //
    // A mod that ships its OWN data files instead of the base `y` ones (Napoleonic
    // Era: proton.xml / techtreen.xml) must fingerprint over THOSE. Hashing the
    // default `y` files there is inert — the AoE3 clone makes them identical for
    // every player — so the room's version gate would let two versions share a
    // match and desync.

    private static ModProfile ProfileWithProbes(params string[] probes) => new()
    {
        Id = "napoleonic-era",
        MultiplayerProbeFiles = new List<string>(probes),
    };

    /// <summary>Empty list = the three defaults. Zero regression for WoL / IM / stock.</summary>
    [Fact]
    public void ProbeFilesFor_EmptyProfile_UsesTheDefaults()
    {
        var files = ModHashService.ProbeFilesFor(new ModProfile { Id = "wol" });
        Assert.Equal(ModHashService.DefaultProbeFiles, files);
    }

    [Fact]
    public void ProbeFilesFor_DeclaredProfile_UsesItsOwn()
    {
        var files = ModHashService.ProbeFilesFor(
            ProfileWithProbes(@"data\proton.xml", @"data\techtreen.xml"));
        Assert.Equal(new[] { @"data\proton.xml", @"data\techtreen.xml" }, files);
    }

    /// <summary>
    /// The bug this fixes: two NE installs differing only in proton.xml must get
    /// DIFFERENT fingerprints. If the launcher hashed the default `y` files, both
    /// would be identical and the gate would pass a version mismatch.
    /// </summary>
    [Fact]
    public async Task TwoInstalls_DifferentOwnData_ProduceDifferentHashes()
    {
        var profile = ProfileWithProbes(@"data\proton.xml", @"data\techtreen.xml");

        var v1 = NewTempDir();
        Write(v1, @"data\proton.xml", "NE 2.1.7b PROTO");
        Write(v1, @"data\techtreen.xml", "TECH");
        var v2 = NewTempDir();
        Write(v2, @"data\proton.xml", "NE 2.1.8 PROTO");   // only this differs
        Write(v2, @"data\techtreen.xml", "TECH");

        var fp1 = await ModHashService.FingerprintAsync(profile, v1);
        var fp2 = await ModHashService.FingerprintAsync(profile, v2);

        Assert.NotEqual(fp1.CombinedHash, fp2.CombinedHash);
    }

    /// <summary>Same own-data bytes → same fingerprint (two peers on one version join).</summary>
    [Fact]
    public async Task TwoInstalls_SameOwnData_ProduceSameHash()
    {
        var profile = ProfileWithProbes(@"data\proton.xml", @"data\techtreen.xml");

        var a = NewTempDir();
        Write(a, @"data\proton.xml", "SAME PROTO");
        Write(a, @"data\techtreen.xml", "SAME TECH");
        var b = NewTempDir();
        Write(b, @"data\proton.xml", "SAME PROTO");
        Write(b, @"data\techtreen.xml", "SAME TECH");

        var fpA = await ModHashService.FingerprintAsync(profile, a);
        var fpB = await ModHashService.FingerprintAsync(profile, b);

        Assert.Equal(fpA.CombinedHash, fpB.CombinedHash);
    }

    /// <summary>
    /// The whole reason to make this per-profile: NE's fingerprint must NOT be
    /// decided by the default `y` files, which the clone leaves identical across
    /// versions. Same `y`, different `n` → still different.
    /// </summary>
    [Fact]
    public async Task IdenticalDefaultFiles_DoNotMaskAnOwnDataDifference()
    {
        var profile = ProfileWithProbes(@"data\proton.xml");

        var v1 = NewTempDir();
        Write(v1, @"data\protoy.xml", "IDENTICAL BASE");   // the default probe
        Write(v1, @"data\proton.xml", "NE VERSION A");
        var v2 = NewTempDir();
        Write(v2, @"data\protoy.xml", "IDENTICAL BASE");
        Write(v2, @"data\proton.xml", "NE VERSION B");

        var fp1 = await ModHashService.FingerprintAsync(profile, v1);
        var fp2 = await ModHashService.FingerprintAsync(profile, v2);

        Assert.NotEqual(fp1.CombinedHash, fp2.CombinedHash);
    }
}
