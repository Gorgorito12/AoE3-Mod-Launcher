using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Achievements (design 121, docs/design_logros): how a tile reads, where a new medal is shown,
/// when the medals are asked for again, and that the section, the card and the notice build.
/// The server decides every medal; what is pinned here is the launcher drawing it honestly — a
/// rating medal never says "0 / 1600", a game the launcher does not know still reads as words, and
/// a celebration never floats over a game.
/// </summary>
[Collection("wpf-and-language")]
public class AchievementsTests
{
    // ---------------------------------------------------------------- tiles

    private static AchievementItem Item(string id, int? goal = null, int? value = null, string? earned = null, string? ladder = null)
        => new() { Id = id, Icon = "trophy", Metal = "s", Name = "Server " + id, Condition = "Server condition", Goal = goal, Value = value, EarnedAt = earned, Ladder = ladder };

    [Fact]
    public void AnEarnedMedalShowsItsDate()
    {
        var t = AchievementsView.Tile(Item("conqueror", 50, 50, "2026-03-02T18:00:00Z"), 0);
        Assert.Equal(MedalTileState.Earned, t.State);
        Assert.False(string.IsNullOrEmpty(t.Foot));
        Assert.Equal(-1, t.Progress);
    }

    [Fact]
    public void ACountUnderWayShowsValueOverGoal()
    {
        var t = AchievementsView.Tile(Item("veteran", 100, 26), 0);
        Assert.Equal(MedalTileState.Progress, t.State);
        Assert.Equal("26 / 100", t.Foot);
        Assert.Equal(0.26, t.Progress, 2);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ARatingMedalWithNoRatingIsLockedNeverZero()
    {
        // In placement, or a number the server withholds from another player: never "0 / 1600".
        var t = AchievementsView.Tile(Item("contender", 1600, null, null, "default"), 2);
        Assert.Equal(MedalTileState.Locked, t.State);
        Assert.DoesNotContain("/", t.Foot);
    }

    [Fact]
    public void ARatingMedalFillsItsRingFrom1500()
    {
        var t = AchievementsView.Tile(Item("elite", 1700, 1612, null, "default"), 2);
        Assert.Equal(MedalTileState.Progress, t.State);
        Assert.Equal((1612 - 1500) / 200.0, t.Progress, 3);
    }

    [Fact]
    public void AYesNoMedalNotWonIsLocked()
    {
        var t = AchievementsView.Tile(Item("phoenix"), 1);
        Assert.Equal(MedalTileState.Locked, t.State);
    }

    [Fact]
    public void PromotionNamesItsAgeAndTheNextOne()
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEn);
        try
        {
            var item = new AchievementItem
            {
                Id = "promotion", Icon = "promotion", Name = "Promotion", Condition = "c",
                Tiers = AchievementsView.PromotionAges.Select((a, i) => new AchievementTier
                {
                    Tier = i + 1, Key = a.Key, EarnedAt = i < 3 ? "2026-08-13T18:00:00Z" : null,
                }).ToList(),
            };
            var t = AchievementsView.Tile(item, 1);
            Assert.Equal(MedalTileState.Earned, t.State);
            Assert.Equal(RankAge.Industrial, t.Age);
            Assert.Contains("Industrial", t.Name);
            Assert.Contains("Imperial", t.Line);
            Assert.Equal(3, t.Pips.Count(p => p != null));
            Assert.Equal(5, t.Pips.Count);
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void AnIdThisLauncherDoesNotKnowReadsAsTheServersWords()
    {
        var t = AchievementsView.Tile(Item("a_medal_from_the_future", 3, 1), 0);
        Assert.Equal("Server a_medal_from_the_future", t.Name);
        Assert.Equal("Server condition", t.Line);
    }

    [Fact]
    public void EveryMedalOfTheSamplesHasANameAndAConditionInBothLanguages()
    {
        foreach (var r in new[] { AchievementsDemoData.Wol(), AchievementsDemoData.Tad(), AchievementsDemoData.WolExtended() })
        {
            foreach (var item in r.Groups!.SelectMany(g => g.Items))
            {
                foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
                {
                    Assert.NotEqual("Achv_" + item.Id, Strings.GetIn(lang, "Achv_" + item.Id));
                    Assert.NotEqual("AchvCond_" + item.Id, Strings.GetIn(lang, "AchvCond_" + item.Id));
                }
            }
            foreach (var g in r.Groups!)
                Assert.NotEqual("AchvGroup_" + g.Id, Strings.GetIn(Strings.LangEs, "AchvGroup_" + g.Id));
        }
    }

    [Fact]
    public void TheSamplesAreTheDesignsCounts()
    {
        // 121a / 121c: 40 and 41 medals ("Promotion" gives one per age).
        Assert.Equal(40, AchievementsDemoData.Wol().Total);
        Assert.Equal(41, AchievementsDemoData.Tad().Total);
        Assert.Equal(14, AchievementsDemoData.Wol().Earned);
        Assert.Equal(36, AchievementsDemoData.Wol().Groups!.Sum(g => g.Items.Count));
        Assert.Equal(37, AchievementsDemoData.Tad().Groups!.Sum(g => g.Items.Count));
    }

    [Fact]
    public void TheExtendedSampleIsTheServersCatalogue()
    {
        // The server's Wars of Liberty since 2026-11-26: 68 achievements, 72 medals, in nine groups.
        var r = AchievementsDemoData.WolExtended();
        Assert.Equal(72, r.Total);
        Assert.Equal(68, r.Groups!.Sum(g => g.Items.Count));
        Assert.Equal(9, r.Groups!.Count);
        Assert.Equal("feats", r.Groups![^1].Id);
        Assert.All(r.Groups![^1].Items, i => Assert.Equal("p", i.Metal));
        Assert.Equal(r.Groups!.SelectMany(g => g.Items).Count(), r.Groups!.SelectMany(g => g.Items).Select(i => i.Id).Distinct().Count());
        Assert.Contains(r.Groups!, g => g.Id == "historical_wars" && g.Items.Count == 8);
        Assert.Equal("20/72", $"{r.Earned}/{r.Total}");
    }

    [Fact]
    public void PlatinumIsInTheLegendOnlyWhereThereIsPlatinum()
    {
        // The design's sheets keep their three metals; a game with a feat shows the fourth.
        Assert.Equal(new[] { "b", "s", "g" }, AchievementsView.Legend(AchievementsDemoData.Wol()).Select(x => x.Metal));
        Assert.Equal(new[] { "b", "s", "g" }, AchievementsView.Legend(AchievementsDemoData.Tad()).Select(x => x.Metal));
        Assert.Equal(new[] { "b", "s", "g", "p" }, AchievementsView.Legend(AchievementsDemoData.WolExtended()).Select(x => x.Metal));
    }

    [Fact]
    public void PlatinumIsItsOwnMetalAndNeverReadsAsSilver()
    {
        Assert.NotEqual(Medal.MetalOf("s").Main, Medal.MetalOf("p").Main);
        Assert.NotEqual(Medal.MetalOf("b").Main, Medal.MetalOf("p").Main);
        Assert.Equal(8, Medal.Enamels.Length);
        var feat = new MedalRef { Id = "grandmaster", Metal = "p", Name = "Grandmaster", Condition = "Reach 1900 ELO in 1v1" };
        foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
            Assert.NotEqual("AchvMetalPlatinum", Strings.GetIn(lang, "AchvMetalPlatinum"));
        Assert.Contains(Strings.Get("AchvMetalPlatinum"), AchievementsView.MedalLine(feat, 52, 72, "Wars of Liberty"));
    }

    [Fact]
    public void APlatinumCardHasItsOwnLightAndStillStopsEveryClock()
    {
        Assert.Same(AchievementUnlockOverlay.Accent.Platinum, AchievementUnlockOverlay.Accent.For("p"));
        Assert.Same(AchievementUnlockOverlay.Accent.Gold, AchievementUnlockOverlay.Accent.For("g"));
        Assert.Same(AchievementUnlockOverlay.Accent.Gold, AchievementUnlockOverlay.Accent.For(null));
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var host = new Grid();
            var model = new AchievementUnlockOverlay.Model(new Medal.Look("climb", 7, true, "p"), "Grandmaster",
                "Reach 1900 ELO in 1v1", "Platinum medal · 52 of 72 in Wars of Liberty", Medal.Hex(Medal.MetalOf("p").Main));
            int before = AchievementUnlockOverlay.LiveClocks;
            var close = AchievementUnlockOverlay.Show(host, model, () => { }, reducedEffects: false);
            Assert.True(AchievementUnlockOverlay.LiveClocks > before);
            close();
            Assert.Equal(before, AchievementUnlockOverlay.LiveClocks);
            Assert.Empty(host.Children);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AGroupCountsAchievementsAndPromotionAsOne()
    {
        var group = new AchievementGroup
        {
            Id = "streaks_ranking",
            Items = new()
            {
                Item("unstoppable", earned: "2026-08-12T18:00:00Z"),
                new AchievementItem
                {
                    Id = "promotion",
                    Tiers = Enumerable.Range(1, 5).Select(i => new AchievementTier { Tier = i, EarnedAt = i <= 2 ? "x" : null }).ToList(),
                },
            },
        };
        Assert.Equal("2/2", AchievementsView.GroupCount(group));
    }

    [Fact]
    public void TheDateIsInTheLaunchersLanguage()
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage(Strings.LangEn);
            var en = AchievementsView.ShortDate("2026-03-02T18:00:00Z");
            Strings.SetLanguage(Strings.LangEs);
            var es = AchievementsView.ShortDate("2026-03-02T18:00:00Z");
            Assert.StartsWith("Mar", en);
            Assert.StartsWith("Mar", es);
            Assert.Equal("", AchievementsView.ShortDate("not a date"));
        }
        finally { Strings.SetLanguage(previous); }
    }

    // ---------------------------------------------------------------- where a new medal shows

    [Theory]
    [InlineData(1, true, false, false, AchievementCelebration.Surface.Card)]
    [InlineData(1, false, false, false, AchievementCelebration.Surface.Notice)]
    [InlineData(1, true, true, false, AchievementCelebration.Surface.Notice)]
    [InlineData(3, true, false, false, AchievementCelebration.Surface.Notice)]
    [InlineData(1, true, false, true, AchievementCelebration.Surface.BellOnly)]
    [InlineData(2, false, true, true, AchievementCelebration.Surface.BellOnly)]
    [InlineData(0, true, false, false, AchievementCelebration.Surface.BellOnly)]
    public void THE_ONE_THAT_MATTERS_NothingFloatsOverAGame(int medals, bool looking, bool inRoom, bool game,
        AchievementCelebration.Surface expected)
        => Assert.Equal(expected, AchievementCelebration.Decide(medals, looking, inRoom, game));

    // ---------------------------------------------------------------- when they are asked for

    [Fact]
    public void TheCacheAsksOnceThenAgainWhenStale()
    {
        var cache = new AchievementsCache();
        var now = DateTime.UtcNow;
        var e = cache.Get("me", "WoL");
        Assert.True(cache.NeedsFetch(e, now));
        e.Loading = true;
        Assert.False(cache.NeedsFetch(e, now));
        e.Loading = false;
        e.Response = new AchievementsResponse();
        e.AskedUtc = now;
        Assert.False(cache.NeedsFetch(e, now.AddSeconds(5)));
        Assert.True(cache.NeedsFetch(e, now + AchievementsCache.StaleAfter));
        cache.MarkStale("me");
        Assert.True(cache.NeedsFetch(cache.Get("me", "wol"), now));
    }

    [Fact]
    public void AFailedReadWaitsBeforeAskingAgain()
    {
        var cache = new AchievementsCache();
        var now = DateTime.UtcNow;
        var e = cache.Get("me", "wol");
        e.Failed = true;
        e.AskedUtc = now;
        Assert.False(cache.NeedsFetch(e, now.AddSeconds(1)));
        Assert.True(cache.NeedsFetch(e, now + AchievementsCache.RetryAfterFailure));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AServerWithoutAchievementsIsNotAskedAgain()
    {
        var cache = new AchievementsCache { Unsupported = true };
        Assert.False(cache.NeedsFetch(cache.Get("me", "wol"), DateTime.UtcNow));
    }

    [Fact]
    public void MarkingOnePlayerStaleLeavesTheOthersAlone()
    {
        var cache = new AchievementsCache();
        var now = DateTime.UtcNow;
        foreach (var who in new[] { "me", "rival" })
        {
            var e = cache.Get(who, "wol");
            e.Response = new AchievementsResponse();
            e.AskedUtc = now;
        }
        cache.MarkStale("me");
        Assert.True(cache.NeedsFetch(cache.Get("me", "wol"), now));
        Assert.False(cache.NeedsFetch(cache.Get("rival", "wol"), now));
    }

    // ---------------------------------------------------------------- the bell

    [Fact]
    public void TheBellSaysAMedalOnceAndRingsNoBalloon()
    {
        var config = new LauncherConfig();
        var center = new NotificationCenter(config, persist: () => { });
        int balloons = 0;
        center.ToastRequested += (_, _) => balloons++;
        Assert.True(center.RaiseAchievements("wol", new[] { "wol|duelist|0" }, "t", "b"));
        Assert.False(center.RaiseAchievements("wol", new[] { "WOL|duelist|0" }, "t", "b"));
        Assert.True(center.RaiseAchievements("wol", new[] { "wol|duelist|0", "wol|gladiator|0" }, "t", "b"));
        Assert.Equal(0, balloons);
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.AchievementUnlocked));
    }

    // ---------------------------------------------------------------- what is drawn

    [Fact]
    public void EveryPictogramParsesAndAnUnknownOneFallsBackToTheShield()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var icon in new[] { "sword", "trophy", "flame", "shield", "flag", "compass", "bolt", "crown", "star",
                                          "clock", "target", "climb", "phoenix", "cap", "globe", "hands", "bracket",
                                          "feather", "people", "check", "host", "promotion" })
            {
                var g = Application.Current.TryFindResource(Medal.IconKey(icon)) as Geometry;
                Assert.NotNull(g);
                Assert.False(g!.Bounds.IsEmpty, icon);
            }
            Assert.Equal("AchvIconShield", Medal.IconKey("promotion"));
            Assert.Null(Application.Current.TryFindResource(Medal.IconKey("no_such_icon")));
            var drawn = Medal.Pictogram("no_such_icon", 20, "#FFFFFF");
            Assert.NotNull(drawn);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AMedalBuildsAtEverySizeTheDesignUses()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var size in new[] { 34.0, 54, 66, 68, 132 })
            {
                foreach (var look in new[]
                         {
                             new Medal.Look("trophy", 2, true, "g"),
                             new Medal.Look("climb", 1, false, "s", null, 0.4),
                             new Medal.Look("shield", 1, true, "b", RankAge.Imperial),
                             new Medal.Look("cap", 6, false),
                         })
                {
                    var medal = Medal.Build(look, size);
                    medal.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.Equal(size, medal.DesiredSize.Width, 1);
                }
            }
            Assert.Equal(Medal.RimKind.Progress, Medal.RimOf(new Medal.Look("x", 0, false, Progress: 0.5)));
            Assert.Equal(Medal.RimKind.Faint, Medal.RimOf(new Medal.Look("x", 0, false)));
            Assert.Equal(Medal.RimKind.Age, Medal.RimOf(new Medal.Look("x", 0, true, Age: RankAge.Colonial)));
        });
        Assert.Null(error);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheProfileDrawsTheSectionInBothLanguages()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
                {
                    Strings.SetLanguage(lang);
                    var tab = new MultiplayerTab();
                    var body = new StackPanel();
                    tab.ProfileBodyOverride = body;
                    tab.ShowDemoElo("profile:achievements");
                    body.Measure(new Size(980, double.PositiveInfinity));
                    body.Arrange(new Rect(0, 0, 980, body.DesiredSize.Height));

                    var all = Walk(body).ToList();
                    Assert.Single(all.OfType<Border>(), b => Equals(b.Tag, "AchvShowcase"));
                    Assert.Equal(7, all.OfType<StackPanel>().Count(p => p.Tag is string s && s.StartsWith("AchvGroup:")));
                    Assert.Equal(36, all.OfType<Border>().Count(b => b.Tag is string s && s.StartsWith("AchvTile:")));
                    // The three latest under the name on the card.
                    Assert.Single(all.OfType<StackPanel>(), p => Equals(p.Tag, "ProfileLatestMedals"));
                    // No raw string key anywhere.
                    foreach (var text in all.OfType<TextBlock>().Select(t => t.Text))
                        Assert.False(text.StartsWith("Achv", StringComparison.Ordinal), text);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheTadSampleDrawsItsOwnGroups()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var body = new StackPanel();
            tab.ProfileBodyOverride = body;
            tab.ShowDemoElo("profile:achievements-tad");
            var all = Walk(body).ToList();
            Assert.Contains(all.OfType<StackPanel>(), p => Equals(p.Tag, "AchvGroup:room_rules"));
            Assert.Equal(37, all.OfType<Border>().Count(b => b.Tag is string s && s.StartsWith("AchvTile:")));
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheExtendedSampleDrawsEveryMedalInBothLanguages()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            string previous = Strings.Language;
            try
            {
                foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
                {
                    Strings.SetLanguage(lang);
                    var tab = new MultiplayerTab();
                    var body = new StackPanel();
                    tab.ProfileBodyOverride = body;
                    tab.ShowDemoElo("profile:achievements-more");
                    var all = Walk(body).ToList();
                    Assert.Contains(all.OfType<StackPanel>(), p => Equals(p.Tag, "AchvGroup:historical_wars"));
                    Assert.Contains(all.OfType<StackPanel>(), p => Equals(p.Tag, "AchvGroup:feats"));
                    Assert.Equal(68, all.OfType<Border>().Count(b => b.Tag is string s && s.StartsWith("AchvTile:")));
                    Assert.Contains(all.OfType<TextBlock>(), t => t.Text == Strings.Get("AchvMetalPlatinumShort"));
                    foreach (var text in all.OfType<TextBlock>().Select(t => t.Text))
                        Assert.False(text.StartsWith("Achv", StringComparison.Ordinal), text);
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData(700, 3)]
    [InlineData(740, 4)]
    [InlineData(500, 2)]
    [InlineData(2000, 4)]
    public void TilesComeFourToARowAndFewerWhenNarrow(double width, int columns)
        => Assert.Equal(columns, MultiplayerTab.AchvColumns(width));

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheCardStopsEveryClockItStarted()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var host = new Grid();
            var model = new AchievementUnlockOverlay.Model(new Medal.Look("climb", 1, true, "g"), "Giant Slayer",
                "Beat someone with 200 more ELO", "Gold medal · 15 of 40 in Wars of Liberty", Colors.Gold);
            int before = AchievementUnlockOverlay.LiveClocks;
            var close = AchievementUnlockOverlay.Show(host, model, () => { }, reducedEffects: false);
            Assert.True(AchievementUnlockOverlay.LiveClocks > before);
            Assert.Equal(Visibility.Visible, host.Visibility);
            close();
            Assert.Equal(before, AchievementUnlockOverlay.LiveClocks);
            Assert.Empty(host.Children);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            close(); // twice is harmless
        });
        Assert.Null(error);
    }

    [Fact]
    public void WithTheEffectsReducedNothingMoves()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var host = new Grid();
            var model = new AchievementUnlockOverlay.Model(new Medal.Look("climb", 1, true, "g"), "n", "l", "m", Colors.Gold);
            int before = AchievementUnlockOverlay.LiveClocks;
            var close = AchievementUnlockOverlay.Show(host, model, () => { }, reducedEffects: true);
            Assert.Equal(before, AchievementUnlockOverlay.LiveClocks);
            close();
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheSmallNoticeWearsTheMedalAndTheGoldButton()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var opts = new AppToast.ToastOptions(AppToast.ToastTone.Gold, AppToast.ToastIcon.Check, "Giant Slayer",
                "Beat someone with 200 more ELO",
                new[] { new AppToast.ToastAction("View", AppToast.ToastButton.Gold, () => { }) },
                Medal: new Medal.Look("climb", 1, true, "g"));
            var card = AppToast.Build(opts, () => { });
            var all = Walk(card).ToList();
            var tile = all.OfType<FrameworkElement>().Single(e => Equals(e.Tag, "ToastTile"));
            Assert.Equal(AppToast.MedalTileSize, tile.Width);
            Assert.Contains(all.OfType<Button>(), b => Equals(b.Tag, AppToast.ToastButton.Gold));
            // 121d is one row with no close button: it closes by itself or with View.
            Assert.DoesNotContain(all.OfType<Button>(), b => Equals(b.Tag, AppToast.ToastCloseTag));
        });
        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
