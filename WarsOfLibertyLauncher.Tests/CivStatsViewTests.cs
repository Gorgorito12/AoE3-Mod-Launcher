using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Which civilizations a player uses, and the one decision behind the card: when a percentage may
/// be stated at all.
///
/// <para><b>The refusals are the file.</b> The Profile already stopped showing a win rate to
/// somebody with one decided match, because it printed "0 % wins" for a player whose single game
/// was a loss. A civilization table repeats that mistake once per civilization, and Wars of
/// Liberty ships 188 of them.</para>
/// </summary>
public class CivStatsViewTests
{
    private static MatchHistoryRow Match(string? civ, double result)
        => new() { Id = "m", ModId = "wol", Civ = civ, Result = result };

    private static List<MatchHistoryRow> Many(string civ, int wins, int losses, int unread = 0)
    {
        var rows = new List<MatchHistoryRow>();
        for (var i = 0; i < wins; i++) rows.Add(Match(civ, 1.0));
        for (var i = 0; i < losses; i++) rows.Add(Match(civ, 0.0));
        for (var i = 0; i < unread; i++) rows.Add(Match(civ, 0.5));
        return rows;
    }

    // ------------------------------------------------------------------ counting

    [Fact]
    public void CountsPlayedWinsAndLossesPerCivilization()
    {
        var rows = Many("Chinese", wins: 3, losses: 1);
        rows.AddRange(Many("Colombians", wins: 0, losses: 2));

        var stats = CivStatsView.Rows(rows);

        Assert.Equal(2, stats.Count);
        var chinese = stats.Single(r => r.Civ == "Chinese");
        Assert.Equal(4, chinese.Played);
        Assert.Equal(3, chinese.Wins);
        Assert.Equal(1, chinese.Losses);
        Assert.Equal(4, chinese.Decided);
    }

    /// <summary>
    /// <b>A 0.5 is not a draw</b> — it is the outcome nobody could read, and it is what MOST
    /// stored matches carry. It counts as a match played and as neither a win nor a loss, which
    /// is why <c>Played</c> and <c>Decided</c> are separate numbers on the card.
    /// </summary>
    [Fact]
    public void AMatchNobodyCouldReadIsPlayedButNotDecided()
    {
        var stats = CivStatsView.Rows(Many("Chinese", wins: 1, losses: 0, unread: 4));

        var row = Assert.Single(stats);
        Assert.Equal(5, row.Played);
        Assert.Equal(1, row.Decided);
    }

    /// <summary>
    /// Most rows in anybody's history have no civilization: every match reported before the field
    /// existed, and every one whose recording could not be joined to the roster.
    /// </summary>
    [Fact]
    public void MatchesWithNoCivilizationAreSkippedEntirely()
    {
        var rows = new List<MatchHistoryRow>
        {
            Match(null, 1.0), Match("", 1.0), Match("   ", 0.0), Match("Chinese", 1.0),
        };

        var row = Assert.Single(CivStatsView.Rows(rows));
        Assert.Equal("Chinese", row.Civ);
        Assert.Equal(1, row.Played);
    }

    [Fact]
    public void NoHistoryIsNoRows()
    {
        Assert.Empty(CivStatsView.Rows(null));
        Assert.Empty(CivStatsView.Rows(new List<MatchHistoryRow>()));
    }

    // ------------------------------------------------------------------ the percentage

    /// <summary>
    /// <b>THE RULE THE CARD EXISTS AROUND.</b> Four decided games say nothing, however they went;
    /// the record beside them says everything four games can.
    /// </summary>
    [Fact]
    public void NoPercentageUntilThereIsEnoughBehindIt()
    {
        var thin = Assert.Single(CivStatsView.Rows(Many("Chinese", wins: 0, losses: 1)));
        Assert.Null(CivStatsView.WinPercent(thin));

        var stillThin = Assert.Single(CivStatsView.Rows(
            Many("Chinese", wins: 2, losses: CivStatsView.MinDecidedForPercent - 3)));
        Assert.Null(CivStatsView.WinPercent(stillThin));

        var enough = Assert.Single(CivStatsView.Rows(
            Many("Chinese", wins: CivStatsView.MinDecidedForPercent, losses: 0)));
        Assert.Equal(100, CivStatsView.WinPercent(enough));
    }

    /// <summary>
    /// The bar counts DECIDED games, not played ones — otherwise a civilization with fifty
    /// unreadable matches and one loss would publish "0 %".
    /// </summary>
    [Fact]
    public void UnreadableMatchesDoNotCountTowardsTheBar()
    {
        var row = Assert.Single(CivStatsView.Rows(
            Many("Chinese", wins: 0, losses: 1, unread: 40)));

        Assert.Equal(41, row.Played);
        Assert.Null(CivStatsView.WinPercent(row));
    }

    [Fact]
    public void ThePercentageIsOverDecidedGames()
    {
        var row = Assert.Single(CivStatsView.Rows(Many("Chinese", wins: 3, losses: 3, unread: 10)));
        Assert.Equal(50, CivStatsView.WinPercent(row));
    }

    // ------------------------------------------------------------------ ordering

    /// <summary>
    /// <b>By matches played, never by a rate.</b> Sorting by a percentage computed from a handful
    /// puts whoever went 1-0 with something at the top and calls it their best civilization.
    /// </summary>
    [Fact]
    public void OrderedByMatchesPlayedAndNotByHowWellTheyWent()
    {
        var rows = Many("Chinese", wins: 1, losses: 9);      // 10 played, 10 % wins
        rows.AddRange(Many("Colombians", wins: 2, losses: 0)); // 2 played, 100 % wins

        var stats = CivStatsView.Rows(rows);

        Assert.Equal("Chinese", stats[0].Civ);
        Assert.Equal("Colombians", stats[1].Civ);
    }

    /// <summary>
    /// A tie must not reshuffle between two visits to the tab — the dictionary's own order is not
    /// stable and the card is rebuilt on every render.
    /// </summary>
    [Fact]
    public void TiesBreakOnTheNameSoTheListHoldsStill()
    {
        var rows = Many("Zulu", wins: 1, losses: 0);
        rows.AddRange(Many("Aztecs", wins: 1, losses: 0));

        var stats = CivStatsView.Rows(rows);

        Assert.Equal("Aztecs", stats[0].Civ);
        Assert.Equal("Zulu", stats[1].Civ);
    }

    /// <summary>The same civilization under a different case is the same civilization.</summary>
    [Fact]
    public void CaseDoesNotSplitACivilizationInTwo()
    {
        var rows = new List<MatchHistoryRow> { Match("Chinese", 1.0), Match("chinese", 0.0) };

        var row = Assert.Single(CivStatsView.Rows(rows));
        Assert.Equal(2, row.Played);
    }
}
