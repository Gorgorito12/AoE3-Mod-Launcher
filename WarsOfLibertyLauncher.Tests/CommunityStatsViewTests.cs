using System;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The community strip's two rules: turning the server's UTC histogram into the viewer's
/// own day, and refusing to call anything a peak hour when there is nothing to go on.
/// </summary>
public class CommunityStatsViewTests
{
    private static int[] Utc(params (int Hour, int Count)[] entries)
    {
        var a = new int[24];
        foreach (var e in entries) a[e.Hour] = e.Count;
        return a;
    }

    [Fact]
    public void HoursShiftIntoTheViewersDay()
    {
        // Latin America: UTC-5. A room opened at 20:00 UTC happened at 15:00 for them,
        // and this is the whole reason the server refuses to guess a timezone.
        var local = CommunityStatsView.ToLocalHours(Utc((20, 7)), TimeSpan.FromHours(-5));
        Assert.Equal(7, local[15]);
        Assert.Equal(0, local[20]);
    }

    [Fact]
    public void ShiftingWestWrapsAroundMidnight_WithoutFallingOffTheArray()
    {
        // 02:00 UTC at UTC-5 is 21:00 the previous day. C#'s % keeps the sign, so a naive
        // (h + shift) % 24 would index -3 here and throw.
        var local = CommunityStatsView.ToLocalHours(Utc((2, 4)), TimeSpan.FromHours(-5));
        Assert.Equal(4, local[21]);
    }

    [Fact]
    public void ShiftingEastWrapsTheOtherWay()
    {
        var local = CommunityStatsView.ToLocalHours(Utc((23, 3)), TimeSpan.FromHours(2));
        Assert.Equal(3, local[1]);
    }

    [Fact]
    public void NoShiftLeavesTheHistogramAlone()
    {
        var local = CommunityStatsView.ToLocalHours(Utc((9, 5), (21, 2)), TimeSpan.Zero);
        Assert.Equal(5, local[9]);
        Assert.Equal(2, local[21]);
    }

    [Fact]
    public void TheBusiestHourIsTheAnswer()
    {
        var local = CommunityStatsView.ToLocalHours(
            Utc((18, 9), (19, 25), (20, 6)), TimeSpan.Zero);
        Assert.Equal(19, CommunityStatsView.PeakHour(local, 40));
    }

    [Fact]
    public void TooSmallASampleHasNoPeakHour()
    {
        // THE case this exists for. A histogram always has a tallest bar; over four rooms
        // that bar means nothing, and printing it under the words "peak hours" dresses
        // noise up as a finding. Below the threshold the card is not shown at all.
        var local = CommunityStatsView.ToLocalHours(Utc((3, 4)), TimeSpan.Zero);
        Assert.Null(CommunityStatsView.PeakHour(local, 4));
    }

    [Fact]
    public void AnEmptyHistogramHasNoPeakHour()
    {
        Assert.Null(CommunityStatsView.PeakHour(new int[24], 100));
        Assert.Null(CommunityStatsView.PeakHour(null, 100));
    }

    [Fact]
    public void RanksComeFromTheServer_NeverRenumberedHere()
    {
        // A client that recomputed the rank after filtering its copy would show the
        // fourth player as the third, and two people looking at the same table would
        // read different numbers.
        var stats = new CommunityStats();
        stats.Leaderboard.Add(new LeaderboardRow { Rank = 2, DisplayName = "b", Rating = 1600 });
        stats.Leaderboard.Add(new LeaderboardRow { Rank = 5, DisplayName = "e", Rating = 1500 });

        var rows = CommunityStatsView.Rows(stats);
        Assert.Equal(2, rows[0].Rank);
        Assert.Equal(5, rows[1].Rank);
    }

    [Fact]
    public void NoStatsIsAnEmptyLadder_NotACrash()
    {
        Assert.Empty(CommunityStatsView.Rows(null));
    }

    [Fact]
    public void TheWinRateDividesByDecidedGames()
    {
        var row = new LeaderboardRow { Wins = 3, Losses = 1, GamesPlayed = 40 };
        // 3 of 4 decided, NOT 3 of 40 played — which would read 8 %.
        Assert.Equal(75, CommunityStatsView.WinPercent(row));
    }

    [Fact]
    public void NothingDecidedIsNoPercentage_NotZero()
    {
        var row = new LeaderboardRow { Wins = 0, Losses = 0, GamesPlayed = 12 };
        Assert.Null(CommunityStatsView.WinPercent(row));
    }

    // ---------- who beat whom, in the community's recent matches ----------

    private static CommunityMatch Match(params (string Name, double Result)[] players)
    {
        var m = new CommunityMatch { Id = "m1", ModId = "wol", MapName = "ESOC_Iowa" };
        foreach (var p in players)
        {
            m.Participants.Add(new MatchHistoryParticipant
            {
                UserId = p.Name, DisplayName = p.Name, Result = p.Result,
            });
        }
        return m;
    }

    [Fact]
    public void ADecidedDuelNamesTheWinnerAndTheLoser()
    {
        var line = CommunityStatsView.Describe(Match(("Gorgorito", 0.0), ("Alucard", 1.0)));

        Assert.True(line.Decided);
        Assert.Equal("Alucard", line.Winner);
        Assert.Equal("Gorgorito", line.Loser);
    }

    /// <summary>
    /// <b>The case that matters.</b> Most stored matches carry 0.5 for everyone because the
    /// outcome could not be read, and a strip that turned that into "X beat Y" would be
    /// inventing the result of nearly every game the community has played.
    /// </summary>
    [Fact]
    public void AMatchNobodyCouldReadNamesNobody()
    {
        var line = CommunityStatsView.Describe(Match(("Gorgorito", 0.5), ("Alucard", 0.5)));

        Assert.False(line.Decided);
        Assert.Null(line.Winner);
        Assert.Null(line.Loser);
    }

    /// <summary>
    /// Past two players one reported loser does not name a winner — the others may have
    /// lost too, and nothing records the order.
    /// </summary>
    [Fact]
    public void ATeamGameNamesNobodyEvenWithAWinner()
    {
        var line = CommunityStatsView.Describe(
            Match(("Ana", 1.0), ("Beto", 0.0), ("Caro", 0.0)));

        Assert.False(line.Decided);
    }

    [Fact]
    public void AMatchWithNoParticipantsNamesNobody()
    {
        Assert.False(CommunityStatsView.Describe(null).Decided);
        Assert.False(CommunityStatsView.Describe(new CommunityMatch()).Decided);
        Assert.False(CommunityStatsView.Describe(Match(("Solo", 1.0))).Decided);
    }

    /// <summary>
    /// Two winners is not a shape the server can produce, and it is exactly the shape that
    /// would make a naive "first is the winner, second is the loser" read invent a defeat.
    /// </summary>
    [Fact]
    public void TwoWinnersNameNobody()
    {
        Assert.False(CommunityStatsView.Describe(Match(("Ana", 1.0), ("Beto", 1.0))).Decided);
    }

    // ---------- the ladder's empty state, and the numbers ----------

    /// <summary>
    /// The requirement is only stated when the SERVER stated it. An older backend has no
    /// such field and deserializes it to 0 — and "you get in with 0 decided matches" is
    /// both wrong and impossible, so the note is not shown at all.
    /// </summary>
    [Fact]
    public void TheLadderRequirementIsOnlyShownWhenTheServerSaysIt()
    {
        Assert.Equal(3, CommunityStatsView.RequiredDecided(new CommunityStats { MinDecided = 3 }));
        Assert.Null(CommunityStatsView.RequiredDecided(new CommunityStats { MinDecided = 0 }));
        Assert.Null(CommunityStatsView.RequiredDecided(null));
    }

    /// <summary>
    /// Null is "this backend does not report it" and hides the card; a genuine zero is a
    /// fact about a quiet month and is shown. Collapsing the two would report a dead
    /// community every time an old server answered.
    /// </summary>
    [Fact]
    public void AbsentTotalsAreNotZeroTotals()
    {
        Assert.Null(CommunityStatsView.Totals(null));
        Assert.Null(CommunityStatsView.Totals(new CommunityStats()));

        var quiet = new CommunityStats { Totals = new CommunityTotals { Matches = 0, Players = 0 } };
        Assert.NotNull(CommunityStatsView.Totals(quiet));
        Assert.Equal(0, CommunityStatsView.Totals(quiet)!.Matches);
    }

    [Fact]
    public void AnOlderBackendHasNoCommunityMatches()
    {
        Assert.Empty(CommunityStatsView.RecentMatches(null));
        Assert.Empty(CommunityStatsView.RecentMatches(new CommunityStats()));
    }

    /// <summary>
    /// The wire contract, read back from a real-shaped payload.
    ///
    /// <para>Every one of these fields is snake_case on the wire and PascalCase here, joined
    /// only by a <c>JsonPropertyName</c> string that no compiler checks. A typo in one does
    /// not fail anything: the property keeps its default, so the card renders 0 matches, 0
    /// players and no winner — indistinguishable from a quiet community. That is the whole
    /// reason this test exists rather than trusting the mapping.</para>
    /// </summary>
    [Fact]
    public void TheWirePayloadDeserializesIntoEveryFieldTheCardsRead()
    {
        const string json = """
        {
          "generated_at": "2026-08-30T10:00:00Z",
          "min_decided": 3,
          "leaderboard": [],
          "totals": {
            "window_days": 30, "matches": 47,
            "players_window_days": 7, "players": 9,
            "top_map": "ESOC_Fertile Crescent", "top_map_matches": 12
          },
          "recent_matches": [
            {
              "id": "m1", "mod_id": "wol", "map_name": "ESOC_Fertile Crescent",
              "duration_seconds": 1070, "reported_at": "2026-08-30 08:00:00",
              "participants": [
                { "user_id": "u2", "discord_username": "alucard",
                  "display_name": "Alucard", "avatar_url": null,
                  "team": 1, "result": 1.0,
                  "rating_before": 1500, "rating_after": 1617 },
                { "user_id": "u1", "discord_username": "gorgorito",
                  "display_name": "Gorgorito", "avatar_url": "https://cdn/a.png",
                  "team": 0, "result": 0.0,
                  "rating_before": 1617, "rating_after": 1500 }
              ]
            }
          ],
          "activity": { "source": "lobbies_created", "window_days": 30,
                        "timezone": "UTC", "total": 99, "hours": [] }
        }
        """;

        var stats = System.Text.Json.JsonSerializer.Deserialize<CommunityStats>(json);

        var totals = CommunityStatsView.Totals(stats);
        Assert.NotNull(totals);
        Assert.Equal(47, totals!.Matches);
        Assert.Equal(30, totals.WindowDays);
        Assert.Equal(9, totals.Players);
        Assert.Equal(7, totals.PlayersWindowDays);
        Assert.Equal("ESOC_Fertile Crescent", totals.TopMap);

        Assert.Equal(3, CommunityStatsView.RequiredDecided(stats));

        var match = Assert.Single(CommunityStatsView.RecentMatches(stats));
        Assert.Equal("wol", match.ModId);
        Assert.Equal("2026-08-30 08:00:00", match.ReportedAt);

        // And the whole point of carrying the participants: the sentence the card writes.
        var line = CommunityStatsView.Describe(match);
        Assert.True(line.Decided);
        Assert.Equal("Alucard", line.Winner);
        Assert.Equal("Gorgorito", line.Loser);
    }
}
