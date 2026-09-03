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
}
