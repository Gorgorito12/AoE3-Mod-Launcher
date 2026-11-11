using System;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The quick match bar (2026-11-10, replacing design 86's tall card): what each state says, which
/// buttons it offers, and when the mod and kind pickers show.
/// </summary>
[Collection("wpf-and-language")]
public class QuickCardViewTests
{
    private static QuickCardModel Of(QuickCardState s, QuickCardWait w = QuickCardWait.Nobody,
        QuickMatchKind kind = QuickMatchKind.Ranked, string? elsewhere = null, bool roomCasual = false)
        => QuickCardView.Of(new QuickCardFacts(s, w, "Wars of Liberty", kind,
            Host: "Kaiser", Minutes: 4, RoomCasual: roomCasual, OtherRooms: 2, Peak: "17:00 y 20:00",
            Rival: "Cuzco", RoomName: "Partida de la noche", ElsewhereMod: elsewhere));

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
                foreach (var k in Enum.GetValues<QuickMatchKind>())
                foreach (var elsewhere in new[] { null, "Hundred Days" })
                {
                    var m = Of(s, w, k, elsewhere);
                    foreach (var text in new[] { m.Title, m.Line, m.Elsewhere }.Where(t => t != null))
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
    public void ReadyNamesWhoIsWaitingAndOffersTheSearchAndThePickers()
    {
        var m = Of(QuickCardState.Ready, QuickCardWait.Room);
        Assert.Contains("Kaiser", m.Line);
        Assert.Equal(QuickCardAction.Search, Assert.Single(m.Buttons).Action);
        Assert.True(m.ShowPickers);
        Assert.Null(m.Elsewhere);
    }

    [Fact]
    public void ACasualRoomSaysItIsCasual()
        => Assert.Equal(Strings.Format("QcWaitRoomCasual", "Kaiser", 4),
            Of(QuickCardState.Ready, QuickCardWait.Room, QuickMatchKind.Either, roomCasual: true).Line);

    /// <summary>Under way, the search has fixed the mod and the kind: no pickers, and the title says both.</summary>
    [Fact]
    public void APickerShowsOnlyWhileNothingIsUnderWay()
    {
        foreach (var state in new[] { QuickCardState.Looking, QuickCardState.Waiting, QuickCardState.Found,
                     QuickCardState.InOtherRoom, QuickCardState.Outdated })
            Assert.False(Of(state).ShowPickers, state.ToString());
    }

    [Fact]
    public void LookingSaysTheModAndTheKind()
    {
        var m = Of(QuickCardState.Looking, kind: QuickMatchKind.Casual);
        Assert.Contains("Wars of Liberty", m.Title);
        Assert.Contains(QuickCardView.KindText(QuickMatchKind.Casual), m.Title);
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
    public void FoundSaysWhoAndOffersTheRoom()
    {
        var m = Of(QuickCardState.Found);
        Assert.Equal(QuickCardMark.Found, m.Mark);
        Assert.Contains("Cuzco", m.Title);
        Assert.True(Assert.Single(m.Buttons).Primary);
    }

    /// <summary>A mod without a ladder is no longer a dead end: it searches casual, and says so.</summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_AModWithoutALadderSearchesCasual()
    {
        var m = Of(QuickCardState.NotRanked, kind: QuickMatchKind.Casual);
        Assert.Contains("Wars of Liberty", m.Line);
        Assert.Equal(QuickCardAction.Search, Assert.Single(m.Buttons).Action);
        Assert.True(m.ShowPickers);
    }

    /// <summary>Nobody in the chosen mod, somebody in another installed one: the bar says where.</summary>
    [Fact]
    public void SomebodyWaitingInAnotherModIsSaid()
    {
        var m = Of(QuickCardState.Ready, QuickCardWait.Nobody, elsewhere: "Hundred Days");
        Assert.Contains("Hundred Days", m.Elsewhere);
        Assert.Contains("Wars of Liberty", m.Line);
        // Not when somebody waits right here.
        Assert.Null(Of(QuickCardState.Ready, QuickCardWait.Room, elsewhere: "Hundred Days").Elsewhere);
    }
}
