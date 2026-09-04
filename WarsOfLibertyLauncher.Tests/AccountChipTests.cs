using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The title-bar account cluster clears when the session goes.
///
/// <para><b>Why this is read off the SOURCE rather than run.</b> The chip lives on
/// <c>MainWindow</c>, which cannot be constructed in this suite, and it is not bound to
/// anything: <c>SetAccountChip</c> writes <c>TextBlock.Text</c> and an <c>Ellipse.Fill</c>
/// and that is the whole mechanism. There is no observable state to assert on and no
/// property to watch — only the question of whether the one method that repaints it is
/// reachable on the path a sign-out takes. That question is answerable from the text.</para>
///
/// <para>The bug this pins: <c>PushAccountChip</c> was called from a one-line
/// <c>RenderBrowser()</c> on the SIGNED-IN branch of <c>RenderRoomsTab</c>, so signing out
/// took <c>ShowSignInPanel</c>'s early return and the username and the rating stayed on the
/// bar of a launcher that had just cleared its token, its user and both sockets. The
/// multiplayer tab looked right because it re-reads <c>Status</c> on every pass; the chip
/// cannot, because it reads nothing.</para>
/// </summary>
public class AccountChipTests
{
    /// <summary>
    /// THE ONE THAT MATTERS. The chip is pushed before <c>RefreshFromSession</c> can return.
    ///
    /// <para>Not "somewhere in the method" — <b>above the first <c>return</c></b>. The
    /// null-session guard is the third statement in the method and it returns, so a push
    /// placed after it is a push that a signed-out launcher never reaches, which is exactly
    /// the shape of the original defect.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheAccountChipIsPushedBeforeAnyEarlyReturn()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));

        var start = source.IndexOf("private void RefreshFromSession()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RefreshFromSession is gone; this guard has to move with it.");

        // Comments first, or this reads its own prose: the line that explains WHY the push
        // sits above every return contains the word "return", and the first version of this
        // test failed on that sentence rather than on any code.
        var code = string.Join("\n", source.Substring(start).Split('\n')
            .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : line));

        var firstReturn = code.IndexOf("return", StringComparison.Ordinal);
        Assert.True(firstReturn > 0, "RefreshFromSession no longer returns early.");

        var beforeAnyReturn = code.Substring(0, firstReturn);
        Assert.True(
            beforeAnyReturn.Contains("PushAccountChip(", StringComparison.Ordinal),
            "RefreshFromSession returns before it pushes the account chip, so signing out "
            + "leaves the username and the ELO painted on the title bar. It belongs beside "
            + "UpdateConnectionStatus(), above every return — that position is the only "
            + "reason the connection pill never had this bug.");
    }

    /// <summary>
    /// The chip has ONE push on the state path, and it is not tied to a subtab.
    ///
    /// <para>Rooms, Tournaments, Ranking and Stats are four render paths and only one of them
    /// ever went near the old call site, so a per-subtab push is the same bug wearing a
    /// different hat. The second reference is <c>LoadStandingAsync</c>, which re-pushes when a
    /// rating lands — that one is about the ELO, not about identity.</para>
    /// </summary>
    [Fact]
    public void NothingElseRendersTheChip()
    {
        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs"));

        var calls = 0;
        for (var i = source.IndexOf("PushAccountChip(", StringComparison.Ordinal); i >= 0;
             i = source.IndexOf("PushAccountChip(", i + 1, StringComparison.Ordinal))
        {
            calls++;
        }

        // One declaration, the state-path push, and the standing re-push.
        Assert.Equal(3, calls);

        Assert.False(
            source.Contains("private void RenderBrowser()", StringComparison.Ordinal),
            "RenderBrowser held the chip push and nothing else. If it is back, check that it "
            + "is not the chip's writer again — it only ever ran when signed in.");
    }

    /// <summary>Walks up to the launcher project, as TextScaleTests does.</summary>
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
