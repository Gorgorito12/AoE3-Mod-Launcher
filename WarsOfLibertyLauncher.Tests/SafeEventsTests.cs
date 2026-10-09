using System;
using System.Collections.Generic;
using System.IO;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A room-socket subscriber that throws must neither silence the next one nor escape into the
/// pump — where an unguarded raise used to fault the run loop and end reconnection for good.
/// </summary>
public class SafeEventsTests
{
    private event EventHandler<string>? Event;

    [Fact]
    public void AThrowingSubscriberDoesNotSilenceTheNext()
    {
        var heard = new List<string>();
        Event += (_, a) => heard.Add("first " + a);
        Event += (_, _) => throw new InvalidOperationException("boom");
        Event += (_, a) => heard.Add("third " + a);

        SafeEvents.Raise(Event, this, "x", _ => { });

        // In subscription order — the session's OnFrame before the tab's.
        Assert.Equal(new[] { "first x", "third x" }, heard);
    }

    [Fact]
    public void NothingEscapes()
    {
        Event += (_, _) => throw new InvalidOperationException("boom");
        var ex = Record.Exception(() => SafeEvents.Raise(Event, this, "x", _ => throw new Exception("reporter")));
        Assert.Null(ex);
        Assert.Null(Record.Exception(() => SafeEvents.Raise<string>(null, this, "x", _ => { })));
    }

    [Fact]
    public void OnErrorIsCalledOncePerThrow()
    {
        Event += (_, _) => throw new InvalidOperationException("a");
        Event += (_, _) => { };
        Event += (_, _) => throw new ArgumentException("b");
        var errors = new List<string>();

        SafeEvents.Raise(Event, this, "x", e => errors.Add(e.Message));

        Assert.Equal(new[] { "a", "b" }, errors);
    }

    /// <summary>Every raise site goes through it — a bare Invoke is the bug coming back.</summary>
    [Fact]
    public void TheSocketRaisesNothingBare()
    {
        var src = File.ReadAllText(LauncherFile("Services/Multiplayer/LobbyWebSocket.cs"));
        Assert.DoesNotContain("Disconnected?.Invoke", src);
        Assert.DoesNotContain("Reconnecting?.Invoke", src);
        Assert.DoesNotContain("FrameReceived?.Invoke", src);
    }

    /// <summary>
    /// The leave stops the reconnect BEFORE its REST call — and does not dispose or abort the
    /// socket first, since the server reads the /leave arriving before the close.
    /// </summary>
    [Fact]
    public void ALeaveStopsTheReconnectBeforeItsRestCallAndLogsWhy()
    {
        var src = File.ReadAllText(LauncherFile("Services/Multiplayer/MultiplayerSession.cs"));
        var start = src.IndexOf("public async Task LeaveCurrentLobbyAsync(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var rest = src.IndexOf("Api.LeaveLobbyAsync(", start, StringComparison.Ordinal);
        var end = src.IndexOf("socket?.EndAfterThisConnection()", start, StringComparison.Ordinal);
        var log = src.IndexOf("Leaving room {lobbyId} ({why})", start, StringComparison.Ordinal);
        Assert.True(end > start && end < rest, "EndAfterThisConnection must come before the REST /leave.");
        Assert.True(log > start && log < rest, "The leave must be logged, with its reason, before the REST call.");
        var dispose = src.IndexOf("socket.DisposeAsync()", start, StringComparison.Ordinal);
        Assert.True(dispose > rest, "The socket must not be disposed before the REST /leave.");
    }

    private static string LauncherFile(string relative)
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
