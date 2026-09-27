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

    // ---- InstallationFromManualRoot: reuse the durable manual AoE3 pin as a clone source ----

    [Fact]
    public void InstallationFromManualRoot_FlatLayout_GameFolderIsRoot()
    {
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: false);
        var install = AoE3Detector.InstallationFromManualRoot(dir);
        Assert.NotNull(install);
        Assert.Equal(dir.TrimEnd('\\', '/'), install!.ModRoot);
        Assert.Equal(dir.TrimEnd('\\', '/'), install.GameFolder);
        Assert.Equal("manual", install.Source);
    }

    [Fact]
    public void InstallationFromManualRoot_BinLayout_GameFolderIsBin()
    {
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: true);   // age3y.exe in bin\, data\ at root
        var install = AoE3Detector.InstallationFromManualRoot(dir);
        Assert.NotNull(install);
        Assert.Equal(dir.TrimEnd('\\', '/'), install!.ModRoot);   // clone source = root
        Assert.Equal(Path.Combine(dir, "bin"), install.GameFolder);
    }

    [Fact]
    public void InstallationFromManualRoot_ModFolder_ReturnsNull()
    {
        // A WoL folder (clean base + zulushield marker) must never be offered as a clone source.
        var dir = NewTempDir();
        MakeCleanAoE3(dir, steamLayout: false);
        Directory.CreateDirectory(Path.Combine(dir, "art", "zulushield"));
        Assert.Null(AoE3Detector.InstallationFromManualRoot(dir));
    }

    [Fact]
    public void InstallationFromManualRoot_NoData_ReturnsNull()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "age3y.exe"), "x");   // exe but no data\
        Assert.Null(AoE3Detector.InstallationFromManualRoot(dir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InstallationFromManualRoot_Blank_ReturnsNull(string? root)
    {
        Assert.Null(AoE3Detector.InstallationFromManualRoot(root));
    }

    // ---- FindByFolderName: the name pass FindAll runs on every call ----
    //
    // A player's base game read as NOT INSTALLED because it lived at
    // D:\Program Files (x86)\Age of Empires III - Complete Collection\bin\age3y.exe,
    // a name none of the fixed store probes knew. Most cases below are refusals:
    // the pass matches by NAME, and mod folders are named after the game too.

    private static readonly string[] Markers = { @"art\zulushield" };

    private static List<AoE3Detector.Installation> ByName(params string[] roots)
        => AoE3Detector.FindByFolderName(roots, Markers).ToList();

    /// <summary>Everything in bin\ — the exe AND data\ — as on the reporting machine.</summary>
    private static void MakeBinHoldsEverything(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "bin", "data"));
        File.WriteAllText(Path.Combine(dir, "bin", "data", "protoy.xml"), "x");
        File.WriteAllText(Path.Combine(dir, "bin", "age3y.exe"), "x");
    }

    [Fact]
    public void TheReportedCompleteCollectionFolderIsFound()
    {
        var drive = NewTempDir();
        var pf86 = Path.Combine(drive, "Program Files (x86)");
        var aoe3 = Path.Combine(pf86, "Age of Empires III - Complete Collection");
        MakeBinHoldsEverything(aoe3);

        // The same machine also held a WoL clone INSIDE that folder. It sits one
        // level too deep for this pass and must not disturb the base game's hit.
        var wol = Path.Combine(aoe3, "Wars of Liberty");
        MakeCleanAoE3(wol, steamLayout: false);
        Directory.CreateDirectory(Path.Combine(wol, "art", "zulushield"));

        var hit = Assert.Single(ByName(pf86, drive));
        Assert.Equal(Path.Combine(aoe3, "bin"), hit.GameFolder);
        Assert.Equal(aoe3, hit.ModRoot);
        Assert.Equal("", hit.Source);
    }

    [Fact]
    public void AFlatFolderUnderMicrosoftStudiosIsFound()
    {
        var pf86 = NewTempDir();
        var aoe3 = Path.Combine(pf86, "Microsoft Studios", "Age of Empires III");
        MakeCleanAoE3(aoe3, steamLayout: false);

        var hit = Assert.Single(ByName(pf86));
        Assert.Equal(aoe3, hit.GameFolder);
    }

    [Fact]
    public void ASteamStyleFolderWithDataAtTheRootIsFound()
    {
        var pf = NewTempDir();
        var aoe3 = Path.Combine(pf, "Age of Empires 3");
        MakeCleanAoE3(aoe3, steamLayout: true);

        var hit = Assert.Single(ByName(pf));
        Assert.Equal(Path.Combine(aoe3, "bin"), hit.GameFolder);
        Assert.Equal(aoe3, hit.ModRoot);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A mod folder named after the game ships its own
    /// age3y.exe and data\, so the name and the files both say "AoE3". Returning it
    /// would hand the base-game profile a MOD to launch.
    /// </summary>
    [Fact]
    public void AModFolderNamedAfterTheGameIsNeverTheBaseGame()
    {
        var pf = NewTempDir();
        var wol = Path.Combine(pf, "Age of Empires III - Wars of Liberty");
        MakeCleanAoE3(wol, steamLayout: false);
        Directory.CreateDirectory(Path.Combine(wol, "art", "zulushield"));

        Assert.Empty(ByName(pf));
    }

    /// <summary>
    /// The data\ check and the mod check must not be answerable at different levels:
    /// a clean bin\ must not launder a marker that sits on the folder above it.
    /// </summary>
    [Fact]
    public void AMarkerOneLevelAboveACleanBinStillRefuses()
    {
        var pf = NewTempDir();
        var mod = Path.Combine(pf, "Age of Empires III - Some Mod");
        MakeBinHoldsEverything(mod);
        Directory.CreateDirectory(Path.Combine(mod, "art", "zulushield"));

        Assert.Empty(ByName(pf));
    }

    [Fact]
    public void ALauncherInstallNamedAfterTheGameIsRefused()
    {
        var pf = NewTempDir();
        var install = Path.Combine(pf, "Age of Empires III Mod");
        MakeCleanAoE3(install, steamLayout: false);
        File.WriteAllText(Path.Combine(install, "install-manifest.json"), "{}");

        Assert.Empty(ByName(pf));
    }

    [Fact]
    public void AFolderWithTheNameButNotTheGameIsRefused()
    {
        var pf = NewTempDir();
        Directory.CreateDirectory(Path.Combine(pf, "Age of Empires II", "data"));   // no age3y.exe
        var noData = Path.Combine(pf, "Age of Empires III");
        Directory.CreateDirectory(noData);
        File.WriteAllText(Path.Combine(noData, "age3y.exe"), "x");                  // no data\

        Assert.Empty(ByName(pf));
    }

    /// <summary>
    /// Documents the scope rather than a wish: the pass is a NAME filter, so a clean
    /// AoE3 in "Juegos\AoE3" is left to the manual pick and the install flow's deep scan.
    /// </summary>
    [Fact]
    public void AFolderWithAnotherNameIsOutOfScope()
    {
        var pf = NewTempDir();
        MakeCleanAoE3(Path.Combine(pf, "AoE3"), steamLayout: false);

        Assert.Empty(ByName(pf));
    }

    [Fact]
    public void AMissingRootIsNotAnError()
    {
        var missing = Path.Combine(NewTempDir(), "does-not-exist");
        Assert.Empty(ByName(missing));
    }
}
