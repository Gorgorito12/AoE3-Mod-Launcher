using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>What design 95 promises about the Profile window.</summary>
[Collection("wpf-and-language")]
public class ProfilePageTests
{
    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var deeper in Under<T>(child)) yield return deeper;
        }
    }

    [Theory]
    [InlineData(780, 220)]
    [InlineData(1040, 268)]
    [InlineData(1440, 300)]
    public void TheCardsWidthFollowsTheWindow(double window, double rail)
        => Assert.Equal(rail, ProfileWindow.RailWidthFor(window));

    private static DeckCardEntry Row(string card, int players)
        => new() { Civ = "British", Card = card, Players = players };

    /// <summary>
    /// The same two refusals the Statistics page makes: no share below the sample of decks, and
    /// none for a card in two decks or fewer.
    /// </summary>
    [Fact]
    public void TheComparisonSaysNothingBelowTheSample()
    {
        var thin = OwnDeckView.CompareWithCommunity(new[] { "A" }, "British",
            new[] { Row("A", DeckStatsView.MinDecksForPercent - 1) });
        Assert.Empty(thin.Shares);
        Assert.Empty(thin.Others);

        var solid = OwnDeckView.CompareWithCommunity(new[] { "A", "C" }, "British",
            new[] { Row("A", 6), Row("B", 5), Row("C", 2) });
        Assert.Equal(6, solid.Decks);
        Assert.Equal(100, solid.Shares["A"]);
        Assert.False(solid.Shares.ContainsKey("C"));
        Assert.Equal("B", Assert.Single(solid.Others).Card);
    }

    /// <summary>The comparison is about one civilization: a row of another is not this deck's.</summary>
    [Fact]
    public void TheComparisonReadsOnlyThatCivilization()
    {
        var rows = new[] { new DeckCardEntry { Civ = "Chinese", Card = "A", Players = 9 } };
        Assert.Equal(0, OwnDeckView.CompareWithCommunity(new[] { "A" }, "British", rows).Decks);
    }

    /// <summary>The players stay folded until asked for: a list of matches is what comes here.</summary>
    [Fact]
    public void AMatchsPlayersOpenFromItsLink()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();
                var card = tab.BuildHistoryRow(EloDemoData.History()[0], EloDemoData.ViewerId);

                var toggle = Under<Button>(card)
                    .First(b => Equals(b.Content, Strings.Get("MpHistoryShowPlayers")));
                var sides = Under<Border>(card).Last(b => b.Visibility == Visibility.Collapsed);

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(Visibility.Visible, sides.Visibility);
                Assert.Equal(Strings.Get("MpHistoryHidePlayers"), toggle.Content);
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }
}
