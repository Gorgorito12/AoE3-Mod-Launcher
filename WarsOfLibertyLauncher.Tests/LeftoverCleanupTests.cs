using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="LeftoverCleanup"/>: an install made before the clone-cleanup rules keeps the
/// base game's compiled files over the mod's data, and Repair now removes them without a download.
/// The refusals are the point — a file the mod ships, a file an addon owns, and a folder that is
/// the player's real game are never touched, and nothing is deleted outright.
/// </summary>
public class LeftoverCleanupTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var d in _dirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    private string NewDir(string prefix)
    {
        var d = Directory.CreateTempSubdirectory(prefix).FullName;
        _dirs.Add(d);
        return d;
    }

    private static void Put(string root, string rel, string text = "x")
    {
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static bool On(string root, string rel)
        => File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The Wars of Liberty shape: the patch chain deletes compiled clone files the snapshot never shipped.</summary>
    private static ModProfile WolShape(ModInstallType type = ModInstallType.IsolatedFolder) => new()
    {
        Id = "mod",
        DisplayName = "Mod",
        InstallType = type,
        UpdateMechanism = ModUpdateMechanism.WolPatcher,
        CloneFilesRemovedByPatches = new[] { @"data\proto.xml.XMB", @"data\protoy.xml.XMB", @"data\stringtabley.xml.XMB" },
    };

    private static InstallManifest Manifest(params string[] shipped) => new()
    {
        ModId = "mod",
        OverlayFiles = shipped.ToList(),
        FileHashes = shipped.ToDictionary(s => s, _ => new FileFingerprint(), StringComparer.OrdinalIgnoreCase),
    };

    private static IReadOnlyList<string> SelectOnDisk(ModProfile p, InstallManifest m, string root,
        IEnumerable<string>? addonOwned = null)
        => LeftoverCleanup.Select(p, m, addonOwned, rel => On(root, rel));

    [Fact]
    public void TheListedCloneFilesStillOnDiskAreSelected()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB");
        Put(root, "data/stringtabley.xml.XMB");
        var picked = SelectOnDisk(WolShape(), Manifest("data/protoy.xml", "data/stringtabley.xml"), root);
        Assert.Equal(new[] { "data/proto.xml.XMB", "data/stringtabley.xml.XMB" }, picked);
    }

    [Fact]
    public void AFileTheModShipsIsNeverSelected()
    {
        var root = NewDir("leftover-");
        Put(root, "data/protoy.xml.XMB");
        var picked = SelectOnDisk(WolShape(), Manifest("data/protoy.xml", "data/protoy.xml.XMB"), root);
        Assert.Empty(picked);
    }

    [Fact]
    public void AFileAnAddonOwnsIsNeverSelected()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB");
        var picked = SelectOnDisk(WolShape(), Manifest("data/protoy.xml"), root, new[] { @"data\proto.xml.XMB" });
        Assert.Empty(picked);
    }

    [Fact]
    public void AnInPlaceOverlayIsThePlayersGameAndIsNeverSelected()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB");
        Assert.Empty(SelectOnDisk(WolShape(ModInstallType.InPlaceOverlay), Manifest("data/protoy.xml"), root));
        Assert.Equal(LeftoverRefusal.NotIsolated,
            LeftoverCleanup.Eligibility(WolShape(ModInstallType.InPlaceOverlay), root, Manifest("data/protoy.xml"),
                Array.Empty<string>()));
    }

    [Fact]
    public void WithoutARecordOfWhatShippedNothingIsSelected()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB");
        Assert.Empty(SelectOnDisk(WolShape(), new InstallManifest { ModId = "mod" }, root));
    }

    /// <summary>
    /// The superseded-XMB rule is OPT-IN, exactly as at install: Wars of Liberty's canonical peers
    /// KEEP the base game's randomnames/unithelpstrings compiled files, so a derived rule would
    /// diverge from every one of them.
    /// </summary>
    [Fact]
    public void SupersededCompiledFilesAreOnlyForAModThatOptsIn()
    {
        var root = NewDir("leftover-");
        Put(root, "data/randomnames.xml.XMB");
        var manifest = Manifest("data/randomnames.xml");
        var plain = new ModProfile { Id = "mod", InstallType = ModInstallType.IsolatedFolder };
        Assert.Empty(SelectOnDisk(plain, manifest, root));

        var optedIn = new ModProfile { Id = "mod", InstallType = ModInstallType.IsolatedFolder, SupersedeCompiledXml = true };
        Assert.Equal(new[] { "data/randomnames.xml.XMB" }, SelectOnDisk(optedIn, manifest, root));
    }

    [Fact]
    public void RemoveMovesToABackupAndNeverDeletes()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB", "vanilla bytes");
        var backup = Path.Combine(NewDir("leftover-bak-"), "set");

        var items = LeftoverCleanup.Remove(root, new[] { "data/proto.xml.XMB" }, backup);

        Assert.True(Assert.Single(items).Removed);
        Assert.False(On(root, "data/proto.xml.XMB"));
        Assert.Equal("vanilla bytes", File.ReadAllText(Path.Combine(backup, "data", "proto.xml.XMB")));
    }

    [Fact]
    public void AHeldOpenFileStaysAndIsReported()
    {
        var root = NewDir("leftover-");
        Put(root, "data/proto.xml.XMB");
        var backup = Path.Combine(NewDir("leftover-bak-"), "set");
        using (new FileStream(Path.Combine(root, "data", "proto.xml.XMB"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var item = Assert.Single(LeftoverCleanup.Remove(root, new[] { "data/proto.xml.XMB" }, backup));
            Assert.False(item.Removed);
            Assert.NotNull(item.Error);
        }
        Assert.True(On(root, "data/proto.xml.XMB"));
    }

    [Fact]
    public void APathClimbingOutOfTheInstallIsRefused()
    {
        var parent = NewDir("leftover-parent-");
        var root = Path.Combine(parent, "install");
        Directory.CreateDirectory(root);
        Put(parent, "outside.XMB");
        var item = Assert.Single(LeftoverCleanup.Remove(root, new[] { "../outside.XMB" },
            Path.Combine(NewDir("leftover-bak-"), "set")));
        Assert.False(item.Removed);
        Assert.True(On(parent, "outside.XMB"));
    }

    [Fact]
    public void OnlyAnIsolatedCloneOfThisModOutsideAnyAoe3FolderIsEligible()
    {
        var root = NewDir("leftover-");
        var manifest = Manifest("data/protoy.xml");
        Assert.Equal(LeftoverRefusal.None, LeftoverCleanup.Eligibility(WolShape(), root, manifest, new[] { NewDir("aoe3-") }));
        Assert.Equal(LeftoverRefusal.NoManifest, LeftoverCleanup.Eligibility(WolShape(), root, null, Array.Empty<string>()));
        Assert.Equal(LeftoverRefusal.StockGame,
            LeftoverCleanup.Eligibility(new ModProfile { Id = "mod", IsStockGame = true }, root, manifest, Array.Empty<string>()));

        var foreign = Manifest("data/protoy.xml");
        foreign.ModId = "someone-else";
        Assert.Equal(LeftoverRefusal.ForeignManifest, LeftoverCleanup.Eligibility(WolShape(), root, foreign, Array.Empty<string>()));

        Assert.Equal(LeftoverRefusal.Aoe3Folder, LeftoverCleanup.Eligibility(WolShape(), root, manifest, new[] { root }));
        Assert.Equal(LeftoverRefusal.Aoe3Folder,
            LeftoverCleanup.Eligibility(WolShape(), root, manifest, new[] { Path.Combine(root, "bin") }));
        // The normal Steam layout: the clone lives INSIDE the game's folder. Eligible.
        Assert.Equal(LeftoverRefusal.None,
            LeftoverCleanup.Eligibility(WolShape(), root, manifest, new[] { Path.GetDirectoryName(root)! }));
    }

    /// <summary>
    /// The verdict names them, and they alone never cost a download: Verify says "leftover", and
    /// Repair's route is Cleanup rather than a multi-GB re-lay.
    /// </summary>
    [Fact]
    public void TheSharedVerdictReportsThemAndTheyNeverRouteToARelay()
    {
        var root = NewDir("leftover-");
        Put(root, "data/protoy.xml", "proto");
        Put(root, "data/techtreey.xml", "tech");
        Put(root, "data/stringtabley.xml", "strings");
        Put(root, "art/a.ddt", "A");
        Put(root, "data/proto.xml.XMB");
        new InstallManifest
        {
            ModId = "mod",
            InstallPath = root,
            OverlayFiles = new() { "art/a.ddt" },
            FileHashes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["art/a.ddt"] = VerifyService.ComputeFingerprintOf(Path.Combine(root, "art", "a.ddt")),
            },
        }.Save();

        var report = IntegrityService.Diagnose(root, WolShape(), new DiagnoseOptions());
        var finding = Assert.Single(report.Findings);
        Assert.Equal(IntegrityKind.Leftover, finding.Kind);
        Assert.Equal("data/proto.xml.XMB", finding.Path);
        Assert.Equal(RepairRoute.Cleanup, IntegrityService.Route(report));

        // Real damage alongside still re-lays — the re-lay removes the leftover as it always did.
        File.WriteAllText(Path.Combine(root, "art", "a.ddt"), "changed");
        Assert.Equal(RepairRoute.Relay, IntegrityService.Route(IntegrityService.Diagnose(root, WolShape(), new DiagnoseOptions())));
    }
}
