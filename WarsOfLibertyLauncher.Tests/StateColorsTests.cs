using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The fixed state colours of docs/design_14nov: the same green, blue, gold, amber, red and grey
/// the Rooms page declares, and a tournament's state reading the same everywhere it shows.
/// </summary>
[Collection("wpf-and-language")]
public class StateColorsTests
{
    /// <summary>The C# copies are the XAML's brushes, so a page built in code and a page in XAML
    /// cannot drift into two greens.</summary>
    [Fact]
    public void TheCodesColoursAreTheBrushesOfTheRoomsPage()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            Color Of(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
            Assert.Equal(Of("RmGroupOpen"), StateColors.Green);
            Assert.Equal(Of("RmGroupOpenText"), StateColors.GreenText);
            Assert.Equal(Of("RmGroupLive"), StateColors.Blue);
            Assert.Equal(Of("RmGroupLiveText"), StateColors.BlueText);
            Assert.Equal(Of("RmGroupOff"), StateColors.Amber);
            Assert.Equal(Of("RmGroupOffText"), StateColors.AmberText);
            Assert.Equal(Of("UiDanger"), StateColors.Red);
            Assert.Equal(Of("UiDangerText"), StateColors.RedText);
            Assert.Equal(Of("UiGold"), StateColors.Gold);
            Assert.Equal(Of("UiGoldText"), StateColors.GoldText);
            Assert.Equal(Of("RmJoinBg"), StateColors.WonFill);
            // The one gold: the favourite star, the competitive label and the ranked dot share it.
            Assert.Equal(Of("UiGold"), Of("UiFavorite"));
            Assert.Equal(Of("UiGold"), Of("MpMatchLabelCompetitive"));
            Assert.Equal(Of("UiGold"), Of("RmDotRanked"));
        });
        Assert.Null(error);
    }

    /// <summary>Registration is green like an open room, being played blue like a room in a match,
    /// finished gold. Only being played beats.</summary>
    [Theory]
    [InlineData("draft", "Neutral", false)]
    [InlineData("registration", "Open", false)]
    [InlineData("ready", "Live", false)]
    [InlineData("running", "Live", true)]
    [InlineData("finished", "Gold", false)]
    [InlineData("cancelled", "Danger", false)]
    [InlineData("abandoned", "Neutral", false)]
    public void ATournamentsStateHasOneColour(string status, string tone, bool pulses)
    {
        Assert.Equal(tone, StateColors.ForTournament(status).ToString());
        Assert.Equal(pulses, StateColors.Pulses(status));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The champion is gold, never the mod's colour: with Wars of Liberty (a
    /// red mod) the champion's card read as an error. And with four players the champion's card
    /// is no longer cut against the round titles: nothing on the sheet starts above it, and a
    /// short sheet stands in 200.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TheChampionIsGoldAndNeverCut()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("finished");
            var canvas = Logical(tab.TournamentDetailPanel).OfType<Canvas>().Single();
            Assert.True(canvas.Height >= MultiplayerTab.SmallSheetHeight);
            foreach (UIElement child in canvas.Children)
                Assert.True(Canvas.GetTop(child) >= 0, "something on the sheet starts above it");

            // No brush on the page carries the mod's red where gold is meant: the champion's rim.
            var red = ModAccentOf("wol");
            var golds = Logical(tab.TournamentDetailPanel).OfType<Border>()
                .Where(b => b.BorderBrush is SolidColorBrush sb && sb.Color.R == StateColors.Gold.R && sb.Color.G == StateColors.Gold.G && sb.Color.B == StateColors.Gold.B)
                .ToList();
            Assert.NotEmpty(golds);
            Assert.DoesNotContain(Logical(tab.TournamentDetailPanel).OfType<TextBlock>(),
                t => t.Text == WarsOfLibertyLauncher.Localization.Strings.Get("TpChampion")
                     && t.Foreground is SolidColorBrush f && f.Color.R == red.R && f.Color.G == red.G && f.Color.B == red.B);
        });
        Assert.Null(error);
    }

    private static Color ModAccentOf(string id)
    {
        var mod = WarsOfLibertyLauncher.Services.ModRegistry.Find(id);
        return WarsOfLibertyLauncher.Services.ModTheme.AccentOf(mod?.AccentColor, true);
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Logical(child)) yield return d;
        }
    }
}
