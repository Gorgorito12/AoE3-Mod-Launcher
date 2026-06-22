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
}
