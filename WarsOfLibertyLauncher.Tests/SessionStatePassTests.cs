using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Opening and creating a room: what runs before the window's first frame, what after, and what
/// the queued session pass may not undo. Source pins where the order IS the fix — none of it runs
/// without a live session, a socket and a window.
/// </summary>
public class SessionStatePassTests
{
    private static string Tab() => File.ReadAllText(UiThreadAttributionTests.LauncherFile("Controls/MultiplayerTab.xaml.cs"));

    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"'{signature}' has moved or been renamed.");
        var next = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        return next > start ? src[start..next] : src[start..];
    }

    // ---------------------------------------------------------------- CoalescedPass

    [Fact]
    public void TheFirstRequestQueuesAndTheRestRideOnIt()
    {
        var pass = new CoalescedPass();
        Assert.True(pass.TryQueue());
        Assert.False(pass.TryQueue());
        Assert.False(pass.TryQueue());
    }

    [Fact]
    public void ARequestDuringTheRunQueuesAnother()
    {
        var pass = new CoalescedPass();
        Assert.True(pass.TryQueue());
        pass.BeginRun();
        Assert.True(pass.TryQueue());
    }

    [Fact]
    public void ConcurrentRequestsQueueExactlyOneRun()
    {
        var pass = new CoalescedPass();
        var queued = 0;
        Parallel.For(0, 10_000, _ => { if (pass.TryQueue()) Interlocked.Increment(ref queued); });
        Assert.Equal(1, queued);
    }

    [Fact]
    public void TheAccountBadgeIsKeyedOnEverythingItIsBuiltFrom()
    {
        var badge = new ShownBadge(BadgeKind.Solo, RankAge.Industrial, 4, RankAge.Colonial, 9);
        Assert.Equal(new AccountBadgeKey(badge, "me", false), new AccountBadgeKey(badge, "me", false));
        Assert.NotEqual(new AccountBadgeKey(badge, "me", false), new AccountBadgeKey(badge, "me", true));
        Assert.NotEqual(new AccountBadgeKey(badge, "me", false), new AccountBadgeKey(badge with { Position = 3 }, "me", false));
        Assert.NotEqual(new AccountBadgeKey(badge, "me", false), new AccountBadgeKey(badge, "other", false));

        var main = File.ReadAllText(UiThreadAttributionTests.LauncherFile("MainWindow.xaml.cs"));
        var chip = main[main.IndexOf("internal void SetAccountChip(", StringComparison.Ordinal)..];
        var compare = chip.IndexOf("_accountBadgeKey != key", StringComparison.Ordinal);
        var build = chip.IndexOf("RankBadge.BuildFor(", StringComparison.Ordinal);
        Assert.True(compare > 0 && build > compare, "SetAccountChip builds a new badge before comparing the key.");
    }

    // ---------------------------------------------------------------- opening the window

    [Fact]
    public void EveryStepOfTheOpenIsTimed()
    {
        var open = Body(Tab(), "private void OpenLobbyWindow()");
        Assert.Contains("MP OpenLobbyWindow: build", open);
        Assert.Contains("\"MP OpenLobbyWindow: paint before Show\"", open);
        Assert.Contains("DiagnosticLog.Time(\"MP OpenLobbyWindow: Show (layout + Loaded)\", w.Show)", open);
    }

    /// <summary>The probes go after the first frame; the roster and the CONNECTION cell before it.</summary>
    [Fact]
    public void TheRoomWindowPaintsBeforeItProbes()
    {
        var open = Body(Tab(), "private void OpenLobbyWindow()");
        var show = open.IndexOf("w.Show)", StringComparison.Ordinal);
        Assert.True(show > 0);
        var roster = open.IndexOf("RenderRoomMembers();", StringComparison.Ordinal);
        var tickEnd = open.IndexOf("_lobbyPingTimer.Start();", StringComparison.Ordinal);
        var ping = open.IndexOf("UpdateLobbyPing();", tickEnd, StringComparison.Ordinal);
        Assert.True(roster > 0 && roster < show, "The roster is no longer painted before Show.");
        Assert.True(ping > tickEnd && ping < show, "The CONNECTION cell is no longer painted before Show.");

        var post = open.IndexOf("DispatcherPriority.Background", show, StringComparison.Ordinal);
        Assert.True(post > show, "The entry probes are no longer posted after Show.");
        var tail = open[post..];
        Assert.Contains("MaybeReportRadminIp();", tail);
        Assert.Contains("MaybeReportInGameName();", tail);
        // And none of them runs inline between the lobby tick and the Show.
        Assert.DoesNotContain("MaybeReportRadminIp();", open[tickEnd..show]);
        Assert.DoesNotContain("MaybeReportInGameName();", open[tickEnd..show]);
    }

    // ---------------------------------------------------------------- creating a room

    [Fact]
    public void CreatingARoomSwitchesToRoomsAndKeepsTheHostFlagThroughThePass()
    {
        var src = Tab();
        var create = Body(src, "private async void CreateRoomButton_Click(");
        var enter = create.IndexOf("EnterHostedLobbyAsync(", StringComparison.Ordinal);
        var rooms = create.IndexOf("_activeSubtab = Subtab.Rooms", StringComparison.Ordinal);
        var created = create.IndexOf("_createdLobbyId = dlg.CreatedLobby.Id", StringComparison.Ordinal);
        Assert.True(rooms > 0 && rooms < enter, "Creating a room no longer switches to Rooms first.");
        Assert.True(created > 0 && created < enter, "The created id is no longer recorded before entering.");

        // The downgrade notice waits for the queued pass that opens the window.
        var yield = create.IndexOf("await Dispatcher.InvokeAsync(", enter, StringComparison.Ordinal);
        var notice = create.IndexOf("MpCreateDialogCompetitiveDowngraded", enter, StringComparison.Ordinal);
        Assert.True(yield > enter && notice > yield, "The downgrade notice is written before the room window exists.");

        Assert.Contains("_createdLobbyId", Body(src, "private void SyncRoomSocketSubscription()"));
    }

    // ---------------------------------------------------------------- startup

    /// <summary>
    /// THE ONE THAT MATTERS for startup: the assistant's auto-open waits for ApplicationIdle and
    /// for a visible tab — and a hidden tab does not spend the session's one chance.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheAssistantWaitsForTheFirstFrame()
    {
        var src = Tab();
        var polling = Body(src, "private void StartRadminPolling()");
        Assert.Contains("ApplicationIdle", polling);
        Assert.Contains("new Action(MaybeAutoOpenAssistant)", polling);
        Assert.DoesNotContain("        MaybeAutoOpenAssistant();", polling);

        var auto = Body(src, "private async void MaybeAutoOpenAssistant()");
        var visible = auto.IndexOf("if (!IsVisible) return;", StringComparison.Ordinal);
        var spent = auto.IndexOf("_radminAssistantAutoOpenedThisSession = true", StringComparison.Ordinal);
        Assert.True(visible > 0 && visible < spent, "A hidden tab spends the session's one auto-open.");
    }
}
