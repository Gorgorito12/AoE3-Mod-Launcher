using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community block and the Players panel are repainted only when what they SHOW changed.
/// The block was rebuilt on every minute's answer — which is never byte-identical, because
/// <c>generated_at</c> is stamped per response and every <c>rd</c> decays — and the panel on every
/// presence frame. The danger runs the other way: a drawn input left out of a key leaves a stale
/// screen until the next real change, so the tests below are mostly about what DOES repaint.
/// </summary>
[Collection("wpf-and-language")]
public class ActivityPaintKeyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly ActivityPaintContext Context = new(
        Error: 0, FallbackIds: null, UserId: "me", UtcOffset: TimeSpan.FromHours(-3),
        LocalDate: new DateTime(2026, 10, 7), Language: "en", TextScale: 1.0,
        ShowPreviousMonth: false, ArtStamp: "", Fluid: "fluid");

    private static CommunityStats Stats(int matches = 4, double topRating = 1700, string generatedAt = "2026-10-07T12:00:00Z")
        => new()
        {
            GeneratedAt = generatedAt,
            Leaderboard = Enumerable.Range(1, 5).Select(i => new LeaderboardRow
            {
                Rank = i, UserId = "u" + i, DisplayName = "Player" + i,
                Rating = i == 1 ? topRating : 1700 - i * 20, Rd = 90 + i,
            }).ToList(),
            RankedPlayers = 5,
            RecentMatches = Enumerable.Range(0, matches).Select(i => new CommunityMatch
            {
                Id = "m" + i,
                ModId = "wol",
                MapName = "ESOC_Fertile Crescent",
                DurationSeconds = 1500,
                Competitive = true,
                ReportedAt = new DateTime(2026, 10, 7, 11, 0, 0, DateTimeKind.Utc).AddMinutes(-i * 40).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "Kaiser", Result = 1 },
                    new() { UserId = "b", DisplayName = "El Taita", Result = 0 },
                },
            }).ToList(),
        };

    private static CommunityStats RoundTrip(CommunityStats s)
        => JsonSerializer.Deserialize<CommunityStats>(JsonSerializer.Serialize(s))!;

    // ------------------------------------------------------------------ the pure key

    [Fact]
    public void AStampAndADecayedDeviationAreNotAChange()
    {
        var before = Stats();
        var after = RoundTrip(before);
        after.GeneratedAt = "2026-10-07T12:01:00Z";
        foreach (var row in after.Leaderboard) row.Rd += 0.37;

        Assert.Equal(ActivityPaintKey.For(before, Context), ActivityPaintKey.For(after, Context));
    }

    [Fact]
    public void EverythingDrawnIsAChange()
    {
        var key = ActivityPaintKey.For(Stats(), Context);
        Assert.NotNull(key);

        Assert.NotEqual(key, ActivityPaintKey.For(Stats(matches: 5), Context));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(topRating: 1701), Context));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { Error = 1 }));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { UserId = "someone else" }));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { Language = "es" }));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { TextScale = 1.1 }));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { ArtStamp = "123" }));
        Assert.NotEqual(key, ActivityPaintKey.For(Stats(), Context with { LocalDate = new DateTime(2026, 10, 8) }));
        Assert.NotEqual(key, ActivityPaintKey.For(null, Context));
    }

    // ------------------------------------------------------------------ the strip, through the seam

    private static bool Paint(MultiplayerTab tab, CommunityStats? stats) => tab.PaintActivityIfChanged(stats);

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnIdenticalAnswerRepaintsNothingAndANewMatchDoes()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var stats = Stats();
            Assert.True(Paint(tab, stats));
            var first = tab.ActivityRecentList.Children[0];

            var again = RoundTrip(stats);
            again.GeneratedAt = "2026-10-07T12:01:00Z";
            foreach (var row in again.Leaderboard) row.Rd -= 0.2;
            Assert.False(Paint(tab, again));
            Assert.Same(first, tab.ActivityRecentList.Children[0]);

            var more = RoundTrip(stats);
            more.RecentMatches.Insert(0, RoundTrip(stats).RecentMatches[0]);
            more.RecentMatches[0].Id = "new";
            Assert.True(Paint(tab, more));
            Assert.NotSame(first, tab.ActivityRecentList.Children[0]);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A direct repaint (a preview, a language change) stores the key of what IT drew, so the next
    /// answer compares against the screen and not against the last fetch.
    /// </summary>
    [Fact]
    public void ADirectRepaintOfSomethingElseIsNeverTakenForTheLastAnswer()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var real = Stats();
            Assert.True(Paint(tab, real));

            // Something paints the block directly from another payload (the previews do).
            typeof(MultiplayerTab).GetField("_communityStats", Private)!.SetValue(tab, Stats(matches: 2));
            typeof(MultiplayerTab).GetMethod("RenderActivityStrip", Private, Type.EmptyTypes)!.Invoke(tab, null);
            Assert.Equal(2, tab.ActivityRecentList.Children.Count);

            // The real answer again, byte for byte: it must paint, the screen shows the other one.
            Assert.True(Paint(tab, RoundTrip(real)));
            Assert.Equal(4, tab.ActivityRecentList.Children.Count);
        });
        Assert.Null(error);
    }

    // ------------------------------------------------------------------ the Players panel

    private static IList Users(MultiplayerTab tab)
        => (IList)typeof(MultiplayerTab).GetField("_globalOnlineUsers", Private)!.GetValue(tab)!;

    private static void RenderPlayers(MultiplayerTab tab)
        => typeof(MultiplayerTab).GetMethod("RenderPlayersPanel", Private)!.Invoke(tab, null);

    private static void SetUsers(MultiplayerTab tab, params MultiplayerTab.OnlinePlayer[] users)
    {
        var list = Users(tab);
        list.Clear();
        foreach (var u in users) list.Add(u);
    }

    private static Grid FirstRow(MultiplayerTab tab) => tab.PlayersPanel.Children.OfType<Grid>().First();

    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePlayersPanelIsRebuiltOnlyWhenWhatItShowsChanged()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            SetUsers(tab,
                new MultiplayerTab.OnlinePlayer("u1", "first", null, "idle", 1600.0, 80.0, 1),
                new MultiplayerTab.OnlinePlayer("u2", "second", null, "idle", 1500.0, 90.0, 2));
            RenderPlayers(tab);
            var row = FirstRow(tab);

            // The same people in the same state, in a new list (every presence frame rebuilds it).
            SetUsers(tab,
                new MultiplayerTab.OnlinePlayer("u1", "first", null, "idle", 1600.0, 80.0, 1),
                new MultiplayerTab.OnlinePlayer("u2", "second", null, "idle", 1500.0, 90.0, 2));
            RenderPlayers(tab);
            Assert.Same(row, FirstRow(tab));

            // Somebody's status changes: rebuilt.
            SetUsers(tab,
                new MultiplayerTab.OnlinePlayer("u1", "first", null, "in_room", 1600.0, 80.0, 1),
                new MultiplayerTab.OnlinePlayer("u2", "second", null, "idle", 1500.0, 90.0, 2));
            RenderPlayers(tab);
            var afterStatus = FirstRow(tab);
            Assert.NotSame(row, afterStatus);

            // The text size raises no event and the rows bake their sizes in: rebuilt.
            WarsOfLibertyLauncher.Services.TextScale.Apply(1.1);
            try
            {
                RenderPlayers(tab);
                Assert.NotSame(afterStatus, FirstRow(tab));
            }
            finally
            {
                WarsOfLibertyLauncher.Services.TextScale.Apply(1.0);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The badges read BOTH ladder sizes, so the branch that notices a size change repaints the
    /// Players panel too (it repainted the rooms and the roster and left the panel stale).
    /// </summary>
    [Fact]
    public void ALadderSizeChangeRepaintsThePlayersPanel()
    {
        var src = File.ReadAllText(UiThreadAttributionTests.LauncherFile("Controls/MultiplayerTab.xaml.cs"));
        var start = src.IndexOf("var soloSizeBefore = LadderSize(team: false);", StringComparison.Ordinal);
        Assert.True(start >= 0, "the ladder-size branch was not found");
        var branch = src.Substring(start, src.IndexOf("if (_activeSubtab == Subtab.Ranking) RenderRanking();", start, StringComparison.Ordinal) - start);
        Assert.Contains("LadderSize(team: true) != teamSizeBefore", branch);
        Assert.Contains("RenderPlayersPanel();", branch);
    }
}
