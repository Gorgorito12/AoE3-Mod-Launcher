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
