using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The SEASON PREVIEW (Settings → Developer, <c>--demo-seasons</c>): the season surfaces drawn by
/// the real code from <see cref="SeasonDemoData"/>, so their art and layout can be judged before
/// the first season ends.
///
/// <para>A fixture like this does not fail by throwing. It fails by CONTRADICTING ITSELF — a medal
/// on the live ladder that the ended table does not back — or by quietly losing the case it exists
/// to show, and then a design decision gets taken on a picture that could never happen. Most of
/// what is pinned here is that. The rest is the preview's promise to change nothing: the sample
/// profile is only ever borrowed, and the sample notification never reaches the config.</para>
/// </summary>
[Collection("wpf-and-language")]
public class SeasonPreviewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // ── the fixture ──

    [Fact]
    public void EveryScene_IsReachableByItsName_AndLabelledInBothLanguages()
    {
        Assert.Equal(7, SeasonDemoData.Scenes.Count);
        Assert.Equal(7, SeasonDemoData.Scenes.Select(SeasonDemoData.NameOf).Distinct().Count());
        foreach (var scene in SeasonDemoData.Scenes)
        {
            var name = SeasonDemoData.NameOf(scene);
            Assert.Equal(scene, SeasonDemoData.SceneByName(name));
            // Typed on a command line: case and spaces are forgiven.
            Assert.Equal(scene, SeasonDemoData.SceneByName("  " + name.ToUpperInvariant() + " "));

            // Both languages, not one: GetIn falls back to English, so a missing Spanish entry
            // would read as the English one here and pass a test that only looked for "a value".
            var key = SeasonDemoData.LabelKeyOf(scene);
            Assert.NotEqual(key, Strings.GetIn(Strings.LangEn, key));
            Assert.NotEqual(Strings.GetIn(Strings.LangEn, key), Strings.GetIn(Strings.LangEs, key));
        }

        Assert.Equal(SeasonPreviewScene.Ranking, SeasonDemoData.SceneByName(null));
        Assert.Equal(SeasonPreviewScene.Ranking, SeasonDemoData.SceneByName(""));
        Assert.Equal(SeasonPreviewScene.Ranking, SeasonDemoData.SceneByName("no-such-scene"));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A medal is a claim about a finish. Every medal the preview puts on the
    /// running ladder has to be backed by the ended table it names — or the preview shows a feature
    /// contradicting itself, and the first thing anybody judging it would do is open that table.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryMedalOnTheLadderIsAFinishTheTablesShow()
    {
        var c = SeasonDemoData.Community(StatsDemoData.PrimaryModId, null, firstDay: false);
        var rows = c.Leaderboard.Concat(c.LeaderboardTeam!).ToList();
        var medalled = rows.Where(r => r.SeasonTitle != null).ToList();
        Assert.NotEmpty(medalled);

        foreach (var row in medalled)
        {
            var title = row.SeasonTitle!;
            Assert.InRange(title.Place, 1, 3);
            var table = SeasonDemoData.SeasonTable(title.Season);
            var ladder = title.IsTeam ? table.LeaderboardTeam : table.Leaderboard;
            Assert.Equal(row.UserId, ladder.Single(r => r.Rank == title.Place).UserId);
        }

        // And the other way round: nobody who finished on a podium is missing his medal.
        foreach (var season in new[] { 1, 2 })
        {
            var table = SeasonDemoData.SeasonTable(season);
            foreach (var podium in table.Leaderboard.Concat(table.LeaderboardTeam).Where(r => r.Rank <= 3))
            {
                var live = c.Leaderboard.SingleOrDefault(r => r.UserId == podium.UserId);
                if (live != null) Assert.NotNull(live.SeasonTitle);
            }
        }

        // A player has ONE medal, whichever of the two tables he is standing on.
        foreach (var team in c.LeaderboardTeam!)
        {
            var solo = c.Leaderboard.SingleOrDefault(r => r.UserId == team.UserId);
            if (solo == null) continue;
            Assert.Equal(solo.SeasonTitle?.Season, team.SeasonTitle?.Season);
            Assert.Equal(solo.SeasonTitle?.Place, team.SeasonTitle?.Place);
            Assert.Equal(solo.SeasonTitle?.Mode, team.SeasonTitle?.Mode);
        }
    }

    [Fact]
    public void TheMedalIsTheNewestSeason_ThenTheBetterPlace()
    {
        SeasonTitleInfo? Of(string name) => SeasonDemoData.MedalOf(SeasonDemoData.IdOf(name));

        // A NEWER bronze beats an OLDER gold: Gommiustan won Season 1's team table and was third
        // in Season 2's. The server shows the most recent finish, and so must the preview.
        var newer = Of("Gommiustan")!;
        Assert.Equal((2, 3, true), (newer.Season, newer.Place, newer.IsTeam));

        // Inside one season the better place: the champion was also second on the team table.
        var champion = Of(SeasonDemoData.ViewerName)!;
        Assert.Equal((2, 1, false), (champion.Season, champion.Place, champion.IsTeam));

        // An old medal is still a medal.
        var old = Of("Aluclown")!;
        Assert.Equal((1, 1, false), (old.Season, old.Place, old.IsTeam));

        // Fourth is not a podium.
        Assert.Null(Of("Maluma"));
    }

    [Fact]
    public void TheLadderShowsEverythingTheDesignHasToBeJudgedOn()
    {
        var c = SeasonDemoData.Community(StatsDemoData.PrimaryModId, null, firstDay: false);
        var titles = c.Leaderboard.Where(r => r.SeasonTitle != null).Select(r => r.SeasonTitle!).ToList();

        // All three metals, both ladders, both ended seasons.
        Assert.Equal(new[] { 1, 2, 3 }, titles.Select(t => t.Place).Distinct().OrderBy(p => p));
        Assert.Contains(titles, t => t.IsTeam);
        Assert.Contains(titles, t => !t.IsTeam);
        Assert.Contains(titles, t => t.Season == 1);
        Assert.Contains(titles, t => t.Season == 2);

        // A medal on an ordinary row past the TOP 5 block, where it has to read on its own.
        Assert.Contains(c.Leaderboard, r => r.Rank > MultiplayerTab.RankingTop5Count && r.SeasonTitle != null);

        // The longest name on the table wears one: the medal's cost in width has to be visible.
        var longest = c.Leaderboard.OrderByDescending(r => r.DisplayName.Length).First();
        Assert.NotNull(longest.SeasonTitle);

        // And most rows have none, which is what a ladder looks like.
        Assert.True(c.Leaderboard.Count(r => r.SeasonTitle == null) > c.Leaderboard.Count / 2);

        // A team ladder, or the TEAMS tab would not exist in the preview at all.
        Assert.NotEmpty(c.LeaderboardTeam!);
        Assert.Equal(c.LeaderboardTeam!.Count, c.RankedPlayersTeam);

        // Two ended seasons and a running one: the selector has a choice to offer.
        Assert.True(SeasonView.OffersAChoice(c.Season));
    }

    [Fact]
    public void TheFirstDayKeepsTheCalendar_AndEmptiesTheRunningTables()
    {
        var c = SeasonDemoData.Community(StatsDemoData.PrimaryModId, null, firstDay: true);
        Assert.Empty(c.Leaderboard);

        // Empty, never null: null is "this server has no team ladder", which hides the TEAMS tab.
        Assert.NotNull(c.LeaderboardTeam);
        Assert.Empty(c.LeaderboardTeam!);
        Assert.Equal(0, c.RankedPlayers);
        Assert.Equal(0, c.RankedPlayersTeam);

        // The ended seasons are still there to be looked at.
        Assert.True(SeasonView.OffersAChoice(c.Season));
        Assert.NotEmpty(SeasonDemoData.SeasonTable(SeasonDemoData.EndedSeason).Leaderboard);
    }

    [Fact]
    public void TheProfileAgreesWithTheTables_AndHasEveryKindOfSeasonLine()
    {
        var p = SeasonDemoData.Profile();
        var lines = SeasonView.ProfileLines(p.Standing);
        Assert.Equal(4, lines.Count);

        foreach (var line in lines)
        {
            var table = SeasonDemoData.SeasonTable(line.Season);
            var ladder = line.IsTeam ? table.LeaderboardTeam : table.Leaderboard;
            var row = ladder.Single(r => r.UserId == p.User.Id);
            Assert.Equal(row.Rank, line.Place);
            Assert.Equal(ladder.Count, line.Size);
            Assert.Equal(row.Rating, line.Rating);
        }

        Assert.Contains(lines, l => l.Place <= 3);   // a line with a medal
        Assert.Contains(lines, l => l.Place > 3);    // and one without
        Assert.Contains(lines, l => l.IsTeam);

        // The header's medal is the server's choice among them.
        var medal = SeasonDemoData.MedalOf(p.User.Id)!;
        Assert.Equal((medal.Season, medal.Place), (p.Standing.SeasonTitle!.Season, p.Standing.SeasonTitle.Place));

        // The running season, with its own record.
        Assert.Equal(StatsDemoData.DemoSeason().Current, p.Standing.Season);
        Assert.NotNull(p.Standing.SeasonWins);
        Assert.NotNull(p.Standing.SeasonLosses);

        // No badge choice: the selector stays hidden, so nothing in the preview can be POSTed.
        Assert.Null(p.Standing.BadgeMode);

        // And he stands on the live ladder where his place says he does.
        var live = SeasonDemoData.Community(StatsDemoData.PrimaryModId, null, firstDay: false);
        Assert.Equal(p.Standing.LadderRank, live.Leaderboard.Single(r => r.UserId == p.User.Id).Rank);
    }

    [Fact]
    public void TheProfileHistoryChainsToTodaysRating_AndKeepsALateResult()
    {
        var p = SeasonDemoData.Profile();
        var current = p.Standing.Season!.Value;
        var running = SeasonView.RowsOfSeason(p.History, current)!;
        var rated = running.Where(MatchHistoryView.IsRated).ToList();

        // A curve, not a line — and every rated match starts where the one after it in the list
        // (the one before it in time) ended, finishing on today's rating.
        Assert.True(rated.Count >= 3);
        Assert.Equal(p.Standing.Rating, rated[0].RatingAfter);
        for (var i = 0; i + 1 < rated.Count; i++)
            Assert.Equal(rated[i + 1].RatingAfter, rated[i].RatingBefore);

        // The season's curve never reaches into the season before.
        Assert.All(running, r => Assert.Equal(current, r.Season));

        // A match decided after its season ended: result kept, nobody's rating moved.
        var late = Assert.Single(p.History, r => r.UnratedReason == "season_closed");
        Assert.Equal(current - 1, late.Season);
        Assert.NotEqual(MatchVerdict.NoResult, MatchOutcomeView.Classify(late.Result));
        Assert.Null(late.RatingAfter);
    }

    [Fact]
    public void TheBellNamesBothFinishesOfTheEndedSeason()
    {
        var plan = SeasonDemoData.EndedNotice();
        Assert.Equal(SeasonNoticeStep.Ring, plan.Step);
        Assert.Equal(SeasonDemoData.EndedSeason, plan.Ended);
        Assert.Equal(SeasonDemoData.EndedSeason + 1, plan.Current);
        Assert.Equal(new[] { false, true }, plan.Places.Select(p => p.IsTeam));

        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var (title, body) = SeasonNotice.Text(plan);
            Assert.Equal("Season 2 is over", title);
            Assert.Contains("#1 of 16", body);
            Assert.Contains("#2 of 10", body);

            Strings.Language = Strings.LangEs;
            Assert.Equal("Terminó la Temporada 2", SeasonNotice.Text(plan).Title);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    [Fact]
    public void ThePlayersSampleWearsTheLaddersOwnMedals()
    {
        var players = SeasonDemoData.Players();
        var live = SeasonDemoData.Community(StatsDemoData.PrimaryModId, null, firstDay: false);
        foreach (var p in players)
        {
            var row = live.Leaderboard.Single(r => r.UserId == p.UserId);
            Assert.Equal(row.Rating, p.Rating);
            Assert.Equal(row.Rank, p.LadderRank);
            Assert.Equal(row.SeasonTitle?.Season, p.SeasonTitle?.Season);
            Assert.Equal(row.SeasonTitle?.Place, p.SeasonTitle?.Place);
        }

        Assert.Equal(new[] { "idle", "in_game", "in_room" }, players.Select(p => p.Status).Distinct().OrderBy(s => s));
        Assert.Equal(new[] { 1, 2, 3 },
            players.Where(p => p.SeasonTitle != null).Select(p => p.SeasonTitle!.Place).Distinct().OrderBy(x => x));
        Assert.Contains(players, p => p.SeasonTitle == null);
        Assert.Contains(players, p => p.SeasonTitle is { IsTeam: true });
        // A name too long for the panel's column, with a medal after it.
        Assert.Contains(players, p => p.Login.Length >= 20 && p.SeasonTitle != null);
    }

    [Fact]
    public void TheRoomPreviewWearsMedals_AndKeepsPlayersWithout()
    {
        var full = RoomDemoData.Full();
        Assert.Equal(new[] { 1, 2, 3 },
            full.Players.Where(p => p.SeasonTitle != null).Select(p => p.SeasonTitle!.Place).Distinct().OrderBy(x => x));
        Assert.Contains(full.Players, p => p.SeasonTitle is { IsTeam: true });
        Assert.Contains(full.Players, p => p.SeasonTitle == null);

        // The name that cannot fit has to give the medal its room, so it carries one.
        Assert.NotNull(RoomDemoData.LongName().Players.Single().SeasonTitle);
    }

    // ── the bell: a preview notification is never saved ──

    /// <summary>
    /// THE ONE THAT MATTERS for the preview's promise. The sample notification is in the bell like
    /// any other, and every path that writes the config — reading it, marking all read, a real one
    /// arriving after it — must still write exactly the real history.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_APreviewNotificationNeverReachesTheConfig()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config, persist: () => { });
        center.RaiseSeasonEnded(1, "Season 1 is over", "real");
        Assert.True(center.AddPreview(NotificationKind.SeasonEnded, "Season 2 is over", "preview", targetId: "2"));
        Assert.Equal(2, center.Items.Count);
        Assert.Equal(2, center.UnreadCount);

        center.MarkRead(center.Items[0]);
        Assert.DoesNotContain(config.Notifications, n => n.Body == "preview");
        center.MarkAllRead();
        Assert.DoesNotContain(config.Notifications, n => n.Body == "preview");
        center.RaiseSeasonEnded(3, "Season 3 is over", "real again");
        Assert.DoesNotContain(config.Notifications, n => n.Body == "preview");
        Assert.Equal(2, config.Notifications.Count);

        // A launcher started from that config has never heard of it.
        var restarted = new NotificationCenter(config, persist: () => { });
        Assert.DoesNotContain(restarted.Items, i => i.Body == "preview");

        // And the flag itself is not part of the file's shape.
        var json = JsonSerializer.Serialize(new NotificationItem { Title = "t", IsPreview = true });
        Assert.DoesNotContain("review", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APreviewNeverPushesARealNotificationOutOfAFullHistory()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config, persist: () => { });
        for (var i = 1; i <= NotificationCenter.MaxItems; i++) center.RaiseSeasonEnded(i, "S" + i, "real");

        center.AddPreview(NotificationKind.SeasonEnded, "preview", "preview");
        center.RaiseSeasonEnded(999, "newest", "real");

        Assert.Equal(NotificationCenter.MaxItems, center.Items.Count(i => !i.IsPreview));
        Assert.Contains(center.Items, i => i.IsPreview);
        Assert.Equal(NotificationCenter.MaxItems, config.Notifications.Count);
        // The one dropped is the OLDEST real one, never the preview.
        Assert.DoesNotContain(center.Items, i => i.Title == "S1");
        Assert.Contains(center.Items, i => i.Title == "S2");
    }

    [Fact]
    public void APreviewRings_ButNeverToasts()
    {
        var center = new NotificationCenter(new LauncherConfig(), persist: () => { });
        NotificationItem? rung = null;
        var toasts = 0;
        center.ItemAdded += (_, item) => rung = item;
        center.ToastRequested += (_, _) => toasts++;

        Assert.True(center.AddPreview(NotificationKind.SeasonEnded, "Season 2 is over", "body", targetId: "2"));
        Assert.NotNull(rung);
        Assert.True(rung!.IsPreview);
        Assert.Equal("2", rung.TargetId);
        Assert.Equal(0, toasts);

        Assert.False(center.AddPreview(NotificationKind.SeasonEnded, "  ", "body"));
    }

    /// <summary>
    /// Every kind has a glyph of its own in the bell's FALLBACK — the icon a row shows when it
    /// carries no mod, or a mod that left the catalog. The season's bell carries no mod, and with
    /// no trigger there it fell back to the plain bell, so "Season 2 is over" read as an ordinary
    /// update. And where the corner badge has a glyph for a kind too, the two must agree.
    /// </summary>
    [Fact]
    public void EveryKindHasItsOwnGlyph_WhereverItFallsBack()
    {
        var doc = XDocument.Load(RepoFile("MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        bool BindsKind(XElement e) => e.Descendants(ns + "DataTrigger")
            .Any(t => (string?)t.Attribute("Binding") == "{Binding Kind}");

        static string SetterOf(XElement trigger, string property)
            => (string)trigger.Elements().First(s => (string?)s.Attribute("Property") == property).Attribute("Value")!;

        Dictionary<string, (string Glyph, string Colour)> GlyphsUnder(XElement root)
            => root.Descendants(ns + "DataTrigger")
                .Where(t => (string?)t.Attribute("Binding") == "{Binding Kind}")
                .ToDictionary(t => (string)t.Attribute("Value")!,
                              t => (SetterOf(t, "Text"), SetterOf(t, "Foreground")));

        var fallback = doc.Descendants(ns + "TextBlock").Single(t =>
            ((string?)t.Attribute("Visibility") ?? "").Contains("ConverterParameter=invert") && BindsKind(t));
        var badge = doc.Descendants(ns + "Grid").Single(g =>
            (string?)g.Attribute("Visibility") == "{Binding ModId, Converter={StaticResource ModIconPresentConv}}"
            && BindsKind(g));

        var fallbackGlyphs = GlyphsUnder(fallback);
        foreach (var kind in Enum.GetNames(typeof(NotificationKind)))
            Assert.True(fallbackGlyphs.ContainsKey(kind),
                $"{kind} has no glyph of its own in the bell's fallback, so it shows the plain bell.");

        foreach (var (kind, glyph) in GlyphsUnder(badge))
            Assert.Equal(glyph, fallbackGlyphs[kind]);
    }

    // ── the result card ──

    [Theory]
    [InlineData("season_closed", true)]
    [InlineData("game_crashed", true)]
    [InlineData("no_decided_result", false)]
    [InlineData("not_1v1", false)]
    [InlineData("not_competitive", false)]
    [InlineData(null, false)]
    public void OnlyAKeptResultExplainsItselfBesideItsVerdict(string? reason, bool kept)
        => Assert.Equal(kept, MatchOutcomeView.KeptResultButMovedNothing(reason));

    /// <summary>
    /// A win whose season had ended by the time it was decided: the card says Victory AND why no
    /// rating moved. The note used to be reachable only from a match with no result, so this card
    /// showed a win beside a rating that did not change, with no word about it.
    /// </summary>
    [Fact]
    public void AWinFromAnEndedSeasonSaysWhyNoRatingMoved()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                MatchOutcomeView Model(string? reason) => new(
                    MatchVerdict.Win, "wol", "ESOC_Tibet", 1500, 2,
                    RatingBefore: null, RatingAfter: null,
                    RivalLogin: "NathanR06", RivalRating: null,
                    Wins: 4, Losses: 1, Rd: 60, UnratedReason: reason);
                var note = Strings.Get("MpResultUnratedSeasonClosed");

                var late = MatchResultCard.Build(Model("season_closed"), new MatchResultCard.Actions(null, null));
                Assert.Contains(Walk(late).OfType<TextBlock>(), t => t.Text == note);

                var rated = MatchResultCard.Build(Model(null), new MatchResultCard.Actions(null, null));
                Assert.DoesNotContain(Walk(rated).OfType<TextBlock>(), t => t.Text == note);
            });
            Assert.Null(error);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    // ── the scenes, drawn by the real code ──

    [Fact]
    public void TheRankingScene_IsTheRealPage_AndSaysItIsSampleData()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoSeasons("ranking");

                Assert.Contains("Season 3", tab.RankingSubtitleText.Text);
                Assert.Equal(Visibility.Visible, tab.RankingSeasonCombo.Visibility);
                Assert.Contains(Walk(tab.RankingBody), d => d is FrameworkElement { Tag: SeasonTitleInfo });

                FrameworkElement Chip() => tab.RankingScopeChips.Children.OfType<FrameworkElement>()
                    .Single(e => Equals(e.Tag, MultiplayerTab.PreviewChipTag));
                Assert.Contains(Walk(Chip()).OfType<TextBlock>(), t => t.Text == "Sample data");

                // A repaint does not stack a second chip.
                tab.ShowDemoSeasons("ranking");
                Assert.NotNull(Chip());
            });
            Assert.Null(error);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    [Fact]
    public void TheFinalScene_IsTheTableTheMedalsCameFrom()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoSeasons("final");

                Assert.Contains("Season 2", tab.RankingSubtitleText.Text);
                Assert.Contains("final", tab.RankingSubtitleText.Text);
                // Its champion is the player wearing the Season 2 gold on the running table.
                Assert.Contains(Walk(tab.RankingBody).OfType<TextBlock>(), t => t.Text == SeasonDemoData.ViewerName);
                // An ended table carries no medals: the server sends none with it.
                Assert.DoesNotContain(Walk(tab.RankingBody), d => d is FrameworkElement { Tag: SeasonTitleInfo });
            });
            Assert.Null(error);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    [Fact]
    public void TheFirstDayScene_HasNoRows_AndTheRankingSceneBringsThemBack()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoSeasons("first-day");
            Assert.DoesNotContain(Walk(tab.RankingBody), d => d is FrameworkElement { Tag: RankAge });
            Assert.NotEmpty(tab.RankingBody.Children.OfType<TextBlock>());

            tab.ShowDemoSeasons("ranking");
            Assert.Contains(Walk(tab.RankingBody), d => d is FrameworkElement { Tag: RankAge });
        });
        Assert.Null(error);
    }

    [Fact]
    public void ThePlayersScene_SaysItIsAPreview_AndSurvivesALivePresenceFrame()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoSeasons("players");

            // Signed out — as here — nothing else would show the rooms browser, and the scene
            // came up as an empty page: the panel full of medals, inside a parent nobody showed.
            Assert.Equal(Visibility.Visible, tab.RoomsView.Visibility);
            Assert.Equal(Visibility.Visible, tab.BrowserPanel.Visibility);
            // And the side column on the Players tab: it opens on the chat otherwise.
            Assert.Equal(Visibility.Visible, tab.PlayersScroll.Visibility);

            var panel = tab.PlayersPanel;
            Assert.Equal(MultiplayerTab.SeasonPreviewNoticeTag, ((FrameworkElement)panel.Children[0]).Tag);
            var medals = Walk(panel).Count(d => d is FrameworkElement { Tag: SeasonTitleInfo });
            Assert.True(medals >= 5, $"only {medals} medals in the players panel");

            // A real frame lands while the preview is up: the sample stays.
            using var frame = JsonDocument.Parse(
                "{\"onlineUsers\":[{\"userId\":\"x\",\"login\":\"someone-real\",\"status\":\"idle\"}]}");
            typeof(MultiplayerTab).GetMethod("ParseOnlineUsers", Private)!
                .Invoke(tab, new object[] { frame.RootElement });
            Assert.DoesNotContain(Walk(panel).OfType<TextBlock>(), t => t.Text == "someone-real");
            Assert.Equal(medals, Walk(panel).Count(d => d is FrameworkElement { Tag: SeasonTitleInfo }));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the profile. The sample is BORROWED for the render: the standing
    /// and the history the rest of the tab reads — the account chip, my own row in a room, the rank
    /// guide — are exactly the real ones before and after, on the first render and on every later
    /// one (a language switch, a fetch landing).
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheSampleProfileNeverReachesTheRealStanding()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var window = new ProfileWindow();
            Set(tab, "_profileWindow", window);
            var real = new EloSnapshot { Rating = 1234, Rd = 80, Season = 3 };
            var realRows = new List<MatchHistoryRow>();
            Set(tab, "_cachedStanding", real);
            Set(tab, "_historyRows", realRows);

            tab.ShowDemoSeasons("profile");

            var body = window.ProfileBody;
            Assert.Equal(MultiplayerTab.SeasonPreviewNoticeTag, ((FrameworkElement)body.Children[0]).Tag);
            Assert.Contains(body.Children.OfType<FrameworkElement>(), e => Equals(e.Tag, "ProfileSeasonsCard"));
            Assert.Contains(Walk(body).OfType<TextBlock>(), t => t.Text == SeasonDemoData.ViewerName);
            Assert.Contains(Walk(body), d => d is FrameworkElement { Tag: SeasonTitleInfo });

            Assert.Same(real, Get(tab, "_cachedStanding"));
            Assert.Same(realRows, Get(tab, "_historyRows"));
            Assert.False((bool)Get(tab, "_renderingPreviewProfile")!);

            Invoke(tab, "RenderProfileTab");
            Assert.Equal(MultiplayerTab.SeasonPreviewNoticeTag, ((FrameworkElement)body.Children[0]).Tag);
            Assert.Same(real, Get(tab, "_cachedStanding"));
            Assert.Same(realRows, Get(tab, "_historyRows"));

            window.Close();
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheSettingsRowListsEveryScene_InBothLanguages_OpeningOnTheRanking()
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { Strings.LangEs, Strings.LangEn })
            {
                Strings.Language = lang;
                var error = DialogXamlTests.RunOnStaThread(() =>
                {
                    var dlg = new LauncherSettingsDialog(new LauncherConfig());
                    var items = dlg.DemoSeasonsCombo.Items.OfType<ComboBoxItem>().ToList();
                    Assert.Equal(SeasonDemoData.Scenes.Select(SeasonDemoData.NameOf), items.Select(i => (string)i.Tag));
                    Assert.All(items, i => Assert.DoesNotContain("SettingsDemoSeasons", (string)i.Content));
                    Assert.Equal("ranking", (string)((ComboBoxItem)dlg.DemoSeasonsCombo.SelectedItem).Tag);
                    Assert.False(string.IsNullOrWhiteSpace(dlg.DemoSeasonsTitle.Text));
                    Assert.False(string.IsNullOrWhiteSpace(dlg.DemoSeasonsHint.Text));
                    dlg.Close();
                });
                Assert.Null(error);
            }
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    // ── helpers ──

    private static void Set(MultiplayerTab tab, string field, object? value)
        => typeof(MultiplayerTab).GetField(field, Private)!.SetValue(tab, value);

    private static object? Get(MultiplayerTab tab, string field)
        => typeof(MultiplayerTab).GetField(field, Private)!.GetValue(tab);

    private static object? Invoke(MultiplayerTab tab, string method)
        => typeof(MultiplayerTab).GetMethod(method, Private)!.Invoke(tab, null);

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

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
