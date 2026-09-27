using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="EngineBaseline"/>: a re-lay, a patch or an addon must never fingerprint a
/// damaged engine file AS the baseline. Every writer used to recompute the map from disk, so the
/// repair meant to fix a reported DLL made it verify as healthy from then on.
/// </summary>
public class EngineBaselineTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var d in _dirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    private static FileFingerprint Fp(string sha, long size = 1) => new(size, sha);

    private static Dictionary<string, FileFingerprint> Map(params (string Key, string Sha)[] entries)
    {
        var d = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, s) in entries) d[k] = Fp(s);
        return d;
    }

    [Fact]
    public void AnUntouchedDamagedFileKeepsTheFingerprintItWasLaidWith()
    {
        var merged = EngineBaseline.Merge(
            Map(("RockallDLL.dll", "good")), Map(("RockallDLL.dll", "damaged")),
            touched: Array.Empty<string>(), overlayKeysNow: Array.Empty<string>(), existsNow: _ => true);
        Assert.Equal("good", merged["RockallDLL.dll"].Sha256);
    }

    [Fact]
    public void AFileTheOperationWroteGetsItsNewFingerprint()
    {
        var merged = EngineBaseline.Merge(
            Map(("granny2.dll", "old")), Map(("granny2.dll", "new")),
            touched: new[] { "granny2.dll" }, overlayKeysNow: Array.Empty<string>(), existsNow: _ => true);
        Assert.Equal("new", merged["granny2.dll"].Sha256);
    }

    [Fact]
    public void AFileTheOperationRemovedIsDropped_OneAlreadyMissingIsKept()
    {
        var previous = Map(("binkw32.dll", "a"), ("granny2.dll", "b"));
        var merged = EngineBaseline.Merge(previous, Map(),
            touched: Array.Empty<string>(), overlayKeysNow: Array.Empty<string>(),
            existsNow: _ => false,
            existedBefore: k => k == "binkw32.dll");
        Assert.False(merged.ContainsKey("binkw32.dll"));   // this op deleted it
        Assert.True(merged.ContainsKey("granny2.dll"));    // was already gone: still reported
    }

    [Fact]
    public void AKeyThatMovedIntoTheOverlayLeavesTheEngineMap()
    {
        var merged = EngineBaseline.Merge(
            Map(("data/protoy.xml", "a")), Map(("data/protoy.xml", "a")),
            touched: Array.Empty<string>(), overlayKeysNow: new[] { "data/protoy.xml" }, existsNow: _ => true);
        Assert.Empty(merged);
    }

    [Fact]
    public void WithNoPreviousMapItIsAPlainRecompute()
    {
        var merged = EngineBaseline.Merge(null, Map(("RockallDLL.dll", "x"), ("data/protoy.xml", "y")),
            touched: Array.Empty<string>(), overlayKeysNow: new[] { "data/protoy.xml" }, existsNow: _ => true);
        Assert.Single(merged);
        Assert.Equal("x", merged["RockallDLL.dll"].Sha256);
    }

    [Fact]
    public void AKeyWithNoPreviousFingerprintGetsItsFirstBaseline()
    {
        var merged = EngineBaseline.Merge(
            Map(("RockallDLL.dll", "a")), Map(("RockallDLL.dll", "b"), ("granny2.dll", "c")),
            touched: Array.Empty<string>(), overlayKeysNow: Array.Empty<string>(), existsNow: _ => true);
        Assert.Equal("a", merged["RockallDLL.dll"].Sha256);
        Assert.Equal("c", merged["granny2.dll"].Sha256);
    }

    [Fact]
    public void OnlyAnIsolatedCloneIsMerged()
    {
        Assert.True(EngineBaseline.Applies(new ModProfile { InstallType = ModInstallType.IsolatedFolder }));
        Assert.False(EngineBaseline.Applies(new ModProfile { InstallType = ModInstallType.InPlaceOverlay }));
    }

    [Fact]
    public async Task AnAddonApplyDoesNotBlessADamagedEngineFile()
    {
        var root = Directory.CreateTempSubdirectory("engine-addon-").FullName;
        _dirs.Add(root);
        File.WriteAllText(Path.Combine(root, "RockallDLL.dll"), "engine");
        Directory.CreateDirectory(Path.Combine(root, "art"));
        File.WriteAllText(Path.Combine(root, "art", "a.ddt"), "MOD A");
        var profile = new ModProfile { Id = "mod", DisplayName = "Mod" };
        new InstallManifest
        {
            ModId = "mod",
            InstallPath = root,
            OverlayFiles = new() { "art/a.ddt" },
            FileHashes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["art/a.ddt"] = VerifyService.ComputeFingerprintOf(Path.Combine(root, "art", "a.ddt")),
            },
            EngineFileHashes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["RockallDLL.dll"] = VerifyService.ComputeFingerprintOf(Path.Combine(root, "RockallDLL.dll")),
            },
        }.Save();

        File.WriteAllText(Path.Combine(root, "RockallDLL.dll"), "ENGINE");   // damaged

        var zip = Path.Combine(Directory.CreateTempSubdirectory("engine-addon-zip-").FullName, "addon.zip");
        _dirs.Add(Path.GetDirectoryName(zip)!);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(archive.CreateEntry("art/a.ddt").Open()))
            w.Write("ADDON A");
        await AddonService.ApplyAsync(root, "ui", zip, profile, false);

        var manifest = InstallManifest.TryLoad(root)!;
        Assert.Single(VerifyService.VerifyEngineFiles(root, manifest, null));
    }
}
