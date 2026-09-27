using System.Text;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the pure rules a repair decides by: which mods may be repaired at all, which version a
/// GitHubReleases repair lays, and which version gets recorded afterwards. Each group exists
/// because of a real bug — "Repair" silently changed a mod's version (and could DOWNGRADE
/// Improvement Mod), an intact repair stamped a version it never laid, and DelegatedExternal /
/// Manual mods could be "repaired" at all.
/// </summary>
public class RepairPolicyTests
{
    private static ModProfile Profile(ModUpdateMechanism mech, bool stock = false) => new()
    {
        Id = "improvement-mod", DisplayName = "Improvement Mod", UpdateMechanism = mech, IsStockGame = stock,
    };

    // ---- eligibility ---------------------------------------------------------

    [Fact]
    public void TheBaseGameIsNeverRepaired()
        => Assert.Equal(RepairRefusal.StockGame,
            RepairEligibility.Evaluate(Profile(ModUpdateMechanism.Manual, stock: true), true, null));

    [Theory]
    [InlineData(ModUpdateMechanism.Manual)]
    [InlineData(ModUpdateMechanism.DelegatedExternal)]
    public void AModTheLauncherCannotInstallIsRefused(ModUpdateMechanism mech)
        => Assert.Equal(RepairRefusal.NotLauncherInstallable, RepairEligibility.Evaluate(Profile(mech), true, null));

    [Theory]
    [InlineData(ModUpdateMechanism.WolPatcher)]
    [InlineData(ModUpdateMechanism.GitHubReleases)]
    public void InstallableMechanismsAreAllowed(ModUpdateMechanism mech)
        => Assert.Equal(RepairRefusal.None, RepairEligibility.Evaluate(Profile(mech), true, "improvement-mod"));

    [Fact]
    public void AForeignManifestIsRefused()
        => Assert.Equal(RepairRefusal.ForeignManifest,
            RepairEligibility.Evaluate(Profile(ModUpdateMechanism.GitHubReleases), true, "napoleonic-era"));

    [Fact]
    public void ALegacyManifestWithNoModIdIsNotEvidenceAgainst()
        => Assert.Equal(RepairRefusal.None,
            RepairEligibility.Evaluate(Profile(ModUpdateMechanism.GitHubReleases), true, null));

    [Fact]
    public void AMissingFolderIsNotInstalled()
        => Assert.Equal(RepairRefusal.NotInstalled,
            RepairEligibility.Evaluate(Profile(ModUpdateMechanism.GitHubReleases), false, null));

    [Fact]
    public void ModIdIsReadWithoutParsingTheWholeManifest()
    {
        var json = Encoding.UTF8.GetBytes("﻿{\n  \"modId\": \"wol\",\n  \"files\": [\"a\", \"b\"");   // truncated on purpose
        Assert.Equal("wol", RepairEligibility.ReadModId(json));
        Assert.Null(RepairEligibility.ReadModId(Encoding.UTF8.GetBytes("{\"files\":[{\"modId\":\"x\"}]}")));
        Assert.Null(RepairEligibility.ReadModId(Encoding.UTF8.GetBytes("not json")));
    }

    // ---- target ----------------------------------------------------------------

    [Fact]
    public void AnUnknownInstalledVersionTakesTheEffectiveTag_TheSelfHeal()
        => Assert.Equal(RepairTargetKind.Effective,
            RepairPolicy.PickTarget("", "24.07.2026", false, true, false).Kind);

    [Fact]
    public void InstalledEqualsEffective_NothingToDecide()
        => Assert.Equal(RepairTargetKind.Effective,
            RepairPolicy.PickTarget("24.07.2026", "24.07.2026", false, true, true).Kind);

    [Fact]
    public void RepairRestoresTheInstalledVersion_NeverSilentlyChangesIt()
    {
        // Improvement Mod: installed newer than the approved tag, follow-latest cache empty.
        var t = RepairPolicy.PickTarget("06.09.2026", "19.07.2026", false, graphKnown: true, installedHasFullPayload: true);
        Assert.Equal(RepairTargetKind.Installed, t.Kind);
        Assert.Equal("06.09.2026", t.Tag);
    }

    [Fact]
    public void AnInstalledVersionThatCanNoLongerBeDownloadedNeedsConsent()
    {
        var t = RepairPolicy.PickTarget("1.0.0", "2.1.7b", false, graphKnown: true, installedHasFullPayload: false);
        Assert.Equal(RepairTargetKind.ConfirmChange, t.Kind);
        Assert.Equal("1.0.0", t.From);
        Assert.Equal("2.1.7b", t.To);
    }

    [Fact]
    public void AnExternalHostOnlyServesTheApprovedTag_SoAnyOtherNeedsConsent()
        => Assert.Equal(RepairTargetKind.ConfirmChange,
            RepairPolicy.PickTarget("1.1", "1.2", externalHosted: true, graphKnown: false, installedHasFullPayload: false).Kind);

    [Fact]
    public void AnUnreadableListingStillAimsAtTheInstalledVersion()
        => Assert.Equal(RepairTargetKind.Installed,
            RepairPolicy.PickTarget("1.1", "1.2", false, graphKnown: false, installedHasFullPayload: false).Kind);

    // ---- stamping --------------------------------------------------------------

    [Fact]
    public void ALaidVersionIsStamped()
        => Assert.Equal("1.2", RepairPolicy.StampAfterRepair(ModUpdateMechanism.GitHubReleases, "1.2", "1.1", null));

    [Fact]
    public void AnIntactGitHubRepairNeverStampsTheEffectiveTag()
    {
        // Nothing laid; the own manifest's version describes the bytes on disk.
        Assert.Equal("06.09.2026",
            RepairPolicy.StampAfterRepair(ModUpdateMechanism.GitHubReleases, null, "06.09.2026", null));
        // No trustworthy label → leave the record alone.
        Assert.Null(RepairPolicy.StampAfterRepair(ModUpdateMechanism.GitHubReleases, null, null, null));
    }

    [Fact]
    public void AnIntactWolRepairStampsOnlyWhatWasDetected()
    {
        Assert.Equal("1.2.0e", RepairPolicy.StampAfterRepair(ModUpdateMechanism.WolPatcher, null, "x", "1.2.0e"));
        Assert.Null(RepairPolicy.StampAfterRepair(ModUpdateMechanism.WolPatcher, null, "x", null));
    }

    // ---- the result stays on screen -------------------------------------------

    /// <summary>
    /// The re-check after a repair wrote "Up to date" over what the repair did, so the player
    /// never saw it. The result stands — unless the re-check has something to offer, which is
    /// the next thing to act on.
    /// </summary>
    [Fact]
    public void TheRepairResultStaysUnlessTheRecheckOffersSomething()
    {
        Assert.True(RepairPolicy.KeepResultAfterRecheck("✓ Repaired 1 file", pendingDownloads: 0, updateOffered: false));
        Assert.False(RepairPolicy.KeepResultAfterRecheck("✓ Repaired 1 file", pendingDownloads: 3, updateOffered: false));
        Assert.False(RepairPolicy.KeepResultAfterRecheck("✓ Repaired 1 file", pendingDownloads: 0, updateOffered: true));
    }

    [Fact]
    public void AFailedRepairHasNoResultToRestate()
    {
        Assert.False(RepairPolicy.KeepResultAfterRecheck(null, 0, false));
        Assert.False(RepairPolicy.KeepResultAfterRecheck("  ", 0, false));
    }
}
