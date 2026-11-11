using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

public class MatchupListViewTests
{
    private static MatchupEntry E(string a, string b, int played, int wins, int losses, string version = "v1")
        => new() { CivA = a, CivB = b, Played = played, WinsA = wins, LossesA = losses, ModVersion = version };

    [Fact]
    public void THE_ONE_THAT_MATTERS_OnePairIsOneLineWhateverTheVersions()
    {
        var lines = MatchupListView.Merge(new[]
        {
            E("Germans", "Dutch", 5, 3, 2, "v1"),
            E("Germans", "Dutch", 4, 1, 3, "v2"),
        });

        var line = Assert.Single(lines);
        Assert.Equal(new MatchupLine("Germans", "Dutch", 9, 4, 5), line);
    }

    [Fact]
    public void APairSentTheOtherWayRoundIsFoldedWithItsRecordTurned()
    {
        var lines = MatchupListView.Merge(new[]
        {
            E("Germans", "Dutch", 5, 3, 2),
            E("Dutch", "Germans", 2, 2, 0),
        });

        Assert.Equal(new MatchupLine("Germans", "Dutch", 7, 3, 4), Assert.Single(lines));
    }

    [Fact]
    public void MostPlayedFirst_AndTiesKeepTheServersOrder()
    {
        var lines = MatchupListView.Merge(new[]
        {
            E("A", "B", 3, 1, 1),
            E("C", "D", 9, 4, 4),
            E("E", "F", 3, 2, 1),
        });

        Assert.Equal(new[] { "C", "A", "E" }, lines.Select(l => l.CivA));
    }

    [Fact]
    public void TheTopIsCapped()
    {
        var rows = Enumerable.Range(0, 600).Select(i => E("Civ" + i, "Other" + i, 600 - i, 1, 1));
        var lines = MatchupListView.Merge(rows);

        Assert.Equal(600, lines.Count);
        Assert.Equal(MatchupListView.TopCount, MatchupListView.Top(lines).Count);
        Assert.Equal("Civ0", MatchupListView.Top(lines)[0].CivA);
    }

    [Fact]
    public void ACivilizationsPairsAreReadFromItsSide()
    {
        var lines = MatchupListView.Merge(new[]
        {
            E("Germans", "Dutch", 10, 7, 3),
            E("British", "Germans", 6, 4, 2),
            E("British", "Dutch", 8, 4, 4),
        });

        var germans = MatchupListView.For(lines, "Germans");

        Assert.Equal(2, germans.Count);
        Assert.All(germans, l => Assert.Equal("Germans", l.CivA));
        Assert.Equal(new MatchupLine("Germans", "Dutch", 10, 7, 3), germans[0]);
        // The British won four of six against them, so the Germans won two.
        Assert.Equal(new MatchupLine("Germans", "British", 6, 2, 4), germans[1]);
    }

    [Fact]
    public void AnAlliesRecordIsThePairsAndNeverTurnsRound()
    {
        var lines = MatchupListView.Merge(new[]
        {
            E("Germans", "Dutch", 5, 3, 2),
            E("Dutch", "Germans", 2, 2, 0),
        }, sameSide: true);

        Assert.Equal(new MatchupLine("Germans", "Dutch", 7, 5, 2), Assert.Single(lines));
        Assert.Equal(new MatchupLine("Dutch", "Germans", 7, 5, 2),
            Assert.Single(MatchupListView.For(lines, "Dutch", sameSide: true)));
    }

    [Fact]
    public void EmptyAndBrokenRowsGiveNothing()
    {
        Assert.Empty(MatchupListView.Merge(null));
        Assert.Empty(MatchupListView.Merge(new[] { E("", "Dutch", 3, 1, 1), E("Germans", " ", 3, 1, 1) }));
    }

    [Fact]
    public void TheCivilizationsListNamesEachOnce()
    {
        var lines = MatchupListView.Merge(new[] { E("A", "B", 1, 1, 0), E("B", "C", 1, 0, 1) });
        Assert.Equal(new[] { "A", "B", "C" }, MatchupListView.Civs(lines).OrderBy(c => c));
    }
}
