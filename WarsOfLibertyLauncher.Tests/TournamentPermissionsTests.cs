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
