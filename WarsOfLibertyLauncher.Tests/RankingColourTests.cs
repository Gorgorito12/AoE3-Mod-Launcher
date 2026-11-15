using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Design 107 (docs/design_14nov): colour where Ranking used to be grey.</summary>
[Collection("wpf-and-language")]
public class RankingColourTests
{
    [Theory]
    [InlineData(1628, 1628, true)]
    [InlineData(1640, 1628, true)]
    [InlineData(1600, 1628, false)]
    public void ThePeakIsGoldOnlyAtThePlayersBest(double rating, double peak, bool gold)
        => Assert.Equal(gold, RankingGroups.AtPeak(rating, peak));

    [Fact]
    public void NoPeakIsNeverGold() => Assert.False(RankingGroups.AtPeak(1500, null));

    [Fact]
    public void ThePodiumIsGoldSilverAndBronzeNotTheAgesColour()
    {
        Assert.Equal(Color.FromRgb(0xE6, 0xC0, 0x6A), RankingGroups.PodiumMetal(1));
        Assert.Equal(Color.FromRgb(0xCF, 0xD9, 0xE6), RankingGroups.PodiumMetal(2));
        Assert.Equal(Color.FromRgb(0xD5, 0x90, 0x56), RankingGroups.PodiumMetal(3));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. «Latest matches» says what happened: a competitive match that did not
    /// count is AMBER (it used to be the same grey as a casual one), and the mode is a pill, gold
    /// when it counted. The Rooms card's row is left exactly as it was.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ACompetitiveMatchThatDidNotCountIsAmber()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var undecided = new CommunityMatch
            {
                Competitive = true,
                MapName = "ESOC_Iowa",
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "Ana", Result = 0.5 },
                    new() { UserId = "b", DisplayName = "Beto", Result = 0.5 },
                },
            };
            var stated = (DependencyObject)MultiplayerTab.BuildRankingMatchRow(undecided, null, stated: true);
            var dot = Walk(stated).OfType<Grid>().Single(g => Equals(g.Tag, "MatchDot"));
            var fill = ((SolidColorBrush)dot.Children.OfType<System.Windows.Shapes.Ellipse>().Last().Fill).Color;
            Assert.Equal(Color.FromRgb(0xF4, 0x90, 0x0C), fill);
            Assert.Contains(Walk(stated).OfType<Border>(), b => Equals(b.Tag, "MatchModePill"));

            var rooms = (DependencyObject)MultiplayerTab.BuildRankingMatchRow(undecided, null);
            Assert.DoesNotContain(Walk(rooms).OfType<FrameworkElement>(), e => Equals(e.Tag, "MatchModePill") || Equals(e.Tag, "MatchDot"));
        });
        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
        if (root is TextBlock t)
            foreach (var c in t.Inlines.OfType<System.Windows.Documents.InlineUIContainer>())
                if (c.Child != null) foreach (var d in Walk(c.Child)) yield return d;
    }
}
