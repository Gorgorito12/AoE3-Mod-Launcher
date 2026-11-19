using WarsOfLibertyLauncher.Services.Repair;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>"Download only what changed" as one rule for repair and update.</summary>
public class IncrementalDownloadsTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_APatchOnlyReleaseStillPatchesWhenOff()
    {
        Assert.True(IncrementalDownloads.MayPatch(enabled: false, targetHasFullZip: false));
        Assert.False(IncrementalDownloads.MayPatch(enabled: false, targetHasFullZip: true));
        Assert.True(IncrementalDownloads.MayPatch(enabled: true, targetHasFullZip: true));
    }

    [Fact]
    public void TheVerifyTextSaysWhatRepairWillDo()
    {
        Assert.Equal("VerifyRelayGranularBody", IncrementalDownloads.VerifyRelayBodyKey(true));
        Assert.Equal("DlgVerifyRepairBody", IncrementalDownloads.VerifyRelayBodyKey(false));
        Assert.True(IncrementalDownloads.MayRestoreFiles(true));
        Assert.False(IncrementalDownloads.MayRestoreFiles(false));
    }
}
