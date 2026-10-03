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
/// Pins the month's highlights (design 55l): which ladder a cell names, when the card is drawn at
/// all, and the real card under the rooms list — three cells, the link to the other month, the
/// empty state, and the cells stacking below 600 px.
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
    /// THE ONE THAT MATTERS: a month with too little behind it shows no cells, and only says it
    /// "has just started" while that is true — past the first week an empty month draws nothing,
    /// because the sentence would be false.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheEmptyStateOnlySaysTheMonthJustStartedWhileItHas()
    {
        var start = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
        var thin = Month(HighlightsView.MinMonthMatches - 1, start, most: P("Pedro", matches: 4));
        Assert.False(HighlightsView.HasCells(thin));
        Assert.Equal(HighlightsCardState.JustStarted, HighlightsView.StateOf(thin, isCurrent: true, start.AddDays(2)));
        Assert.Equal(HighlightsCardState.Hidden, HighlightsView.StateOf(thin, isCurrent: true, start.AddDays(10)));
        // Last month is never "just starting".
        Assert.Equal(HighlightsCardState.Hidden, HighlightsView.StateOf(thin, isCurrent: false, start.AddDays(2)));

        var full = Month(HighlightsView.MinMonthMatches, start, most: P("Pedro", matches: 4));
        Assert.Equal(HighlightsCardState.Cells, HighlightsView.StateOf(full, isCurrent: true, start.AddDays(20)));

        // Enough matches but nobody to name is not a card of cells.
        var nobody = Month(40, start);
        Assert.False(HighlightsView.HasCells(nobody));
        Assert.Equal(HighlightsCardState.Hidden, HighlightsView.StateOf(null, isCurrent: true, start));
    }

    [Fact]
    public void TheMonthIsNamedInTheLaunchersLanguage()
    {
        Assert.Equal("octubre", HighlightsView.MonthName("2026-10", CultureInfo.GetCultureInfo("es")));
        Assert.Equal("October", HighlightsView.MonthName("2026-10", CultureInfo.GetCultureInfo("en")));
        Assert.Null(HighlightsView.MonthName("not-a-month", CultureInfo.GetCultureInfo("en")));
    }

    /// <summary>The highlights give way before the rooms and before the panel's folded strip.</summary>
    [Fact]
    public void TheHighlightsGiveWayBeforeTheRooms()
    {
        const double rooms = 208;   // header + two rows, as ApplyActivityLayout computes it
        Assert.True(RoomsActivityLayout.HighlightsFit(700, rooms, 140, hasActivity: true));
        // 700 - 14 - 140 - 14 - 44 = 488 ≥ 208; at 400 it is 188 < 208.
        Assert.False(RoomsActivityLayout.HighlightsFit(400, rooms, 140, hasActivity: true));
        // Without activity there is no folded strip to keep: 400 - 14 - 140 = 246 ≥ 208.
        Assert.True(RoomsActivityLayout.HighlightsFit(400, rooms, 140, hasActivity: false));
        Assert.True(RoomsActivityLayout.HighlightsFit(0, rooms, 140, hasActivity: true));
    }

    /// <summary>The real card: three cells, the bigger climb's ladder named, the link to last month.</summary>
    [Fact]
    public void TheCardShowsThreeCellsAndOpensLastMonth()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("highlights");

                var card = tab.HighlightsHost.Child;
                Assert.NotNull(card);
                Assert.Equal(true, tab.HighlightsHost.Tag);
                Assert.Equal(3, Walk(card).OfType<Border>().Count(b => Equals(b.Tag, MultiplayerTab.HighlightsCellTag)));

                var sample = EloDemoData.Highlights();
                var thisMonth = HighlightsView.MonthName(sample.Current!.Month, Strings.Culture)!;
                var lastMonth = HighlightsView.MonthName(sample.Previous!.Month, Strings.Culture)!;
                var text = AllText(card);
                Assert.Contains(text, t => t.StartsWith("DESTACADOS DE " + thisMonth.ToUpper(Strings.Culture)));
                Assert.Contains(text, t => t == "+96");
                Assert.Contains(text, t => t == "en Equipos · 12 partidas");
                Assert.Contains(text, t => t == "41");
                Assert.Contains(text, t => t == "\U0001F5259");
                Assert.Contains(text, t => t == "victorias seguidas en 1v1");

                var link = Walk(card).OfType<Button>().Single();
                Assert.Equal("Ver " + lastMonth, link.Content);
                link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                var back = AllText(tab.HighlightsHost.Child);
                Assert.Contains(back, t => t == "+187");
                Assert.Contains(back, t => t == "en 1v1 · 22 partidas");
                // A finished month says no "hasta hoy".
                Assert.DoesNotContain(back, t => t.Contains("hasta hoy"));
                Assert.Equal("Volver a " + thisMonth, Walk(tab.HighlightsHost.Child).OfType<Button>().Single().Content);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>The month just started: the sentence, with the number, and the way to last month.</summary>
    [Fact]
    public void AMonthThatJustStartedSaysSoAndOffersLastMonth()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = new MultiplayerTab();
                tab.ShowDemoElo("highlights");
                SetCommunityHighlights(tab, EloDemoData.Highlights(justStarted: true));

                var card = tab.HighlightsHost.Child;
                Assert.NotNull(card);
                Assert.DoesNotContain(Walk(card).OfType<Border>(), b => Equals(b.Tag, MultiplayerTab.HighlightsCellTag));
                Assert.Contains(AllText(card), t => t.StartsWith("El mes recién empieza.") && t.Contains("al menos 10 partidas"));
                Assert.Single(Walk(card).OfType<Button>());
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>Below 600 px the three cells stack one under the other (55l).</summary>
    [Fact]
    public void BelowSixHundredPixelsTheCellsStack()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoElo("highlights");

            void Layout(double width)
            {
                tab.RoomsLeftColumn.Measure(new Size(width, 1400));
                tab.RoomsLeftColumn.Arrange(new Rect(0, 0, width, 1400));
                tab.RoomsLeftColumn.UpdateLayout();
                tab.ApplyActivityLayout();
                tab.RoomsLeftColumn.Measure(new Size(width, 1400));
                tab.RoomsLeftColumn.Arrange(new Rect(0, 0, width, 1400));
                tab.RoomsLeftColumn.UpdateLayout();
            }

            Grid Cells() => Walk(tab.HighlightsHost.Child).OfType<Grid>()
                .Single(g => g.Children.OfType<Border>().Count(b => Equals(b.Tag, MultiplayerTab.HighlightsCellTag)) == 3);

            Layout(900);
            Assert.Equal(Visibility.Visible, tab.HighlightsHost.Visibility);
            Assert.Equal(5, Cells().ColumnDefinitions.Count);

            Layout(520);
            Assert.Equal(5, Cells().RowDefinitions.Count);
            Assert.Empty(Cells().ColumnDefinitions);
        });
        Assert.Null(error);
    }

    private static void SetCommunityHighlights(MultiplayerTab tab, MonthlyHighlights highlights)
    {
        var field = typeof(MultiplayerTab).GetField("_communityStats",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var stats = (CommunityStats)field.GetValue(tab)!;
        stats.MonthlyHighlights = highlights;
        typeof(MultiplayerTab).GetMethod("RenderHighlights",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(tab, null);
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
