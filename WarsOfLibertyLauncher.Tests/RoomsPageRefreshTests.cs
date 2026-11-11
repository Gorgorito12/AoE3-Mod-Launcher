using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Rooms page's background work — the 5-s list poll and the 3-s cell tick — pauses behind an
/// open room window and runs only on the Rooms page. Both used to run behind the window the player
/// was looking at, and the cell tick on every subtab.
/// </summary>
[Collection("wpf-and-language")]
public class RoomsPageRefreshTests
{
    [Theory]
    // signedIn, onRooms, roomWindowOpen, launcherActive, expected
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, true, true)]     // the player came back to the launcher
    [InlineData(true, true, true, false, false)]   // THE ONE THAT MATTERS: behind the room window
    [InlineData(true, false, false, true, false)]  // another subtab
    [InlineData(false, true, false, true, false)]  // not signed in
    public void TheListPollsOnlyWhereSomebodyCanSeeIt(bool signedIn, bool onRooms, bool roomWindowOpen,
        bool launcherActive, bool expected)
        => Assert.Equal(expected, RoomsPageRefresh.ShouldPollList(signedIn, onRooms, roomWindowOpen, launcherActive));

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    public void TheCellsTickOnlyOnTheRoomsPage(bool onRooms, bool roomWindowOpen, bool launcherActive, bool expected)
        => Assert.Equal(expected, RoomsPageRefresh.ShouldTickCells(onRooms, roomWindowOpen, launcherActive));

    /// <summary>A room announced on the live channel is asked for at once — only where the poll itself
    /// would run, and never with the tab hidden (that would spend the per-IP budget for nobody).</summary>
    [Theory]
    // signedIn, onRooms, tabVisible, roomWindowOpen, launcherActive, expected
    [InlineData(true, true, true, false, false, true)]   // THE ONE THAT MATTERS: on the list, it comes now
    [InlineData(true, true, false, false, true, false)]  // tab hidden
    [InlineData(true, false, true, false, true, false)]  // another subtab
    [InlineData(true, true, true, true, false, false)]   // behind the room window
    [InlineData(false, true, true, false, true, false)]  // signed out
    public void AnAnnouncedRoomIsFetchedOnlyWhereTheListIsShown(bool signedIn, bool onRooms, bool tabVisible,
        bool roomWindowOpen, bool launcherActive, bool expected)
        => Assert.Equal(expected, RoomsPageRefresh.ShouldRefreshOnAnnouncement(signedIn, onRooms, tabVisible, roomWindowOpen, launcherActive));

    /// <summary>The frame handler queues the request instead of fetching per frame: a burst is one.</summary>
    [Fact]
    public void ABurstOfAnnouncementsIsOneRequest()
    {
        var source = File.ReadAllText(Path.Combine(LauncherDir(), "Controls", "MultiplayerTab.xaml.cs"));
        var body = source[source.IndexOf("private void QueueRoomsRefreshFromAnnouncement()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("private void HandleLobbyCreatedFrame", StringComparison.Ordinal)];
        Assert.Contains("if (!_roomsAnnounceTimer.IsEnabled) _roomsAnnounceTimer.Start();", body);
        Assert.Contains("RoomsPageRefresh.AnnouncementDebounceMs", body);
        Assert.True(RoomsPageRefresh.AnnouncementDebounceMs is > 0 and <= 1000);
    }

    private static string LauncherDir([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "WarsOfLibertyLauncher"));

    /// <summary>Both timers ask the rule, and closing the room window brings the list current.</summary>
    [Fact]
    public void BothTimersGoThroughTheRuleAndClosingTheRoomRefreshesTheList()
    {
        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("Controls/MultiplayerTab.xaml.cs"));

        var ping = Slice(src, "_roomsPingTimer.Tick +=", "});");
        Assert.Contains("_activeSubtab != Subtab.Rooms", ping);
        Assert.Contains("RoomsPageRefresh.ShouldTickCells", ping);
        Assert.True(ping.IndexOf("UpdateRoomsUpdatedLabel", StringComparison.Ordinal)
                    < ping.IndexOf("ShouldTickCells", StringComparison.Ordinal),
            "the 'updated X ago' label must keep ticking while the cells are paused");

        Assert.Contains("RoomsPageRefresh.ShouldPollList", Slice(src, "_roomsListTimer.Tick +=", "});"));

        var closed = Slice(src, "private void HandleLobbyWindowClosed(", "\n    private ");
        Assert.Contains("RefreshRoomsListAsync(quiet: true)", closed);
        Assert.True(closed.IndexOf("if (sample)", StringComparison.Ordinal)
                    < closed.IndexOf("RefreshRoomsListAsync", StringComparison.Ordinal),
            "a sample room is not a real one: it must not refresh the real list");
    }

    /// <summary>
    /// The viewer's own ping is said ONCE, above the list, and updated in place: the same
    /// TextBlock across every tick, with the text and colour of the latest reading. With no
    /// reading it hides rather than showing a dash.
    /// </summary>
    [Fact]
    public void MyPingIsUpdatedInPlaceAndHiddenWithoutAMeasure()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var text = new TextBlock();

            MultiplayerTab.FillMyPing(text, null);
            Assert.Equal(System.Windows.Visibility.Collapsed, text.Visibility);

            MultiplayerTab.FillMyPing(text, 42.0);
            Assert.Equal(System.Windows.Visibility.Visible, text.Visibility);
            Assert.Equal(WarsOfLibertyLauncher.Localization.Strings.Format("RmMyPing", 42), text.Text);
            Assert.Same(System.Windows.Application.Current.FindResource("RmPingGood"), text.Foreground);

            MultiplayerTab.FillMyPing(text, 250.0);
            Assert.Equal(WarsOfLibertyLauncher.Localization.Strings.Format("RmMyPing", 250), text.Text);
            Assert.Same(System.Windows.Application.Current.FindResource("RmPingSlow"), text.Foreground);
        });
        Assert.Null(error);
    }


    private static string Slice(string src, string start, string end)
    {
        var a = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(a >= 0, $"'{start}' not found");
        var b = src.IndexOf(end, a + start.Length, StringComparison.Ordinal);
        Assert.True(b > a, $"'{end}' not found after '{start}'");
        return src.Substring(a, b - a);
    }

    /// <summary>With the live channel announcing every change the poll is a fallback and slows to 15 s;
    /// without it (an older server, a dropped socket) it stays the 5 s it always was.</summary>
    [Fact]
    public void ThePollSlowsOnlyWhileChangesArriveLive()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), Services.Multiplayer.RoomsPageRefresh.PollInterval(liveChangesSeen: false));
        Assert.Equal(TimeSpan.FromSeconds(15), Services.Multiplayer.RoomsPageRefresh.PollInterval(liveChangesSeen: true));
    }
}
