using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The end-of-match statistics AoE3 writes into the AI's memory file, and the store that keeps
/// them because the game does not.
///
/// <para>The fixture is a trimmed copy of a REAL <c>.personality</c> — same element names, same
/// nesting, same mixed content — rather than a shape invented to match my reading of the format,
/// which would pass by construction and prove nothing.</para>
/// </summary>
public class AiGameStatsTests
{
    /// <summary>
    /// Two games, in the shape the game really writes. Note the two mixed-content elements:
    /// <c>player</c> carries the name as its own text and <c>stattime</c> carries the duration as
    /// its own text, both followed by children.
    ///
    /// <para>The FIRST game has zeroed totals and real unit counts — that is not a mistake in the
    /// fixture, it is what every block but the newest looks like on disk.</para>
    /// </summary>
    private const string RealShape = """
    <?xml version="1.0" encoding="UTF-16"?>
    <ai>
      <version>2</version>
      <forcedciv>UnitedStates</forcedciv>
      <playernames><nameid>444504<civ>French</civ></nameid></playernames>
      <history>
        <player>Gorgorito
          <uservars><lastgamedifficulty>2.0000</lastgamedifficulty></uservars>
          <game>
            <unitcounts>
              <wolniruarcher>177</wolniruarcher>
              <tigerman>118</tigerman>
              <towncenter>1</towncenter>
            </unitcounts>
            <myteamwon>1</myteamwon>
            <firstattacktime>-1</firstattacktime>
            <stattime>757000
              <score>0</score>
              <totalresources>
                <gold>0</gold><wood>0</wood><food>0</food>
                <fame>0</fame><xp>0</xp><ships>0</ships><trade>0</trade>
              </totalresources>
            </stattime>
          </game>
          <game>
            <unitcounts>
              <gwtank>56</gwtank>
              <towncenter>1</towncenter>
            </unitcounts>
            <myteamwon>0</myteamwon>
            <firstattacktime>420</firstattacktime>
            <stattime>1067806
              <score>664331</score>
              <totalresources>
                <gold>300820</gold><wood>205693</wood><food>143079</food>
                <fame>153476</fame><xp>84506</xp><ships>42</ships><trade>41150</trade>
              </totalresources>
            </stattime>
          </game>
        </player>
      </history>
    </ai>
    """;

    private static readonly DateTime T0 = new(2026, 8, 25, 20, 16, 0, DateTimeKind.Utc);

    private static IReadOnlyList<AiGameRecord> ParseReal(DateTime? at = null)
        => AiGameStats.Parse("wolMenelik", "wol", RealShape, at ?? T0);

    // ------------------------------------------------------------------ parsing

    [Fact]
    public void ReadsBothGamesWithTheirUnitsAndTotals()
    {
        var games = ParseReal();

        Assert.Equal(2, games.Count);

        var newest = games[1];
        Assert.Equal("wolMenelik", newest.Personality);
        Assert.Equal("wol", newest.ModId);
        Assert.Equal(1067806, newest.DurationMs);
        Assert.Equal(664331, newest.Score);
        Assert.Equal(42, newest.Shipments);
        Assert.Equal(300820, newest.Gold);
        Assert.Equal(84506, newest.Xp);
        Assert.Equal(420, newest.FirstAttackSeconds);
        Assert.Equal(56, newest.Units["gwtank"]);
    }

    /// <summary>
    /// <b>The player's name is the element's own TEXT, not its Value.</b> <c>player</c> is mixed
    /// content — the name, then <c>uservars</c> and every <c>game</c> as children — so reading
    /// <c>Element.Value</c> concatenates the whole subtree and hands back the name with every
    /// number in the file stuck to it. Silent, and it would poison the dedup key of every record.
    /// </summary>
    [Fact]
    public void ThePlayerNameIsJustTheName()
    {
        foreach (var game in ParseReal()) Assert.Equal("Gorgorito", game.PlayerName);
    }

    /// <summary>
    /// Same trap one level down: the duration is <c>stattime</c>'s own text, with the score and
    /// the resource totals as its children.
    /// </summary>
    [Fact]
    public void TheDurationIsStattimesOwnText()
        => Assert.Equal(757000, ParseReal()[0].DurationMs);

    /// <summary>
    /// <b>1 means the HUMAN won, and this had to be measured rather than assumed</b> — the field
    /// is called <c>myteamwon</c> and "my" could as easily have been the AI's team, in which case
    /// every result the launcher shows would be inverted. Checked against the outcome trailer of
    /// the recordings these games paired with (the personality file and the recording share a
    /// write time to the minute): three games, both directions, two losses reading 0 and a win
    /// reading 1.
    /// </summary>
    [Fact]
    public void MyTeamWonIsTheHumansResult()
    {
        var games = ParseReal();
        Assert.True(games[0].Won);
        Assert.False(games[1].Won);
    }

    [Fact]
    public void AFileWithNoHistoryYieldsNothing()
    {
        Assert.Empty(AiGameStats.Parse("x", "wol", "<ai><version>2</version></ai>", T0));
        Assert.Empty(AiGameStats.Parse("x", "wol", "", T0));
    }

    /// <summary>
    /// A mod author's malformed file costs the statistics of one match and never an exception —
    /// this runs on the path that fires the moment a game closes.
    /// </summary>
    [Fact]
    public void BrokenXmlIsSwallowed()
        => Assert.Empty(AiGameStats.Parse("x", "wol", "<ai><history><player>Oops", T0));

    /// <summary>Units with no count, or a count of zero, are not units.</summary>
    [Fact]
    public void AZeroOrUnreadableUnitCountIsDropped()
    {
        var games = AiGameStats.Parse("x", "wol", """
        <ai><history><player>P<game><unitcounts>
          <good>3</good><zero>0</zero><rubbish>abc</rubbish>
        </unitcounts></game></player></history></ai>
        """, T0);

        var units = Assert.Single(games).Units;
        Assert.Equal(3, units["good"]);
        Assert.False(units.ContainsKey("zero"));
        Assert.False(units.ContainsKey("rubbish"));
    }

    // ------------------------------------------------------------------ the store

    /// <summary>
    /// <b>THE RULE THIS WHOLE FEATURE RESTS ON.</b> The same game read again after the next match
    /// comes back with its totals zeroed, because AoE3 keeps them for the newest block only. If
    /// the merge took the newer reading, every stored game would lose its score, its resources
    /// and its shipment count one launch at a time — the exact data the store exists to preserve,
    /// destroyed by the thing meant to keep it, and invisibly.
    /// </summary>
    [Fact]
    public void ARereadWithZeroedTotalsNeverErasesWhatWasCaptured()
    {
        var rich = ParseReal(T0).ToList();
        var degraded = rich.Select(g => new AiGameRecord
        {
            Personality = g.Personality,
            ModId = g.ModId,
            PlayerName = g.PlayerName,
            DurationMs = g.DurationMs,
            Won = g.Won,
            FirstAttackSeconds = g.FirstAttackSeconds,
            Units = g.Units,
            CapturedAtUtc = T0.AddDays(1).ToString("o"),
            // everything else left at zero, which is what a re-read really looks like
        }).ToList();

        var merged = AiGameStatsStore.Merge(rich, degraded);

        Assert.Equal(2, merged.Count);
        var newest = merged.Single(g => g.DurationMs == 1067806);
        Assert.Equal(664331, newest.Score);
        Assert.Equal(42, newest.Shipments);
        Assert.Equal(300820, newest.Gold);
    }

    /// <summary>
    /// And the same reading twice does not grow the store — the file is re-read in full after
    /// every single game, AI or not.
    /// </summary>
    [Fact]
    public void ReadingTheSameFileTwiceStoresEachGameOnce()
    {
        var games = ParseReal().ToList();

        Assert.Equal(2, AiGameStatsStore.Merge(games, games).Count);
        Assert.Equal(2, AiGameStatsStore.Merge(AiGameStatsStore.Merge(games, games), games).Count);
    }

    /// <summary>
    /// Two different games are two entries. The obvious case, and worth pinning because the
    /// dedup key deliberately drops the score: duration and unit counts are all that separate
    /// them.
    /// </summary>
    [Fact]
    public void DifferentGamesAreKeptApart()
    {
        var games = ParseReal().ToList();
        var another = ParseReal().ToList()[0];
        another.DurationMs = 999999;

        Assert.Equal(3, AiGameStatsStore.Merge(games, new[] { another }).Count);
    }

    /// <summary>
    /// A re-read must not make an old game look new, or the list reorders itself every time
    /// anything is played.
    /// </summary>
    [Fact]
    public void TheFirstSightingIsTheOneThatCounts()
    {
        var first = ParseReal(T0).ToList();
        var later = ParseReal(T0.AddDays(3)).ToList();

        var merged = AiGameStatsStore.Merge(first, later);

        Assert.All(merged, g => Assert.Equal(T0.ToString("o"), g.CapturedAtUtc));
    }

    [Fact]
    public void NewestFirst()
    {
        var old = ParseReal(T0).ToList();
        var recent = AiGameStats.Parse("wolShaka", "wol", RealShape, T0.AddDays(2)).ToList();

        var merged = AiGameStatsStore.Merge(old, recent);

        Assert.Equal("wolShaka", merged[0].Personality);
    }

    /// <summary>
    /// Nothing prunes this but the cap, and a player who only ever plays the AI would otherwise
    /// grow it for years.
    /// </summary>
    [Fact]
    public void TheStoreIsBounded()
    {
        var many = Enumerable.Range(0, AiGameStatsStore.MaxGames + 50)
            .Select(i => new AiGameRecord
            {
                Personality = "ai",
                PlayerName = "P",
                DurationMs = i,
                CapturedAtUtc = T0.AddMinutes(i).ToString("o"),
            })
            .ToList();

        Assert.Equal(AiGameStatsStore.MaxGames, AiGameStatsStore.Merge(many, Array.Empty<AiGameRecord>()).Count);
    }
}
