using System;
using System.IO;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the two things that made every match on the live server arrive with no civilization,
/// neither of which any other test could see.
///
/// <para><b>What was measured.</b> 31 of 32 rated matches carried no civilization at all, while
/// their confirmations carried a recording seed and a decided result — so the recording had
/// parsed perfectly and the identity join had refused. The join needs the AoE3 profile name every
/// launcher publishes in the room, and that name was reaching the frozen roster only by luck.</para>
///
/// <para><b>Why these are source assertions and not behaviour ones.</b> Both facts are about WHERE
/// a call sits — one of them literally about which line comes first — inside a method that needs a
/// live session, a room socket and a lobby window to run at all. A behavioural test would have to
/// fake all three and would still not be checking the thing that broke. The failure mode here is
/// somebody tidying a call away, and that is exactly what a source check catches.</para>
/// </summary>
public class InGameNamePublishingTests
{
    private static string Tab() => File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));

    /// <summary>
    /// THE ONE THAT MATTERS: the name is published BEFORE the roster is frozen.
    ///
    /// <para><c>MatchContext.Capture</c> takes a snapshot of the published names and everything
    /// after the match reads that snapshot, never the room. The publish call used to sit in the
    /// tail of the same method — below the capture — so a name that only landed at launch could
    /// never reach the map that needs it. Ordering is the whole of the fix, and nothing about a
    /// call being 49 lines too late looks wrong in a diff.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheNameIsPublishedBeforeTheRosterIsFrozen()
    {
        var src = Tab();
        var capture = src.IndexOf("MatchContext.Capture(", StringComparison.Ordinal);
        Assert.True(capture > 0, "MatchContext.Capture has moved or been renamed.");

        var publish = src.LastIndexOf("MaybeReportInGameName();", capture, StringComparison.Ordinal);
        Assert.True(
            publish > 0,
            "Nothing publishes the AoE3 profile name before MatchContext.Capture freezes the "
            + "roster, so a name that lands at launch can never reach the slot map.");
    }

    /// <summary>
    /// The name is RETRIED, like the Radmin IP it sits beside.
    ///
    /// <para>It used to be sent exactly once, on room entry, and gave up silently when the room
    /// socket was not up yet — while <c>MaybeReportRadminIp</c>, on the neighbouring line, has
    /// always had the lobby tick behind it. The comment in that file even reads "same reset, same
    /// reason: see MaybeReportInGameName": the guard reset was copied across and the retry was
    /// not. The dedup guard makes the repeat free once it has landed.</para>
    /// </summary>
    [Fact]
    public void TheNameIsRetriedOnTheLobbyTickLikeItsNeighbour()
    {
        var src = Tab();
        var tick = src.IndexOf("_lobbyPingTimer.Tick += ", StringComparison.Ordinal);
        Assert.True(tick > 0, "The lobby tick has moved or been renamed.");

        var end = src.IndexOf("_lobbyPingTimer.Start();", tick, StringComparison.Ordinal);
        Assert.True(end > tick, "Could not find the end of the lobby tick.");

        var body = src[tick..end];
        Assert.Contains("MaybeReportRadminIp();", body);
        Assert.True(
            body.Contains("MaybeReportInGameName();", StringComparison.Ordinal),
            "The lobby tick re-publishes the Radmin IP but not the AoE3 profile name, so the name "
            + "is back to having a single chance — and when it misses it, nobody in the room gets "
            + "a civilization.");
    }

    /// <summary>
    /// Our own name goes into the roster locally, not by asking the server to repeat it.
    ///
    /// <para>The room's member list was filled only by the server's <c>member_ingame_name</c>
    /// broadcast, so this machine's own name made a round trip over the network before it would
    /// admit knowing something it had just read off its own disk. Miss the round trip and OUR row
    /// is absent from the map the match freezes.</para>
    /// </summary>
    [Fact]
    public void OurOwnNameIsRecordedLocallyAndNotAwaitedFromTheServer()
    {
        var src = Tab();
        var start = src.IndexOf("private void MaybeReportInGameName()", StringComparison.Ordinal);
        Assert.True(start > 0, "MaybeReportInGameName has moved or been renamed.");

        // Bounded by the next member, not by a character count: the method is long enough that a
        // guess at its size silently checked half of it.
        var next = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var body = next > start ? src[start..next] : src[start..];
        var send = body.IndexOf("SendSetInGameNameAsync", StringComparison.Ordinal);
        Assert.True(send > 0, "MaybeReportInGameName no longer sends the name.");

        var local = body.IndexOf("InGameName = name", StringComparison.Ordinal);
        Assert.True(
            local > 0 && local < send,
            "MaybeReportInGameName sends our name but does not put it in _roomMembers itself, so "
            + "our own civilization depends on the server echoing back a name we read locally.");
    }

    /// <summary>Same walk-up <c>TextScaleTests</c> uses, so a layout change fails loudly here
    /// instead of quietly skipping every check in this file.</summary>
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}
