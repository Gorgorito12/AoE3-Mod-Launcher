using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The guide's 1v1 / Teams selector and its Teams tab (docs/design_guia_rangos_equipos, 53a/53b).
/// The card is built in code, so a style, brush or string key that does not exist fails only when
/// somebody opens it — which is why every tab is built here, in both languages.
/// </summary>
[Collection("wpf-and-language")]
public class RankGuideTeamsTabTests
{
    private const string Me = "me";

    /// <summary>53a: on the Teams tab EVERY badge is the double shield — the header, the notice
    /// and all six rows. One single shield anywhere would say the 1v1 table.</summary>
    [Theory]
    [InlineData(Strings.LangEn)]
    [InlineData(Strings.LangEs)]
    public void EveryBadgeOfTheTeamsTabIsTheDoubleShield(string language) => InLanguage(language, () =>
    {
        var card = Card(Inputs(teamRank: 5), BadgeKind.Team);
        var all = Walk(card).OfType<FrameworkElement>().ToList();

        var teamRoots = all.Where(e => e.Tag is RankBadge.TeamBadgeTag).ToList();
        var fronts = all.Where(IsBadgeFront).ToList();
        Assert.Equal(1 + 1 + 6, teamRoots.Count);           // header, notice, six ages
        Assert.Equal(teamRoots.Count, fronts.Count);         // and no single shield beside them
        foreach (var front in fronts)
            Assert.IsType<RankBadge.TeamBadgeTag>(((FrameworkElement)LogicalTreeHelper.GetParent(front)).Tag);

        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideAgesTitleTeam"));
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideHowTitleTeam"));
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideHow1Team"));
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideHow2"));
        Assert.Equal(Strings.Format("MpGuideFooterTeam", 9), Footer(all));
        Assert.Equal(Strings.Get("MpGuideOpenTeamRanking"), (string)Open(all).Content);
        Assert.Equal("active", Segment(all, "RankGuideTabTeam").Tag);
        Assert.Null(Segment(all, "RankGuideTab1v1").Tag);
        NoRawKeys(all);
    });

    /// <summary>The 1v1 tab keeps its single shields and its texts; only the selector and the
    /// footer ("on the 1v1 ladder") are new.</summary>
    [Theory]
    [InlineData(Strings.LangEn)]
    [InlineData(Strings.LangEs)]
    public void TheSoloTabIsTodaysGuidePlusTheSelector(string language) => InLanguage(language, () =>
    {
        var card = Card(Inputs(teamRank: 5), BadgeKind.Solo);
        var all = Walk(card).OfType<FrameworkElement>().ToList();

        Assert.DoesNotContain(all, e => e.Tag is RankBadge.TeamBadgeTag);
        Assert.Contains(all, e => Equals(e.Tag, "RankGuideSelector"));
        Assert.Equal("active", Segment(all, "RankGuideTab1v1").Tag);
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideAgesTitle"));
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideHow1"));
        Assert.Equal(Strings.Format("MpGuideFooterSolo", 14), Footer(all));
        Assert.Equal(Strings.Get("MpGuideOpenRanking"), (string)Open(all).Content);
        NoRawKeys(all);
    });

    /// <summary>Pressing the other segment rebuilds the card on that ladder, in place.</summary>
    [Fact]
    public void TheSelectorSwitchesTheCardInPlace()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var card = Card(Inputs(teamRank: 5), BadgeKind.Solo);
            Segment(Walk(card).OfType<FrameworkElement>(), "RankGuideTabTeam")
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var all = Walk(card).OfType<FrameworkElement>().ToList();
            Assert.Equal("active", Segment(all, "RankGuideTabTeam").Tag);
            Assert.Contains(all, e => e.Tag is RankBadge.TeamBadgeTag);
            Assert.Equal(Strings.Format("MpGuideFooterTeam", 9), Footer(all));

            Segment(all, "RankGuideTab1v1").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.DoesNotContain(Walk(card).OfType<FrameworkElement>(), e => e.Tag is RankBadge.TeamBadgeTag);
        });
        Assert.Null(error);
    }

    /// <summary>53b rule 3: no team tab — no selector, and the card is the 1v1 guide word for word,
    /// footer included. Asking for Teams then opens the only tab there is.</summary>
    [Fact]
    public void WithoutATeamLadderThereIsNoSelectorAndTodaysFooter()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var inputs = Inputs(teamRank: null, withTeamLadder: false);
            Assert.Null(inputs.Team);
            var card = Card(inputs, BadgeKind.Team);
            var all = Walk(card).OfType<FrameworkElement>().ToList();

            Assert.DoesNotContain(all, e => Equals(e.Tag, "RankGuideSelector"));
            Assert.DoesNotContain(all, e => e.Tag is RankBadge.TeamBadgeTag);
            Assert.Equal(Strings.Format("MpGuideFooter", 14), Footer(all));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// 53b rule 2: no decided team match. The header is the double Discovery shield with "play a
    /// competitive team match to join" and no rating; the notice quotes the SERVER's entry bar
    /// (and says no number when the server did not give one); and no row is lit.
    /// </summary>
    [Theory]
    [InlineData(Strings.LangEn)]
    [InlineData(Strings.LangEs)]
    public void ATeamDiscoverySaysHowToJoinAndLightsNoRow(string language) => InLanguage(language, () =>
    {
        foreach (var (bar, key) in new[]
                 {
                     (1, "MpGuideNextTeamFirstOne"),
                     (3, "MpGuideNextTeamFirstMany"),
                     (0, "MpGuideNextTeamFirst"),
                 })
        {
            var all = Walk(Card(Inputs(teamRank: 0, minDecided: bar), BadgeKind.Team)).OfType<FrameworkElement>().ToList();

            var youAre = all.OfType<TextBlock>().Single(t => Equals(t.Tag, "RankGuideYouAre")).Text;
            Assert.Equal(Strings.Format("MpGuideYouAre", Strings.Get(RankAges.NameKey(RankAge.Discovery)))
                         + " · " + Strings.Get("MpGuideTeamPlayToJoin"), youAre);
            Assert.DoesNotContain("1455", youAre);

            var notice = (Border)all.Single(e => Equals(e.Tag, "RankGuideNext"));
            var sentence = Walk(notice).OfType<TextBlock>().Single(t => t.TextWrapping == TextWrapping.Wrap).Text;
            Assert.Equal(bar > 1 ? Strings.Format(key, bar) : Strings.Get(key), sentence);

            Assert.DoesNotContain(all, e => Equals(e.Tag, "RankGuideMine"));
            Assert.Contains(all, e => e.Tag is RankBadge.TeamBadgeTag { Age: RankAge.Discovery });
            Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("MpGuideRangeDiscoveryTeam"));
            NoRawKeys(all);
        }
    });

    /// <summary>The 1v1 guide keeps lighting its Discovery row: the "no row lit" rule is the Teams
    /// tab's alone, and the 1v1 tab stays as it was.</summary>
    [Fact]
    public void TheSoloDiscoveryRowIsStillLit()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var inputs = Inputs(teamRank: 0);
            var soloDiscovery = RankGuideView.Build(0, 14, new Dictionary<int, string>());
            var card = RankGuideCard.Build(new RankGuideTab(soloDiscovery, null, null),
                new RankGuideTab(inputs.Team!.View, null, null), BadgeKind.Solo, () => { }, inputs.EntryBar);
            Assert.Contains(Walk(card).OfType<FrameworkElement>(), e => Equals(e.Tag, "RankGuideMine"));
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static LeaderboardRow Row(int rank, string id, string name)
        => new() { Rank = rank, UserId = id, DisplayName = name, DiscordUsername = name.ToLowerInvariant() };

    internal static RankGuideInputs Inputs(int? teamRank, bool withTeamLadder = true, int minDecided = 1)
    {
        var stats = new CommunityStats
        {
            MinDecided = minDecided,
            Leaderboard = Enumerable.Range(1, 14).Select(i => Row(i, "s" + i, "Solo" + i)).ToList(),
            RankedPlayers = 14,
            LeaderboardTeam = withTeamLadder
                ? Enumerable.Range(1, 9).Select(i => Row(i, "t" + i, "Team" + i)).ToList()
                : null,
            RankedPlayersTeam = withTeamLadder ? 9 : 0,
        };
        var standing = new EloSnapshot
        {
            Rating = 1388, Rd = 90, GamesPlayed = 12, LadderRank = 7, LadderSize = 14,
            LadderRankTeam = teamRank, LadderSizeTeam = withTeamLadder ? 9 : null,
            RatingTeam = 1455, RdTeam = 120, GamesPlayedTeam = teamRank is > 0 ? 6 : 0,
        };
        return RankGuideSources.Gather(standing, stats, Me);
    }

    internal static FrameworkElement Card(RankGuideInputs inputs, BadgeKind initial)
        => RankGuideCard.Build(
            new RankGuideTab(inputs.Solo.View, inputs.Solo.Rating, () => { }),
            inputs.Team is { } t ? new RankGuideTab(t.View, t.Rating, () => { }) : null,
            initial, () => { }, inputs.EntryBar);

    /// <summary>A badge's front shield: tagged with its age, and not an age ROW (rows are Grids
    /// with a minimum height).</summary>
    internal static bool IsBadgeFront(FrameworkElement e) => e.Tag is RankAge && e is not Grid { MinHeight: > 0 };

    private static string? Footer(IEnumerable<FrameworkElement> all)
        => all.OfType<TextBlock>().SingleOrDefault(t => Equals(t.Tag, "RankGuideFooter"))?.Text;

    private static Button Open(IEnumerable<FrameworkElement> all)
        => (Button)all.Single(e => Equals(e.Tag, "RankGuideOpenRanking"));

    private static Button Segment(IEnumerable<FrameworkElement> all, string name)
        => (Button)all.Single(e => e.Name == name);

    private static void NoRawKeys(IEnumerable<FrameworkElement> all)
    {
        foreach (var text in all.OfType<TextBlock>().Select(t => t.Text))
            Assert.DoesNotContain("MpGuide", text);   // a missing key renders as itself
        foreach (var b in all.OfType<Button>().Select(b => b.Content).OfType<string>())
            Assert.DoesNotContain("Mp", b);
    }

    private static void InLanguage(string language, System.Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var before = Strings.Language;
            Strings.SetLanguage(language);
            try { body(); }
            finally { Strings.SetLanguage(before); }
        });
        Assert.Null(error);
    }

    internal static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
