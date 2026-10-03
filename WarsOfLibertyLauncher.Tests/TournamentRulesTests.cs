using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The server's bracket and entrant rules as the tournament preview runs them — a port of
/// <c>wol-launcher-lobby-node/src/tournaments/bracket.test.ts</c> and <c>entrants.test.ts</c>, case
/// for case where a case exists.
///
/// <para>The REFUSALS are the point, as they are on the server: a preview whose bracket advanced
/// where the server's would not is a picture of something that cannot happen, and a design decision
/// taken on it is taken on nothing.</para>
/// </summary>
public class TournamentRulesTests
{
    private static List<TournamentMatch> Build(int n)
    {
        var entrants = Enumerable.Range(1, n).Select(i => ($"e{i}", i)).ToList();
        return TournamentRules.Generate(entrants, (r, p) => $"r{r}p{p}");
    }

    private static TournamentMatch At(List<TournamentMatch> ms, int round, int position)
        => ms.Single(m => m.Round == round && m.Position == position);

    // ---------------------------------------------------------------- shape

    [Fact]
    public void TheBracketIsTheNextPowerOfTwo_NeverSmallerThanTwo()
    {
        Assert.Equal(2, TournamentRules.BracketSize(2));
        Assert.Equal(4, TournamentRules.BracketSize(3));
        Assert.Equal(8, TournamentRules.BracketSize(5));
        Assert.Equal(8, TournamentRules.BracketSize(8));
        Assert.Equal(16, TournamentRules.BracketSize(13));
        Assert.Equal(2, TournamentRules.BracketSize(1));
        Assert.Equal(2, TournamentRules.BracketSize(0));
    }

    [Fact]
    public void RoundCountsFollowTheSize()
    {
        Assert.Equal(1, TournamentRules.RoundsFor(2));
        Assert.Equal(3, TournamentRules.RoundsFor(5));
        Assert.Equal(4, TournamentRules.RoundsFor(16));
    }

    [Fact]
    public void SeedOrderIsTheServers()
    {
        Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, TournamentRules.SeedOrder(8));
        Assert.Equal(
            new[] { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 },
            TournamentRules.SeedOrder(16));
    }

    [Fact]
    public void EveryFirstRoundPairSumsToSizePlusOne()
    {
        foreach (int size in new[] { 2, 4, 8, 16, 32 })
        {
            var order = TournamentRules.SeedOrder(size);
            Assert.Equal(Enumerable.Range(1, size), order.OrderBy(x => x));
            for (int p = 0; p < size; p += 2) Assert.Equal(size + 1, order[p] + order[p + 1]);
        }
    }

    [Fact]
    public void TheTopTwoSeedsCannotMeetBeforeTheFinal()
    {
        foreach (int n in new[] { 8, 16 })
        {
            var ms = Build(n);
            List<string> PathOf(string entrant)
            {
                var m = ms.First(x => x.Round == 1 && (x.Entrant1Id == entrant || x.Entrant2Id == entrant));
                var path = new List<string> { m.Id };
                while (!string.IsNullOrEmpty(m.NextMatchId))
                {
                    m = ms.Single(x => x.Id == m.NextMatchId);
                    path.Add(m.Id);
                }
                return path;
            }
            var shared = PathOf("e1").Intersect(PathOf("e2")).ToList();
            Assert.Single(shared);
            Assert.Equal(TournamentRules.RoundsFor(n), ms.Single(m => m.Id == shared[0]).Round);
        }
    }

    [Fact]
    public void LinksPointOneRoundAhead_AndOnlyTheFinalHasNone()
    {
        var ms = Build(13);
        int rounds = TournamentRules.RoundsFor(13);
        int finals = 0;
        foreach (var m in ms)
        {
            if (string.IsNullOrEmpty(m.NextMatchId))
            {
                finals++;
                Assert.Equal(rounds, m.Round);
                Assert.Null(m.NextSlot);
                continue;
            }
            var next = ms.Single(x => x.Id == m.NextMatchId);
            Assert.Equal(m.Round + 1, next.Round);
            Assert.Equal(m.Position >> 1, next.Position);
            Assert.Equal((m.Position & 1) + 1, m.NextSlot);
        }
        Assert.Equal(1, finals);
    }

    [Fact]
    public void EnsureLinksWritesTheSameLinksGenerationDoes()
    {
        var generated = Build(8);
        var stripped = generated
            .Select(m => new TournamentMatch { Id = m.Id, Round = m.Round, Position = m.Position })
            .ToList();
        TournamentRules.EnsureLinks(stripped, 3);
        foreach (var m in generated)
        {
            var s = stripped.Single(x => x.Id == m.Id);
            Assert.Equal(m.NextMatchId, s.NextMatchId);
            Assert.Equal(m.NextSlot, s.NextSlot);
        }
    }

    [Fact]
    public void ByesGoToTheTopSeeds_AreSizeMinusN_AndNeverPairUp()
    {
        foreach (int n in new[] { 3, 5, 6, 7, 13 })
        {
            var ms = Build(n);
            var first = ms.Where(m => m.Round == 1).ToList();
            var byes = first.Where(m => m.Status == "bye").ToList();
            Assert.Equal(TournamentRules.BracketSize(n) - n, byes.Count);
            Assert.All(first, m => Assert.True(
                !string.IsNullOrEmpty(m.Entrant1Id) || !string.IsNullOrEmpty(m.Entrant2Id)));
            var gotBye = byes.Select(m => m.WinnerEntrantId).OrderBy(x => x).ToList();
            var expected = Enumerable.Range(1, TournamentRules.BracketSize(n) - n)
                .Select(i => $"e{i}").OrderBy(x => x).ToList();
            Assert.Equal(expected, gotBye);
        }
    }

    [Fact]
    public void AByeIsResolvedAtGeneration_AndSeatsItsWinner()
    {
        var ms = Build(5);
        var bye = ms.First(m => m.Round == 1 && m.Status == "bye");
        Assert.Equal("bye", bye.Outcome);
        Assert.Equal("e1", bye.WinnerEntrantId);
        var next = ms.Single(m => m.Id == bye.NextMatchId);
        Assert.Equal("e1", bye.NextSlot == 1 ? next.Entrant1Id : next.Entrant2Id);
    }

    [Fact]
    public void ARoundTwoMatchFedByTwoByesIsImmediatelyPlayable()
    {
        var ms = Build(5);
        var ready = ms.Where(m => m.Round == 2 && TournamentRules.Playable(m)).ToList();
        Assert.Single(ready);
        Assert.Equal(new[] { "e2", "e3" }, new[] { ready[0].Entrant1Id, ready[0].Entrant2Id }.OrderBy(x => x));
    }

    [Fact]
    public void GenerationRefusesMalformedSeeding()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TournamentRules.Generate(new[] { ("e1", 1) }, (r, p) => $"{r}{p}"));
        Assert.Throws<InvalidOperationException>(() =>
            TournamentRules.Generate(new[] { ("a", 1), ("b", 1) }, (r, p) => $"{r}{p}"));
        Assert.Throws<InvalidOperationException>(() =>
            TournamentRules.Generate(new[] { ("a", 1), ("b", 3) }, (r, p) => $"{r}{p}"));
    }

    // ---------------------------------------------------------------- advancing

    [Fact]
    public void AWinMovesTheWinnerIntoTheRightSlot()
    {
        var ms = Build(8);
        var m0 = At(ms, 1, 0);
        var winner = m0.Entrant1Id!;
        var r = TournamentRules.Advance(ms, m0.Id, winner, "played");
        Assert.True(r.Ok);
        Assert.False(r.TournamentDone);
        Assert.Equal(winner, ms.Single(m => m.Id == m0.NextMatchId).Entrant1Id);
        Assert.Equal("done", m0.Status);
        Assert.Equal("played", m0.Outcome);
    }

    [Fact]
    public void PositionOneFeedsSlotTwo_AndThePairMakesTheNextMatchReady()
    {
        var ms = Build(8);
        TournamentRules.Advance(ms, At(ms, 1, 0).Id, At(ms, 1, 0).Entrant1Id!, "played");
        var m1 = At(ms, 1, 1);
        var r = TournamentRules.Advance(ms, m1.Id, m1.Entrant2Id!, "played");
        Assert.Equal(m1.Entrant2Id, ms.Single(m => m.Id == m1.NextMatchId).Entrant2Id);
        Assert.Equal(new[] { m1.NextMatchId }, r.NewlyReady);
    }

    [Fact]
    public void DecidingTheFinalNamesTheChampion()
    {
        var ms = Build(2);
        var r = TournamentRules.Advance(ms, At(ms, 1, 0).Id, "e1", "played");
        Assert.True(r.TournamentDone);
        Assert.Equal("e1", r.ChampionEntrantId);
    }

    [Fact]
    public void TheSameMatchCannotBeDecidedTwice_AndARefusalChangesNothing()
    {
        var ms = Build(8);
        var m0 = At(ms, 1, 0);
        TournamentRules.Advance(ms, m0.Id, m0.Entrant1Id!, "played");
        var before = Snapshot(ms);
        var again = TournamentRules.Advance(ms, m0.Id, m0.Entrant2Id!, "played");
        Assert.False(again.Ok);
        Assert.Equal("already_decided", again.Refusal);
        Assert.Equal(before, Snapshot(ms));
    }

    [Fact]
    public void AByeCannotBeWon_AndAnUnknownMatchIsRefused()
    {
        var ms = Build(5);
        var bye = ms.First(m => m.Status == "bye");
        Assert.Equal("is_bye", TournamentRules.Advance(ms, bye.Id, "e1", "played").Refusal);
        Assert.Equal("match_not_found", TournamentRules.Advance(ms, "nope", "e1", "played").Refusal);
    }

    [Fact]
    public void SomebodyNotInTheMatchCannotWinIt()
    {
        var ms = Build(8);
        var r = TournamentRules.Advance(ms, At(ms, 1, 0).Id, "e7", "played");
        Assert.False(r.Ok);
        Assert.Equal("winner_not_in_match", r.Refusal);
    }

    // ---------------------------------------------------------------- disqualification

    [Fact]
    public void ADisqualificationHandsOverEveryMatchWithAKnownOpponent()
    {
        var ms = Build(8);
        var m0 = At(ms, 1, 0);
        var victim = m0.Entrant1Id!;
        var beneficiary = m0.Entrant2Id!;
        TournamentRules.Disqualify(ms, victim);
        Assert.Equal("done", m0.Status);
        Assert.Equal("dq", m0.Outcome);
        Assert.Equal(beneficiary, m0.WinnerEntrantId);
    }

    [Fact]
    public void ADisqualificationWithNoOpponentYetWaits_ThenResolves()
    {
        var ms = Build(5);
        var r2 = ms.First(m => m.Round == 2 && !TournamentRules.Playable(m));
        var waiting = r2.Entrant1Id ?? r2.Entrant2Id!;

        var touched = TournamentRules.Disqualify(ms, waiting);
        Assert.DoesNotContain(r2.Id, touched);

        var feeder = ms.First(m => m.NextMatchId == r2.Id && m.Status == "pending");
        var arriving = feeder.Entrant1Id!;
        TournamentRules.Advance(ms, feeder.Id, arriving, "played", new HashSet<string> { waiting });
        Assert.Equal("done", r2.Status);
        Assert.Equal("dq", r2.Outcome);
        Assert.Equal(arriving, r2.WinnerEntrantId);
    }

    [Fact]
    public void TwoDisqualifiedEntrantsMeetingIsLeftForAHuman()
    {
        var ms = Build(8);
        var m0 = At(ms, 1, 0);
        var m1 = At(ms, 1, 1);
        var a = m0.Entrant1Id!;
        var b = m1.Entrant1Id!;
        var outSet = new HashSet<string> { a, b };
        TournamentRules.Advance(ms, m0.Id, a, "played", outSet);
        var r = TournamentRules.Advance(ms, m1.Id, b, "played", outSet);
        var next = ms.Single(m => m.Id == m1.NextMatchId);
        Assert.Equal("pending", next.Status);
        Assert.Null(next.WinnerEntrantId);
        Assert.False(r.TournamentDone);
    }

    /// <summary>
    /// The server's disqualify() counts ONLY the entrant being thrown out, so somebody disqualified
    /// earlier who is met during the cascade is not treated as gone. Copied on purpose: the preview
    /// shows what the server does, and a "fixed" copy here would show something it does not.
    /// </summary>
    [Fact]
    public void THE_SERVERS_QUIRK_ACascadeIgnoresEarlierDisqualifications()
    {
        var ms = Build(4);
        // e1 v e4 and e2 v e3. e1 wins and waits in the final, then is thrown out with nobody
        // opposite yet, so nothing can be awarded.
        TournamentRules.Advance(ms, At(ms, 1, 0).Id, "e1", "played");
        TournamentRules.Disqualify(ms, "e1");
        var final = ms.Single(m => m.Round == 2);
        Assert.Equal("pending", final.Status);

        // e2 is thrown out next: e3 wins that semifinal by dq and arrives opposite e1, who is
        // ALREADY disqualified. Only e2 counts as out in this call, so instead of handing e3 the
        // final, the server leaves it standing as if e1 could still play it.
        TournamentRules.Disqualify(ms, "e2");
        Assert.Equal("e3", final.Entrant2Id);
        Assert.Equal("pending", final.Status);
        Assert.True(TournamentRules.Playable(final));
    }

    // ---------------------------------------------------------------- entrants

    [Fact]
    public void ARosterIsRefusedForWrongSize_DuplicateOrAlreadyEntered()
    {
        var none = new HashSet<string>();
        Assert.Null(TournamentRules.ValidateRoster("3v3", new[] { "a", "b", "c" }, none));
        Assert.Equal("wrong_size", TournamentRules.ValidateRoster("3v3", new[] { "a", "b" }, none));
        Assert.Equal("wrong_size", TournamentRules.ValidateRoster("1v1", new[] { "a", "b" }, none));
        Assert.Equal("duplicate_member", TournamentRules.ValidateRoster("2v2", new[] { "a", "a" }, none));
        Assert.Equal("already_entered",
            TournamentRules.ValidateRoster("1v1", new[] { "a" }, new HashSet<string> { "a" }));
    }

    [Fact]
    public void AnUnratedPlayerIsWorthTheBottom_AndABigDeviationCannotBuyATopSeed()
    {
        Assert.Equal(800, TournamentRules.ConservativeRating(null));
        Assert.True(TournamentRules.ConservativeRating((1800, 300))
                    < TournamentRules.ConservativeRating((1600, 60)));
    }

    [Fact]
    public void ATeamIsSeededOnTheMeanOfItsMembers()
    {
        var ratings = new Dictionary<string, (double, double)>
        {
            ["star"] = (2200, 50), ["novice1"] = (1100, 50), ["novice2"] = (1100, 50),
            ["solid1"] = (1600, 50), ["solid2"] = (1600, 50), ["solid3"] = (1600, 50),
        };
        var seeds = TournamentRules.SeedByRating(
            new[]
            {
                new TournamentRules.SeedableEntrant("carried", new[] { "star", "novice1", "novice2" }, 1),
                new TournamentRules.SeedableEntrant("even", new[] { "solid1", "solid2", "solid3" }, 2),
            },
            id => ratings.TryGetValue(id, out var r) ? r : null);
        Assert.Equal("even", seeds[0].EntrantId);
        Assert.Equal(1, seeds[0].Seed);
    }

    [Fact]
    public void TiesBreakOnRegistrationThenId()
    {
        var seeds = TournamentRules.SeedByRating(
            new[]
            {
                new TournamentRules.SeedableEntrant("b", new[] { "x" }, 5),
                new TournamentRules.SeedableEntrant("a", new[] { "y" }, 5),
                new TournamentRules.SeedableEntrant("c", new[] { "z" }, 1),
            },
            _ => null);
        Assert.Equal(new[] { "c", "a", "b" }, seeds.Select(s => s.EntrantId));
        Assert.Equal(new[] { 1, 2, 3 }, seeds.Select(s => s.Seed));
    }

    [Fact]
    public void TheWaitlistPromotesInTheOrderPeopleAsked_AndOnlyThoseWaiting()
    {
        var order = TournamentRules.PromoteFromWaitlist(
            new (string, string?, long)[]
            {
                ("late", "waitlist", 9), ("early", "waitlist", 2), ("in", "confirmed", 1), ("mid", "waitlist", 5),
            },
            2);
        Assert.Equal(new[] { "early", "mid" }, order);
        Assert.Empty(TournamentRules.PromoteFromWaitlist(new (string, string?, long)[] { ("a", "waitlist", 1) }, 0));
    }

    [Fact]
    public void OpenEntryConfirmsWhileThereIsRoom_ApprovalAlwaysAsks()
    {
        Assert.Equal("confirmed", TournamentRules.EntryStatusFor("open", true));
        Assert.Equal("waitlist", TournamentRules.EntryStatusFor("open", false));
        Assert.Equal("pending", TournamentRules.EntryStatusFor("approval", true));
    }

    [Fact]
    public void TheStableHashIsFnv1a()
    {
        // The published FNV-1a 32-bit vectors. A hash that changed between runs would make the
        // preview's simulated results change between runs, and a screenshot unrepeatable.
        Assert.Equal(0x811C9DC5u, TournamentRules.StableHash(""));
        Assert.Equal(0xE40C292Cu, TournamentRules.StableHash("a"));
        Assert.Equal(0xBF9CF968u, TournamentRules.StableHash("foobar"));
    }

    private static string Snapshot(IEnumerable<TournamentMatch> ms)
        => string.Join(";", ms.OrderBy(m => m.Id)
            .Select(m => $"{m.Id}:{m.Entrant1Id}:{m.Entrant2Id}:{m.WinnerEntrantId}:{m.Status}:{m.Outcome}"));
}
