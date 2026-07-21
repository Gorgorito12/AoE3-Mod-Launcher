using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Workshop's "my mods" collection is a VISIBILITY list and nothing more.
///
/// These exist because the removal confirmation dialog makes a promise in its
/// copy — "no file is deleted, nothing is downloaded again if you add it back" —
/// and that promise is only true as long as <see cref="LauncherConfig.RemoveUserMod"/>
/// keeps its hands off <see cref="LauncherConfig.Mods"/>. If someone ever made
/// removal also clear the per-mod state, the launcher would start lying to the
/// user at exactly the moment they were being asked to trust it. That is the
/// regression these pin.
/// </summary>
public class UserModCollectionTests
{
    private const string ModId = "improvement-mod";

    private static LauncherConfig ConfigWithInstalledMod()
    {
        var config = new LauncherConfig();
        config.AddUserMod(ModId);

        var state = config.GetState(ModId);
        state.InstallPath = @"D:\Games\Improvement Mod";
        state.LastKnownVersion = "19.07.2026";
        state.ActiveTranslationId = "es";
        return config;
    }

    [Fact]
    public void Remove_DropsVisibility_ButKeepsEveryPerModField()
    {
        var config = ConfigWithInstalledMod();

        config.RemoveUserMod(ModId);

        Assert.False(config.IsUserMod(ModId));

        // The install is still fully described — this is what makes "your files
        // stay where they are" true rather than wishful.
        var state = config.GetState(ModId);
        Assert.Equal(@"D:\Games\Improvement Mod", state.InstallPath);
        Assert.Equal("19.07.2026", state.LastKnownVersion);
        Assert.Equal("es", state.ActiveTranslationId);
    }

    [Fact]
    public void AddRemoveAdd_RoundTrips_WithoutLosingTheInstall()
    {
        var config = ConfigWithInstalledMod();

        config.RemoveUserMod(ModId);
        config.AddUserMod(ModId);

        Assert.True(config.IsUserMod(ModId));
        Assert.Equal(@"D:\Games\Improvement Mod", config.GetState(ModId).InstallPath);
        Assert.Equal("19.07.2026", config.GetState(ModId).LastKnownVersion);
    }

    [Fact]
    public void Add_IsIdempotent_NoDuplicateIds()
    {
        var config = new LauncherConfig();

        config.AddUserMod(ModId);
        config.AddUserMod(ModId);
        config.AddUserMod(ModId.ToUpperInvariant());

        Assert.Single(config.UserModIds);
    }

    /// <summary>
    /// Built-ins are always implicitly in the collection — the launcher needs
    /// something to fall back on if the user empties it — so the Workshop shows
    /// them as a disabled "Built-in" pill and never raises the remove event.
    /// This is the defensive backstop underneath that UI rule.
    /// </summary>
    [Fact]
    public void BuiltIn_CannotBeRemoved()
    {
        var config = new LauncherConfig();
        Assert.True(config.IsUserMod("wol"));

        config.RemoveUserMod("wol");

        Assert.True(config.IsUserMod("wol"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIds_AreIgnoredByBothOperations(string? id)
    {
        var config = new LauncherConfig();

        config.AddUserMod(id);
        Assert.Empty(config.UserModIds);

        config.AddUserMod(ModId);
        config.RemoveUserMod(id);
        Assert.Single(config.UserModIds);
    }
}
