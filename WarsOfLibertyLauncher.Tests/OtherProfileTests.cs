using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Another player's profile is the viewer's page drawn for a SUBJECT, with his standing swapped in
/// for one render. The guard that matters: the viewer's own cached standing is untouched after it,
/// and the subject's private rating is never printed (the missing number would read 0).
/// </summary>
[Collection("wpf-and-language")]
public class OtherProfileTests
{
    private static EloSnapshot Standing(double rating, bool? isPrivate = null) => new()
    {
        Rating = rating,
        GamesPlayed = 40,
        RatingPrivate = isPrivate,
        Ladders = new LadderStandings
        {
            Default = new LadderStanding { Rating = rating, GamesPlayed = 40, PlacementPlayed = 10, PlacementRequired = 10, LadderRank = 3, LadderSize = 20 },
        },
    };

    private static object? Field(object o, string name)
        => typeof(MultiplayerTab).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);

    private static void SetField(object o, string name, object? value)
        => typeof(MultiplayerTab).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);

    private static List<string> AllText(DependencyObject root)
        => Walk(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_DrawingSomebodyElseLeavesTheViewersStandingAlone()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEn);
            try
            {
                var tab = new MultiplayerTab();
                var body = new StackPanel();
                tab.ProfileBodyOverride = body;
                var mine = Standing(1777);
                SetField(tab, "_cachedStanding", mine);

                var cache = (ProfileCache)Field(tab, "_profileCache")!;
                var entry = cache.Get("other-1", System.DateTime.UtcNow);
                entry.Standing = Standing(0, isPrivate: true);
                entry.History = new List<MatchHistoryRow>();
                entry.AskedUtc = System.DateTime.UtcNow;

                tab.OpenProfileOf("other-1", "Rival", "rival");
                Assert.True(tab.ProfileShowsOther);
                Assert.Same(mine, Field(tab, "_cachedStanding"));

                var texts = AllText(body);
                Assert.Contains(texts, t => t == Strings.Get("MpEloHiddenPrivate"));
                Assert.DoesNotContain(texts, t => t.Contains("1777"));
                // Decks are the viewer's own files: not on another player's page.
                Assert.DoesNotContain(texts, t => t == Strings.Get("MpProfileSectionDecks"));
                Assert.Contains(Walk(body).OfType<Button>(), b => Equals(b.Content, Strings.Get("MpProfileBackToMine")));
                // His page speaks about him, never to the viewer.
                Assert.DoesNotContain(texts, t => t == Strings.Get("MpH2HEmptyYou"));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("u2", "u1", ProfileLinkRule.Target.Other)]
    [InlineData("u1", "u1", ProfileLinkRule.Target.Own)]
    [InlineData("", "u1", ProfileLinkRule.Target.None)]
    [InlineData(null, "u1", ProfileLinkRule.Target.None)]
    [InlineData("u2", null, ProfileLinkRule.Target.Other)]
    public void AClickGoesToTheRightProfile(string? userId, string? viewer, ProfileLinkRule.Target expected)
        => Assert.Equal(expected, ProfileLinkRule.Decide(userId, viewer));

    [Fact]
    public void APrivateRatingOfAPlacedPlayerReadsHiddenAndPlacementIsNotPrivate()
    {
        Assert.True(RatingDisplay.IsPrivateRating(null, 40, 10));
        Assert.False(RatingDisplay.IsPrivateRating(null, 3, 10));   // being placed: not private
        Assert.False(RatingDisplay.IsPrivateRating(1500, 40, 10));
        Assert.False(RatingDisplay.IsPrivateRating(null, null, 10)); // an older server's silence
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryNameOfAMatchRowKnowsWhoItIs()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var match = new CommunityMatch
            {
                Id = "m", ModId = "wol", MapName = "ESOC_Manchac", DurationSeconds = 1560, Competitive = true,
                ReportedAt = System.DateTime.UtcNow.AddHours(-1).ToString("o"),
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "a", DisplayName = "El Taita", Result = 1 },
                    new() { UserId = "b", DisplayName = "skibidi", Result = 0 },
                },
            };
            var row = (FrameworkElement)MultiplayerTab.BuildRankingMatchRow(match, null);
            var names = MultiplayerTab.NameRunsIn(row)
                .Select(r => ((MatchParticipantLine)r.Tag).UserId).OrderBy(x => x).ToList();
            // Both players, each once — a name with no player could never open his profile.
            Assert.Equal(new[] { "a", "b" }, names);
        });
        Assert.Null(error);
    }

    [Fact]
    public void AHistoryCardOffersItsPlayersButNotThePagesOwner()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var linked = new List<string>();
            var line = new MatchParticipantLine("b", "skibidi", null, false, MatchVerdict.Loss, null);
            MultiplayerTab.BuildHistoryPlayerRow(line, (el, p) => linked.Add(p.UserId + ":" + el.GetType().Name));
            // The photo and the name, both.
            Assert.Equal(2, linked.Count);
            Assert.All(linked, l => Assert.StartsWith("b:", l));
            // With no link the row is what it always was.
            MultiplayerTab.BuildHistoryPlayerRow(line);
        });
        Assert.Null(error);
    }
}
