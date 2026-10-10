using System;
using System.IO;
using System.Text.Json;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The server's <c>matches_changed</c> frame, and the refresh it drives: somebody finishes a match
/// and every open launcher shows it, instead of the player having to go to Library and back.
/// </summary>
public class MatchesChangedTests
{
    [Fact]
    public void TheFrameNamesItsMatchesAndPlayers_AndAMalformedOneNamesNothing()
    {
        using var ok = JsonDocument.Parse(
            "{\"type\":\"matches_changed\",\"matchIds\":[\"m1\",\"m2\"],\"userIds\":[\"a\",\"b\",\"\",7]}");
        Assert.Equal(new[] { "m1", "m2" }, MatchesChanged.MatchIds(ok.RootElement));
        // Blanks and non-strings are dropped rather than trusted.
        Assert.Equal(new[] { "a", "b" }, MatchesChanged.UserIds(ok.RootElement));

        using var bad = JsonDocument.Parse("{\"type\":\"matches_changed\",\"userIds\":\"a\"}");
        Assert.Empty(MatchesChanged.UserIds(bad.RootElement));
        Assert.Empty(MatchesChanged.MatchIds(bad.RootElement));
    }

    [Fact]
    public void TheViewersOwnHistoryIsFetchedOnlyForTheirOwnMatches()
    {
        var users = new[] { "a", "b" };
        Assert.True(MatchesChanged.IncludesViewer(users, "b"));
        Assert.False(MatchesChanged.IncludesViewer(users, "B"));   // ids are compared exactly
        Assert.False(MatchesChanged.IncludesViewer(users, "c"));
        Assert.False(MatchesChanged.IncludesViewer(users, null));
        Assert.False(MatchesChanged.IncludesViewer(Array.Empty<string>(), "a"));
    }

    /// <summary>
    /// A refresh replaces the list, so it may redraw an open Ranking › Matches only for somebody at
    /// the top — never under a reader scrolled further down, whose list waits for the next entry.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(MatchesChanged.NearTopPx, true)]
    [InlineData(MatchesChanged.NearTopPx + 1, false)]
    [InlineData(900, false)]
    public void AnOpenListIsRedrawnOnlyNearTheTop(double offset, bool expected)
        => Assert.Equal(expected, MatchesChanged.MayRedrawOpenList(offset));

    [Fact]
    public void TheDebounceWaitsAndSpreadsLaunchersOut()
    {
        Assert.True(MatchesChanged.DebounceMs >= 1000);
        Assert.True(MatchesChanged.JitterMs > 0);
    }

    /// <summary>
    /// Entering Ranking › Matches refreshes it; RENDERING it must not, because the render runs on
    /// every repaint of the Ranking page — every community payload — and a reload there would ask
    /// the server once a minute for as long as the page is open, and yank the scroll each time.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheMatchesViewRefreshesOnEntryAndNeverFromItsRender()
    {
        var file = SessionRejectionTests.RepoFile("Controls/MultiplayerTab.RankingMatches.cs");
        var source = File.ReadAllText(file).Replace("\r\n", "\n");
        var render = source.IndexOf("private void RenderRankingMatches()", StringComparison.Ordinal);
        Assert.True(render > 0);
        var end = source.IndexOf("\n    }\n", render, StringComparison.Ordinal);
        Assert.DoesNotContain("MaybeRefreshMatchesOnEntry", source[render..end]);

        // And the ways IN do call it.
        Assert.Contains("MaybeRefreshMatchesOnEntry();", source[..render]);
        var tab = File.ReadAllText(SessionRejectionTests.RepoFile("Controls/MultiplayerTab.xaml.cs"));
        Assert.Contains("case \"matches_changed\":", tab);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(tab, @"MaybeRefreshMatchesOnEntry\(\);").Count >= 3,
            "ShowRanking, the Ranking subtab and showing the tab again must each refresh a stale list");
    }
}
