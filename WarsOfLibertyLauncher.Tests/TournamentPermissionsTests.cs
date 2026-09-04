using System.Collections.Generic;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// What the launcher offers the person looking at a tournament.
///
/// <para><b>Every refusal here is the point.</b> The server re-checks all of this and
/// answers 403, so nothing in this file is what enforces anything — it is what stops the
/// launcher offering a button that cannot work. Getting it wrong in the permissive
/// direction is a player clicking Seed and being told no; getting it wrong in the other
/// direction is an owner who cannot start their own tournament and has no idea why.</para>
/// </summary>
public class TournamentPermissionsTests
{
    private const string Me = "me";
    private const string Other = "other";

    private static TournamentDetail T(
        string status,
        string owner = Me,
        List<TournamentEntrant>? entrants = null)
        => new()
        {
            Id = "c1",
            Name = "Copa",
            Status = status,
            OwnerUserId = owner,
            Entrants = entrants ?? new List<TournamentEntrant>(),
        };

    private static TournamentEntrant E(
        string id, string status, string captain, int? seed = null, params string[] members)
        => new()
        {
            Id = id,
            Status = status,
            CaptainUserId = captain,
            Seed = seed,
            MemberIds = new List<string>(members.Length > 0 ? members : new[] { captain }),
        };

    // ---------------------------------------------------------------- ownership

    [Fact]
    public void OwnershipIsDerivedFromTheIdTheServerSent()
    {
        // The detail carries owner_user_id rather than an is_owner flag, exactly as
        // room_state carries host_user_id. The client compares; it never decides.
        Assert.True(TournamentPermissions.IsOwner(T("draft", owner: Me), Me));
        Assert.False(TournamentPermissions.IsOwner(T("draft", owner: Other), Me));
        Assert.False(TournamentPermissions.IsOwner(T("draft", owner: Me), null));
        Assert.False(TournamentPermissions.IsOwner(null, Me));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_SomebodyElsesTournamentOffersNoOwnerButtonAtAll()
    {
        var theirs = T("ready", owner: Other, entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1), E("e2", "confirmed", "b", 2),
        });

        Assert.False(TournamentPermissions.CanOpenRegistration(theirs, Me));
        Assert.False(TournamentPermissions.CanCloseRegistration(theirs, Me));
        Assert.False(TournamentPermissions.CanSeed(theirs, Me));
        Assert.False(TournamentPermissions.CanStart(theirs, Me));
        Assert.False(TournamentPermissions.CanCancel(theirs, Me));
        Assert.False(TournamentPermissions.CanAwardOrDisqualify(theirs, Me));
    }

    // ------------------------------------------------- co-organisers

    private static TournamentDetail WithManager(string status, string manager, string owner = Other)
    {
        var t = T(status, owner: owner, entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1), E("e2", "confirmed", "b", 2),
        });
        t.ManagerUserIds = new List<string> { manager };
        return t;
    }

    /// <summary>
    /// THE ONE THAT MATTERS FOR THE GRANT. Somebody else's tournament, and I am one of its
    /// co-organisers: the running-it buttons appear, and the two that never delegate do not.
    ///
    /// <para>This is the twin of
    /// <see cref="THE_ONE_THAT_MATTERS_SomebodyElsesTournamentOffersNoOwnerButtonAtAll"/>,
    /// and it exists because that test would have gone on passing after the permission was
    /// widened — its fixture has no managers, so it stopped covering the path that changed.
    /// A guard test that cannot fail is worse than no guard test: it reads as coverage.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ACoOrganiserRunsItButCannotEndItOrAppointAnybody()
    {
        Assert.True(TournamentPermissions.CanCloseRegistration(
            WithManager("registration", Me), Me));
        Assert.True(TournamentPermissions.CanSeed(WithManager("ready", Me), Me));
        Assert.True(TournamentPermissions.CanAwardOrDisqualify(WithManager("running", Me), Me));
        Assert.True(TournamentPermissions.CanDecideEntrant(
            WithManager("registration", Me), Me, E("e3", "pending", "c", null)));

        // The two that stay the owner's, whatever else is delegated.
        Assert.False(TournamentPermissions.CanCancel(WithManager("running", Me), Me));
        Assert.False(TournamentPermissions.CanAppointManagers(WithManager("running", Me), Me));

        // And a co-organiser did not create it.
        Assert.False(TournamentPermissions.IsOwner(WithManager("running", Me), Me));
    }

    /// <summary>
    /// Being a co-organiser of one tournament grants nothing anywhere else — the whole point
    /// of the grant living on the tournament row rather than on the person.
    /// </summary>
    [Fact]
    public void ManagingOneTournamentGrantsNothingInAnother()
    {
        var other = T("running", owner: Other, entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1), E("e2", "confirmed", "b", 2),
        });

        Assert.False(TournamentPermissions.IsOwnerOrManager(other, Me));
        Assert.False(TournamentPermissions.CanAwardOrDisqualify(other, Me));
    }

    /// <summary>
    /// A server that predates co-organisers sends nothing, and nothing must read as nobody
    /// rather than as everybody.
    /// </summary>
    [Fact]
    public void AnOlderServerSendsNoManagersAndThatGrantsNobody()
    {
        var t = T("running", owner: Other);
        Assert.Null(t.ManagerUserIds);
        Assert.False(TournamentPermissions.IsOwnerOrManager(t, Me));
    }

    /// <summary>The owner is a manager of their own tournament without a row saying so.</summary>
    [Fact]
    public void TheOwnerNeedsNoRowToRunTheirOwnTournament()
    {
        Assert.True(TournamentPermissions.IsOwnerOrManager(T("running"), Me));
        Assert.True(TournamentPermissions.CanAppointManagers(T("running"), Me));
    }

    // ------------------------------------------------- settling a match by hand

    private static TournamentMatch M(
        string status = "pending", string? e1 = "a", string? e2 = "b")
        => new() { Id = "m1", Round = 1, Position = 0, Status = status, Entrant1Id = e1, Entrant2Id = e2 };

    /// <summary>
    /// The owner may hand a running match to one side. Nobody else may, ever.
    /// </summary>
    [Fact]
    public void OnlyTheOwnerOfARunningTournamentMaySettleAMatch()
    {
        Assert.True(TournamentPermissions.CanAwardMatch(T("running"), Me, M()));

        Assert.False(TournamentPermissions.CanAwardMatch(T("running", owner: Other), Me, M()));
        // Nothing to settle before the bracket exists, or after it is over.
        Assert.False(TournamentPermissions.CanAwardMatch(T("registration"), Me, M()));
        Assert.False(TournamentPermissions.CanAwardMatch(T("ready"), Me, M()));
        Assert.False(TournamentPermissions.CanAwardMatch(T("finished"), Me, M()));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A match that is already settled offers nothing, because the
    /// launcher cannot unsettle it — undoing a bracket result is the maintainer's CLI on
    /// purpose. A button here would be a button whose only outcome is a 409, on the one
    /// action that has no way back.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ASettledMatchOffersNothing()
    {
        Assert.False(TournamentPermissions.CanAwardMatch(T("running"), Me, M(status: "done")));
        Assert.False(TournamentPermissions.CanAwardMatch(T("running"), Me, M(status: "bye")));
    }

    /// <summary>
    /// THE ONE THAT MATTERS for a replay: exactly the OPPOSITE half of the award rule.
    ///
    /// <para>Awarding needs a match that is still open so it can close it; ordering a replay
    /// needs one that is still open so it can stay open. They meet at the same predicate and
    /// diverge nowhere \u2014 which is the point: <b>a decided tie is refused by both</b>, and
    /// for the same underlying reason. Its winner is already seated in the round above and its
    /// game already counted for the ladder, so handing it back would have to un-seat one and
    /// could not un-rate the other. That is why undoing a played result is the maintainer's
    /// CLI and not a button, and this is the client-side half of keeping it that way.</para>
    ///
    /// <para>Nothing here can reverse anything. The tie was never decided, so a replay writes
    /// no result, moves no rating and touches no round above it \u2014 it says out loud that the
    /// slot the players already share is open, which today nobody tells them.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_OnlyAnUndecidedTieCanBeReplayed()
    {
        Assert.True(TournamentPermissions.CanReplayMatch(T("running"), Me, M()));

        // Decided, either way: never.
        Assert.False(TournamentPermissions.CanReplayMatch(T("running"), Me, M(status: "done")));
        Assert.False(TournamentPermissions.CanReplayMatch(T("running"), Me, M(status: "bye")));

        // Half a slot has nobody to tell.
        Assert.False(TournamentPermissions.CanReplayMatch(T("running"), Me, M(e2: null)));
        Assert.False(TournamentPermissions.CanReplayMatch(T("running"), Me, null));

        // Not my tournament, and not before or after it runs.
        Assert.False(TournamentPermissions.CanReplayMatch(T("running", owner: Other), Me, M()));
        Assert.False(TournamentPermissions.CanReplayMatch(T("ready"), Me, M()));
        Assert.False(TournamentPermissions.CanReplayMatch(T("finished"), Me, M()));
    }

    /// <summary>
    /// A co-organiser may order a replay, for the same reason they may award: it is running
    /// the tournament, not owning it. Cancelling and appointing stay the owner's alone.
    /// </summary>
    [Fact]
    public void ACoOrganiserMayOrderAReplay()
    {
        var t = WithManager("running", manager: Me);
        Assert.True(TournamentPermissions.CanReplayMatch(t, Me, M()));
        Assert.False(TournamentPermissions.IsOwner(t, Me));
    }

    /// <summary>
    /// Both sides have to be there. Awarding a half-empty slot would advance somebody into a
    /// round nobody has reached, and the server refuses it.
    /// </summary>
    [Fact]
    public void AMatchMissingASideCannotBeSettled()
    {
        Assert.False(TournamentPermissions.CanAwardMatch(T("running"), Me, M(e2: null)));
        Assert.False(TournamentPermissions.CanAwardMatch(T("running"), Me, M(e1: "")));
        Assert.False(TournamentPermissions.CanAwardMatch(T("running"), Me, null));
    }

    // ---------------------------------------------------------------- lifecycle gates

    [Fact]
    public void RegistrationOpensFromDraftOrClosedAndClosesOnlyWhileOpen()
    {
        Assert.True(TournamentPermissions.CanOpenRegistration(T("draft"), Me));
        Assert.True(TournamentPermissions.CanOpenRegistration(T("ready"), Me));
        Assert.False(TournamentPermissions.CanOpenRegistration(T("running"), Me));
        Assert.False(TournamentPermissions.CanOpenRegistration(T("finished"), Me));

        Assert.True(TournamentPermissions.CanCloseRegistration(T("registration"), Me));
        Assert.False(TournamentPermissions.CanCloseRegistration(T("draft"), Me));
    }

    [Fact]
    public void SeedingNeedsRegistrationClosed()
    {
        Assert.True(TournamentPermissions.CanSeed(T("ready"), Me));
        Assert.False(TournamentPermissions.CanSeed(T("registration"), Me));
        Assert.False(TournamentPermissions.CanSeed(T("running"), Me));
    }

    [Fact]
    public void StartingNeedsTwoSeededEntrants()
    {
        // The server refuses a bracket it cannot draw, and generateBracket throws rather
        // than answering, so the button must not be offered when it would fail.
        var one = T("ready", entrants: new List<TournamentEntrant> { E("e1", "confirmed", "a", 1) });
        Assert.False(TournamentPermissions.CanStart(one, Me));

        var unseeded = T("ready", entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a"), E("e2", "confirmed", "b"),
        });
        Assert.False(TournamentPermissions.CanStart(unseeded, Me));

        var partial = T("ready", entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1), E("e2", "confirmed", "b"),
        });
        Assert.False(TournamentPermissions.CanStart(partial, Me));

        var ready = T("ready", entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1), E("e2", "confirmed", "b", 2),
        });
        Assert.True(TournamentPermissions.CanStart(ready, Me));
    }

    [Fact]
    public void OnlyConfirmedEntrantsCountTowardsStarting()
    {
        // A waitlisted or pending entrant has no place in the bracket, so two of them are
        // not two entrants.
        var t = T("ready", entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", "a", 1),
            E("e2", "waitlist", "b", 2),
            E("e3", "pending", "c", 3),
        });
        Assert.False(TournamentPermissions.CanStart(t, Me));
    }

    [Fact]
    public void CancellingStopsOnceItIsOver()
    {
        foreach (var live in new[] { "draft", "registration", "ready", "running" })
            Assert.True(TournamentPermissions.CanCancel(T(live), Me), live);
        foreach (var over in new[] { "finished", "cancelled", "abandoned" })
            Assert.False(TournamentPermissions.CanCancel(T(over), Me), over);
    }

    // ---------------------------------------------------------------- entering

    [Fact]
    public void EnteringNeedsOpenRegistrationAndNotBeingInAlready()
    {
        Assert.True(TournamentPermissions.CanEnter(T("registration"), Me));
        Assert.False(TournamentPermissions.CanEnter(T("ready"), Me));
        Assert.False(TournamentPermissions.CanEnter(T("running"), Me));
        Assert.False(TournamentPermissions.CanEnter(T("registration"), null));

        var already = T("registration", entrants: new List<TournamentEntrant>
        {
            E("e1", "confirmed", Me),
        });
        Assert.False(TournamentPermissions.CanEnter(already, Me));
    }

    [Fact]
    public void HavingWithdrawnLetsYouEnterAgain()
    {
        // A withdrawn entrant frees its players, which is what makes re-registering after
        // a mistake possible. MyEntrant must not find it.
        var t = T("registration", entrants: new List<TournamentEntrant>
        {
            E("e1", "withdrawn", Me),
        });
        Assert.Null(TournamentPermissions.MyEntrant(t, Me));
        Assert.True(TournamentPermissions.CanEnter(t, Me));
    }

    [Fact]
    public void ONLY_THE_CAPTAIN_WithdrawsALineUp()
    {
        // A team-mate pulling the whole entry out from under the captain is exactly the
        // kind of thing that has to be refused before the button is drawn.
        var t = T("registration", entrants: new List<TournamentEntrant>
        {
            E("t1", "confirmed", "captain", null, "captain", Me),
        });
        Assert.False(TournamentPermissions.CanWithdraw(t, Me));
        Assert.True(TournamentPermissions.CanWithdraw(t, "captain"));
    }

    [Fact]
    public void NobodyWithdrawsOnceTheBracketIsDrawn()
    {
        var entrants = new List<TournamentEntrant> { E("e1", "confirmed", Me) };
        Assert.False(TournamentPermissions.CanWithdraw(T("running", entrants: entrants), Me));
        Assert.False(TournamentPermissions.CanWithdraw(T("finished", entrants: entrants), Me));
        Assert.True(TournamentPermissions.CanWithdraw(T("registration", entrants: entrants), Me));
    }

    [Fact]
    public void ApplicationsAreOnlyDecidableWhilePending()
    {
        var t = T("registration");
        Assert.True(TournamentPermissions.CanDecideEntrant(t, Me, E("e1", "pending", "a")));
        Assert.False(TournamentPermissions.CanDecideEntrant(t, Me, E("e1", "confirmed", "a")));
        Assert.False(TournamentPermissions.CanDecideEntrant(t, Me, null));
        // And never for somebody else's tournament.
        Assert.False(TournamentPermissions.CanDecideEntrant(
            T("registration", owner: Other), Me, E("e1", "pending", "a")));
    }

    [Fact]
    public void AwardingAndDisqualifyingNeedABracket()
    {
        Assert.True(TournamentPermissions.CanAwardOrDisqualify(T("running"), Me));
        Assert.False(TournamentPermissions.CanAwardOrDisqualify(T("ready"), Me));
        Assert.False(TournamentPermissions.CanAwardOrDisqualify(T("finished"), Me));
    }
}
