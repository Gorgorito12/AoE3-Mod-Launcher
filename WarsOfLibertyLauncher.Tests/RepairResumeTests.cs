using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the ticket an elevated relaunch carries to finish a repair (<see cref="RepairResume"/>).
/// A command line is text any program can write, so the REFUSALS are the point: only a well-formed
/// mod id ever comes back, and a malformed one is not a ticket at all.
/// </summary>
public class RepairResumeTests
{
    [Theory]
    [InlineData("wol", false, "--repair-now=wol")]
    [InlineData("improvement-mod", true, "--repair-now=improvement-mod:update")]
    public void ATicketRoundTrips(string id, bool update, string arg)
    {
        Assert.Equal(arg, RepairResume.Build(id, update));
        var t = RepairResume.TryParse(new[] { "--minimized", arg });
        Assert.NotNull(t);
        Assert.Equal(id, t!.ModId);
        Assert.Equal(update, t.Update);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("\"quoted\"")]
    [InlineData(@"..\evil")]
    [InlineData("-leading-dash")]
    public void AnIdThatCannotRoundTripBuildsNothing(string id)
        => Assert.Null(RepairResume.Build(id, update: false));

    [Theory]
    [InlineData("--repair-now")]
    [InlineData("--repair-now=")]
    [InlineData("--repair-now=:update")]
    [InlineData("--repair-now=a b")]
    [InlineData("--repair-now=../../x")]
    [InlineData("--repair-nowwol")]
    public void AMalformedTicketIsNoTicket(string arg)
        => Assert.Null(RepairResume.TryParse(new[] { arg }));

    [Fact]
    public void AnyTicketShapeKeepsTheStartupAutoUpdateFromRestartingUnderIt()
    {
        Assert.True(RepairResume.IsPresent(new[] { "--repair-now=wol" }));
        Assert.True(RepairResume.IsPresent(new[] { "--repair-now=" }));
        Assert.False(RepairResume.IsPresent(new[] { "--update-now", "--minimized" }));
    }

    /// <summary>
    /// The fixed 3 GiB guess could never warn for the one mod big enough to fill a disk: a
    /// multi-part payload briefly exists twice (parts + the concatenated zip).
    /// </summary>
    [Fact]
    public void TheRepairTempRequirementFollowsThePayload()
    {
        const long gib = DiskSpaceService.GiB;
        Assert.Equal(DiskSpaceService.RepairAllowanceBytes, DiskSpaceService.RepairTempRequirement(-1, true));
        Assert.Equal(DiskSpaceService.RepairAllowanceBytes, DiskSpaceService.RepairTempRequirement(0, false));
        Assert.Equal(10 * gib + DiskSpaceService.RepairTempHeadroomBytes,
            DiskSpaceService.RepairTempRequirement(5 * gib, directExtract: true));
        Assert.Equal(15 * gib + DiskSpaceService.RepairTempHeadroomBytes,
            DiskSpaceService.RepairTempRequirement(5 * gib, directExtract: false));
        Assert.True(DiskSpaceService.RepairTempRequirement(100L * 1024 * 1024, false) < DiskSpaceService.RepairAllowanceBytes);
    }
}
