using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="GitHubReleaseDownloader.PickAssetIndex"/> — which asset on a GitHub release is
/// the mod's FULL payload.
///
/// <para>This exists because the old rule was "the first <c>.zip</c>", and a delta-patch release
/// carries two. GitHub returns assets in upload order, so a modder who uploaded
/// <c>patch-…-to-….zip</c> before the full overlay — following the project's own documented
/// three-asset recipe — handed the launcher the patch as if it were the whole mod. On an update
/// that made <c>ApplyUpdateDeletions</c> compute "net-new files the new release no longer ships"
/// against those few files and delete essentially the entire overlay, discarding the backups
/// because that is the success path.</para>
///
/// <para>The decision was <c>private</c> and took a <c>private</c> DTO, so it could not be tested
/// at all — which is why it shipped. It is name-only and <c>internal</c> now.</para>
/// </summary>
public class ReleaseAssetPickTests
{
    private static int? Pick(string? pattern, params string[] names)
        => GitHubReleaseDownloader.PickAssetIndex(new List<string>(names), pattern);

    /// <summary>
    /// THE regression. The patch zip is listed first — upload order is the modder's, not ours —
    /// and the full overlay must still win.
    /// </summary>
    [Fact]
    public void ThePatchZipIsNeverMistakenForTheFullPayload()
    {
        Assert.Equal(1, Pick(null, "patch-v1-to-v2.zip", "mod-v2.zip"));
    }

    /// <summary>Order must not matter: the full overlay wins from either position.</summary>
    [Fact]
    public void TheFullZipWinsWhicheverOrderTheAssetsWereUploadedIn()
    {
        Assert.Equal(0, Pick(null, "mod-v2.zip", "patch-v1-to-v2.zip"));
        Assert.Equal(1, Pick(null, "patch-v1-to-v2.zip", "mod-v2.zip"));
    }

    /// <summary>The descriptor is a patch asset too, and is never a payload.</summary>
    [Fact]
    public void TheDescriptorIsNeverChosen()
    {
        Assert.Equal(2, Pick(null, "patch-v1-to-v2.json", "patch-v1-to-v2.zip", "mod-v2.zip"));
    }

    /// <summary>
    /// A loose pattern must not smuggle a patch back in — the exclusion happens before matching.
    /// </summary>
    [Fact]
    public void ALoosePatternStillCannotSelectAPatch()
    {
        Assert.Equal(1, Pick("*.zip", "patch-v1-to-v2.zip", "mod-v2.zip"));
    }

    /// <summary>An explicit pattern still expresses intent, over the plain .zip heuristic.</summary>
    [Fact]
    public void AnExplicitPatternStillWins()
    {
        Assert.Equal(2, Pick("mod-*.zip", "patch-v1-to-v2.zip", "extra.zip", "mod-v2.zip"));
    }

    /// <summary>
    /// The half of the fix that keeps it from being a regression: with no descriptor in sight, a
    /// lone <c>patch-*.zip</c> is not patch machinery at all — it is a mod whose payload simply
    /// happens to be named that way, and it must keep working exactly as before. Excluding it
    /// would make the release uninstallable, which is worse than the bug.
    /// </summary>
    [Fact]
    public void ALonePatchNamedZipWithNoDescriptorIsStillAPayload()
    {
        Assert.Equal(0, Pick(null, "patch-v1-to-v2.zip"));
        Assert.Equal(1, Pick(null, "readme.txt", "patch-a-to-b.zip"));
    }

    /// <summary>
    /// The other half, and the one that makes patch-only releases possible: once a descriptor is
    /// present the zip beside it really is a patch, so a release carrying only patches has NO
    /// full payload. Answering otherwise would hand the planner a phantom baseline and reinstall
    /// a mod from a patch — the original bug, arriving through the fallback instead.
    /// </summary>
    [Fact]
    public void APatchOnlyReleaseHasNoFullPayloadAtAll()
    {
        Assert.Null(Pick(null, "patch-v1-to-v2.zip", "patch-v1-to-v2.json"));
        Assert.Null(Pick(null, "patch-v1-to-v2.json", "patch-v1-to-v2.zip", "notes.txt"));
    }

    /// <summary>
    /// The hyphen in the prefix is load-bearing: a mod's own release notes are not patch
    /// machinery.
    /// </summary>
    [Fact]
    public void APlausibleLookalikeIsNotAPatchAsset()
    {
        Assert.Equal(0, Pick(null, "patchnotes.zip"));
        Assert.False(DeltaPatchService.PatchAssetNaming.IsPatchAsset("patchnotes.zip"));
        Assert.True(DeltaPatchService.PatchAssetNaming.IsPatchAsset("patch-v1-to-v2.zip"));
    }

    /// <summary>A patch's payload is not a descriptor, and neither is a stray name.</summary>
    [Fact]
    public void IsDescriptorRecognisesOnlyTheJsonHalf()
    {
        Assert.True(DeltaPatchService.PatchAssetNaming.IsDescriptor("patch-v1-to-v2.json"));
        Assert.False(DeltaPatchService.PatchAssetNaming.IsDescriptor("patch-v1-to-v2.zip"));
        Assert.False(DeltaPatchService.PatchAssetNaming.IsDescriptor("notes.json"));
        Assert.False(DeltaPatchService.PatchAssetNaming.IsDescriptor(null));
    }

    /// <summary>A release with nothing installable resolves to null, and the caller throws.</summary>
    [Fact]
    public void NoZipAtAllYieldsNothing()
    {
        Assert.Null(Pick(null, "notes.txt", "screenshot.png"));
        Assert.Null(Pick(null));
    }
}
