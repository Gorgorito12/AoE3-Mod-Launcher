using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Rooms list's action button in the colours the Discord announcement gives each room
/// (design 66): green Join, blue In game, amber Full, each with a dot — and every other state
/// exactly as it was. Nothing here throws when broken: a wrong colour still renders, and a caption
/// that no longer fits its 96-px column is simply painted past the edge of the button.
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

    private static LobbySummary Room(string id, int max, int current, string status = "open", string hostId = "someone-else") => new()
    {
        Id = id,
        Title = "Sala " + id,
        ModId = "wol",
        MaxPlayers = max,
        CurrentPlayers = current,
        Status = status,
        Host = new LobbyHost { Id = hostId, DiscordUsername = "host-" + id },
    };

    /// <summary>A tab whose config says Wars of Liberty is installed, so its rooms can be joined.</summary>
    private static MultiplayerTab TabWithModInstalled(bool installed = true)
    {
        var tab = new MultiplayerTab();
        var config = new LauncherConfig();
        if (installed) config.GetState("wol").InstallPath = @"C:\Games\Wars of Liberty";
        typeof(MultiplayerTab).GetField("_config", Private)!.SetValue(tab, config);
        return tab;
    }

    private static Button ActionOf(MultiplayerTab tab, LobbySummary lobby)
    {
        var card = (Border)typeof(MultiplayerTab).GetMethod("BuildRoomCard", Private)!
            .Invoke(tab, new object[] { lobby, 0 })!;
        return Descendants(card).OfType<Button>().Single(b => ReferenceEquals(b.Tag, lobby));
    }

    private static object Res(string key) => Application.Current.FindResource(key);

    private static Ellipse? DotOf(Button b) => Descendants(b).OfType<Ellipse>()
        .SingleOrDefault(e => Equals(e.Tag, MultiplayerTab.RoomActionDotTag));

    [Fact]
    public void EachStateWearsTheColourTheDiscordPostGivesIt()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tab = TabWithModInstalled();

            var join = ActionOf(tab, Room("OPEN0001", max: 4, current: 2));
            Assert.Same(Res("MpRoomActionJoin"), join.Style);
            Assert.True(join.IsEnabled);
            Assert.Same(Res("MpRoomJoinBg"), join.Background);
            Assert.Same(Res("MpRoomJoinRim"), join.BorderBrush);
            Assert.Same(Res("MpRoomJoinDot"), DotOf(join)!.Fill);

            var inGame = ActionOf(tab, Room("GAME0001", max: 2, current: 2, status: "in_game"));
            Assert.Same(Res("MpRoomActionInGame"), inGame.Style);
            Assert.False(inGame.IsEnabled);
            Assert.Same(Res("MpRoomInGameBg"), inGame.Background);
            Assert.Same(Res("MpRoomInGameRim"), inGame.BorderBrush);
            Assert.Same(Res("MpRoomInGameText"), inGame.Foreground);
            Assert.Same(Res("MpRoomInGameDot"), DotOf(inGame)!.Fill);

            var full = ActionOf(tab, Room("FULL0001", max: 4, current: 4));
            Assert.Same(Res("MpRoomActionFull"), full.Style);
            Assert.False(full.IsEnabled);
            Assert.Same(Res("MpRoomFullBg"), full.Background);
            Assert.Same(Res("MpRoomFullRim"), full.BorderBrush);
            Assert.Same(Res("MpRoomFullText"), full.Foreground);
            Assert.Same(Res("MpRoomFullDot"), DotOf(full)!.Fill);

            // The caption beside the dot sets no colour of its own, or the button's hover could
            // never reach it.
            foreach (var b in new[] { join, inGame, full })
            {
                var caption = Descendants(b).OfType<TextBlock>().Single();
                Assert.Equal(DependencyProperty.UnsetValue, caption.ReadLocalValue(TextBlock.ForegroundProperty));
            }
        });
        Assert.Null(error);
    }

    [Fact]
    public void JoinBrightensItsRimOnHover_AndAStatusSaysNothingToTheMouse()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var join = (Style)Res("MpRoomActionJoin");
            var hover = join.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
            var setters = hover.Setters.OfType<Setter>().ToList();
            Assert.Contains(setters, s => s.Property == Control.BackgroundProperty
                && s.Value is DynamicResourceExtension d && Equals(d.ResourceKey, "MpRoomJoinHoverBg"));
            Assert.Contains(setters, s => s.Property == Control.BorderBrushProperty
                && s.Value is DynamicResourceExtension d && Equals(d.ResourceKey, "MpRoomJoinRimHover"));

            foreach (var key in new[] { "MpRoomActionInGame", "MpRoomActionFull" })
            {
                var style = (Style)Res(key);
                for (var s = style; s != null; s = s.BasedOn)
                    Assert.Empty(s.Triggers);
                var b = new Button { Style = style };
                Assert.Same(Cursors.Arrow, b.Cursor);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the dot costs width in a 96-px column whose button keeps 10 px of
    /// padding a side, and a caption that does not fit is not trimmed — it is painted past the
    /// button's edge. Spanish is the case that binds ("En partida"); in English all of it fits
    /// with room to spare. The button is built exactly as the row builds it.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheDottedCaptionsFitTheActionColumn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                foreach (var language in new[] { "es", "en" })
                {
                    Strings.SetLanguage(language);
                    foreach (var (style, caption, dot) in new[]
                    {
                        ("MpRoomActionInGame", "MpRoomStatusInGame", "MpRoomInGameDot"),
                        ("MpRoomActionFull", "MpRoomFull", "MpRoomFullDot"),
                        ("MpRoomActionJoin", "MpRoomJoin", "MpRoomJoinDot"),
                    })
                    {
                        var b = new Button
                        {
                            Style = (Style)Res(style),
                            Padding = new Thickness(10, 4, 10, 4),
                            Content = MultiplayerTab.RoomActionContent(Strings.Get(caption), dot),
                        };
                        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(b.DesiredSize.Width <= 96,
                            $"'{Strings.Get(caption)}' ({language}) needs {b.DesiredSize.Width:0.#} px; the column is 96.");
                    }
                }
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The maintainer kept every other state as it was: a Join for a mod that is not installed
    /// stays the inert look with a plain caption — a dot there would say "open to you" on a button
    /// that is not — and Re-enter and Your room keep their styles and plain captions. Those two
    /// need a signed-in session to reach, so they are read from the source.
    /// </summary>
    [Fact]
    public void TheOtherStatesAreExactlyAsTheyWere()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var noMod = ActionOf(TabWithModInstalled(installed: false), Room("NOMOD001", max: 4, current: 1));
            Assert.Same(Res("MpRoomActionInert"), noMod.Style);
            Assert.False(noMod.IsEnabled);
            Assert.Null(DotOf(noMod));
            Assert.Equal(Strings.Get("MpRoomJoin"), noMod.Content as string);
        });
        Assert.Null(error);

        var source = File.ReadAllText(RepoFile("Controls/MultiplayerTab.xaml.cs")).Replace("\r\n", "\n");
        Assert.Contains("actionBtn.Style = reenter;\n            actionBtn.Content = Strings.Get(\"MpRoomReenter\");", source);
        Assert.Contains("actionBtn.Style = inert;\n            actionBtn.Content = Strings.Get(\"MpRoomYours\");", source);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = System.IO.Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(System.IO.Path.Combine(project, "App.xaml")))
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(project, relative));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }
}
