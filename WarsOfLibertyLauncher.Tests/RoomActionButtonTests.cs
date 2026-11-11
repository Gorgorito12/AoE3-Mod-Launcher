using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The rooms list's one action per row, as design 86 draws it: a faint secondary button for
/// everything a viewer can do, the solid blue only for the room that is theirs, and "Full" as a
/// word rather than a button nobody can press. The rule is <see cref="RoomActionRule"/>; these
/// check that the row paints what it decides.
/// </summary>
[Collection("wpf-and-language")]
public class RoomActionButtonTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static LobbySummary Room(string id, int max, int current, string status = "open", string mod = "wol") => new()
    {
        Id = id,
        Title = "Sala " + id,
        ModId = mod,
        MaxPlayers = max,
        CurrentPlayers = current,
        Status = status,
        Host = new LobbyHost { Id = "someone-else", DiscordUsername = "host-" + id },
    };

    private static MultiplayerTab TabWithModInstalled(bool installed = true)
    {
        var tab = new MultiplayerTab();
        var config = new LauncherConfig();
        if (installed) config.GetState("wol").InstallPath = @"C:\Games\Wars of Liberty";
        typeof(MultiplayerTab).GetField("_config", Private)!.SetValue(tab, config);
        return tab;
    }

    private static Border Card(MultiplayerTab tab, LobbySummary lobby)
        => (Border)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!.Invoke(tab, new object[] { lobby, 0 })!;

    private static Button? ActionOf(Border card, LobbySummary lobby)
        => Descendants(card).OfType<Button>().SingleOrDefault(b => ReferenceEquals(b.Tag, lobby));

    private static object Res(string key) => Application.Current.FindResource(key);

    /// <summary>Every action is the faint secondary button except "Unirse", which carries a blue rim
    /// so the rooms you can enter stand out (2026-11-10), and "Ver sala", the only solid one.</summary>
    [Fact]
    public void EachRoomOffersWhatTheRuleSays_InTheFaintSecondaryButton()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithModInstalled();

            var open = Room("OPEN0001", max: 4, current: 2);
            var join = ActionOf(Card(tab, open), open)!;
            Assert.Same(Res("RmRowButtonJoin"), join.Style);
            Assert.Same(Res("RmRowButton"), ((Style)join.Style).BasedOn);
            Assert.Equal(Strings.Get("RmActJoin"), join.Content);
            Assert.True(join.IsEnabled);

            var game = Room("GAME0001", max: 2, current: 2, status: "in_game");
            var see = ActionOf(Card(tab, game), game)!;
            Assert.Same(Res("RmRowButton"), see.Style);
            Assert.Equal(Strings.Get("RmActSeeMatch"), see.Content);
            Assert.True(see.IsEnabled);

            var other = Room("HDAY0001", max: 4, current: 1, mod: "hundred-days");
            var install = ActionOf(Card(tab, other), other)!;
            Assert.Equal(Strings.Get("RmActInstall"), install.Content);
            Assert.True(install.IsEnabled);
        });
        Assert.Null(error);
    }

    /// <summary>A full room says so as text: a disabled button would still read as something to try.</summary>
    [Fact]
    public void AFullRoomSaysFullAndHasNoButton()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithModInstalled();
            var full = Room("FULL0001", max: 4, current: 4);
            var card = Card(tab, full);
            Assert.Null(ActionOf(card, full));
            Assert.Contains(Descendants(card).OfType<TextBlock>(), t => t.Text == Strings.Get("RmActFull"));
        });
        Assert.Null(error);
    }

    /// <summary>The captions fit the 136-px action cell in both languages ("Revisar mi copia" binds).</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryCaptionFitsTheActionColumn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var language in new[] { "es", "en" })
                {
                    Strings.SetLanguage(language);
                    foreach (var action in Enum.GetValues<RoomAction>().Where(a => a != RoomAction.Full))
                    {
                        var b = new Button
                        {
                            Style = (Style)Res(action == RoomAction.OpenMine ? "RmRowButtonPrimary" : "RmRowButton"),
                            Content = Strings.Get(RoomActionRule.CaptionKey(action)),
                        };
                        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(b.DesiredSize.Width <= 136,
                            $"'{b.Content}' ({language}) needs {b.DesiredSize.Width:0.#} px; the cell is 136.");
                    }
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The list is grouped under the design's three headings, in its order, and every row sits
    /// under the heading its own action belongs to.
    /// </summary>
    [Fact]
    public void TheListIsGroupedUnderTheThreeHeadingsInOrder()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithModInstalled();
            var rooms = new List<LobbySummary>
            {
                Room("FULL0001", max: 4, current: 4),
                Room("GAME0001", max: 2, current: 2, status: "in_game"),
                Room("OPEN0001", max: 4, current: 2),
            };
            typeof(MultiplayerTab).GetField("_lastBrowserList", Private)!.SetValue(tab, rooms);
            typeof(MultiplayerTab).GetMethod("RenderRoomRows", Private)!.Invoke(tab, new object?[] { rooms });

            var order = tab.RoomsListPanel.Children.OfType<FrameworkElement>()
                .Select(e => e.Tag switch
                {
                    LobbySummary l => l.Id,
                    string s => s,
                    _ => "?",
                })
                .ToList();
            Assert.Equal(new[]
            {
                "RoomGroup:" + RoomGroup.CanEnter, "OPEN0001",
                "RoomGroup:" + RoomGroup.InGame, "GAME0001",
                "RoomGroup:" + RoomGroup.Unavailable, "FULL0001",
            }, order);
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Design 104a: a row wears the colour of its GROUP - green if you can enter, blue in a match,
    /// amber if not available - as its rim and as a tint, and no row is left grey.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_EveryRowWearsItsGroupsColour()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithModInstalled();
            var cases = new (LobbySummary Room, string Rim, string Tint)[]
            {
                (Room("OPEN0001", max: 4, current: 2), "RmGroupOpenRim", "RmRowTintOpen"),
                (Room("GAME0001", max: 2, current: 2, status: "in_game"), "RmGroupLiveRim", "RmRowTintLive"),
                (Room("FULL0001", max: 4, current: 4), "RmGroupOffRim", "RmRowTintOff"),
            };
            foreach (var (room, rim, tint) in cases)
            {
                var card = Card(tab, room);
                Assert.Same(tab.FindResource("RmRowCard"), card.Style);
                Assert.Same(Res(rim), card.BorderBrush);
                Assert.Equal(new Thickness(1), card.BorderThickness);
                var layer = Descendants(card).OfType<Border>().Single(b => Equals(b.Tag, "RoomTint"));
                Assert.Same(Res(tint), layer.Background);
            }
        });
        Assert.Null(error);
    }
}
