using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="UninstallService.ShouldRemoveOverlayOnly"/> — the single decision that
/// separates "delete the mod's files" from "recursively delete this folder", where getting it
/// wrong costs the player their Age of Empires III install.
///
/// The case that matters is the first one: a manifest claiming <c>clonedAoe3: true</c> on a path
/// the detector recognises as a real AoE3 root. Older launcher builds stamped that flag on every
/// install, including overlays laid straight into the game folder, so those manifests exist in
/// the wild and the manifest alone must never be trusted over the detector.
/// </summary>
public class UninstallSafetyTests
{
    [Fact]
    public void AManifestClaimingACloneNeverOverridesARealAoE3Root()
    {
        Assert.True(UninstallService.ShouldRemoveOverlayOnly(
            hasManifest: true, clonedAoe3: true, isRealAoe3Root: true));
    }

    [Fact]
    public void ALauncherMadeCloneIsStillFullyRemoved()
    {
        // The normal case must keep working: a clone in its own folder is deleted outright.
        Assert.False(UninstallService.ShouldRemoveOverlayOnly(
            hasManifest: true, clonedAoe3: true, isRealAoe3Root: false));
    }

    [Fact]
    public void AnInPlaceOverlayIsAlwaysOverlayOnly()
    {
        Assert.True(UninstallService.ShouldRemoveOverlayOnly(
            hasManifest: true, clonedAoe3: false, isRealAoe3Root: false));
    }

    [Theory]
    [InlineData(true, true)]     // no manifest, but the detector says this is their game
    [InlineData(false, false)]   // no manifest and not a game root: a clone that lost its manifest
    public void WithNoManifestTheDetectorDecidesAlone(bool isRealAoe3Root, bool expectedOverlayOnly)
    {
        // Deliberately unchanged behaviour: a clone whose manifest went missing still gets a
        // normal folder removal, because that is the correct uninstall for it.
        Assert.Equal(expectedOverlayOnly, UninstallService.ShouldRemoveOverlayOnly(
            hasManifest: false, clonedAoe3: false, isRealAoe3Root: isRealAoe3Root));
    }

    // ---------------------------------------------------------------------
    // Whether a folder carrying no manifest of OURS may be deleted at all.
    //
    // Uninstall's validity gate used to be one File.Exists on the probe file, and that
    // is what turned a detection mistake into data loss: a stray orphan `age3n.exe` in
    // the AoE3 root is cloned into every IsolatedFolder install, so the launcher deleted
    // a user's Struggle of Indonesia — 11,408 files — while reporting that it was
    // uninstalling Napoleonic Era.
    // ---------------------------------------------------------------------

    /// <summary>
    /// ⚠ The case a naive tightening to <c>== Match</c> would break, and the reason this
    /// predicate is not written that way. An install that died mid-write is precisely the
    /// thing a user needs to remove; refusing it leaves a half-written folder with no way
    /// out of the launcher.
    /// </summary>
    [Fact]
    public void AnInterruptedInstallCanStillBeUninstalled()
    {
        Assert.True(UninstallService.MayUninstallWithoutOwnership(
            ProbeOutcome.InstallInProgress, legacyRegistryValid: false));
    }

    [Fact]
    public void ANormalInstallIsStillRemovable()
    {
        Assert.True(UninstallService.MayUninstallWithoutOwnership(
            ProbeOutcome.Match, legacyRegistryValid: false));
    }

    /// <summary>
    /// The legacy profile that declares no probe file at all is recognised by the registry
    /// instead. ModInstallProbe deliberately does not carry that allowance, so it has to be
    /// passed in — drop it and those installs become unremovable.
    /// </summary>
    [Fact]
    public void ALegacyRegistryRecognisedInstallIsRemovable()
    {
        Assert.True(UninstallService.MayUninstallWithoutOwnership(
            ProbeOutcome.ProbeMissing, legacyRegistryValid: true));
    }

    /// <summary>
    /// The rejections. Each of these passed the old bare probe-file check, and the first
    /// one is the shape of a base-game folder — the accident the gate exists to stop.
    /// </summary>
    [Theory]
    [InlineData(ProbeOutcome.MarkerMissing)]
    [InlineData(ProbeOutcome.EngineMissing)]
    [InlineData(ProbeOutcome.ForeignInstall)]
    [InlineData(ProbeOutcome.ProbeMissing)]
    [InlineData(ProbeOutcome.NotADirectory)]
    public void AnythingElseIsRefused(ProbeOutcome outcome)
    {
        Assert.False(UninstallService.MayUninstallWithoutOwnership(
            outcome, legacyRegistryValid: false));
    }

    /// <summary>
    /// The incident itself, end to end and on disk: Napoleonic Era's profile pointed at
    /// Struggle of Indonesia's folder, which satisfied every content signal NE declares
    /// because the stray `age3n.exe` had been cloned into it.
    ///
    /// <para>Asserting the refusal is half of it. The other half is that <b>the folder is
    /// still there afterwards</b> — that is what was actually lost, and a plan that
    /// refuses while something else has already started deleting would pass a
    /// refusal-only assertion.</para>
    /// </summary>
    [Fact]
    public void PlanRefusesAnotherModsFolderAndLeavesItUntouched()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("wol-uninstall-foreign-").FullName;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "age3n.exe"), "stray");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "RockallDLL.dll"), "engine");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "data.bar"), "content");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, WarsOfLibertyLauncher.Models.InstallManifest.FileName),
                "{ \"modId\": \"struggle-of-indonesia\" }");

            var ne = new WarsOfLibertyLauncher.Models.ModProfile
            {
                Id = "napoleonic-era",
                DisplayName = "Napoleonic Era",
                InstallType = WarsOfLibertyLauncher.Models.ModInstallType.IsolatedFolder,
                InstallProbeFile = "age3n.exe",
                InstallMarker = "",
            };

            var plan = new UninstallService().Plan(ne, dir);

            Assert.Equal(UninstallMode.NotAValidInstall, plan.Mode);
            Assert.Equal(4, System.IO.Directory.GetFiles(dir).Length);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ---------------------------------------------------------------------
    // A folder that CONTAINS the player's Age of Empires III.
    //
    // Uninstall of a copy opens the same plan for any registered folder, so a copy the
    // player pointed at the wrong place — a Steam library, a parent of their game — must be
    // refused by the plan itself, whatever its manifest or probe file say.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The case that matters: an OWNED, probe-matching folder whose tree holds the base
    /// game. Every other gate says yes; only the containment check stands between it and a
    /// recursive delete of the player's AoE3. The files must still be there afterwards.
    /// </summary>
    [Fact]
    public void PlanRefusesAFolderThatContainsAoE3AndLeavesItUntouched()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("wol-uninstall-contains-").FullName;
        try
        {
            var wol = WarsOfLibertyLauncher.Services.ModRegistry.Find(
                WarsOfLibertyLauncher.Services.ModRegistry.WolId)!;
            var probe = System.IO.Path.Combine(dir, wol.InstallProbeFile);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(probe)!);
            System.IO.File.WriteAllText(probe, "x");
            new WarsOfLibertyLauncher.Models.InstallManifest
                { ModId = wol.Id, InstallPath = dir, ClonedAoe3 = true }.Save();

            var aoe3 = System.IO.Path.Combine(dir, "steamapps", "common", "Age Of Empires 3");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(aoe3, "bin"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(aoe3, "bin", "age3y.exe"), "game");

            var plan = new UninstallService().Plan(wol, dir,
                new[] { System.IO.Path.Combine(aoe3, "bin"), aoe3 });

            Assert.Equal(UninstallMode.NotAValidInstall, plan.Mode);
            Assert.True(plan.ContainsBaseGame);
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(aoe3, "bin", "age3y.exe")));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>The exact AoE3 root keeps today's behaviour: overlay-only, never refused.</summary>
    [Fact]
    public void PlanOnTheAoE3RootItselfStaysOverlayOnly()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("wol-uninstall-root-").FullName;
        try
        {
            var wol = WarsOfLibertyLauncher.Services.ModRegistry.Find(
                WarsOfLibertyLauncher.Services.ModRegistry.WolId)!;
            var probe = System.IO.Path.Combine(dir, wol.InstallProbeFile);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(probe)!);
            System.IO.File.WriteAllText(probe, "x");
            new WarsOfLibertyLauncher.Models.InstallManifest
                { ModId = wol.Id, InstallPath = dir, ClonedAoe3 = true }.Save();

            // Steam layout: the mod root also holds its own bin\ game folder.
            var plan = new UninstallService().Plan(wol, dir,
                new[] { System.IO.Path.Combine(dir, "bin"), dir });

            Assert.Equal(UninstallMode.Valid, plan.Mode);
            Assert.True(plan.OverlayOnly);
            Assert.False(plan.ContainsBaseGame);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(@"C:\Games", @"C:\Games\Age Of Empires 3", true)]
    [InlineData(@"C:\Games\", @"C:\Games\Age Of Empires 3", true)]
    [InlineData(@"C:\Steam\steamapps", @"C:\Steam\steamapps\common\Age Of Empires 3\bin", true)]
    [InlineData(@"c:\games", @"C:\GAMES\Age Of Empires 3", true)]
    // The rejections — these are what keep ordinary copies uninstallable.
    [InlineData(@"C:\Games\Age Of Empires 3", @"C:\Games\Age Of Empires 3", false)]
    [InlineData(@"C:\Games\Age Of Empires 3\Wars of Liberty", @"C:\Games\Age Of Empires 3", false)]
    [InlineData(@"C:\Games\Age Of Empires 3 Mods", @"C:\Games\Age Of Empires 3", false)]
    [InlineData(@"D:\Games", @"C:\Games\Age Of Empires 3", false)]
    public void ContainmentIsByWholePathSegments(string path, string root, bool expected)
    {
        Assert.Equal(expected, UninstallService.ContainsAoe3Root(path, new[] { root }));
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"D:\", true)]
    [InlineData(@"C:\Games", false)]
    [InlineData(@"C:\Games\Wars of Liberty", false)]
    public void ADriveRootIsNeverAnInstall(string path, bool expected)
    {
        Assert.Equal(expected, UninstallService.IsDriveRoot(path));
    }
}
