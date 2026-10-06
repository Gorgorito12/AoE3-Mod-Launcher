using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which badges move, and what a still one costs.
///
/// <para>With the clocks fixed, a player's laptop still froze: every frame cost ~300 ms there,
/// and the lights were spread across the whole window — the chat, the players panel, the rooms,
/// the room's roster, the ranking, the account — so WPF redrew most of it 30 times a second. Only
/// the player's own badge and the top of a ladder move now, a badge in a list is built still, and
/// a PC measured to draw too slowly turns every light off for the session.</para>
/// </summary>
// Serialised with the other WPF tests: AnimationsOverride and the reduced-effects flag are STATICS.
[Collection("wpf-and-language")]
public class RankBadgeMotionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void AStillBadgeIsTheSameBadgeWithNoLight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var lit = (FrameworkElement)RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
                var still = (FrameworkElement)RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1", animated: false);

                Assert.True(RankBadge.AnimationCount(lit) > 0);
                Assert.Equal(0, RankBadge.AnimationCount(still));
                Assert.Equal(lit.Tag, still.Tag);
                Assert.Equal(lit.Width, still.Width);
                Assert.Equal(lit.Height, still.Height);
                // Lighter, not just paused: none of the light's layers is built at all.
                Assert.True(Walk(still).Count() < Walk(lit).Count());

                var team = RankBadge.BuildFor(
                    new ShownBadge(BadgeKind.Team, RankAge.Imperial, 2, null, 0), 24, "user-2", animated: false);
                Assert.All(Badges(team), b => Assert.Equal(0, RankBadge.AnimationCount(b)));
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. Every list draws its badges still — the players panel, the rooms and
    /// the room's roster are built through the real tab, at first place, where a light would be
    /// brightest.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ListsDrawStillBadges()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var tab = new MultiplayerTab();

                var users = (System.Collections.IList)typeof(MultiplayerTab)
                    .GetField("_globalOnlineUsers", Private)!.GetValue(tab)!;
                users.Add(new MultiplayerTab.OnlinePlayer("u1", "first", null, "idle", 1900.0, 60.0, 1));
                typeof(MultiplayerTab).GetMethod("RenderPlayersPanel", Private)!.Invoke(tab, null);
                var playersBadges = Badges(tab.PlayersPanel);

                var room = (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!
                    .Invoke(tab, new object[]
                    {
                        new LobbySummary
                        {
                            Id = "abc123", Title = "Sala", ModId = "wol", MaxPlayers = 2, Status = "open",
                            Host = new LobbyHost { Id = "u1", DiscordUsername = "first", LadderRank = 1 },
                        },
                        0,
                    })!;

                var memberType = typeof(MultiplayerTab).GetNestedType("RoomMemberEntry", BindingFlags.NonPublic)!;
                var member = Activator.CreateInstance(memberType, nonPublic: true)!;
                memberType.GetProperty("UserId")!.SetValue(member, "u2");
                memberType.GetProperty("Login")!.SetValue(member, "rival");
                memberType.GetProperty("Rating")!.SetValue(member, 1900.0);
                memberType.GetProperty("Rd")!.SetValue(member, 60.0);
                memberType.GetProperty("LadderRank")!.SetValue(member, 1);
                var roster = (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildMemberRow", Private)!
                    .Invoke(tab, new[] { member })!;

                foreach (var (where, badges) in new[]
                         {
                             ("players panel", playersBadges),
                             ("rooms row", Badges(room)),
                             ("room roster", Badges(roster)),
                         })
                {
                    Assert.True(badges.Length > 0, $"{where}: no badge drawn at all");
                    Assert.All(badges, b => Assert.True(RankBadge.AnimationCount(b) == 0, $"{where}: the badge carries a light"));
                }
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>The Rooms page's ranking card: the first three wear a light, the rest are still.</summary>
    [Fact]
    public void OnlyTheTopOfTheLadderWearsALight()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var tab = new MultiplayerTab();
                var stats = StatsDemoData.Community();
                typeof(MultiplayerTab).GetField("_communityStats", Private)!.SetValue(tab, stats);
                var build = typeof(MultiplayerTab).GetMethod("BuildStripLeaderboardRow", Private)!;

                var rows = stats.Leaderboard.Take(5).ToList();
                Assert.True(rows.Count > RankBadge.AnimatedTopPlaces, "the sample must reach past the lit places");
                foreach (var row in rows)
                {
                    var built = (DependencyObject)build.Invoke(tab, new object[] { row, false })!;
                    var badge = Badges(built).Single();
                    var lit = RankBadge.AnimationCount(badge) > 0;
                    Assert.True(lit == RankBadge.AnimatesAt(row.Rank)
                                || (RankAge)((FrameworkElement)badge).Tag! == RankAge.Discovery,
                        $"place {row.Rank}: lit={lit}");
                }
                Assert.True(RankBadge.AnimatesAt(1) && RankBadge.AnimatesAt(3));
                Assert.False(RankBadge.AnimatesAt(4) || RankBadge.AnimatesAt(0));
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The automatic switch: once this PC is found to draw too slowly, what was running stops and
    /// nothing built or started afterwards carries a light — the update pill hears about it too.
    /// </summary>
    [Fact]
    public void ReducedEffectsTurnEveryLightOffAndKeepItOff()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            var raised = 0;
            void OnChanged() => raised++;
            RankBadge.ReducedEffectsChanged += OnChanged;
            try
            {
                var before = RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1");
                RankBadge.Start(before);
                var clocks = RankBadge.ClocksOf(before).ToList();
                Assert.NotEmpty(clocks);

                RankBadge.SetReducedEffects(true);
                Tick();

                Assert.Equal(1, raised);
                Assert.False(RankBadge.IsRunning(before));
                Assert.All(clocks, c => Assert.Equal(ClockState.Stopped, c.CurrentState));

                RankBadge.Start(before);
                Assert.False(RankBadge.IsRunning(before));
                Assert.Equal(0, RankBadge.AnimationCount(RankBadge.Build(RankAge.Sovereign, "1", 28, "user-1")));
                Assert.Equal(0, RankBadge.AnimationCount(RankBadge.BuildRowBanner(RankAge.Sovereign)));
            }
            finally
            {
                RankBadge.SetReducedEffects(false);
                RankBadge.ReducedEffectsChanged -= OnChanged;
                RankBadge.AnimationsOverride = null;
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The blurred layers are cached, so their blur is worked out once and not on every frame —
    /// and NOTHING holding the crisp numeral is, because a cached layer loses ClearType.
    /// </summary>
    [Fact]
    public void TheBlurIsCachedAndTheNumeralIsNot()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            RankBadge.AnimationsOverride = true;
            try
            {
                var badge = (Grid)RankBadge.Build(RankAge.Imperial, "2", 28, "user-1");
                var aura = badge.Children.OfType<Path>().Single(p => p.Effect is BlurEffect);
                Assert.IsType<BitmapCache>(aura.CacheMode);

                var glow = badge.Children.OfType<TextBlock>().Single(t => ReferenceEquals(t.Tag, RankBadge.NumeralGlowTag));
                Assert.IsType<BitmapCache>(glow.CacheMode);

                var numeral = badge.Children.OfType<TextBlock>().Single(t => t.Effect == null);
                Assert.Null(numeral.CacheMode);
                Assert.Null(badge.CacheMode);
            }
            finally { RankBadge.AnimationsOverride = null; }
        });
        Assert.Null(error);
    }

    // ── helpers ──

    /// <summary>Advances this thread's animation clocks as a render would (see RankBadgeClockTests).</summary>
    private static void Tick()
    {
        System.Threading.Thread.Sleep(30);
        var mcType = typeof(Visual).Assembly.GetType("System.Windows.Media.MediaContext")!;
        var mc = mcType.GetMethod("From", Any, new[] { typeof(Dispatcher) })!.Invoke(null, new object[] { Dispatcher.CurrentDispatcher })!;
        var tm = mcType.GetProperty("TimeManager", Any)!.GetValue(mc)!;
        tm.GetType().GetMethod("Tick", Any, Type.EmptyTypes)!.Invoke(tm, null);
    }

    /// <summary>Every badge in the element's logical tree — a badge's root carries its age as Tag.</summary>
    private static FrameworkElement[] Badges(DependencyObject root)
        => Walk(root).OfType<FrameworkElement>().Where(e => e.Tag is RankAge).ToArray();

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var d in Walk(child)) yield return d;
    }
}
