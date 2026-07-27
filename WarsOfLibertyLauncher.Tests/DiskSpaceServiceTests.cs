using System;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="DiskSpaceService"/> — the conservative disk-space
/// estimate + free-space probe behind the "not enough space" warning. The
/// estimate is pure math (clone bytes + a fixed allowance); the free-space probe
/// must never throw and must return -1 (== "unknown, don't warn") on a bad path.
/// </summary>
public class DiskSpaceServiceTests
{
    // ---------------- Check: two volumes at once ----------------

    private const string OnD = @"D:\Games\Wars of Liberty";
    private const string OnC = @"C:\Users\x\AppData\Local\Temp";

    /// <summary>Free space by drive letter, so these tests don't depend on the machine.</summary>
    private static Func<string?, long> Free(long cFree, long dFree) => path =>
        path != null && path.StartsWith("D", StringComparison.OrdinalIgnoreCase) ? dFree : cFree;

    [Fact]
    public void Check_WithRoomOnBothVolumes_SaysNothing()
    {
        Assert.Null(DiskSpaceService.Check(
            OnD, 2 * DiskSpaceService.GiB, OnC, 3 * DiskSpaceService.GiB,
            Free(cFree: 50 * DiskSpaceService.GiB, dFree: 50 * DiskSpaceService.GiB)));
    }

    [Fact]
    public void Check_NamesTheTempVolumeWhenThatIsTheShortOne()
    {
        // The case that had no warning at all before: game on a roomy D:, %TEMP% on a full C:,
        // and the payload actually lands on C:.
        var shortfall = DiskSpaceService.Check(
            OnD, 2 * DiskSpaceService.GiB, OnC, 3 * DiskSpaceService.GiB,
            Free(cFree: 1 * DiskSpaceService.GiB, dFree: 500 * DiskSpaceService.GiB));

        Assert.NotNull(shortfall);
        Assert.StartsWith("C", shortfall!.Drive, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3 * DiskSpaceService.GiB, shortfall.RequiredBytes);
        Assert.Equal(1 * DiskSpaceService.GiB, shortfall.FreeBytes);
    }

    [Fact]
    public void Check_NamesTheDestinationVolumeWhenThatIsTheShortOne()
    {
        var shortfall = DiskSpaceService.Check(
            OnD, 40 * DiskSpaceService.GiB, OnC, 3 * DiskSpaceService.GiB,
            Free(cFree: 100 * DiskSpaceService.GiB, dFree: 5 * DiskSpaceService.GiB));

        Assert.NotNull(shortfall);
        Assert.StartsWith("D", shortfall!.Drive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_OnOneVolume_AddsBothRequirementsInsteadOfCheckingThemApart()
    {
        // The reason this is one function. 4 GB free, 3 GB needed for the download and 3 GB for
        // the result: each check passes on its own, and the disk still fills up.
        var sameVolumeTemp = @"D:\Temp";

        var shortfall = DiskSpaceService.Check(
            OnD, 3 * DiskSpaceService.GiB, sameVolumeTemp, 3 * DiskSpaceService.GiB,
            Free(cFree: 0, dFree: 4 * DiskSpaceService.GiB));

        Assert.NotNull(shortfall);
        Assert.Equal(6 * DiskSpaceService.GiB, shortfall!.RequiredBytes);
    }

    [Fact]
    public void Check_WithAnUnreadableVolume_StaysQuiet()
    {
        // Same rule IsShort already follows: an unknown reading is never a warning.
        Assert.Null(DiskSpaceService.Check(
            OnD, 40 * DiskSpaceService.GiB, OnC, 40 * DiskSpaceService.GiB,
            _ => -1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Check_IgnoresAPathItWasNotGiven(string? missing)
    {
        // Callers that only write to one volume pass null for the other; that must not warn.
        Assert.Null(DiskSpaceService.Check(
            missing, 40 * DiskSpaceService.GiB, OnC, 1 * DiskSpaceService.GiB,
            Free(cFree: 50 * DiskSpaceService.GiB, dFree: 0)));
    }

    [Fact]
    public void Check_IgnoresARequirementOfZero()
    {
        Assert.Null(DiskSpaceService.Check(
            OnD, 0, OnC, 0, Free(cFree: 0, dFree: 0)));
    }

    [Fact]
    public void Check_WithOnlyOnePath_IsTheSelfUpdateShape()
    {
        // The launcher self-update writes to ONE volume (beside the running exe) and passes null
        // for the other. It must still warn — the earlier version of Check that required both
        // paths would have made that call silently do nothing.
        var shortfall = DiskSpaceService.Check(
            @"C:\Users\x\Downloads\Aoe3ModLauncher.exe", 400 * DiskSpaceService.MiB,
            tempPath: null, tempRequired: 0,
            Free(cFree: 100 * DiskSpaceService.MiB, dFree: 0));

        Assert.NotNull(shortfall);
        Assert.StartsWith("C", shortfall!.Drive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_UnknownDestinationDoesNotHideAShortTemp()
    {
        // A regression worth pinning: with the destination unmeasurable, the ?? chain must fall
        // through to the temp volume rather than reporting "all fine".
        var shortfall = DiskSpaceService.Check(
            OnD, 40 * DiskSpaceService.GiB, OnC, 3 * DiskSpaceService.GiB,
            path => path != null && path.StartsWith("D", StringComparison.OrdinalIgnoreCase)
                ? -1
                : 1 * DiskSpaceService.GiB);

        Assert.NotNull(shortfall);
        Assert.StartsWith("C", shortfall!.Drive, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- the per-flow multipliers ----------------
    //
    // These are constants, so the point isn't arithmetic — it's that a factor of 0 would make
    // `required` zero, which Check reads as "nothing to check" and silently disables the warning
    // for that flow. Nothing else would fail: the build stays green and the app looks fine.

    [Fact]
    public void EveryFlowFactor_IsPositive_SoNoCheckIsSilentlyDisabled()
    {
        Assert.True(DiskSpaceService.DeltaTempFactor > 0);
        Assert.True(DiskSpaceService.DeltaInstallFactor > 0);
        Assert.True(DiskSpaceService.TranslationInstallFactor > 0);
        Assert.True(DiskSpaceService.AddonFactor > 0);
        Assert.True(DiskSpaceService.RadminInstallAllowanceBytes > 0);
        Assert.True(DiskSpaceService.OverlayHeadroomBytes > 0);
    }

    [Fact]
    public void DeltaChargesTempMoreThanTheInstall()
    {
        // Temp carries the patch zip AND the rollback backup of every overwritten file; the
        // install only receives the extracted result. Flipping these would under-charge the
        // volume that actually runs out first.
        Assert.True(DiskSpaceService.DeltaTempFactor > DiskSpaceService.DeltaInstallFactor);
    }

    [Fact]
    public void DeltaShape_WithATightTempVolume_NamesTemp()
    {
        // The delta's real call shape: install charged on one volume, %TEMP% on another.
        const long compressed = 200 * DiskSpaceService.MiB;

        var shortfall = DiskSpaceService.Check(
            OnD, compressed * DiskSpaceService.DeltaInstallFactor,
            OnC, compressed * DiskSpaceService.DeltaTempFactor,
            Free(cFree: 100 * DiskSpaceService.MiB, dFree: 500 * DiskSpaceService.GiB));

        Assert.NotNull(shortfall);
        Assert.StartsWith("C", shortfall!.Drive, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EstimateInstallRequirement_AddsFixedAllowanceToCloneBytes()
    {
        long clone = 10 * DiskSpaceService.GiB;
        Assert.Equal(clone + DiskSpaceService.InstallExtraAllowanceBytes,
            DiskSpaceService.EstimateInstallRequirement(clone));
    }

    [Fact]
    public void EstimateInstallRequirement_ZeroOrNegativeClone_IsJustTheAllowance()
    {
        Assert.Equal(DiskSpaceService.InstallExtraAllowanceBytes,
            DiskSpaceService.EstimateInstallRequirement(0));
        Assert.Equal(DiskSpaceService.InstallExtraAllowanceBytes,
            DiskSpaceService.EstimateInstallRequirement(-123));
    }

    [Fact]
    public void SafeFreeSpace_ValidPath_IsPositive()
    {
        var free = DiskSpaceService.SafeFreeSpace(Path.GetTempPath());
        Assert.True(free > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"\\?\nonsense::path")]
    public void SafeFreeSpace_BadPath_ReturnsMinusOne_NeverThrows(string? path)
    {
        Assert.Equal(-1, DiskSpaceService.SafeFreeSpace(path));
    }

    [Fact]
    public void IsShort_OnlyTrueForRealReadingBelowRequirement()
    {
        Assert.True(DiskSpaceService.IsShort(1 * DiskSpaceService.GiB, 5 * DiskSpaceService.GiB));
        Assert.False(DiskSpaceService.IsShort(9 * DiskSpaceService.GiB, 5 * DiskSpaceService.GiB));
        // Unknown reading (-1) must never be "short" — we don't cry wolf.
        Assert.False(DiskSpaceService.IsShort(-1, 5 * DiskSpaceService.GiB));
    }

    [Fact]
    public void FormatBytes_IsHumanReadable()
    {
        // Unknown → "?". The GB rendering is culture-dependent on purpose (a
        // Spanish user sees "10,0 GB"), so assert unit + magnitude, not the exact
        // decimal separator.
        Assert.Equal("?", DiskSpaceService.FormatBytes(-1));
        var gb = DiskSpaceService.FormatBytes(10 * DiskSpaceService.GiB);
        Assert.Contains("GB", gb);
        Assert.StartsWith("10", gb);
    }
}
