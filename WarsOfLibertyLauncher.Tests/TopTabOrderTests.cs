using System.Text.Json;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The nav bar's order and the tab that opens on launch are two settings (2026-11-13). The bar
/// used to be drawn Multiplayer · Library whatever the config said; Library is first now.
/// </summary>
public class TopTabOrderTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheLibraryIsFirstInTheBarByDefault()
    {
        var config = new LauncherConfig();
        Assert.Equal(new[] { "library", "multiplayer", "workshop" }, config.GetTopTabBarOrder());
        Assert.Equal("library", config.GetTopTabOrder()[0]);
    }

    [Fact]
    public void AConfigWrittenBeforeTheSettingGetsTheDefault()
    {
        // A real older config: it has topTabOrder (Multiplayer opens) and no topTabBarOrder.
        var config = JsonSerializer.Deserialize<LauncherConfig>(
            """{ "topTabOrder": ["multiplayer", "library", "workshop"] }""")!;
        Assert.Equal("library", config.GetTopTabBarOrder()[0]);
        Assert.Equal("multiplayer", config.GetTopTabOrder()[0]);
    }

    [Fact]
    public void TheTwoSettingsAreIndependent()
    {
        var config = new LauncherConfig
        {
            TopTabOrder = new[] { "library", "workshop", "multiplayer" },
            TopTabBarOrder = new[] { "multiplayer", "library", "workshop" },
        };
        Assert.Equal("library", config.GetTopTabOrder()[0]);
        Assert.Equal("multiplayer", config.GetTopTabBarOrder()[0]);
    }

    [Fact]
    public void AHandEditedBarOrderIsSanitisedToTheWholeSet()
    {
        var config = new LauncherConfig { TopTabBarOrder = new[] { " Multiplayer ", "bogus", "multiplayer", "" } };
        Assert.Equal(new[] { "multiplayer", "library", "workshop" }, config.GetTopTabBarOrder());

        config.TopTabBarOrder = null!;
        Assert.Equal(new[] { "library", "multiplayer", "workshop" }, config.GetTopTabBarOrder());
    }
}
