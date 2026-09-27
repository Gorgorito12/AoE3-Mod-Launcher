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
/// Pins <see cref="EngineRestore"/>: damaged engine files of a launcher clone are put back from the
/// player's own AoE3, and ONLY with byte-identical copies. The refusals are the point — a wrong
/// source, a file the mod ships, or a destination that is the player's real game must never be
/// written.
/// </summary>
public class EngineRestoreTests : IDisposable
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

    private static void Put(string root, string rel, string text)
    {
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static readonly ModProfile Isolated = new()
    {
        Id = "mod", DisplayName = "Mod", InstallType = ModInstallType.IsolatedFolder,
    };

    /// <summary>A clone whose manifest recorded RockallDLL.dll as "good", then damaged on disk.</summary>
    private (string Install, InstallManifest Manifest) MakeDamagedClone(string goodBytes = "good engine")
    {
        var install = NewDir("engine-clone-");
        Put(install, "RockallDLL.dll", goodBytes);
        var manifest = new InstallManifest
        {
            ModId = "mod",
            InstallPath = install,
            EngineFileHashes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["RockallDLL.dll"] = VerifyService.ComputeFingerprintOf(Path.Combine(install, "RockallDLL.dll")),
            },
        };
        manifest.Save();
        Put(install, "RockallDLL.dll", "BROKEN");
        return (install, manifest);
    }

    private IReadOnlyList<EngineRestoreItem> Run(string install, InstallManifest manifest, IReadOnlyList<string> sources,
        IReadOnlyCollection<string>? registered = null, ISet<string>? never = null)
        => EngineRestore.Restore(install, manifest, new[] { "RockallDLL.dll" },
            never ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            sources, registered ?? new[] { install }, Path.Combine(NewDir("engine-bak-"), "set"));

    [Fact]
    public void AByteIdenticalCopyInThePlayersGameIsRestored_AndTheDamagedFileBackedUp()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "RockallDLL.dll", "good engine");

        var backupParent = NewDir("engine-bak-");
        var result = EngineRestore.Restore(install, manifest, new[] { "RockallDLL.dll" },
            new HashSet<string>(), new[] { aoe3 }, new[] { install }, Path.Combine(backupParent, "set"));

        Assert.Equal(EngineRestoreResult.Restored, Assert.Single(result).Result);
        Assert.Equal("good engine", File.ReadAllText(Path.Combine(install, "RockallDLL.dll")));
        Assert.Equal("BROKEN", File.ReadAllText(Path.Combine(backupParent, "set", "RockallDLL.dll")));
        Assert.False(File.Exists(Path.Combine(install, "RockallDLL.dll" + EngineRestore.StagingSuffix)));
    }

    [Fact]
    public void TheBinFolderOfASteamLayoutIsSearchedToo()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "bin/RockallDLL.dll", "good engine");
        Assert.Equal(EngineRestoreResult.Restored, Run(install, manifest, new[] { aoe3 })[0].Result);
    }

    [Fact]
    public void DifferentBytesAreNeverACopy()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "RockallDLL.dll", "good engin3");   // same size, other bytes
        Assert.Equal(EngineRestoreResult.NoSource, Run(install, manifest, new[] { aoe3 })[0].Result);
        Assert.Equal("BROKEN", File.ReadAllText(Path.Combine(install, "RockallDLL.dll")));
    }

    [Fact]
    public void ACandidateInsideAnotherInstallIsNeverASource()
    {
        var (install, manifest) = MakeDamagedClone();
        var otherInstall = NewDir("other-install-");
        Put(otherInstall, "RockallDLL.dll", "good engine");
        var result = Run(install, manifest, new[] { otherInstall }, registered: new[] { install, otherInstall });
        Assert.Equal(EngineRestoreResult.NoSource, result[0].Result);
    }

    [Fact]
    public void TheDestinationIsNeverItsOwnSource()
    {
        var (install, manifest) = MakeDamagedClone();
        // The clone listed as a root: it must not "restore" the damaged file onto itself.
        Assert.Equal(EngineRestoreResult.NoSource,
            Run(install, manifest, new[] { install }, registered: Array.Empty<string>())[0].Result);
    }

    [Fact]
    public void AFileTheModShipsOrATranslationCoversIsNeverRestored()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "RockallDLL.dll", "good engine");
        var never = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RockallDLL.dll" };
        Assert.Equal(EngineRestoreResult.NoSource, Run(install, manifest, new[] { aoe3 }, never: never)[0].Result);
        Assert.Equal("BROKEN", File.ReadAllText(Path.Combine(install, "RockallDLL.dll")));
    }

    [Fact]
    public void NeverRestoreCollectsShippedCoveredRemovedAndAddonFiles()
    {
        var profile = new ModProfile
        {
            Id = "mod",
            Translations = new TranslationsSettings { CoveredFiles = new() { @"data\stringtabley.xml" } },
            CloneFilesRemovedByPatches = new[] { @"data\proto.xml.XMB" },
        };
        var manifest = new InstallManifest { OverlayFiles = new() { "art/a.ddt" } };
        var never = EngineRestore.NeverRestore(profile, manifest, new[] { "art/addon.ddt" });
        Assert.Contains("data/stringtabley.xml", never);
        Assert.Contains("data/proto.xml.XMB", never);
        Assert.Contains("art/a.ddt", never);
        Assert.Contains("art/addon.ddt", never);
    }

    [Fact]
    public void ALockedTargetIsInUse_NotUnrestorable()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "RockallDLL.dll", "good engine");
        using (new FileStream(Path.Combine(install, "RockallDLL.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(EngineRestoreResult.InUse, Run(install, manifest, new[] { aoe3 })[0].Result);
    }

    [Fact]
    public void OnlyAnIsolatedCloneOfThisModOutsideAnyAoe3FolderIsEligible()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");

        Assert.Equal(EngineRestoreRefusal.None, EngineRestore.Eligibility(Isolated, install, manifest, new[] { aoe3 }));
        Assert.Equal(EngineRestoreRefusal.NotIsolated, EngineRestore.Eligibility(
            new ModProfile { Id = "mod", InstallType = ModInstallType.InPlaceOverlay }, install, manifest, new[] { aoe3 }));
        Assert.Equal(EngineRestoreRefusal.StockGame, EngineRestore.Eligibility(
            new ModProfile { Id = "mod", IsStockGame = true }, install, manifest, new[] { aoe3 }));
        Assert.Equal(EngineRestoreRefusal.NoManifest, EngineRestore.Eligibility(Isolated, install, null, new[] { aoe3 }));

        // The install IS a real AoE3 folder, or holds one: never write there.
        Assert.Equal(EngineRestoreRefusal.Aoe3Folder, EngineRestore.Eligibility(Isolated, install, manifest, new[] { install }));
        Assert.Equal(EngineRestoreRefusal.Aoe3Folder,
            EngineRestore.Eligibility(Isolated, install, manifest, new[] { Path.Combine(install, "bin") }));
    }

    /// <summary>
    /// The NORMAL Steam layout — the clone lives inside the game's folder
    /// (…\Age Of Empires 3\Wars of Liberty) — must stay eligible. Refusing it switched the feature
    /// off for exactly the installs the launcher makes by default.
    /// </summary>
    [Fact]
    public void ACloneInsideTheGamesOwnFolderIsEligible()
    {
        var (install, manifest) = MakeDamagedClone();
        Assert.Equal(EngineRestoreRefusal.None,
            EngineRestore.Eligibility(Isolated, install, manifest, new[] { Path.GetDirectoryName(install)! }));
    }

    [Fact]
    public void AManifestAnOlderBuildResetToNotClonedIsStillEligible()
    {
        var (install, manifest) = MakeDamagedClone();
        manifest.ClonedAoe3 = false;
        Assert.Equal(EngineRestoreRefusal.None,
            EngineRestore.Eligibility(Isolated, install, manifest, new[] { NewDir("aoe3-") }));
    }

    [Fact]
    public void AnotherModsManifestIsRefused()
    {
        var (install, manifest) = MakeDamagedClone();
        manifest.ModId = "someone-else";
        Assert.Equal(EngineRestoreRefusal.ForeignManifest,
            EngineRestore.Eligibility(Isolated, install, manifest, new[] { NewDir("aoe3-") }));
    }

    [Fact]
    public void AKeyThatIsNotInTheEngineMapIsNeverWritten()
    {
        var (install, manifest) = MakeDamagedClone();
        var aoe3 = NewDir("aoe3-");
        Put(aoe3, "granny2.dll", "anything");
        var result = EngineRestore.Restore(install, manifest, new[] { "granny2.dll" },
            new HashSet<string>(), new[] { aoe3 }, new[] { install }, Path.Combine(NewDir("engine-bak-"), "set"));
        Assert.Equal(EngineRestoreResult.NoSource, result[0].Result);
        Assert.False(File.Exists(Path.Combine(install, "granny2.dll")));
    }
}
