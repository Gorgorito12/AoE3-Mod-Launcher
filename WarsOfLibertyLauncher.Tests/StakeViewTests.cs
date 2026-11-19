using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The points at stake before a ranked match (2026-11-18). The server's figures, rounded by the
/// result card's own rule, never drawn where they would give a hidden rating back, and the chance
/// of winning drawn ONCE in a 1v1 room. The refusals are the point.
/// </summary>
[Collection("wpf-and-language")]
public class StakeViewTests
{
    private static RoomStake Stake(double before, double win, double loss, int? pct = null) =>
        new() { Before = before, WinAfter = win, LossAfter = loss, WinPct = pct };

    private static StakeView From(RoomStake? s, int? odds = null, bool competitive = true, bool plays = true,
        bool complete = true, bool placing = false, bool @private = false)
        => StakeView.From(s, odds, competitive, plays, complete, placing, @private);

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheStakeIsRoundedByTheResultCardsOwnRule()
    {
        // −16.5 is −17 on the card (away from zero), so it is −17 here too.
        var v = From(Stake(1500, 1508.5, 1483.5, 60));
        Assert.Equal(StakeState.Shown, v.State);
        Assert.Equal(MatchOutcomeView.Delta(1500, 1508.5), v.Gain);
        Assert.Equal(-17, v.Loss);
        Assert.Equal(9, v.Gain);
        Assert.Equal("+9", v.GainText);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ARivalsHiddenRatingNeverComesBackThroughTheStake()
    {
        // Figures that arrived anyway (an older server, a race) are not drawn.
        Assert.Equal(StakeState.HiddenPlacement, From(Stake(1500, 1510, 1490), placing: true).State);
        Assert.Equal(StakeState.HiddenPrivate, From(Stake(1500, 1510, 1490), @private: true).State);
        Assert.Null(From(Stake(1500, 1510, 1490), placing: true).Gain);
        // And the server's own reason wins.
        Assert.Equal(StakeState.HiddenPrivate, From(new RoomStake { HiddenReason = "private" }).State);
        Assert.Equal(StakeState.HiddenPlacement, From(new RoomStake { HiddenReason = "placement" }).State);
    }

    [Fact]
    public void NoStakeInACasualRoom_NorForASpectator_NorWithAnEmptySeat()
    {
        var s = Stake(1500, 1510, 1490, 50);
        Assert.Equal(StakeState.None, From(s, competitive: false).State);
        Assert.Equal(StakeState.None, From(s, plays: false).State);
        Assert.Equal(StakeState.None, From(s, complete: false).State);
    }

    [Fact]
    public void AnOlderServerStillDrawsTheBar()
    {
        var v = From(null, odds: 62);
        Assert.Equal(StakeState.OddsOnly, v.State);
        Assert.Equal(62, v.WinPercent);
        Assert.Null(v.Gain);
        Assert.Equal(StakeState.None, From(null).State);
    }

    [Fact]
    public void TheNoteSaysWhoseStreakItIs()
    {
        var win = From(new RoomStake { Before = 1383, WinAfter = 1400.3, LossAfter = 1377.6, WinFactor = 0.8, WinStreak = 4 });
        Assert.Equal(new StakeNote(true, 80, 4), win.Note);
        var loss = From(new RoomStake { Before = 1383, WinAfter = 1404.7, LossAfter = 1380, LossFactor = 0.7, LossStreak = 5 });
        Assert.Equal(new StakeNote(false, 70, 5), loss.Note);
        Assert.Null(From(Stake(1383, 1404.7, 1377.6)).Note);
    }

    [Fact]
    public void AnOlderFrameNeverReplacesANewerOne()
    {
        Assert.True(StakeView.IsNewer(null, new RoomStake { Seq = 1 }));
        Assert.True(StakeView.IsNewer(new RoomStake { Seq = 2 }, new RoomStake { Seq = 3 }));
        Assert.False(StakeView.IsNewer(new RoomStake { Seq = 3 }, new RoomStake { Seq = 2 }));
    }

    // ---- the room window ----

    private static List<string> Texts(DependencyObject root)
    {
        var all = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb) all.Add(RevealText.PlainTextOf(tb));
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(c);
        }
        Walk(root);
        return all;
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ThePercentageIsDrawnOnceInAOneVOneRoom()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = EloPreviewTests.RoomTab();
                tab.ShowSampleRoom(RoomDemoData.All().Single(s => s.Name == "87b"));
                var w = tab.LobbyWindowForTests!;
                var stake = Texts(w.RoomStakeBlock);
                Assert.Contains("+22", stake);
                Assert.Contains("-5", stake);
                Assert.Contains("20 % a favor", stake);
                // Nowhere else in the window.
                Assert.DoesNotContain(Texts(w.RoomMembersPanel), t => t.Contains("20 %"));
                Assert.Equal(Visibility.Collapsed, w.RoomStakeText.Visibility);
                w.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("87b-farm", "Victoria reducida al 80 %")]
    [InlineData("87b-hidden", "Se muestra cuando tu rival termine el posicionamiento")]
    [InlineData("87b-private", "No se muestra: un jugador de la partida mantiene su ELO en privado.")]
    public void EachSampleSaysWhatItIsFor(string sample, string expected)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            Strings.SetLanguage(Strings.LangEs);
            try
            {
                var tab = EloPreviewTests.RoomTab();
                tab.ShowSampleRoom(RoomDemoData.All().Single(s => s.Name == sample));
                var w = tab.LobbyWindowForTests!;
                Assert.Contains(Texts(w.RoomStakeBlock), t => t.Contains(expected));
                if (sample != "87b-farm") Assert.DoesNotContain(Texts(w.RoomStakeBlock), t => t.StartsWith("+"));
                w.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    // ---- why the points ----

    private static MatchOutcomeView Outcome(MatchVerdict v, double before, double after, int? pct,
        double? factor = null, int? played = null, int? required = null)
        => new(v, "wol", "map", 900, 2, before, after, "Kaiser", 1630, 1, 0, 60,
            EloFactor: factor, PlacementPlayed: played, PlacementRequired: required, WinExpected: pct);

    [Fact]
    public void FavouriteUnderdogAndEven_ByTheServersPercentage()
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEs);
        try
        {
            Assert.Equal(new ResultWhy("+6", "eras el favorito (72 %)."), ResultWhyView.For(Outcome(MatchVerdict.Win, 1600, 1606, 72)));
            Assert.Equal(new ResultWhy("+21", "ganaste sin ser el favorito (31 %)."), ResultWhyView.For(Outcome(MatchVerdict.Win, 1500, 1521, 31)));
            Assert.Equal("partida pareja (50 %).", ResultWhyView.For(Outcome(MatchVerdict.Loss, 1500, 1488, 50))!.Text);
            Assert.Equal("perdiste siendo el favorito (70 %).", ResultWhyView.For(Outcome(MatchVerdict.Loss, 1600, 1583, 70))!.Text);
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_NoPercentageWithoutTheServers()
        => Assert.Null(ResultWhyView.For(Outcome(MatchVerdict.Win, 1600, 1606, null)));

    [Fact]
    public void PlacementExplainsTheBigSwing()
    {
        var why = ResultWhyView.For(Outcome(MatchVerdict.Win, 1500, 1742, 50, played: 1, required: 10))!;
        Assert.Equal("+242", why.Prefix);
        Assert.Contains("1/10", why.Text);
    }

    [Fact]
    public void AntiFarmKeepsItsSentenceAndAddsOnlyTheOdds()
    {
        var why = ResultWhyView.For(Outcome(MatchVerdict.Win, 1600, 1603, 72, factor: 0.4))!;
        Assert.Null(why.Prefix);
    }

    [Fact]
    public void TheResultCardDrawsTheLine()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var card = MatchResultCard.BuildCaseCard(Outcome(MatchVerdict.Win, 1600, 1606, 72));
            var found = false;
            void Walk(DependencyObject d)
            {
                if (d is TextBlock { Tag: MatchResultCard.WhyTag }) found = true;
                foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(c);
            }
            Walk(card);
            Assert.True(found);
        });
        Assert.Null(error);
    }
}
