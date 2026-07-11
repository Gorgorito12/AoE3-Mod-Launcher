using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the content-based AoE3 base detection added to
/// <see cref="AoE3Detector"/>: the pure <see cref="AoE3Detector.IsCleanAoE3Folder"/>
/// predicate (has <c>age3y.exe</c> + <c>data\</c>, and is NOT a mod install) and its
/// use as a <see cref="ModInstallScanner.FindDeep"/> predicate to find a clean AoE3 in
/// a NON-STANDARD folder (e.g. <c>Microsoft Studios\Age of Empires III</c>) without
/// ever returning a mod folder (which would seed a contaminated clone).
/// </summary>
public class AoE3DetectorTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-aoe3-test-").FullName;
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

    /// <summary>Lay down a clean AoE3 base: data\ + age3y.exe (flat or Steam bin\ layout).</summary>
    private static void MakeCleanAoE3(string dir, bool steamLayout)
    {
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        File.WriteAllText(Path.Combine(dir, "data", "protoy.xml"), "x");
        if (steamLayout)
        {
            Directory.CreateDirectory(Path.Combine(dir, "bin"));
            File.WriteAllText(Path.Combine(dir, "bin", "age3y.exe"), "x");
        }
        else
        {
            File.WriteAllText(Path.Combine(dir, "age3y.exe"), "x");
        }
    }

    [Fact]
    public void IsCleanAoE3Folder_AcceptsFlatLayout()
    {
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: false);
        Assert.True(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void IsCleanAoE3Folder_AcceptsSteamBinLayout()
    {
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: true);
        Assert.True(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void IsCleanAoE3Folder_RejectsWhenDataMissing()
    {
        // age3y.exe present but no data\ — not a real AoE3 base.
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "age3y.exe"), "x");
        Assert.False(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void IsCleanAoE3Folder_RejectsWhenExeMissing()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        Assert.False(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void IsCleanAoE3Folder_RejectsLauncherModInstall()
    {
        // A launcher-made mod install carries install-manifest.json — never a clean base.
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: false);
        File.WriteAllText(Path.Combine(dir, "install-manifest.json"), "{}");
        Assert.False(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void IsCleanAoE3Folder_RejectsWolFolderByMarker()
    {
        // A WoL install has age3y.exe + data\ too, but carries the art\zulushield
        // marker (a known ModRegistry marker) — must be rejected so it's never
        // cloned as the "base game" (WoL-on-WoL contamination).
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: false);
        Directory.CreateDirectory(Path.Combine(dir, "art", "zulushield"));
        Assert.False(AoE3Detector.IsCleanAoE3Folder(dir));
    }

    [Fact]
    public void FindDeep_WithPredicate_FindsAoE3InNonStandardFolder()
    {
        // root\Program Files (x86)\Microsoft Studios\Age of Empires III\bin\age3y.exe
        // — the "Studios" folder the name-based probes miss.
        var root = NewTempDir();
        var aoe3 = Path.Combine(root, "Program Files (x86)", "Microsoft Studios", "Age of Empires III");
        MakeCleanAoE3(aoe3, steamLayout: true);

        var hits = ModInstallScanner
            .FindDeep(root, AoE3Detector.IsCleanAoE3Folder, maxDepth: 4)
            .ToList();

        Assert.Contains(hits, h => string.Equals(
            Path.GetFullPath(h).TrimEnd('\\'), Path.GetFullPath(aoe3).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindDeep_WithPredicate_NeverReturnsWolFolder()
    {
        // A WoL install (age3y.exe + data\ + art\zulushield) under the scan root
        // must NOT be surfaced as a clean AoE3 base.
        var root = NewTempDir();
        var wol = Path.Combine(root, "Microsoft Studios", "Wars of Liberty");
        MakeCleanAoE3(wol, steamLayout: false);
        Directory.CreateDirectory(Path.Combine(wol, "art", "zulushield"));

        var hits = ModInstallScanner
            .FindDeep(root, AoE3Detector.IsCleanAoE3Folder, maxDepth: 4)
            .ToList();

        Assert.Empty(hits);
    }
}
