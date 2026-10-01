using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Profile's badge selector (docs/design_insignia_equipos, 51c). It is built in code, so no
/// compile step checks a resource it looks up by name — constructing it here is that check, in
/// both languages. The refusals are the point: no card for a server that cannot store the choice,
/// and no clickable Teams for somebody with no team match.
/// </summary>
[Collection("wpf-and-language")]
public class BadgeModeCardTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void AServerThatSendsNoPreferenceGetsNoSelector()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWith(new EloSnapshot { Rating = 1566, Rd = 80, GamesPlayed = 9, LadderRank = 5 });
            Assert.Null(tab.BuildBadgeModeCard(User()));
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TheChoiceIsLitAndTeamsUnlocksOnlyWithATeamPlace(string language)
    {
        var before = Strings.Language;
        try
        {
            Strings.SetLanguage(language);
            var error = DialogXamlTests.RunOnStaThread(() =>
            {
                var locked = TabWith(Standing(team: 0, mode: "1v1")).BuildBadgeModeCard(User())!;
                var lockedSegments = Segments(locked);
                Assert.Equal(3, lockedSegments.Length);
                Assert.Equal("active", lockedSegments[1].Tag);
                Assert.Null(lockedSegments[0].Tag);
                Assert.False(lockedSegments[2].IsEnabled);
                Assert.True(ToolTipService.GetShowOnDisabled(lockedSegments[2]));

                var open = TabWith(Standing(team: 2, mode: "team")).BuildBadgeModeCard(User())!;
                var openSegments = Segments(open);
                Assert.True(openSegments[2].IsEnabled);
                Assert.Equal("active", openSegments[2].Tag);

                // The preview says what others see, in words: the team badge was chosen.
                Assert.Contains(Texts(open), t => t.Contains(Strings.Get("MpModeTeams")) && t.Contains(" · "));
            });
            Assert.Null(error);
        }
        finally { Strings.SetLanguage(before); }
    }

    [Fact]
    public void ALadderBoxNeverPrintsHashZero()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = TabWith(Standing(team: 0, mode: "highest")).BuildBadgeModeCard(User())!;
            Assert.DoesNotContain(Texts(card), t => t.Contains("#0"));
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static EloSnapshot Standing(int team, string mode) => new()
    {
        Rating = 1566, Rd = 80, GamesPlayed = 9, LadderRank = 5, LadderSize = 18,
        LadderRankTeam = team, LadderSizeTeam = 6, RatingTeam = 1612, RdTeam = 90, GamesPlayedTeam = team > 0 ? 3 : 0,
        BadgeMode = mode,
    };

    private static LobbyUserSummary User() => new() { Id = "me", DiscordUsername = "geaf", DisplayName = "Geaf_Argento" };

    private static MultiplayerTab TabWith(EloSnapshot standing)
    {
        var tab = new MultiplayerTab();
        typeof(MultiplayerTab).GetField("_cachedStanding", Private)!.SetValue(tab, standing);
        return tab;
    }

    private static Button[] Segments(UIElement card)
        => Walk(card).OfType<Button>().ToArray();

    private static string[] Texts(UIElement card)
        => Walk(card).OfType<TextBlock>().Select(t => t.Text).ToArray();

    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
