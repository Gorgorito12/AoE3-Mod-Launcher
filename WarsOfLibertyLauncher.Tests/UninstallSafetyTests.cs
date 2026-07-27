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
}
