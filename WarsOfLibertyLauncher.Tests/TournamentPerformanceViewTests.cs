using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Tournament performance on an entrant's tile (2026-11-18): the server's figure, never one
/// worked out here, and a hidden one says why only on the viewer's own tile.</summary>
[Collection("wpf-and-language")]
public class TournamentPerformanceViewTests
{
    [Fact]
    public void TheCapIsMarked()
    {
        Assert.Equal("1986+", TournamentPerformanceView.For(new EntrantPerformance { Rating = 1986, Capped = "above", Games = 2 }, false)!.Figure);
        Assert.Equal("1120−", TournamentPerformanceView.For(new EntrantPerformance { Rating = 1120, Capped = "below", Games = 2 }, false)!.Figure);
        Assert.Equal("1712", TournamentPerformanceView.For(new EntrantPerformance { Rating = 1712, Games = 3 }, false)!.Figure);
    }

    [Fact]
    public void OneGameStillNamesTheBestWin()
    {
        var line = TournamentPerformanceView.For(new EntrantPerformance
        {
            Games = 1, BestWin = new EntrantBestWin { Name = "Gorgo", Rating = 1688 },
        }, false)!;
        Assert.Null(line.Figure);
        Assert.Equal("Gorgo (1688)", line.BestWin);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_AHiddenFigureSaysWhyOnlyOnMyOwnTile()
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEs);
        try
        {
            var hidden = new EntrantPerformance { Hidden = "placement" };
            Assert.Null(TournamentPerformanceView.For(hidden, mine: false));
            var mine = TournamentPerformanceView.For(hidden, mine: true)!;
            Assert.Null(mine.Figure);
            Assert.Contains("posicionamiento", mine.HiddenWhy);
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void NothingFromAnOlderServer()
        => Assert.Null(TournamentPerformanceView.For(null, true));
}
