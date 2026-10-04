using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which <c>My Games</c> folder a multiplayer match is READ from
/// (<see cref="UserDataService.ResolveMatchFolderName"/>).
///
/// Every mod shares the rating ladder, the base game included, and a match scores only when
/// the launcher can read who won from the recording. The base game's matches used to come back
/// "no result" every single time because <see cref="UserDataService.ResolveFolderName"/> answers
/// nothing for it — so the match path got its own, narrower door.
///
/// <para><b>The guard is the point:</b> <c>ResolveFolderName</c> must KEEP answering nothing for
/// the base game, because that empty answer is what keeps backup/restore, settings sharing, the
/// user-data seed and the recording purge out of the player's own base-game folder. A "fix" that
/// widened <c>ResolveFolderName</c> instead would pass the first test below and fail the last.</para>
/// </summary>
public class MatchFolderTests
{
    private static ModProfile Stock() => ModRegistry.All.Single(p => p.IsStockGame);

    [Fact]
    public void TheBaseGamesMatchesAreReadFromTheVanillaFolder()
    {
        Assert.Equal(
            UserDataService.VanillaFolderName,
            UserDataService.ResolveMatchFolderName(Stock(), new LauncherConfig()));
    }

    [Fact]
    public void AModReadsItsMatchesFromItsOwnFolder_TheSameAnswerAsEverythingElse()
    {
        var mod = new ModProfile { Id = "some-mod", DisplayName = "Some Mod", UserDataFolder = "Some Mod" };
        var config = new LauncherConfig();

        Assert.Equal("Some Mod", UserDataService.ResolveMatchFolderName(mod, config));
        Assert.Equal(
            UserDataService.ResolveFolderName(mod, config),
            UserDataService.ResolveMatchFolderName(mod, config));
    }

    [Fact]
    public void WarsOfLibertyIsUnchanged()
    {
        var wol = ModRegistry.Find("wol")!;
        var config = new LauncherConfig();
        Assert.Equal(
            UserDataService.ResolveFolderName(wol, config),
            UserDataService.ResolveMatchFolderName(wol, config));
    }

    // THE ONE THAT MATTERS. If this ever returns the vanilla folder, the backup, settings
    // sharing, user-data seed and recording purge all start reaching into the player's own
    // base-game saves.
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheUserDataFolderOfTheBaseGameStaysEmpty()
    {
        Assert.Equal("", UserDataService.ResolveFolderName(Stock(), new LauncherConfig()));
    }

    [Fact]
    public void NoProfileMeansNoFolder()
    {
        Assert.Equal("", UserDataService.ResolveMatchFolderName(null!, new LauncherConfig()));
    }
}
