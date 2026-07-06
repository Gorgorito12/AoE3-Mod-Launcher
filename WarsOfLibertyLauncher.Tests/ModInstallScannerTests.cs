using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="ModInstallScanner.FindDeep"/> — the bounded, depth-limited
/// content scan that makes finding an existing WoL install robust (nested folders,
/// arbitrary locations) without ever relaxing the anti-vanilla marker rule or
/// turning into a full-disk crawl. The per-folder decision is delegated to
/// <see cref="ModInstallProbe.LooksLikeModInstall"/>, so a folder with the probe
/// but no marker (vanilla AoE3) is never matched.
/// </summary>
public class ModInstallScannerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewTempDir()
    {
        var dir = Directory.CreateTempSubdirectory("wol-scan-test-").FullName;
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

    private static ModProfile WolLikeProfile() => new()
    {
        Id = "wol",
        DisplayName = "Wars of Liberty",
        InstallType = ModInstallType.IsolatedFolder,
        InstallProbeFile = @"data\stringtabley.xml",
        InstallMarker = @"art\zulushield",
    };

    /// <summary>Lay down a real-looking WoL install (probe + marker) at a path.</summary>
    private static void MakeWolInstall(string installDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(installDir, @"data\stringtabley.xml"))!);
        File.WriteAllText(Path.Combine(installDir, @"data\stringtabley.xml"), "x");
        Directory.CreateDirectory(Path.Combine(installDir, @"art\zulushield"));
    }

    [Fact]
    public void FindDeep_FindsInstallNestedTwoLevelsDeep()
    {
        // root\Age Of Empires 3\Mods\WoL  → WoL is 3 levels under root.
        var root = NewTempDir();
        var wol = Path.Combine(root, "Age Of Empires 3", "Mods", "WoL");
        MakeWolInstall(wol);

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 3).ToList();
        Assert.Contains(hits, h => string.Equals(
            Path.GetFullPath(h).TrimEnd('\\'), Path.GetFullPath(wol).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindDeep_RespectsMaxDepth()
    {
        // WoL is 3 levels under root; a maxDepth of 2 must NOT find it.
        var root = NewTempDir();
        var wol = Path.Combine(root, "a", "b", "WoL");
        MakeWolInstall(wol);

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 2).ToList();
        Assert.DoesNotContain(hits, h => string.Equals(
            Path.GetFullPath(h).TrimEnd('\\'), Path.GetFullPath(wol).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindDeep_MatchesRootItself()
    {
        var root = NewTempDir();
        MakeWolInstall(root); // the chosen folder IS the install

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 0).ToList();
        Assert.Single(hits);
    }

    [Fact]
    public void FindDeep_RejectsVanillaLikeFolderWithProbeButNoMarker()
    {
        // Probe present, marker absent (mimics vanilla AoE3) — never a match.
        var root = NewTempDir();
        var vanilla = Path.Combine(root, "Age Of Empires 3");
        Directory.CreateDirectory(Path.Combine(vanilla, "data"));
        File.WriteAllText(Path.Combine(vanilla, @"data\stringtabley.xml"), "x");
        // no art\zulushield

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 3).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void FindDeep_SkipsSystemNoiseDirectories()
    {
        // A WoL install buried under an "AppData" folder must be skipped (the
        // scanner never descends into system/noise dirs).
        var root = NewTempDir();
        var wol = Path.Combine(root, "AppData", "WoL");
        MakeWolInstall(wol);

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 4).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void FindDeep_FindsMultipleInstalls()
    {
        var root = NewTempDir();
        var a = Path.Combine(root, "CopyA");
        var b = Path.Combine(root, "sub", "CopyB");
        MakeWolInstall(a);
        MakeWolInstall(b);

        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 3).ToList();
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void FindDeep_NonExistentRoot_IsEmpty()
    {
        var root = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.Empty(ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 3));
    }

    [Fact]
    public void FindDeep_SharedVisitedSet_SkipsOverlappingRoots()
    {
        // Scanning a parent then a child with a shared visited-set must not
        // double-yield an install that lives under both.
        var root = NewTempDir();
        var child = Path.Combine(root, "child");
        var wol = Path.Combine(child, "WoL");
        MakeWolInstall(wol);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = ModInstallScanner.FindDeep(root, WolLikeProfile(), maxDepth: 3, default, visited).ToList();
        hits.AddRange(ModInstallScanner.FindDeep(child, WolLikeProfile(), maxDepth: 3, default, visited));

        Assert.Single(hits); // the child scan re-walks nothing already visited
    }
}
