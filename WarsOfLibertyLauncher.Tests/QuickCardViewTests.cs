using System;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>The quick match card of design 86: what each state says and which buttons it offers.</summary>
[Collection("wpf-and-language")]
public class QuickCardViewTests
{
    private static QuickCardModel Of(QuickCardState s, QuickCardWait w = QuickCardWait.Nobody, bool compact = false)
        => QuickCardView.Of(new QuickCardFacts(s, w, "Wars of Liberty", compact,
            Host: "Kaiser", Minutes: 4, OtherRooms: 2, Peak: "17:00 y 20:00", Rival: "Cuzco", RoomName: "Partida de la noche"));

    [Fact]
    public void EveryStateSaysSomethingInBothLanguagesAndHasExactlyOnePrimaryAtMost()
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { "es", "en" })
            {
                Strings.SetLanguage(lang);
                foreach (var s in Enum.GetValues<QuickCardState>())
                foreach (var w in Enum.GetValues<QuickCardWait>())
                foreach (var compact in new[] { false, true })
                {
                    var m = Of(s, w, compact);
                    foreach (var text in new[] { m.Kicker, m.Title, m.Line, m.Foot }.Where(t => t != null))
                    {
                        Assert.DoesNotContain("{", text);
                        Assert.False(text!.StartsWith("Qc") || text.StartsWith("Qm"), text);
                    }
                    Assert.NotEmpty(m.Buttons);
                    Assert.True(m.Buttons.Count(b => b.Primary) <= 1, $"{s} {w}");
                    Assert.All(m.Buttons, b => Assert.False(b.Caption.StartsWith("Q"), b.Caption));
                }
            }
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void ReadyNamesWhoIsWaitingAndOffersTheSearch()
    {
        var m = Of(QuickCardState.Ready, QuickCardWait.Room);
        Assert.Contains("Kaiser", m.Line);
        Assert.Equal(QuickCardAction.Search, Assert.Single(m.Buttons).Action);
        Assert.True(m.ShowVersion);
        Assert.NotNull(m.Foot);
    }

    [Fact]
    public void WaitingShowsTheClockAndTwoWaysOut()
    {
        var m = Of(QuickCardState.Waiting);
        Assert.True(m.ShowClock);
        Assert.Equal(QuickCardMark.Pulse, m.Mark);
        Assert.Equal(new[] { QuickCardAction.ViewRoom, QuickCardAction.Stop }, m.Buttons.Select(b => b.Action));
        Assert.DoesNotContain(m.Buttons, b => b.Primary);
    }

    [Fact]
    public void FoundIsGreenAndSaysWhoHosts()
    {
        var m = Of(QuickCardState.Found);
        Assert.True(m.KickerGreen);
        Assert.Contains("Cuzco", m.Title);
        Assert.True(Assert.Single(m.Buttons).Primary);
    }

    [Fact]
    public void TheCompactCardHasNoKickerNoFootAndAShortTitle()
    {
        var m = Of(QuickCardState.Ready, QuickCardWait.Room, compact: true);
        Assert.Null(m.Kicker);
        Assert.Null(m.Foot);
        Assert.False(m.ShowVersion);
        Assert.Equal(Strings.Format("QcTitleCompact", "Wars of Liberty"), m.Title);
        // A state with its own kicker keeps its own title in the compact card.
        Assert.Equal(Strings.Get("QcWaitingTitle"), Of(QuickCardState.Waiting, compact: true).Title);
    }

    /// <summary>A mod without a ladder offers no search: the card says why and points at the Library.</summary>
    [Fact]
    public void AModWithoutALadderOffersNoSearch()
    {
        var m = Of(QuickCardState.NotRanked);
        Assert.Contains("Wars of Liberty", m.Title);
        Assert.Contains("Wars of Liberty", m.Line);
        Assert.DoesNotContain(m.Buttons, b => b.Action == QuickCardAction.Search);
        Assert.Equal(new[] { QuickCardAction.Library }, m.Buttons.Select(b => b.Action));
        Assert.False(m.ShowVersion);
    }
}
