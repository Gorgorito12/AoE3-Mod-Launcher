using System;
using System.IO;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins WHERE the room-socket wiring sits, which no behavioural test can reach without a live
/// session, a room socket and a lobby window.
///
/// <para>Same reasoning as <see cref="InGameNamePublishingTests"/>: every failure here is a call
/// moved to the wrong side of a dispatcher callback, or a field written by the wrong method, and
/// none of them throws, builds red or looks wrong in a diff. The decisions themselves are pure
/// and pinned in <see cref="RoomSocketCloseTests"/>; these keep the tab calling them from the
/// right place.</para>
/// </summary>
public class RoomSocketWiringTests
{
    private static string Tab() => File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));

    /// <summary>The body of one member, bounded by the next private member rather than a count.</summary>
    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"'{signature}' has moved or been renamed.");
        var next = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        return next > start ? src[start..next] : src[start..];
    }

    /// <summary>
    /// THE ONE THAT MATTERS: each handler decides "is this the current socket" INSIDE its
    /// dispatcher callback. Checked when the event is raised, a callback queued behind a socket
    /// swap would still act on the new room.
    /// </summary>
    [Theory]
    [InlineData("private void OnRoomDisconnected(")]
    [InlineData("private void OnRoomReconnecting(")]
    [InlineData("private void OnRoomFrame(")]
    public void THE_ONE_THAT_MATTERS_ALateEventFromThePreviousRoomNeverTouchesTheNewOne(string handler)
    {
        var body = Body(Tab(), handler);
        var invoke = body.IndexOf("InvokeAsync(", StringComparison.Ordinal);
        var check = body.IndexOf("IsCurrentRoomSocket(sender)", StringComparison.Ordinal);
        Assert.True(invoke > 0, $"{handler} no longer marshals to the dispatcher.");
        Assert.True(check > invoke,
            $"{handler} asks whether its sender is current outside the dispatcher callback, or not at all.");

        // FIFO within ONE priority is what keeps every frame behind the per-room reset; a
        // handler raised to Send would overtake it.
        Assert.DoesNotContain("DispatcherPriority.Send", body);
    }

    [Fact]
    public void ATerminalCloseStopsTheSenderNeverWhateverSocketIsCurrent()
    {
        var src = Tab();
        Assert.DoesNotContain("_session?.RoomSocket?.StopReconnect", Body(src, "private void OnRoomDisconnected("));
        Assert.Contains("RoomSocketEvents.Route(", Body(src, "private void OnRoomDisconnected("));
        Assert.Contains(".IsStopped", Body(src, "private void OnRoomDisconnected("));
        Assert.Contains(".IsStopped", Body(src, "private void OnRoomReconnecting("));
    }

    /// <summary>
    /// The handlers are attached synchronously at the state change — ALWAYS, after the pass is
    /// queued (or found already queued) — so the first room_state cannot land in the gap before
    /// the queued pass subscribes, and a raise folded into an earlier pass still gets its socket
    /// listened to.
    /// </summary>
    [Fact]
    public void TheTabListensBeforeTheDispatcherRunsAgain()
    {
        var src = Tab();
        var body = Body(src, "private void OnSessionStateChanged(");
        var queue = body.IndexOf("_sessionPass.TryQueue()", StringComparison.Ordinal);
        var attach = body.IndexOf("AttachRoomSocketHandlers(", StringComparison.Ordinal);
        Assert.True(queue > 0, "OnSessionStateChanged no longer coalesces its pass.");
        Assert.True(attach > queue,
            "The synchronous attach must come AFTER the pass is queued, or the per-room reset is "
            + "no longer ahead of the new socket's frames.");
        // Nothing between them may return: a coalesced raise must still attach.
        Assert.DoesNotContain("return;", body[queue..attach]);

        var pass = Body(src, "private void QueueSessionPass()");
        var invoke = pass.IndexOf("InvokeAsync(", StringComparison.Ordinal);
        var begin = pass.IndexOf("_sessionPass.BeginRun()", StringComparison.Ordinal);
        var sync = pass.IndexOf("SyncRoomSocketSubscription()", StringComparison.Ordinal);
        var refresh = pass.IndexOf("RefreshFromSession()", StringComparison.Ordinal);
        Assert.True(invoke > 0 && begin > invoke && sync > begin && refresh > begin,
            "BeginRun must be the first thing the queued pass does, so a raise during it queues another.");
    }

    [Fact]
    public void ThePerRoomResetStillRunsAndTheAttachNeverHidesIt()
    {
        var src = Tab();
        var sync = Body(src, "private void SyncRoomSocketSubscription()");
        Assert.DoesNotContain("+= OnRoomFrame", sync);
        Assert.Contains("AttachRoomSocketHandlers(", sync);
        Assert.Contains("_nameState.Reset()", sync);

        // Writing _attachedSocket here would make the queued pass see no change and skip the
        // reset in silence.
        Assert.DoesNotContain("_attachedSocket =", Body(src, "private void AttachRoomSocketHandlers("));
    }

    /// <summary>The Radmin IP is confirmed by the SERVER, like the AoE3 name beside it.</summary>
    [Fact]
    public void TheRadminIpIsConfirmedByTheServer_NotByHavingBeenSent()
    {
        var src = Tab();
        Assert.Contains("_radminIpState.ShouldSend(", Body(src, "private void MaybeReportRadminIp()"));
        Assert.Contains("_radminIpState.RoomState(", Body(src, "private void HandleRoomState("));
        Assert.Contains("_radminIpState.Echo(", Body(src, "private void HandleMemberNet("));
        Assert.Contains("_radminIpState.ConnectionLost()", Body(src, "private void OnRoomDisconnected("));
        Assert.Contains("_radminIpState.Reset()", Body(src, "private void SyncRoomSocketSubscription()"));
        Assert.DoesNotContain("_lastReportedRadminIp", src);
    }

    /// <summary>
    /// A dropped room is cleared from the session BEFORE its window closes, so the window's close
    /// handler finds the session idle and sends no /leave for a room the server already closed.
    /// </summary>
    [Fact]
    public void TheSessionIsDroppedBeforeTheWindowCloses()
    {
        var body = Body(Tab(), "private void MaybeDropClosedRoom(");
        var drop = body.IndexOf("DropClosedLobby(", StringComparison.Ordinal);
        var close = body.IndexOf("CloseLobbyWindow()", StringComparison.Ordinal);
        Assert.True(drop > 0 && close > drop, "The room window closes before the session is dropped.");
        Assert.Contains("RoomMatchState.ShouldDropGoneRoom(", body);
    }

    /// <summary>
    /// The room window follows the SESSION on every subtab, not only from the Rooms render — and a
    /// sample room, which is not on the session, keeps its exemption.
    /// </summary>
    [Fact]
    public void TheRoomWindowClosesFromEverySubtab()
    {
        var body = Body(Tab(), "private void RefreshFromSession()");
        var sw = body.IndexOf("switch (_activeSubtab)", StringComparison.Ordinal);
        var rule = body.IndexOf("RoomWindowRule.InARoom(", StringComparison.Ordinal);
        Assert.True(sw > 0 && rule > sw, "The window rule is not applied after the subtab switch.");
        var demo = body.LastIndexOf("_demoRoomWindow", rule, StringComparison.Ordinal);
        Assert.True(demo > sw, "The sample-room exemption is missing from the window rule.");
    }

    /// <summary>
    /// Closing the launcher during a match never blocks on an awaited confirm. The old shape —
    /// an async confirm OnClosing waited on for ten seconds — deadlocked: its "yes" branch awaited
    /// a continuation that needed the UI thread the Wait was holding, then cancelled the close.
    /// The deadlock itself cannot be unit-tested (a test host has no SynchronizationContext), so
    /// the SHAPE is what is pinned.
    /// </summary>
    [Fact]
    public void ClosingDuringAMatchNeverWaitsOnTheUiThread()
    {
        var main = File.ReadAllText(RepoFile("MainWindow.xaml.cs"));
        var onClosing = Body(main, "protected override void OnClosing(");
        Assert.Contains("MultiplayerView.ConfirmCloseDuringMatch()", onClosing);
        Assert.DoesNotContain("ConfirmCloseDuringMatchAsync", main);

        var confirm = Body(Tab(), "public bool ConfirmCloseDuringMatch()");
        Assert.DoesNotContain("await ", confirm);
        Assert.DoesNotContain(".Wait(", confirm);

        var socket = File.ReadAllText(RepoFile("Services/Multiplayer/LobbyWebSocket.cs"));
        var send = Body(socket, "private async Task SendRawAsync(");
        Assert.Contains("_writeLock.WaitAsync(ct).ConfigureAwait(false)", send);
        Assert.Contains("endOfMessage: true, ct).ConfigureAwait(false)", send);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "WarsOfLibertyLauncher", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
