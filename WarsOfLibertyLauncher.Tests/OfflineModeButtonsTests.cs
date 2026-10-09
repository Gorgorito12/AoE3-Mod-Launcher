using WarsOfLibertyLauncher.Controls;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Going offline greys the three multiplayer actions that need the network; coming back must
/// give every one of them back.
///
/// <para><b>Why this needs a test.</b> The online branch of <c>SetOfflineMode</c> re-enabled
/// only Refresh and left Sign in and "+ Create room" to "the session logic" — which never
/// enables them: <c>RefreshFromSession</c> only re-applies the OFFLINE state. "+ Create room"
/// had exactly one writer of <c>IsEnabled</c> in the whole tab, and it wrote false, so a single
/// network blip disabled it for the rest of the session. The build is green and nothing throws;
/// the button is simply grey with no tooltip saying why.</para>
/// </summary>
[Collection("wpf-and-language")]
public class OfflineModeButtonsTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_GoingBackOnlineGivesBackEveryButtonOfflineTookAway()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // No Attach: the buttons' never-offline state is what the XAML gives them, and no
            // session means nothing else can have touched them.
            var tab = new MultiplayerTab();
            var before = State(tab);

            // Twice, because the first round trip is what used to leave them dead, and a second
            // must not depend on anything the first one set up.
            for (var round = 0; round < 2; round++)
            {
                tab.SetOfflineMode(true, "needs internet", "offline");
                Assert.False(tab.SignInButton.IsEnabled);
                Assert.False(tab.RefreshButton.IsEnabled);
                Assert.False(tab.CreateRoomButton.IsEnabled);

                tab.SetOfflineMode(false, "needs internet", "offline");
                Assert.Equal(before, State(tab));
            }

            Assert.True(tab.SignInButton.IsEnabled);
            Assert.True(tab.CreateRoomButton.IsEnabled);
        });
        Assert.Null(error);
    }

    private static (bool SignIn, bool Refresh, bool Create, bool EmptyCreate) State(MultiplayerTab tab)
        => (tab.SignInButton.IsEnabled, tab.RefreshButton.IsEnabled,
            tab.CreateRoomButton.IsEnabled, tab.EmptyCreateButton.IsEnabled);
}
