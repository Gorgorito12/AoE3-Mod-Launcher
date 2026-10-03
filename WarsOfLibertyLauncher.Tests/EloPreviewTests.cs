using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rating preview (design handoff 55): every scene has a name the Settings list and
/// <c>--demo-elo=&lt;scene&gt;</c> share, the sample is coherent across screens, and the ranking
/// scene draws what 55a draws.
/// </summary>
[Collection("wpf-and-language")]
public class EloPreviewTests
{
    [Fact]
    public void EverySceneHasANameThatComesBackAsItself_AndALabel()
    {
        foreach (var scene in EloDemoData.Scenes)
        {
            Assert.Equal(scene, EloDemoData.SceneByName(EloDemoData.NameOf(scene)));
            Assert.Equal(scene, EloDemoData.SceneByName(EloDemoData.NameOf(scene).ToUpperInvariant()));
            var key = EloDemoData.LabelKeyOf(scene);
            Assert.NotEqual(key, Strings.GetIn(Strings.LangEs, key));
            Assert.NotEqual(key, Strings.GetIn(Strings.LangEn, key));
        }
        Assert.Equal(EloPreviewScene.Ranking, EloDemoData.SceneByName(null));
        Assert.Equal(EloPreviewScene.Ranking, EloDemoData.SceneByName("nope"));
    }

    /// <summary>
    /// The sample is ONE community: the viewer is ranked in 1v1 and placing in teams on the
    /// ranking AND on the profile, so two screens of the preview never contradict each other.
    /// </summary>
    [Fact]
    public void TheViewerIsTheSamePlayerOnEveryScreen()
    {
        var stats = StatsDemoData.Community();
        Assert.Contains(stats.Leaderboard, r => r.UserId == EloDemoData.ViewerId);
        Assert.DoesNotContain(stats.LeaderboardTeam!, r => r.UserId == EloDemoData.ViewerId);
        var placing = Assert.Single(stats.LeaderboardTeamPlacement!, r => r.UserId == EloDemoData.ViewerId);

        var profile = EloDemoData.Profile();
        Assert.Equal(EloDemoData.ViewerId, profile.User.Id);
        Assert.Equal(stats.Leaderboard.First(r => r.UserId == EloDemoData.ViewerId).Rank,
            profile.Standing.Ladders!.Default!.LadderRank);
        Assert.Equal(placing.PlacementPlayed, profile.Standing.Ladders.Team!.PlacementPlayed);
        Assert.Equal(placing.PlacementPlayed, profile.Standing.Ladders.Team.PlacementResults!.Count);
    }

    /// <summary>The placement rows come in the server's order: most matches first, then by name.</summary>
    [Fact]
    public void ThePlacementSampleIsInTheServersOrder()
    {
        foreach (var team in new[] { false, true })
        {
            var rows = StatsDemoData.DemoPlacement(team);
            var ordered = rows.OrderByDescending(r => r.PlacementPlayed)
                .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(ordered.Select(r => r.UserId), rows.Select(r => r.UserId));
        }
    }

    /// <summary>
    /// 55a, drawn: the ranked rows with the viewer's own marked "YOU", the streak pills from 3,
    /// INACTIVE on an inactive player, and the placement rows after them; the summary counts both.
    /// </summary>
    [Fact]
    public void TheRankingSceneDrawsTheHandoffsStates()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // No window: the rows are built synchronously, and a window shown here would end the
            // test application for the snapshot harness that runs after this class's other tests.
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("ranking");
                tab.Measure(new Size(1440, 1000));
                tab.Arrange(new Rect(0, 0, 1440, 1000));

                var rows = tab.RankingBody.Children.OfType<Border>().ToList();
                var ranked = rows.Where(b => b.Tag is LeaderboardRow).ToList();
                var placing = rows.Where(b => b.Tag is PlacementRow).ToList();
                Assert.Equal(StatsDemoData.DemoLadder().Count, ranked.Count);
                Assert.Equal(StatsDemoData.DemoPlacement().Count, placing.Count);

                var mine = Assert.Single(ranked, b => ((LeaderboardRow)b.Tag).UserId == EloDemoData.ViewerId);
                Assert.NotNull(mine.Background);
                Assert.Contains(Texts(mine), t => t == Strings.Get("MpRankYouTag"));

                var pills = ranked.Count(b => Tagged(b, "StreakPill"));
                Assert.Equal(StatsDemoData.DemoLadder().Count(r => r.Streak >= 3), pills);
                Assert.Equal(StatsDemoData.DemoLadder().Count(r => r.Inactive == true),
                    ranked.Count(b => Tagged(b, "InactiveTag")));

                Assert.Contains(StatsDemoData.DemoLadder().Count.ToString(), tab.RankingSubtitleText.Text);
            }
        });
        Assert.Null(error);
    }

    /// <summary>55c: with nobody ranked, the box takes the ranked rows' place and the placement rows follow.</summary>
    [Fact]
    public void ThePlacementSceneIsTheNobodyRankedBox()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // No window: the rows are built synchronously, and a window shown here would end the
            // test application for the snapshot harness that runs after this class's other tests.
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("placement");
                tab.Measure(new Size(1440, 1000));
                tab.Arrange(new Rect(0, 0, 1440, 1000));

                var children = tab.RankingBody.Children.OfType<Border>().ToList();
                Assert.Equal("RankingEmptyPlacement", children[0].Tag);
                Assert.All(children.Skip(1), b => Assert.IsType<PlacementRow>(b.Tag));
                Assert.Empty(tab.RankingHeaderHost.Children);
            }
        });
        Assert.Null(error);
    }

    // --------------------------------------------------------------- the profile (55d-55f)

    /// <summary>Builds the real profile page for a sample, into a panel rather than a window.</summary>
    private static StackPanel ProfileOf(MultiplayerTab tab, EloProfileSample sample)
    {
        var body = new StackPanel();
        tab.ProfileBodyOverride = body;
        tab.ShowDemoEloProfile(sample);
        body.Measure(new Size(980, double.PositiveInfinity));
        body.Arrange(new Rect(0, 0, 980, body.DesiredSize.Height));
        return body;
    }

    private static List<Border> ModeCards(DependencyObject root)
        => Walk(root).OfType<Border>().Where(b => Equals(b.Tag, MultiplayerTab.ModeCardTag)).ToList();

    private static Border TaggedBorder(DependencyObject root, object tag)
        => Walk(root).OfType<Border>().Single(b => Equals(b.Tag, tag));

    /// <summary>
    /// 55d: one card per mode - ranked in 1v1 (badge, place, peak and low, three streak cells,
    /// the flame), placing 2/5 in teams (the "?", five segments, what is left) - and "against
    /// each opponent" folded to four rows with "See all 6".
    /// </summary>
    [Fact]
    public void TheProfileDrawsOneCardPerMode_AndTheHeadToHeadFolded()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var body = ProfileOf(tab, EloDemoData.Profile());

                var cards = ModeCards(body);
                Assert.Equal(2, cards.Count);
                var solo = AllText(cards[0]);
                // The viewer is on a streak of 2 (as on the ranking): the plain figure, with the "i"
                // that says what ends a streak.
                Assert.True(Tagged(cards[0], "StreakInfo"));
                Assert.Contains(solo, t => t.StartsWith("M\u00e1ximo:"));
                Assert.Contains(solo, t => t.StartsWith("M\u00ednimo:"));
                Assert.Contains(solo, t => t == Strings.Get("MpProfileLongestWin"));

                var team = AllText(cards[1]);
                Assert.Contains(team, t => t == "1534?");
                Assert.Contains(team, t => t == "Posicionamiento 2/5");
                Assert.Contains(team, t => t == "Faltan 3 partidas puntuadas para entrar en la tabla.");
                Assert.True(Tagged(cards[1], "PlacementSegments"));
                Assert.DoesNotContain(team, t => t.StartsWith("M\u00e1ximo:"));

                // The header names where the player stands in each mode, and no longer carries a
                // rating of its own: that is the cards' now.
                Assert.Contains(AllText(TaggedBorder(body, "ProfileHeader")),
                    t => t.EndsWith("en posicionamiento en Equipos"));

                var h2h = TaggedBorder(body, MultiplayerTab.HeadToHeadCardTag);
                Assert.Equal(ProfileModeView.HeadToHeadFolded,
                    Walk(h2h).OfType<Border>().Count(b => b.Tag is HeadToHeadEntry));
                var more = Walk(h2h).OfType<Button>().Single(b => Equals(b.Tag, "H2HSeeAll"));
                Assert.Equal("Ver los 6 rivales", more.Content);

                // Unfolding lists everybody the server sent.
                more.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                h2h = TaggedBorder(body, MultiplayerTab.HeadToHeadCardTag);
                Assert.Equal(6, Walk(h2h).OfType<Border>().Count(b => b.Tag is HeadToHeadEntry));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>55e: placing in 1v1 (6/10, own results coloured) and nothing yet in teams.</summary>
    [Fact]
    public void APlayerStillPlacingSeesTheQuestionMarkAndNoPeakOrStreaks()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var body = ProfileOf(tab, EloDemoData.ProfilePlacing());
                var cards = ModeCards(body);
                Assert.Equal(2, cards.Count);
                var solo = AllText(cards[0]);
                Assert.Contains(solo, t => t == "1490?");
                Assert.Contains(solo, t => t == "Posicionamiento 6/10");
                Assert.DoesNotContain(solo, t => t.StartsWith("M\u00e1ximo:"));
                Assert.DoesNotContain(solo, t => t == Strings.Get("MpProfileStreak"));

                Assert.Contains(AllText(cards[1]),
                    t => t.StartsWith("Todav\u00eda no jugaste partidas puntuadas en equipos."));
                Assert.Contains(AllText(TaggedBorder(body, "ProfileHeader")),
                    t => t == "En posicionamiento en 1v1");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 55f: nothing rated anywhere - no mode card, the header's sentence and its dash, and the
    /// head-to-head saying it has nobody yet. Never a 1500 nobody earned.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_NoRatedMatchShowsADashAndNoCards()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var body = ProfileOf(tab, EloDemoData.ProfileNoGames());
            Assert.Empty(ModeCards(body));
            var texts = AllText(TaggedBorder(body, "ProfileHeader"));
            Assert.Contains(texts, t => t == Strings.Get("MpProfileNoMatchesYou"));
            Assert.Contains(texts, t => t == Strings.Get("MpDash"));
            Assert.DoesNotContain(texts, t => t.Contains("1500"));
            Assert.Contains(AllText(TaggedBorder(body, MultiplayerTab.HeadToHeadCardTag)),
                t => t == Strings.Get("MpH2HEmptyYou"));
        });
        Assert.Null(error);
    }

    /// <summary>55f: inactive keeps the place and shows the amber notice; a streak that ran out
    /// for lack of play reads a dash with the date it ended.</summary>
    [Fact]
    public void InactiveAndAnExpiredStreakSayWhy()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var body = ProfileOf(tab, EloDemoData.ProfileLimits());
                var cards = ModeCards(body);
                Assert.True(Tagged(cards[0], "InactiveNotice"));
                Assert.DoesNotContain(AllText(cards[0]), t => t == Strings.Get("MpProfileStreak"));
                Assert.Contains(AllText(cards[1]), t => t.StartsWith("Se cort\u00f3 el"));
                Assert.False(Tagged(cards[1], "StreakInfo"), "no streak, so no 'i' to explain one");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    // --------------------------------------------------------------- rooms (55g-55h)

    /// <summary>A tab with a session whose room window is built but never shown.</summary>
    internal static MultiplayerTab RoomTab()
    {
        var tab = new MultiplayerTab { SuppressLobbyShow = true };
        tab.UseSessionForTests(new MultiplayerSession(new LauncherConfig()));
        return tab;
    }

    /// <summary>
    /// 55g: under the two players of a competitive 1v1, the SERVER's chance in one sentence and a
    /// bar; the rival's line carries "your record 9-2"; the heading names the format.
    /// </summary>
    [Fact]
    public void TheOneVersusOneRoomShowsTheServersOddsAndTheBalance()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = RoomTab();
                tab.ShowDemoElo("room1v1");
                var w = tab.LobbyWindowForTests;
                Assert.NotNull(w);

                Assert.Equal("JUGADORES \u00B7 COMPETITIVA 1V1", w!.PlayersListHeader.Text);

                var odds = w.RoomMembersPanel.Children.OfType<Border>().Single(b => Equals(b.Tag, "RoomOdds1v1"));
                Assert.Contains(AllText(odds), t => t == "Tienes un 62 % de probabilidad de ganar");

                var lines = AllText(w.RoomMembersPanel);
                Assert.Contains(lines, t => t.Contains("tu balance 9\u20132"));
                // The balance is about the OTHER player: never on my own row.
                Assert.Single(lines, t => t.Contains("tu balance"));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 55h: a full competitive 2v2 with one player still to pick — two columns, the NO TEAM box,
    /// the server's odds, the warning, and the reason Start is locked naming who has to pick.
    /// The host may move anybody, so every picker is live; a "?" marks who is still placing.
    /// </summary>
    [Fact]
    public void TheTeamRoomShowsBothColumnsTheOddsAndWhyStartIsLocked()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = RoomTab();
                tab.ShowDemoElo("roomteams");
                var w = tab.LobbyWindowForTests;
                Assert.NotNull(w);
                Assert.Equal("JUGADORES \u00B7 COMPETITIVA 2V2 \u00B7 4 DE 4", w!.PlayersListHeader.Text);
                Assert.Equal(MultiplayerTab.TeamRoomLeftColumnWidth, w.LobbyLeftColumnDef.Width.Value);

                var panel = w.RoomMembersPanel;
                var columns = Walk(panel).OfType<Border>().Where(b => Equals(b.Tag, MultiplayerTab.TeamColumnTag)).ToList();
                Assert.Equal(2, columns.Count);
                Assert.Equal(2, Walk(columns[0]).OfType<Grid>().Count(g => g.Tag is string id && id.StartsWith("demo-")));
                Assert.Equal(1, Walk(columns[1]).OfType<Grid>().Count(g => g.Tag is string id && id.StartsWith("demo-")));
                Assert.Contains(AllText(columns[0]), t => t.StartsWith("1534?"));

                var none = Walk(panel).OfType<Border>().Single(b => Equals(b.Tag, MultiplayerTab.NoTeamBoxTag));
                Assert.Contains(AllText(none), t => t == "Bai Yu Feng");

                var odds = Walk(panel).OfType<Border>().Single(b => Equals(b.Tag, MultiplayerTab.TeamOddsTag));
                Assert.Contains(AllText(odds), t => t == "Equipo 1: 57 % \u00B7 Equipo 2: 43 %");

                var reason = Walk(panel).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.StartReasonTag));
                Assert.Equal("No puedes empezar todav\u00eda: falta que Bai Yu Feng elija equipo.", reason.Text);

                // The host may move everybody: every picker is live.
                Assert.All(Walk(panel).OfType<Button>().Where(b => b.Content is "1" or "2"), b => Assert.True(b.IsEnabled));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Picking a team in the sample moves the player at once (a sample has no server); once
    /// everybody is on a side and the sides are even, the reason says everything is ready.
    /// </summary>
    [Fact]
    public void PickingTheLastTeamUnlocksStart()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = RoomTab();
                tab.ShowDemoElo("roomteams");
                Assert.False(tab.CurrentStartGate().CanStart);
                tab.PickTeamAsync("demo-Bai Yu Feng", 2).GetAwaiter().GetResult();
                Assert.True(tab.CurrentStartGate().CanStart);
                var w = tab.LobbyWindowForTests!;
                Assert.DoesNotContain(Walk(w.RoomMembersPanel).OfType<Border>(), b => Equals(b.Tag, MultiplayerTab.NoTeamBoxTag));
                var reason = Walk(w.RoomMembersPanel).OfType<TextBlock>().Single(t => Equals(t.Tag, MultiplayerTab.StartReasonTag));
                Assert.Equal(Strings.Get("MpStartReady"), reason.Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>A casual team room: the two columns, but no odds, no warning, nothing locked.</summary>
    [Fact]
    public void ACasualTeamRoomHasColumnsButNoOddsOrWarning()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RoomTab();
            var sample = EloDemoData.RoomTeams();
            var casual = new RoomDemoData.Sample
            {
                Name = "casual-teams", Code = sample.Code, RoomName = sample.RoomName, Seats = 4,
                Competitive = false, Players = sample.Players, Odds = sample.Odds,
                ViewerId = sample.ViewerId, ViewerStanding = sample.ViewerStanding,
            };
            tab.ShowSampleRoom(casual);
            var panel = tab.LobbyWindowForTests!.RoomMembersPanel;
            Assert.Equal(2, Walk(panel).OfType<Border>().Count(b => Equals(b.Tag, MultiplayerTab.TeamColumnTag)));
            Assert.DoesNotContain(Walk(panel).OfType<Border>(), b => Equals(b.Tag, MultiplayerTab.TeamOddsTag));
            Assert.DoesNotContain(Walk(panel).OfType<Border>(), b => Equals(b.Tag, "RoomTeamsWarning"));
            Assert.True(tab.CurrentStartGate().CanStart);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 55i: a team room counts down on a card in the left column with both line-ups written out;
    /// the chat line is not shown beside it, and the chat itself stays where it is.
    /// </summary>
    [Fact]
    public void TheTeamCountdownWritesTheLineUpsOut()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = RoomTab();
                tab.ShowDemoElo("countdown");
                var w = tab.LobbyWindowForTests!;
                Assert.Equal(Visibility.Visible, w.TeamCountdownOverlay.Visibility);
                Assert.Equal(Visibility.Collapsed, w.CountdownOverlay.Visibility);
                Assert.Equal(Visibility.Collapsed, w.LobbyLeftColumn.Visibility);
                Assert.Equal(
                    "Equipo 1: Gorgorito12 y Geaf_Argento \u00B7 Equipo 2: Lincoln y Bai Yu Feng. Elijan esto en el juego.",
                    RevealText.PlainTextOf(w.TeamCountdownTeams));
                Assert.Equal("Si no coinciden, la partida no contar\u00e1.", w.TeamCountdownFoot.Text);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>The countdown frame's teams: ids to 1 or 2; anything else is no line-up at all.</summary>
    [Fact]
    public void TheCountdownFramesTeamsAreReadOrIgnored()
    {
        using var withTeams = System.Text.Json.JsonDocument.Parse(
            "{\"type\":\"game_countdown\",\"duration_ms\":5000,\"teams\":{\"a\":1,\"b\":2,\"c\":3}}");
        var teams = MultiplayerTab.ParseCountdownTeams(withTeams.RootElement);
        Assert.NotNull(teams);
        Assert.Equal(new[] { "a", "b" }, teams!.Keys.OrderBy(k => k));
        using var none = System.Text.Json.JsonDocument.Parse("{\"type\":\"game_countdown\",\"teams\":null}");
        Assert.Null(MultiplayerTab.ParseCountdownTeams(none.RootElement));
        using var old = System.Text.Json.JsonDocument.Parse("{\"type\":\"game_countdown\"}");
        Assert.Null(MultiplayerTab.ParseCountdownTeams(old.RootElement));
    }

    /// <summary>
    /// 55j: the five cases. A streak shows the pill; the anti-farm win shows "40 %" beside the
    /// delta and the sentence after "+5 (40 %):"; the teams that did not match name the game's
    /// sides; the new-account loss reads "—" and NO PUNTUADA; the finished placement names the
    /// place.
    /// </summary>
    [Fact]
    public void TheResultCardSaysWhatEachCaseIs()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var cases = EloDemoData.ResultCases();
                Assert.Equal(new[]
                {
                    ResultCase.Streak, ResultCase.AntiFarm, ResultCase.Unrated, ResultCase.Unrated,
                    ResultCase.PlacementDone,
                }, cases.Select(c => c.Case));

                var streak = MatchResultCard.BuildCaseCard(cases[0]);
                Assert.True(Tagged(streak, "StreakPill"));
                Assert.Contains(AllText(streak), t => t == "contra Siux \u00B7 1v1");
                Assert.Contains(AllText(streak), t => t == "1598 \u2192 1612");

                var farm = AllText(MatchResultCard.BuildCaseCard(cases[1]));
                Assert.Contains(farm, t => t == "40 %");
                Assert.Contains(farm, t => t.StartsWith("+5 (40 %): 8.\u00aa victoria seguida contra este rival."));

                var mismatch = MatchResultCard.BuildCaseCard(cases[2]);
                Assert.True(Tagged(mismatch, "UnratedTag"));
                Assert.Contains(AllText(mismatch), t => t == "Gorgorito12 y Geaf_Argento contra Lincoln y Bai Yu Feng \u00B7 2v2");
                Assert.Contains(AllText(mismatch), t => t.EndsWith("(en el juego: Gorgorito12 y Lincoln contra Geaf_Argento y Bai Yu Feng)."));

                var fresh = AllText(MatchResultCard.BuildCaseCard(cases[3]));
                Assert.Contains(fresh, t => t == Strings.Get("MpDash"));
                Assert.Contains(fresh, t => t == "Esta partida no cont\u00f3 para el ELO (cuenta nueva y partida muy corta).");

                var placed = MatchResultCard.BuildCaseCard(cases[4]);
                Assert.Contains(AllText(placed), t => t == "Entras en la tabla en el puesto 7.");
                Assert.Contains(Walk(placed).OfType<FrameworkElement>(), e => e.Tag is RankAge);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>The anti-farm LOSER's line names who beat him and how many times.</summary>
    [Fact]
    public void TheAntiFarmLoserIsToldWhoBeatHimAndHowOften()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var lost = new MatchOutcomeView(MatchVerdict.Loss, "wol", null, 1200, 2, 1616, 1612,
                    "Pedro", null, 0, 0, 90)
                { EloFactor = 0.4, FarmStreak = 8, FarmRivalNames = "Pedro" };
                Assert.Equal(ResultCase.AntiFarm, lost.Case);
                Assert.Contains(AllText(MatchResultCard.BuildCaseCard(lost)),
                    t => t.StartsWith("-4 (40 %): Pedro te gan\u00f3 8 veces seguidas."));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 55k, card by card: the anti-farm win shows "· 40 %" beside "+5"; the line under the result
    /// has the hour and no span; the 2v2 that did not count names both sides, says NO PUNTUADA and
    /// why, and shows "—" with no rating move; the tournament game says TORNEO, its round, and
    /// never a percentage; the unreadable match is "Sin resultado".
    /// </summary>
    [Fact]
    public void TheHistoryCardsSayWhatEachMatchWas()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                var rows = EloDemoData.History();
                Border Card(int i) => tab.BuildHistoryRow(rows[i], EloDemoData.ViewerId);

                var farmed = Card(0);
                var farmedText = AllText(farmed);
                Assert.Contains(farmedText, t => t == "+5");
                Assert.Contains(Walk(farmed).OfType<TextBlock>(),
                    t => Equals(t.Tag, MultiplayerTab.HistoryFarmTag) && t.Text == "\u00B7 40 %");
                Assert.Contains(farmedText, t => t == "contra Aluclown \u00B7 1v1");
                Assert.Contains(farmedText, t => t == "1607 \u2192 1612");
                // Line 2 is "{mod} · {map} · {hour}": the hour the match started, no span.
                var hour = MatchHistoryView.FormatStart(
                    MatchHistoryView.ParseLocal(rows[0].StartedAt), null, Strings.Culture)!;
                Assert.Contains(farmedText, t => t.Contains("Great Plains \u00B7 " + hour));
                Assert.DoesNotContain(farmedText, t => t.Contains(" \u2013 "));
                Assert.False(Tagged(farmed, "UnratedTag"));

                var plain = Card(1);
                Assert.False(Tagged(plain, MultiplayerTab.HistoryFarmTag));
                Assert.Contains(AllText(plain), t => t == "+14");

                var mismatch = Card(2);
                var mismatchText = AllText(mismatch);
                Assert.True(Tagged(mismatch, "UnratedTag"));
                Assert.Contains(mismatchText, t => t == "NO PUNTUADA");
                Assert.Contains(mismatchText, t => t == "Gorgorito12 y Luis contra Pedro y Sara \u00B7 2v2");
                Assert.Contains(mismatchText, t => t == "Los equipos del juego no coincidieron con los de la sala.");
                Assert.Contains(mismatchText, t => t == Strings.Get("MpDash"));
                Assert.DoesNotContain(mismatchText, t => t.Contains('\u2192'));

                Assert.Contains(AllText(Card(3)),
                    t => t == "Esta partida no cont\u00f3 para el ELO (cuenta nueva y partida muy corta).");

                var cup = Card(4);
                var cupText = AllText(cup);
                Assert.True(Tagged(cup, "TournamentTag"));
                Assert.False(Tagged(cup, "UnratedTag"));
                Assert.False(Tagged(cup, MultiplayerTab.HistoryFarmTag));
                Assert.Contains(cupText, t => t.StartsWith("Copa de septiembre \u00B7 semifinal \u00B7 "));
                Assert.Contains(cupText, t => t == "1602 \u2192 1593");

                var unread = AllText(Card(5));
                Assert.Contains(unread, t => t == "Sin resultado");
                Assert.Contains(unread, t => t == "No se pudo leer el resultado: Record Game estaba desactivado.");
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A tournament game the anti-farm rule would have touched still never shows a percentage —
    /// the server never applies it to a bracket game, and 55k says so in as many words.
    /// </summary>
    [Fact]
    public void ATournamentCardNeverShowsAPercentage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var cup = EloDemoData.History()[4];
            cup.Result = 1;
            cup.RatingBefore = 1593;
            cup.RatingAfter = 1598;
            cup.EloFactor = 0.4;
            Assert.False(Tagged(tab.BuildHistoryRow(cup, EloDemoData.ViewerId), MultiplayerTab.HistoryFarmTag));
        });
        Assert.Null(error);
    }

    /// <summary>No odds from the server, no card: the launcher never works a probability out.</summary>
    [Fact]
    public void WithoutTheServersOddsThereIsNoCard()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = RoomTab();
            tab.ShowSampleRoom(RoomDemoData.OneVOne());
            var w = tab.LobbyWindowForTests;
            Assert.NotNull(w);
            Assert.DoesNotContain(w!.RoomMembersPanel.Children.OfType<Border>(), b => Equals(b.Tag, "RoomOdds1v1"));
        });
        Assert.Null(error);
    }

    // --------------------------------------------------------------- snapshots

    /// <summary>
    /// Renders the preview's scenes to PNG, in both languages and at a wide and a narrow width, for
    /// the handoff's screenshots. Does nothing unless <c>AOE3ML_ELO_PREVIEW_SNAPSHOTS</c> names a
    /// folder, like the tournament preview's snapshots.
    /// </summary>
    [Fact]
    public void Snapshots()
    {
        var folder = Environment.GetEnvironmentVariable("AOE3ML_ELO_PREVIEW_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var only = Environment.GetEnvironmentVariable("AOE3ML_ELO_PREVIEW_SCENES");

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // ONE window for everything: closing the last window ends the test application.
            var window = OffscreenWindow(1440, 1000);
            window.Show();
            try
            {
                foreach (var language in new[] { "es", "en" })
                {
                    var previous = Strings.Language;
                    Strings.SetLanguage(language);
                    try
                    {
                        // The profile's four samples (55d-55f), drawn into a panel in this window.
                        if (string.IsNullOrWhiteSpace(only)
                            || only.Split(',').Contains("profile", StringComparer.OrdinalIgnoreCase))
                        {
                            foreach (var (tag, sample) in new (string, Func<EloProfileSample>)[]
                                     {
                                         ("main", EloDemoData.Profile),
                                         ("placing", EloDemoData.ProfilePlacing),
                                         ("nogames", EloDemoData.ProfileNoGames),
                                         ("limits", EloDemoData.ProfileLimits),
                                         ("history", () => EloDemoData.Profile() with { History = EloDemoData.History() }),
                                         ("refund", () =>
                                         {
                                             var s = EloDemoData.Profile();
                                             s.Standing.Refunds = new List<RefundNotice> { EloDemoData.Refund() };
                                             return s;
                                         }),
                                     })
                            {
                                foreach (var (w, h0, size) in new[] { (1040.0, 1500.0, "wide"), (560.0, 1700.0, "narrow") })
                                {
                                    var h = h0;
                                    window.Width = w;
                                    window.Height = h;
                                    var body = new StackPanel { Margin = new Thickness(20) };
                                    window.Content = new Border
                                    {
                                        Background = (Brush)Application.Current.FindResource("MpAppBg"),
                                        Child = body,
                                    };
                                    var tab = new MultiplayerTab { ProfileBodyOverride = body };
                                    tab.ShowDemoEloProfile(sample());
                                    if (tag == "history")
                                    {
                                        // The cards are at the foot of a long page: keep only them.
                                        foreach (var child in body.Children.OfType<FrameworkElement>().ToList())
                                            if (!Equals(child.Tag, MultiplayerTab.ProfileHistoryTag)) body.Children.Remove(child);
                                    }
                                    Settle(window);
                                    Settle(window);
                                    Save(window, Path.Combine(folder, $"{language}-profile-{tag}-{size}.png"));
                                }
                            }
                        }

                        foreach (var scene in EloDemoData.Scenes)
                        {
                            var name = EloDemoData.NameOf(scene);
                            // The profile scenes are drawn above, into this window.
                            if (scene is EloPreviewScene.Profile or EloPreviewScene.Refund or EloPreviewScene.History) continue;
                            if (!string.IsNullOrWhiteSpace(only)
                                && !only.Split(',').Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                            // The room scenes: the room window is built unshown and its content
                            // moved into this one.
                            if (scene is EloPreviewScene.Room1v1 or EloPreviewScene.RoomTeams
                                or EloPreviewScene.Countdown or EloPreviewScene.Result)
                            {
                                var roomTab = RoomTab();
                                roomTab.ShowDemoElo(name);
                                var lw = roomTab.LobbyWindowForTests;
                                if (lw == null) continue;
                                var content = (UIElement)lw.Content;
                                lw.Content = null;
                                window.Width = 980;
                                window.Height = 700;
                                window.Content = new Border
                                {
                                    Background = (Brush)Application.Current.FindResource("MpAppBg"),
                                    Child = content,
                                };
                                Settle(window);
                                Settle(window);
                                Save(window, Path.Combine(folder, $"{language}-{name}.png"));
                                continue;
                            }
                            if (!string.IsNullOrWhiteSpace(only)
                                && !only.Split(',').Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                            foreach (var (w, h, tag) in new[] { (1440.0, 1000.0, "wide"), (1100.0, 900.0, "narrow") })
                            {
                                window.Width = w;
                                window.Height = h;
                                var tab = new MultiplayerTab();
                                window.Content = tab;
                                if (tag == "narrow") tab.RankingWidthOverride = 540;
                                tab.ShowDemoElo(name);
                                Settle(window);
                                Settle(window);
                                Save(window, Path.Combine(folder, $"{language}-{name}-{tag}.png"));
                                if (scene is EloPreviewScene.Ranking)
                                {
                                    // The Teams side: the double shield, the viewer placing 2/5.
                                    tab.RankingModeTeam.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                                    Settle(window);
                                    Save(window, Path.Combine(folder, $"{language}-{name}-teams-{tag}.png"));
                                }
                            }
                        }
                    }
                    finally { Strings.SetLanguage(previous); }
                }
            }
            finally
            {
                // Empty it while the window is alive, so whatever it held is UNLOADED: a room's
                // title bar subscribes to the language change on Loaded and only unsubscribes on
                // Unloaded, and left subscribed it would be called from the next test's thread.
                window.Content = null;
                Settle(window);
                window.Close();
            }
        });
        Assert.Null(error);
    }

    // --------------------------------------------------------------- helpers

    internal static Window OffscreenWindow(double width, double height) => new()
    {
        Width = width,
        Height = height,
        WindowStyle = WindowStyle.None,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = -30000,
        Top = -30000,
    };

    /// <summary>Everything the dispatcher has queued — layout passes, Loaded handlers.</summary>
    internal static void Settle(Window window)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    internal static void Save(Window window, string file)
    {
        var target = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap(
            Math.Max(1, (int)target.ActualWidth), Math.Max(1, (int)target.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(target);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        png.Save(fs);
    }

    private static bool Tagged(DependencyObject root, string tag)
        => Walk(root).OfType<FrameworkElement>().Any(e => Equals(e.Tag, tag));

    private static System.Collections.Generic.IEnumerable<string> Texts(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(t => t.Text);

    /// <summary>Every TextBlock's shown text, Runs included (a Run-built block's Text is empty).</summary>
    private static List<string> AllText(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
