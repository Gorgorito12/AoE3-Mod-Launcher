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
    /// A ping cell is updated IN PLACE: the same TextBlock across every 3-s tick, with the text and
    /// colour of the latest reading. It was cleared and rebuilt for every room on every tick.
    /// </summary>
    [Fact]
    public void APingCellKeepsItsTextBlockAcrossTicks()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var fill = typeof(MultiplayerTab).GetMethod("FillPingCell", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cell = new StackPanel();

            fill.Invoke(tab, new object?[] { cell, null });
            var first = Assert.IsType<TextBlock>(Assert.Single(cell.Children));
            Assert.Equal("—", first.Text);

            fill.Invoke(tab, new object?[] { cell, 42.0 });
            Assert.Same(first, Assert.Single(cell.Children));
            Assert.Equal("42 ms", first.Text);
            var good = first.Foreground;

            fill.Invoke(tab, new object?[] { cell, 250.0 });
            Assert.Same(first, Assert.Single(cell.Children));
            Assert.Equal("250 ms", first.Text);
            Assert.NotSame(good, first.Foreground);

            fill.Invoke(tab, new object?[] { cell, null });
            Assert.Same(first, Assert.Single(cell.Children));
            Assert.Equal("—", first.Text);
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
}
