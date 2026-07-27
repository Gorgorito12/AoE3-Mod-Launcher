using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins which mods may appear in the "import settings from…" list
/// (<see cref="GameSettingsStore.CanImportFrom"/>).
///
/// Every rule here exists to keep a wrong choice off the menu rather than to explain it
/// afterwards — the base game manages none of its own files, a mod with no user-data folder has
/// no settings to give, and offering the mod you are importing INTO is a no-op the player could
/// pick by accident.
/// </summary>
public class GameSettingsCandidateTests
{
    private const string Target = "napoleonic-era";

    [Fact]
    public void AnInstalledModWithItsOwnDataFolderQualifies()
    {
        Assert.True(GameSettingsStore.CanImportFrom(
            "wol", isStockGame: false, userDataFolder: "Wars of Liberty", installed: true, Target));
    }

    [Fact]
    public void TheModBeingImportedIntoIsNeverOffered()
    {
        Assert.False(GameSettingsStore.CanImportFrom(
            Target, isStockGame: false, userDataFolder: "Napoleonic Era Beta 2", installed: true, Target));
    }

    [Fact]
    public void CaseDoesNotLetTheSameModThrough()
    {
        Assert.False(GameSettingsStore.CanImportFrom(
            "Napoleonic-Era", isStockGame: false, userDataFolder: "x", installed: true, Target));
    }

    [Fact]
    public void TheBaseGameIsNeverOffered()
    {
        // Detect-only: the launcher never installs, updates or writes to the player's own copy.
        Assert.False(GameSettingsStore.CanImportFrom(
            "aoe3-tad", isStockGame: true, userDataFolder: "Age of Empires 3", installed: true, Target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AModWithNoUserDataFolderHasNothingToGive(string? folder)
    {
        Assert.False(GameSettingsStore.CanImportFrom(
            "some-mod", isStockGame: false, userDataFolder: folder, installed: true, Target));
    }

    [Fact]
    public void AModThatIsNotInstalledIsNotOffered()
    {
        Assert.False(GameSettingsStore.CanImportFrom(
            "improvement-mod", isStockGame: false, userDataFolder: "AoE3 Improvement Mod",
            installed: false, Target));
    }

    [Fact]
    public void AnEmptyIdIsNotOffered()
    {
        Assert.False(GameSettingsStore.CanImportFrom(
            "", isStockGame: false, userDataFolder: "x", installed: true, Target));
    }
}
