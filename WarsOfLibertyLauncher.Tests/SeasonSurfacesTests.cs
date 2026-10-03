using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Rating seasons on screen: the medal after a name, an ended season's table, the profile's
/// season history, and the bell's own kind. The code-built pieces are checked by nothing at
/// compile time, and the ranking's past tables are only ever drawn once somebody picks a season.
/// </summary>
[Collection("wpf-and-language")]
public class SeasonSurfacesTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // ── the medal ──

    [Fact]
    public void NoTitleOrNoTopThreeFinishDrawsNoMedal()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            Assert.Null(SeasonTitleBadge.Build(null, 16));
            Assert.Null(SeasonTitleBadge.Build(new SeasonTitleInfo { Season = 1, Place = 4 }, 16));
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData(1, "SeasonMedalGold")]
    [InlineData(2, "SeasonMedalSilver")]
    [InlineData(3, "SeasonMedalBronze")]
    public void EachPlaceWearsItsMetal_AndTheSeasonNumber(int place, string fillKey)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var title = new SeasonTitleInfo { Season = 2, Place = place, Mode = "default" };
            var medal = SeasonTitleBadge.Build(title, 16)!;
            Assert.Same(title, medal.Tag);
            Assert.False(medal.Tag is RankAge);   // never mistaken for a rank badge
            var disc = Walk(medal).OfType<Ellipse>().Single();
            Assert.Same(Application.Current.FindResource(fillKey), disc.Fill);
            Assert.Equal("2", Walk(medal).OfType<TextBlock>().Single().Text);
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheMedalSaysWhatItIsFor_InBothLanguages()
    {
        var previous = Strings.Language;
        try
        {
            var team = new SeasonTitleInfo { Season = 1, Place = 1, Mode = "team" };
            Strings.Language = Strings.LangEn;
            Assert.Equal("1st place in Season 1 (Teams)", SeasonTitleBadge.TooltipText(team));
            Strings.Language = Strings.LangEs;
            Assert.Equal("1.er puesto de la Temporada 1 (Equipos)", SeasonTitleBadge.TooltipText(team));
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    // ── the ranking ──

    /// <summary>
    /// THE ONE THAT MATTERS. An ended season's table is cut by ITS OWN size: a badge on a past
    /// table is the age that player finished at, and today's ladder would hand him another one.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_APastTableCutsItsBadgesByItsOwnSize()
    {
        // The precondition that makes the test able to see the bug at all.
        Assert.NotEqual(RankAges.For(2, 4), RankAges.For(2, 18));

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithPastSeasonTable(out _);
            var rows = RankingRows(tab.RankingBody).ToList();
            Assert.Equal(4, rows.Count);
            var second = rows[1].Children.OfType<FrameworkElement>().Single(e => e.Tag is RankAge);
            Assert.Equal(RankAges.For(2, 4), second.Tag);
        });
        Assert.Null(error);
    }

    [Fact]
    public void ThePastTableSaysWhichSeasonItIs_AndHidesTodaysMatches()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var tab = TabWithPastSeasonTable(out _);
                Assert.Contains("Season 1", tab.RankingSubtitleText.Text);
                Assert.Contains("4 players finished", tab.RankingSubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, tab.RankingHistoryCard.Visibility);
                Assert.Equal(Visibility.Collapsed, tab.RankingScopeWindowChip.Visibility);

                // The selector: newest first, the running season marked.
                Assert.Equal(Visibility.Visible, tab.RankingSeasonCombo.Visibility);
                var items = tab.RankingSeasonCombo.Items.OfType<ComboBoxItem>().ToList();
                Assert.Equal(new[] { 3, 2, 1 }, items.Select(i => (int)i.Tag));
                Assert.Equal("Season 3 (current)", items[0].Content);
                Assert.Equal(1, (int)((ComboBoxItem)tab.RankingSeasonCombo.SelectedItem).Tag);
            });
            Assert.Null(error);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    [Fact]
    public void TheLiveTableNamesTheRunningSeason_AndWearsItsMedals()
    {
        var previous = Strings.Language;
        try
        {
            Strings.Language = Strings.LangEn;
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var tab = new MultiplayerTab();
                Set(tab, "_communityStats", StatsDemoData.Community());
                Invoke(tab, "RenderRanking");
                Assert.Contains("Season 3", tab.RankingSubtitleText.Text);
                Assert.Contains("until", tab.RankingSubtitleText.Text);
                // The demo ladder carries a gold medal on its first row.
                var first = RankingRows(tab.RankingBody).First();
                Assert.Contains(Walk(first), d => d is FrameworkElement { Tag: SeasonTitleInfo { Place: 1 } });
            });
            Assert.Null(error);
        }
        finally
        {
            Strings.Language = previous;
        }
    }

    /// <summary>
    /// A backend older than seasons: no selector, no season in the subtitle — the page exactly
    /// as it was.
    /// </summary>
    [Fact]
    public void WithoutACalendarThePageIsWhatItWas()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var stats = StatsDemoData.Community();
            stats.Season = null;
            Set(tab, "_communityStats", stats);
            Invoke(tab, "RenderRanking");
            Assert.Equal(Visibility.Collapsed, tab.RankingSeasonCombo.Visibility);
            Assert.DoesNotContain("·", tab.RankingSubtitleText.Text);
        });
        Assert.Null(error);
    }

    // ── the profile ──

    [Fact]
    public void TheProfileListsEndedSeasons_AndHasNoCardWithoutThem()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Set(tab, "_cachedStanding", new EloSnapshot { Season = 2, PastSeasons = new List<PastSeasonEntry>() });
            Assert.Null(Invoke(tab, "BuildProfileSeasons"));

            Set(tab, "_cachedStanding", new EloSnapshot
            {
                Season = 2,
                PastSeasons = new List<PastSeasonEntry>
                {
                    new() { Season = 1, Mode = "default", Place = 3, Size = 18, Rating = 1612, Wins = 9, Losses = 4 },
                },
            });
            var card = (FrameworkElement)Invoke(tab, "BuildProfileSeasons")!;
            var texts = Walk(card).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(texts, t => t.Contains("#3") && t.Contains("18"));
            Assert.Contains(Walk(card), d => d is FrameworkElement { Tag: SeasonTitleInfo });
            Assert.Contains(Walk(card), d => d is FrameworkElement { Tag: RankAge });
        });
        Assert.Null(error);
    }

    // ── the room roster and the Players panel ──

    [Fact]
    public void TheRosterWearsTheMedal_AndTheLiveLineStillFindsItsRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var type = typeof(MultiplayerTab).GetNestedType("RoomMemberEntry", BindingFlags.NonPublic)!;
            object Member(SeasonTitleInfo? title)
            {
                var m = Activator.CreateInstance(type, nonPublic: true)!;
                type.GetProperty("UserId")!.SetValue(m, "u2");
                type.GetProperty("Login")!.SetValue(m, "rival");
                type.GetProperty("SeasonTitle")!.SetValue(m, title);
                return m;
            }
            FrameworkElement Row(SeasonTitleInfo? title)
                => (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildMemberRow", Private)!
                    .Invoke(tab, new[] { Member(title) })!;

            var plain = Row(null);
            var medalled = Row(new SeasonTitleInfo { Season = 1, Place = 2 });
            Assert.DoesNotContain(Walk(plain), d => d is FrameworkElement { Tag: SeasonTitleInfo });
            Assert.Contains(Walk(medalled), d => d is FrameworkElement { Tag: SeasonTitleInfo });

            // The name gives up the medal's width.
            double NameCap(FrameworkElement row) => Walk(row).OfType<TextBlock>().Single(t => t.Text == "rival").MaxWidth;
            Assert.True(NameCap(medalled) < NameCap(plain));

            // RefreshRosterLiveCells reads a TextBlock with a string Tag straight inside the
            // row's StackPanel; the medal must not move or imitate it.
            var grid = (Grid)((Border)medalled).Child;
            var liveLines = grid.Children.OfType<StackPanel>()
                .SelectMany(s => s.Children.OfType<TextBlock>())
                .Where(t => t.Tag is string)
                .ToList();
            Assert.Equal("u2", Assert.Single(liveLines).Tag);
        });
        Assert.Null(error);
    }

    [Fact]
    public void ThePlayersPanelWearsTheMedal_WithoutGrowingTheRow()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var users = (System.Collections.IList)typeof(MultiplayerTab)
                .GetField("_globalOnlineUsers", Private)!.GetValue(tab)!;
            users.Add(new MultiplayerTab.OnlinePlayer("u1", "champion", null, "idle", 1500.0, 80.0, 3,
                SeasonTitle: new SeasonTitleInfo { Season = 1, Place = 1 }));
            users.Add(new MultiplayerTab.OnlinePlayer("u2", "plain", null, "idle", 1500.0, 80.0, 3));
            Invoke(tab, "RenderPlayersPanel");

            var panel = tab.PlayersPanel;
            panel.Measure(new Size(260, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, 260, panel.DesiredSize.Height));
            var rows = panel.Children.OfType<Grid>().ToList();
            FrameworkElement RowOf(string login) => rows.Single(r => Walk(r).OfType<TextBlock>().Any(t => t.Text == login));
            Assert.Contains(Walk(RowOf("champion")), d => d is FrameworkElement { Tag: SeasonTitleInfo });
            Assert.DoesNotContain(Walk(RowOf("plain")), d => d is FrameworkElement { Tag: SeasonTitleInfo });
            Assert.Equal(RowOf("plain").ActualHeight, RowOf("champion").ActualHeight, 1);
        });
        Assert.Null(error);
    }

    // ── the bell ──

    [Fact]
    public void TheBellItemCarriesTheSeason_AndHasItsOwnGlyph()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config, persist: () => { });
        Assert.False(center.RaiseSeasonEnded(0, "x", "y"));
        Assert.True(center.RaiseSeasonEnded(1, "Season 1 is over", "You finished #3 of 18 in 1v1."));
        var item = Assert.Single(center.Items);
        Assert.Equal(NotificationKind.SeasonEnded, item.Kind);
        Assert.Equal("1", item.TargetId);
        Assert.Equal("", item.ModId);

        // Without a trigger a new kind silently falls back to the plain bell glyph.
        var xaml = File.ReadAllText(RepoFile("MainWindow.xaml"));
        Assert.Contains("Value=\"SeasonEnded\"", xaml);
    }

    // ── helpers ──

    /// <summary>
    /// A tab showing Season 1's final table: the demo calendar (three seasons, the third running)
    /// and a hand-made table of FOUR players, so its size differs from the live ladder's 18.
    /// </summary>
    private static MultiplayerTab TabWithPastSeasonTable(out SeasonStandings table)
    {
        var tab = new MultiplayerTab();
        Set(tab, "_communityStats", StatsDemoData.Community());
        table = new SeasonStandings
        {
            Season = 1,
            EndsAt = "2026-12-01T06:00:00.000Z",
            MinDecided = 1,
            Leaderboard = Enumerable.Range(1, 4).Select(i => new LeaderboardRow
            {
                Rank = i,
                UserId = "p" + i,
                DiscordUsername = "player" + i,
                DisplayName = "player" + i,
                Rating = 1700 - 50 * i,
                Rd = 90,
                GamesPlayed = 10,
                Wins = 6,
                Losses = 4,
                SeasonWins = 6,
                SeasonLosses = 4,
            }).ToList(),
            LeaderboardTeam = new List<LeaderboardRow>(),
            RankedPlayers = 4,
            RankedPlayersTeam = 0,
        };
        var cache = (Dictionary<int, SeasonStandings>)typeof(MultiplayerTab)
            .GetField("_seasonStandings", Private)!.GetValue(tab)!;
        cache[1] = table;
        Set(tab, "_rankingSeason", (int?)1);
        Invoke(tab, "RenderRanking");
        return tab;
    }

    private static void Set(MultiplayerTab tab, string field, object? value)
        => typeof(MultiplayerTab).GetField(field, Private)!.SetValue(tab, value);

    private static object? Invoke(MultiplayerTab tab, string method)
        => typeof(MultiplayerTab).GetMethod(method, Private)!.Invoke(tab, null);

    /// <summary>Every leaderboard row's Grid under <paramref name="root"/>, in order.</summary>
    private static IEnumerable<Grid> RankingRows(Panel root)
    {
        foreach (UIElement child in root.Children)
        {
            if (child is Border { Child: Grid g } && g.Children.OfType<FrameworkElement>().Any(e => e.Tag is RankAge))
                yield return g;
            else if (child is Panel p)
                foreach (var inner in RankingRows(p)) yield return inner;
            else if (child is Border { Child: Panel bp })
                foreach (var inner in RankingRows(bp)) yield return inner;
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "WarsOfLibertyLauncher", relative)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!.FullName, "WarsOfLibertyLauncher", relative);
    }
}
