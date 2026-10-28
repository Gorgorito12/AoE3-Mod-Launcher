using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// A rating still being PLACED is its owner's alone: he sees "1490?", everybody else sees
/// "In placement 3/10" where the number would be, and nobody sees a win probability while somebody
/// in the room is placing (the percentage would give the hidden number back).
///
/// <para>The server withholds the number (the lobby backend's <c>src/elo/placementPrivacy.ts</c>);
/// these pin the launcher's half — the second layer, which also hides it when an older server still
/// sends it. Every surface is fed the OLD server's shape on purpose (the number present beside the
/// flag): if the number reaches the screen there, the second layer is broken.</para>
///
/// <para>As everywhere in this corner of the code, the refusals are the point, and the mirror cases
/// matter as much: the player still sees his own number, and a placed player's stays visible.</para>
/// </summary>
[Collection("wpf-and-language")]
public class PlacementPrivacyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // ── the one rule (RatingDisplay.LookFor) ──

    [Fact]
    public void LookFor_SomebodyElsePlacingIsHidden_TheViewerSeesHisOwnWithTheQuestionMark()
    {
        Assert.Equal(RatingDisplay.Look.HiddenPlacement,
            RatingDisplay.LookFor(isViewer: false, rd: 210, gamesPlayed: 3, inPlacement: true));
        Assert.Equal(RatingDisplay.Look.Provisional,
            RatingDisplay.LookFor(isViewer: true, rd: 210, gamesPlayed: 3, inPlacement: true));
    }

    [Fact]
    public void LookFor_NothingKnownHidesNothing()
    {
        // An older server: no flag, no counts. Turning that silence into "hidden" would invent a
        // state, the same refusal ShouldShow makes about a null rating.
        Assert.Equal(RatingDisplay.Look.Number, RatingDisplay.LookFor(false, 80, null, null));
        Assert.Equal(RatingDisplay.Look.Number, RatingDisplay.LookFor(false, 80, 14, false));
    }

    [Fact]
    public void LookFor_UnratedOutranksPlacement()
    {
        // Nobody with no rated match is "placing": an unplayed 1500 is everybody's, and says so.
        Assert.Equal(RatingDisplay.Look.Unrated, RatingDisplay.LookFor(false, 500, 0, true));
        Assert.Equal(RatingDisplay.Look.Unrated, RatingDisplay.LookFor(true, 500, 0, null));
    }

    [Fact]
    public void LookOf_IsTheViewersOwnLook()
        => Assert.Equal(RatingDisplay.Look.Provisional, RatingDisplay.LookOf(210, 3, true));

    [Fact]
    public void IsPlacing_TheServersFlagFirstThenTheCounts()
    {
        Assert.True(RatingDisplay.IsPlacing(true, null, 10));
        Assert.False(RatingDisplay.IsPlacing(false, 3, 10), "the server's word beats the counts");
        Assert.True(RatingDisplay.IsPlacing(null, 3, 10));
        Assert.False(RatingDisplay.IsPlacing(null, 10, 10));
        Assert.Null(RatingDisplay.IsPlacing(null, null, 10));
        Assert.Null(RatingDisplay.IsPlacing(null, 3, 0));
    }

    [Fact]
    public void PlacementLabel_LongShortAndWithoutACount()
    {
        WithLanguage(Strings.LangEs, () =>
        {
            Assert.Equal("En posicionamiento 3/10", RatingDisplay.PlacementLabel(3, 10, shortForm: false));
            Assert.Equal("3/10", RatingDisplay.PlacementLabel(3, 10, shortForm: true));
            Assert.Equal("En posicionamiento", RatingDisplay.PlacementLabel(null, 10, shortForm: false));
            // A stale count never contradicts the rule it explains.
            Assert.Equal("5/5", RatingDisplay.PlacementLabel(7, 5, shortForm: true));
        });
        WithLanguage(Strings.LangEn, () =>
            Assert.Equal("In placement 2/5", RatingDisplay.PlacementLabel(2, 5, shortForm: false)));
    }

    // ── the ladder beside the badge ──

    [Fact]
    public void RatingFor_KeepsTheTeamLadderWhenItsRatingWasWithheld()
    {
        // A current server sends no team rating for somebody placing in teams — only his count and
        // flag. Falling back to his 1v1 number beside his TEAM badge was the trap: another ladder's
        // figure, presented as the one he is being placed on.
        var teamBadge = new ShownBadge(BadgeKind.Team, RankAge.Discovery, 0, null, 0);
        var chosen = MultiplayerTab.RatingForLadder(teamBadge, rating: 1610, rd: 80, ratingTeam: null, rdTeam: null,
            games: 14, gamesTeam: 2, inPlacement: false, inPlacementTeam: true);
        Assert.True(chosen.Team);
        Assert.Null(chosen.Rating);
        Assert.Equal(2, chosen.Games);
        Assert.True(chosen.InPlacement);

        var old = MultiplayerTab.RatingFor(teamBadge, 1610, 80, null, null, 14, 2, false, true);
        Assert.Null(old.Rating);
    }

    [Fact]
    public void RatingFor_WithNothingAboutTeamsTheOneVsOnePairStands()
    {
        // A server that predates the team ladder: the 1v1 pair, exactly as before.
        var teamBadge = new ShownBadge(BadgeKind.Team, RankAge.Discovery, 0, null, 0);
        var chosen = MultiplayerTab.RatingForLadder(teamBadge, 1610, 80, null, null, 14);
        Assert.False(chosen.Team);
        Assert.Equal(1610, chosen.Rating);
    }

    // ── the derived numbers ──

    [Fact]
    public void WinOdds_HiddenWhenTheServerSaysSoOrTheLauncherSeesSomebodyPlacing()
    {
        Assert.True(WinOddsView.HiddenByPlacement(new RoomOdds { HiddenReason = "placement" }, anyPlayerPlacing: false));
        // An older server still sends the odds; the launcher's own reading hides them.
        var sent = new RoomOdds { Players = new Dictionary<string, int> { ["a"] = 70, ["b"] = 30 } };
        Assert.True(WinOddsView.HiddenByPlacement(sent, anyPlayerPlacing: true));
        Assert.False(WinOddsView.HiddenByPlacement(sent, anyPlayerPlacing: false));
        Assert.False(WinOddsView.HiddenByPlacement(null, false));
    }

    [Fact]
    public void History_AnotherPlayersPlacingChangeIsWithheld_MineIsNot()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = "me", DisplayName = "Ana", Result = 0, RatingBefore = 1350, RatingAfter = 1330,
                    InPlacement = true, PlacementPlayed = 4, PlacementRequired = 10 },
            new() { UserId = "him", DisplayName = "Pedro", Result = 1, RatingBefore = 1340, RatingAfter = 1380,
                    InPlacement = true, PlacementPlayed = 4, PlacementRequired = 10 },
        }, myUserId: "me", rated: true);

        var him = lines.Single(l => l.UserId == "him");
        Assert.Null(him.RatingDelta);
        Assert.True(him.HiddenPlacement);
        Assert.Equal(4, him.PlacementPlayed);

        var me = lines.Single(l => l.UserId == "me");
        Assert.Equal(-20, me.RatingDelta);
        Assert.False(me.HiddenPlacement);
    }

    [Fact]
    public void History_AnUnratedMatchHasNoChangeToWithhold()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = "him", DisplayName = "Pedro", Result = 0.5, InPlacement = true,
                    PlacementPlayed = 4, PlacementRequired = 10 },
        }, myUserId: "me", rated: false);
        Assert.False(lines.Single().HiddenPlacement);
        Assert.Null(lines.Single().RatingDelta);
    }

    [Fact]
    public void ResultCard_ARivalStillPlacingHasNoNumber_OneThisMatchPlacedDoes()
    {
        var placing = Outcome(rivalPlayed: 4, rivalRequired: 10);
        Assert.True(placing.RivalInPlacement);
        Assert.Null(placing.RivalRatingShown);

        var placedNow = Outcome(rivalPlayed: 10, rivalRequired: 10);
        Assert.False(placedNow.RivalInPlacement);
        Assert.Equal(1420, placedNow.RivalRatingShown);

        // An older server sends no progress: nothing is hidden by this.
        Assert.Equal(1420, Outcome(null, null).RivalRatingShown);
    }

    // ── on screen ──

    /// <summary>
    /// Every surface that can show another player's rating, fed the shape an OLDER server sends —
    /// the number present beside the "placing" flag. Pedro, 3 matches into placement at 1380, must
    /// not appear as a number anywhere, and each surface must say how far along he is instead.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AnotherPlayersPlacementRatingNeverReachesTheScreen()
    {
        var error = DialogXamlTests.RunOnStaThread(() => WithLanguage(Strings.LangEs, () =>
        {
            var tab = new MultiplayerTab();

            // #1 the rooms table: the host's cell.
            var card = (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!
                .Invoke(tab, new object[] { PedroHostsARoom(), 0 })!;
            AssertNoNumberAndAPlacementLabel(card, "1380");

            // #3 the room's roster line.
            var line = (string)typeof(MultiplayerTab).GetMethod("MemberDetailLine", Private)!
                .Invoke(tab, new[] { PedroAsAMember() })!;
            Assert.DoesNotContain("1380", line);
            Assert.Contains(RatingDisplay.PlacementLabel(3, 10, shortForm: false), line);

            // #8 the players panel.
            var users = (System.Collections.IList)typeof(MultiplayerTab)
                .GetField("_globalOnlineUsers", Private)!.GetValue(tab)!;
            users.Add(new MultiplayerTab.OnlinePlayer("pedro", "Pedro", null, "idle", 1380.0, 210.0, 0,
                GamesPlayed: 3, InPlacement: true));
            typeof(MultiplayerTab).GetMethod("RenderPlayersPanel", Private)!.Invoke(tab, null);
            AssertNoNumberAndAPlacementLabel(tab.PlayersPanel, "1380");

            // #9 the ranking's placement rows.
            var row = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
            {
                UserId = "pedro", DisplayName = "Pedro", Rating = 1380, Rd = 210,
                PlacementPlayed = 3, PlacementRequired = 10,
            }, isMe: false, Services.Multiplayer.RankingTableLayout.WithoutPeak, ownResults: null);
            AssertNoNumberAndAPlacementLabel(row, "1380");
            Assert.Contains(Texts(row), t => t.Contains(Strings.Format("MpPlacementProgress", 3, 10)));

            // #10 the history's player rows.
            var history = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
            {
                new() { UserId = "pedro", DisplayName = "Pedro", Result = 1, RatingBefore = 1340, RatingAfter = 1380,
                        InPlacement = true, PlacementPlayed = 3, PlacementRequired = 10 },
            }, myUserId: "ana", rated: true).Single();
            var historyRow = MultiplayerTab.BuildHistoryPlayerRow(history);
            AssertNoNumberAndAPlacementLabel(historyRow, "+40");

            // #11 the result card.
            var resultCard = MatchResultCard.Build(Outcome(3, 10) with { RivalLogin = "Pedro", RivalRating = 1380 },
                new MatchResultCard.Actions(OnRematch: null, OnDismiss: () => { }));
            Assert.DoesNotContain(Texts(resultCard), t => t.Contains("1380"));
            // The short form on the card (its cell is ~95 px wide), the long one in the tooltip.
            Assert.Contains(Texts(resultCard), t => t.Contains("Pedro · " + RatingDisplay.PlacementLabel(3, 10, shortForm: true)));
            var rivalCell = Walk(resultCard).OfType<Border>()
                .Single(b => b.ToolTip is TextBlock tip
                             && tip.Text == Strings.Format("MpResultRivalInPlacement", "Pedro", 3, 10));
            Assert.DoesNotContain("1380", ((TextBlock)rivalCell.ToolTip).Text);
        }));
        Assert.Null(error);
    }

    /// <summary>The mirror, on screen: my own placing row still carries my number and its "?".</summary>
    [Fact]
    public void MyOwnPlacementRowKeepsMyNumber_EvenWhenTheServerSentNone()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            // A current server sends nobody a number on these rows, me included: mine comes from
            // my own standing.
            var row = (DependencyObject)tab.BuildPlacementRow(new PlacementRow
            {
                UserId = "me", DisplayName = "Ana", Rating = null, PlacementPlayed = 3, PlacementRequired = 10,
            }, isMe: true, Services.Multiplayer.RankingTableLayout.WithoutPeak, ownResults: null, ownRating: 1534);
            Assert.Contains(Texts(row), t => t.Contains("1534?"));
        });
        Assert.Null(error);
    }

    /// <summary>A placed host's number is still everybody's.</summary>
    [Fact]
    public void APlacedHostKeepsHisNumberInTheRoomsTable()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var lobby = PedroHostsARoom();
            lobby.Host.Rating = 1610;
            lobby.Host.GamesPlayed = 14;
            lobby.Host.InPlacement = false;
            var card = (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!
                .Invoke(tab, new object[] { lobby, 0 })!;
            Assert.Contains(Texts(card), t => t.Contains("1610"));
            Assert.DoesNotContain(Walk(card).OfType<FrameworkElement>(), e => Equals(e.Tag, MultiplayerTab.HiddenPlacementTag));
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static MatchOutcomeView Outcome(int? rivalPlayed, int? rivalRequired)
        => new(MatchVerdict.Loss, "wol", "Texas", 1440, 2,
            RatingBefore: 1524, RatingAfter: 1508,
            RivalLogin: "Pedro", RivalRating: 1420,
            Wins: 4, Losses: 1, Rd: 60)
        {
            RivalPlacementPlayed = rivalPlayed,
            RivalPlacementRequired = rivalRequired,
        };

    private static LobbySummary PedroHostsARoom() => new()
    {
        Id = "abc123",
        Title = "WoL · Ranked 1v1",
        ModId = "wol",
        MaxPlayers = 2,
        Status = "open",
        Competitive = true,
        Host = new LobbyHost
        {
            Id = "pedro", DiscordUsername = "pedro", DisplayName = "Pedro",
            Rating = 1380, Rd = 210, GamesPlayed = 3, InPlacement = true,
        },
    };

    private static object PedroAsAMember()
    {
        var type = typeof(MultiplayerTab).GetNestedType("RoomMemberEntry", BindingFlags.NonPublic)!;
        var m = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("UserId")!.SetValue(m, "pedro");
        type.GetProperty("Login")!.SetValue(m, "Pedro");
        type.GetProperty("Rating")!.SetValue(m, 1380.0);
        type.GetProperty("Rd")!.SetValue(m, 210.0);
        type.GetProperty("GamesPlayed")!.SetValue(m, 3);
        type.GetProperty("InPlacement")!.SetValue(m, true);
        return m;
    }

    private static void AssertNoNumberAndAPlacementLabel(DependencyObject root, string number)
    {
        Assert.DoesNotContain(Texts(root), t => t.Contains(number));
        Assert.Contains(Walk(root).OfType<FrameworkElement>(), e => Equals(e.Tag, MultiplayerTab.HiddenPlacementTag));
    }

    private static List<string> Texts(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(WarsOfLibertyLauncher.RevealText.PlainTextOf).ToList();

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    private static void WithLanguage(string language, Action action)
    {
        var previous = Strings.Language;
        Strings.SetLanguage(language);
        try { action(); }
        finally { Strings.SetLanguage(previous); }
    }
}
