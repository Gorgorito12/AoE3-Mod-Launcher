using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="InstallIdentity.ForReoverlay"/> — what a repair or full update keeps when it
/// rewrites an install in place. Before it, a re-overlay derived the identity afresh from a label
/// no repair passes: repairing a COPY re-pointed the primary's shortcuts and Add/Remove key, and
/// every re-overlay stamped <c>clonedAoe3:false</c>, so uninstall left the clone on disk.
/// The refusals (a foreign manifest must never lend its identity) are the cases that matter.
/// </summary>
public class InstallIdentityTests
{
    private const string WolGuid = "{EB448764-CABB-4766-8055-495AEA292020}_is1";

    private static ModProfile Wol() => new()
    {
        Id = "wol", DisplayName = "Wars of Liberty", ProductGuid = WolGuid,
    };

    private static InstallManifest Manifest(string modId, string guid, string app, bool cloned = true,
        string? source = @"C:\Games\Age Of Empires 3") => new()
    {
        ModId = modId, ProductGuid = guid, AppName = app, ClonedAoe3 = cloned, Aoe3SourcePath = source,
    };

    [Fact]
    public void NoPreviousManifest_IsAFreshPrimaryAndNeverAClone()
    {
        var id = InstallIdentity.ForReoverlay(Wol(), previous: null, callerLabel: null);
        Assert.Equal(WolGuid, id.ProductGuid);
        Assert.Equal("Wars of Liberty", id.AppName);
        Assert.False(id.ClonedAoe3);
        Assert.Null(id.Aoe3SourcePath);
        Assert.False(id.Carried);
    }

    [Fact]
    public void RepairingTheCloneKeepsItsCloneStatusAndSource()
    {
        // B1: this used to come out clonedAoe3:false, and uninstall then left the clone behind.
        var id = InstallIdentity.ForReoverlay(Wol(), Manifest("wol", WolGuid, "Wars of Liberty"), null);
        Assert.True(id.ClonedAoe3);
        Assert.Equal(@"C:\Games\Age Of Empires 3", id.Aoe3SourcePath);
        Assert.True(id.Carried);
    }

    [Fact]
    public void RepairingACopyKeepsTheCopysOwnKeyAndName_NotThePrimarys()
    {
        // B2: the copy's own identity must survive, or its repair hijacks the primary's entry.
        var copyGuid = WolGuid + "_Wars of Liberty (4)";
        var id = InstallIdentity.ForReoverlay(Wol(), Manifest("wol", copyGuid, "Wars of Liberty (4)"), null);
        Assert.Equal(copyGuid, id.ProductGuid);
        Assert.Equal("Wars of Liberty (4)", id.AppName);
    }

    [Fact]
    public void AForeignManifestLendsNothing()
    {
        var id = InstallIdentity.ForReoverlay(Wol(),
            Manifest("napoleonic-era", "napoleonic-era_launcher", "Napoleonic Era"), null);
        Assert.Equal(WolGuid, id.ProductGuid);
        Assert.False(id.ClonedAoe3);
        Assert.Null(id.Aoe3SourcePath);
        Assert.False(id.Carried);
    }

    [Fact]
    public void ARenamedModStillOwnsTheManifestItsOldIdWrote()
    {
        var kb = new ModProfile
        {
            Id = "knights-and-barbarians-remastered", DisplayName = "Knights and Barbarians",
            ProductGuid = "knights-and-barbarians_launcher",
            PreviousIds = new[] { "knights-and-barbarians" },
        };
        var id = InstallIdentity.ForReoverlay(kb,
            Manifest("knights-and-barbarians", "knights-and-barbarians_launcher", "Knights and Barbarians"), null);
        Assert.True(id.Carried);
        Assert.True(id.ClonedAoe3);
    }

    [Fact]
    public void ALegacyManifestWithNoModIdIsOursOnlyByItsGuid()
    {
        Assert.True(InstallIdentity.ForReoverlay(Wol(), Manifest("", WolGuid + "_Copy", "Copy"), null).Carried);
        Assert.False(InstallIdentity.ForReoverlay(Wol(), Manifest("", "other_launcher", "Other"), null).Carried);
        Assert.False(InstallIdentity.ForReoverlay(Wol(), Manifest("", "", "Wars of Liberty"), null).Carried);
    }

    [Fact]
    public void NamesTravelOnlyAsAPair()
    {
        // A GUID without a name would put shortcuts on one install and the Add/Remove key on another.
        var id = InstallIdentity.ForReoverlay(Wol(), Manifest("wol", "", "Wars of Liberty (4)"), null);
        Assert.Equal(WolGuid, id.ProductGuid);
        Assert.Equal("Wars of Liberty", id.AppName);
        Assert.True(id.ClonedAoe3);   // clone status still carried
    }

    [Fact]
    public void AnExplicitCallerLabelWins()
    {
        var id = InstallIdentity.ForReoverlay(Wol(), Manifest("wol", WolGuid, "Wars of Liberty"), "Wars of Liberty (2)");
        Assert.Equal(WolGuid + "_Wars of Liberty (2)", id.ProductGuid);
        Assert.Equal("Wars of Liberty (2)", id.AppName);
        Assert.True(id.ClonedAoe3);
    }
}
