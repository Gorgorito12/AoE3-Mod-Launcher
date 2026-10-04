using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the month's highlights: which ladder a cell names and when anything is drawn (design
/// 55l), and where they are seen on the Rooms page since designs 60 and 61 — as facts on the
/// community block's header line, beside the community's own figures: the mockup's facts in its
/// order, none of the four highlights that live in Ranking › Highlights, the link to the other
/// month (which moves only the month's facts), and the same facts when folded.
/// </summary>
[Collection("wpf-and-language")]
public class HighlightsTests
{
    private static HighlightPlayer P(string name, int? points = null, int? matches = null, int? wins = null)
        => new() { UserId = name, DisplayName = name, Points = points, Matches = matches, Wins = wins };

    private static MonthHighlights Month(int total, DateTime start, HighlightPlayer? most = null) => new()
    {
        Month = start.ToString("yyyy-MM"),
        StartsAt = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        TotalRated = total,
        MostMatches = most,
    };

    /// <summary>The bigger climb of the two ladders is shown, with its ladder; a tie goes to 1v1.</summary>
    [Fact]
    public void TheBiggerLadderWins_AndATieGoesTo1v1()
    {
        var m = Month(40, DateTime.UtcNow);
        m.BiggestClimb = new HighlightPerMode { Default = P("Siux", points: 58), Team = P("Pedro", points: 96) };
        Assert.Equal(("Pedro", "team"), (HighlightsView.TopClimb(m).Player!.DisplayName, HighlightsView.TopClimb(m).Mode));

        m.BiggestClimb.Team!.Points = 58;
        Assert.Equal("default", HighlightsView.TopClimb(m).Mode);

        m.BiggestClimb = new HighlightPerMode { Team = P("Luis", points: 10) };
        Assert.Equal("team", HighlightsView.TopClimb(m).Mode);

        m.BestStreak = null;
        Assert.Null(HighlightsView.BestStreak(m).Player);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a month with too little behind it has no highlights to show — with
    /// three matches played, "most matches" is whoever played two of them — and enough matches
    /// with nobody to name is no highlights either.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AThinMonthHasNoHighlights()
    {
        var start = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        Assert.False(HighlightsView.HasCells(Month(HighlightsView.MinMonthMatches - 1, start, most: P("Pedro", matches: 4))));
        Assert.True(HighlightsView.HasCells(Month(HighlightsView.MinMonthMatches, start, most: P("Pedro", matches: 4))));
        Assert.False(HighlightsView.HasCells(Month(40, start)));
        Assert.False(HighlightsView.HasCells(null));
    }

    [Fact]
    public void TheMonthIsNamedInTheLaunchersLanguage()
    {
        Assert.Equal("octubre", HighlightsView.MonthName("2026-10", CultureInfo.GetCultureInfo("es")));
        Assert.Equal("October", HighlightsView.MonthName("2026-10", CultureInfo.GetCultureInfo("en")));
        Assert.Null(HighlightsView.MonthName("not-a-month", CultureInfo.GetCultureInfo("en")));
    }

    /// <summary>
    /// The real data strip, open (60a): the mockup's facts in its order — the month's biggest
    /// climb (it has one), most matches and best streak, then matches, players and the most played
    /// map — each a label in capitals over its value; and the link to last month, which brings
    /// last month's highlights back without touching the community's figures.
    /// </summary>
    [Fact]
    public void TheDataStripShowsTheMockupsFactsAndOpensLastMonth()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = OpenScene();
                var sample = EloDemoData.Highlights();
                var mon = ActivityFactsView.MonthAbbreviation(sample.Current!.Month, Strings.Culture)!;
                var stats = Stats(tab);

                var facts = Facts(tab.ActivityFacts);
                Assert.Equal(new[]
                {
                    Up($"Quién más subió · {mon}"),
                    Up($"Más partidas · {mon}"),
                    Up($"Mejor racha · {mon}"),
                    Up($"Partidas · {stats.Totals!.WindowDays} d"),
                    Up($"Jugadores · {stats.Totals.PlayersWindowDays} d"),
                    Up("Mapa más jugado"),
                }, facts.Select(LabelOf).ToArray());

                var text = AllText(tab.ActivityBlock);
                Assert.Contains(text, t => t == "Pedro");
                Assert.Contains(text, t => t == "+96");
                Assert.Contains(text, t => t == "41");
                Assert.Contains(text, t => t == "Geaf_Argento");
                Assert.Contains(text, t => t == stats.Totals.TopMap!.Replace('_', ' '));

                var thisMonth = HighlightsView.MonthName(sample.Current.Month, Strings.Culture)!;
                var lastMonth = HighlightsView.MonthName(sample.Previous!.Month, Strings.Culture)!;
                Assert.Equal(Visibility.Visible, tab.ActivityFactsMonthLink.Visibility);
                Assert.Equal("Ver " + lastMonth, tab.ActivityFactsMonthLink.Content);

                var figuresBefore = Facts(tab.ActivityFacts).Skip(3).Select(ValueTextOf).ToList();
                tab.ActivityFactsMonthLink.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                var back = AllText(tab.ActivityBlock);
                Assert.Contains(back, t => t == "+187");
                Assert.Contains(back, t => t == "58");
                Assert.Equal("Volver a " + thisMonth, tab.ActivityFactsMonthLink.Content);
                // The community's figures are the same: the link moves only the month's facts.
                Assert.Equal(figuresBefore, Facts(tab.ActivityFacts).Skip(3).Select(ValueTextOf).ToList());
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS for the split between the two pages: the four highlights added after
    /// 60 (most wins, best win rate, biggest upset, civilization of the month) are NOT on the Rooms
    /// page — they live in Ranking › Highlights, in depth — and the "+N" window that used to hold
    /// what did not fit is gone: the strip wraps instead. The demo month carries all four, so their
    /// absence is the rule and not a lack of data.
    /// </summary>
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void THE_ONE_THAT_MATTERS_TheFourNewHighlightsAreNotOnTheRoomsPage(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                var tab = OpenScene();
                var month = EloDemoData.Highlights().Current!;
                Assert.NotNull(month.MostWins);
                Assert.NotNull(month.BestWinRate);
                Assert.NotNull(month.BiggestUpset);
                Assert.NotNull(month.TopCiv);

                var text = AllText(tab.ActivityBlock);
                foreach (var key in new[] { "MpHlStripMostWins", "MpHlStripBestRate", "MpHlStripUpset", "MpHlStripTopCiv" })
                    Assert.DoesNotContain(text, t => t.Contains(Strings.Get(key), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(text, t => t == "+238");

                // The only buttons on the header line are the month link and Hide: no "+N".
                Assert.Equal(new[] { tab.ActivityFactsMonthLink, tab.ActivityToggle },
                    Walk(tab.ActivityHeader).OfType<Button>().ToArray());
                Assert.Equal(6, Facts(tab.ActivityFacts).Count);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A fact with nothing to say is left out — never a dash, never "Nobody yet" (56a). Here the
    /// month has no climb: five facts, and no climb label anywhere.
    /// </summary>
    [Fact]
    public void AFactWithNothingToSayIsNotDrawn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = OpenScene();
            var sample = EloDemoData.Highlights();
            sample.Current!.BiggestClimb = null;
            SetCommunityHighlights(tab, sample);

            Assert.Equal(5, Facts(tab.ActivityFacts).Count);
            var text = AllText(tab.ActivityBlock);
            Assert.DoesNotContain(text, t => t.Contains(Strings.Get("MpHlStripTopGain"), StringComparison.OrdinalIgnoreCase));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A month with too little behind it gives the strip no month facts: the community's figures
    /// stand alone, and the way to last month (which has some) is still offered.
    /// </summary>
    [Fact]
    public void AMonthWithTooLittleLeavesTheCommunityFiguresAndOffersLastMonth()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = OpenScene();
                var sample = EloDemoData.Highlights(justStarted: true);
                SetCommunityHighlights(tab, sample);

                var facts = Facts(tab.ActivityFacts);
                Assert.Equal(3, facts.Count);
                Assert.StartsWith(Up("Partidas"), LabelOf(facts[0]));
                var lastMonth = HighlightsView.MonthName(sample.Previous!.Month, Strings.Culture)!;
                Assert.Equal(Visibility.Visible, tab.ActivityFactsMonthLink.Visibility);
                Assert.Equal("Ver " + lastMonth, tab.ActivityFactsMonthLink.Content);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Folded, the block is its header line alone — the SAME facts, same labels and values, beside
    /// the title (61 puts them there in both states); only the cards go.
    /// </summary>
    [Fact]
    public void FoldedTheSameFactsStayBesideTheTitle()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = OpenScene();
            var open = Facts(tab.ActivityFacts).Select(f => (LabelOf(f), ValueTextOf(f))).ToList();
            Assert.NotEmpty(open);

            typeof(MultiplayerTab).GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(tab, new WarsOfLibertyLauncher.Models.LauncherConfig { RoomsActivityChoice = false });
            tab.ApplyActivityLayout();

            Assert.Equal("Folded", tab.ActivityMode.ToString());
            Assert.Equal(Visibility.Collapsed, tab.ActivityStrip.Visibility);
            Assert.Equal(open, Facts(tab.ActivityFacts).Select(f => (LabelOf(f), ValueTextOf(f))).ToList());
        });
        Assert.Null(error);
    }

    /// <summary>The cells come in priority order, and only the ones with somebody in them.</summary>
    [Fact]
    public void TheCellsComeInPriorityOrderAndOnlyWithSomebody()
    {
        var full = EloDemoData.Highlights().Current!;
        Assert.Equal(new[]
        {
            HighlightCellKind.TopClimb, HighlightCellKind.MostWins, HighlightCellKind.MostMatches,
            HighlightCellKind.BestStreak, HighlightCellKind.BestWinRate, HighlightCellKind.BiggestUpset,
            HighlightCellKind.TopCiv,
        }, HighlightsView.Cells(full));

        // An older server sends none of the four new fields: exactly today's three cells.
        var old = EloDemoData.Highlights().Current!;
        old.MostWins = null;
        old.BestWinRate = null;
        old.BiggestUpset = null;
        old.TopCiv = null;
        Assert.Equal(new[] { HighlightCellKind.TopClimb, HighlightCellKind.MostMatches, HighlightCellKind.BestStreak },
            HighlightsView.Cells(old));

        // Fields that are present but empty are nobody, too.
        old.MostWins = P("Ana", wins: 0, matches: 3);
        old.BiggestUpset = new HighlightUpset { Gap = 0, Winners = new() { P("Ana") } };
        old.TopCiv = new HighlightCiv { ModId = "wol", Civ = " ", Picks = 4 };
        Assert.Equal(3, HighlightsView.Cells(old).Count);
    }

    /// <summary>One of the new fields alone is enough for the month to count as having highlights.</summary>
    [Fact]
    public void ANewFieldAloneIsEnoughForTheStrip()
    {
        var m = Month(40, DateTime.UtcNow);
        Assert.False(HighlightsView.HasCells(m));
        m.TopCiv = new HighlightCiv { ModId = "wol", Civ = "Germans", Picks = 5, Wins = 3 };
        Assert.True(HighlightsView.HasCells(m));
        Assert.Equal(new[] { HighlightCellKind.TopCiv }, HighlightsView.Cells(m));
    }

    /// <summary>The server's JSON, the four new fields included, reaches the model.</summary>
    [Fact]
    public void TheNewFieldsAreRead()
    {
        const string json = """
            {
              "month": "2026-10", "starts_at": "2026-10-01T06:00:00.000Z", "total_rated": 40,
              "most_wins": { "user_id": "a", "display_name": "Ana", "wins": 12, "matches": 15 },
              "best_win_rate": { "user_id": "b", "display_name": "Beto", "wins": 9, "matches": 10, "percent": 90 },
              "top_civ": { "mod_id": "wol", "civ": "Germans", "picks": 8, "wins": 5 },
              "biggest_upset": { "mode": "team", "match_id": "m1", "gap": 160,
                                 "winners": [ { "user_id": "a", "display_name": "Ana" }, { "user_id": "c", "display_name": "Ciro" } ],
                                 "losers": [ { "user_id": "d", "display_name": "Dora" } ],
                                 "winners_rating": 1520, "losers_rating": 1680 }
            }
            """;
        var m = System.Text.Json.JsonSerializer.Deserialize<MonthHighlights>(json)!;
        Assert.Equal(12, m.MostWins!.Wins);
        Assert.Equal(90, m.BestWinRate!.Percent);
        Assert.Equal(("wol", "Germans", 8, 5), (m.TopCiv!.ModId, m.TopCiv.Civ, m.TopCiv.Picks, m.TopCiv.Wins));
        Assert.Equal(160, m.BiggestUpset!.Gap);
        Assert.Equal(2, m.BiggestUpset.Winners!.Count);
        Assert.Equal(1680, m.BiggestUpset.LosersRating);
    }

    /// <summary>The highlights scene of the rooms page, laid out once so the block is open (60a).</summary>
    private static MultiplayerTab OpenScene()
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoElo("highlights");
        tab.ApplyActivityLayout();
        Assert.Equal("Fixed", tab.ActivityMode.ToString());
        return tab;
    }

    private static CommunityStats Stats(MultiplayerTab tab)
        => (CommunityStats)typeof(MultiplayerTab).GetField("_communityStats",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(tab)!;

    private static string Up(string s) => s.ToUpper(Strings.Culture);

    private static System.Collections.Generic.List<Border> Facts(DependencyObject slot)
        => Walk(slot).OfType<Border>().Where(b => Equals(b.Tag, MultiplayerTab.ActivityFactTag)).ToList();

    /// <summary>A fact is one line (61): its label, then its value's parts.</summary>
    private static string LabelOf(Border fact)
        => ((TextBlock)((StackPanel)fact.Child).Children[0]).Text;

    private static string ValueTextOf(Border fact)
        => string.Join(" ", ((StackPanel)fact.Child).Children.OfType<UIElement>().Skip(1).SelectMany(c => AllText(c)));

    private static void SetCommunityHighlights(MultiplayerTab tab, MonthlyHighlights highlights)
    {
        Stats(tab).MonthlyHighlights = highlights;
        typeof(MultiplayerTab).GetMethod("RenderActivityStrip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(tab, null);
        tab.ApplyActivityLayout();
    }

    private static System.Collections.Generic.List<string> AllText(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
