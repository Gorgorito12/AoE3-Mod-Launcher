using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="RoomFormats"/> — which shape of match a room was created for.
///
/// <para><b>The refusals are the point.</b> The format is DERIVED from the room's size rather
/// than stored, so the only way it can go wrong is by answering where it should not: reading a
/// casual room as a rated 1v1, or reading a competitive room of an impossible size as one.</para>
/// </summary>
public class RoomFormatTests
{
    [Theory]
    [InlineData(2, RoomFormat.OneVOne)]
    [InlineData(4, RoomFormat.TwoVTwo)]
    [InlineData(6, RoomFormat.ThreeVThree)]
    public void ACompetitiveRoomIsNamedByItsSize(int seats, RoomFormat expected)
    {
        Assert.Equal(expected, RoomFormats.Resolve(competitive: true, seats));
        Assert.Equal(seats, RoomFormats.PlayersFor(expected));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. <b>A room with no observer resolves to exactly what it always
    /// did.</b> Spectator seats are the change most able to break something that works today:
    /// every room ever created has none, every server that does not send the field means none,
    /// and if the arithmetic moved any of those by one, every one of those matches would stop
    /// scoring. The whole theory above runs again through the new parameter, explicitly zero.
    /// </summary>
    [Theory]
    [InlineData(2, RoomFormat.OneVOne)]
    [InlineData(4, RoomFormat.TwoVTwo)]
    [InlineData(6, RoomFormat.ThreeVThree)]
    public void THE_ONE_THAT_MATTERS_NoObserverMeansNothingChanged(int seats, RoomFormat expected)
    {
        Assert.Equal(expected, RoomFormats.Resolve(competitive: true, seats, spectatorSlots: 0));
        Assert.Equal(RoomFormats.Resolve(competitive: true, seats),
                     RoomFormats.Resolve(competitive: true, seats, spectatorSlots: 0));
        Assert.Equal(seats, RoomFormats.PlayingSeats(seats, 0));
    }

    /// <summary>
    /// The seats an observer occupies come off the top before the format is read.
    ///
    /// <para>The 2v2 case is the one that was actually broken: five seats matched no format, so
    /// the room was downgraded to casual and <b>the match did not score</b> — a tournament
    /// semi-final played in front of an observer quietly counted for nothing.</para>
    /// </summary>
    [Theory]
    [InlineData(3, 1, RoomFormat.OneVOne)]
    [InlineData(5, 1, RoomFormat.TwoVTwo)]
    [InlineData(7, 1, RoomFormat.ThreeVThree)]
    [InlineData(8, 2, RoomFormat.ThreeVThree)]
    [InlineData(4, 2, RoomFormat.OneVOne)]
    public void AnObserverSeatDoesNotChangeTheFormat(int seats, int obs, RoomFormat expected)
    {
        Assert.Equal(expected, RoomFormats.Resolve(competitive: true, seats, obs));
        Assert.Equal(RoomFormats.PlayersFor(expected), RoomFormats.PlayingSeats(seats, obs));
    }

    /// <summary>
    /// A negative observer count is read as none, never subtracted.
    ///
    /// <para>It is meaningless data, and the arithmetic would otherwise ADD seats: a 2-seat room
    /// with -2 observers would come out a rated 2v2. Bad data must not be able to promote a room
    /// into a team format nobody agreed to play, which is the same refusal
    /// <see cref="RoomFormat.Unknown"/> exists to make.</para>
    /// </summary>
    [Theory]
    [InlineData(2, -2)]
    [InlineData(2, -4)]
    [InlineData(2, int.MinValue)]
    public void ANegativeObserverCountCannotPromoteARoom(int seats, int obs)
    {
        Assert.Equal(RoomFormat.OneVOne, RoomFormats.Resolve(competitive: true, seats, obs));
        Assert.Equal(seats, RoomFormats.PlayingSeats(seats, obs));
    }

    /// <summary>
    /// A room that is nothing but observers names no format, and is not guessed at.
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(1, 5)]
    public void ARoomWithNoPlayersLeftIsUnknown(int seats, int obs)
        => Assert.Equal(RoomFormat.Unknown, RoomFormats.Resolve(competitive: true, seats, obs));

    /// <summary>
    /// THE ONE THAT MATTERS. A room with no observer seats answers exactly what it always
    /// did: full when the head count reaches the size, joinable until then.
    ///
    /// <para>Every room that exists today is this room. If the split moved any of these by
    /// one, the rooms list would start hiding joinable rooms or offering seats that are not
    /// there — and it would do it to every room, not just the new ones.</para>
    /// </summary>
    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(8, 7, false)]
    [InlineData(8, 8, true)]
    public void THE_ONE_THAT_MATTERS_ARoomWithNoObserverSeatsIsUnchanged(
        int max, int present, bool full)
    {
        var seats = RoomFormats.SeatsOf(max, spectatorSlots: 0, present, spectatorsPresent: 0);

        Assert.Equal(full, seats.Full);
        Assert.Equal(full, seats.PlayingFull);
        Assert.False(seats.CanWatch);
        Assert.Equal(max, seats.PlayerSeats);
        Assert.Equal(present, seats.PlayersSeated);
    }

    /// <summary>
    /// The two kinds of seat run out separately, which is the whole reason for the split.
    ///
    /// <para>Four people in a five-seat 2v2-with-a-caster is either four players and no
    /// caster, or three players and a caster. Those are opposite answers to "can I play?",
    /// and the total head count cannot tell them apart.</para>
    /// </summary>
    [Fact]
    public void PlayingSeatsAndWatchingSeatsRunOutIndependently()
    {
        // 5 seats, 1 of them for watching. Four in, none watching: the players are full and
        // the caster's seat is still open.
        var playersFull = RoomFormats.SeatsOf(5, 1, currentPlayers: 4, spectatorsPresent: 0);
        Assert.True(playersFull.PlayingFull);
        Assert.True(playersFull.CanWatch);
        Assert.False(playersFull.Full);

        // Same four people, but one of them is the caster: a playing seat is still free.
        var casterIn = RoomFormats.SeatsOf(5, 1, currentPlayers: 4, spectatorsPresent: 1);
        Assert.False(casterIn.PlayingFull);
        Assert.False(casterIn.CanWatch);
        Assert.Equal(3, casterIn.PlayersSeated);

        // Everyone in. Nothing left of either kind.
        var full = RoomFormats.SeatsOf(5, 1, currentPlayers: 5, spectatorsPresent: 1);
        Assert.True(full.Full);
    }

    /// <summary>
    /// Numbers off the wire are clamped to something a room could actually have.
    ///
    /// <para>A negative free seat would offer a chair that does not exist; more watchers than
    /// watching seats would subtract them all from the players and show a full room as empty.
    /// Neither is worth trusting a remote number for.</para>
    /// </summary>
    [Fact]
    public void NonsenseFromTheWireCannotInventOrHideASeat()
    {
        var negativeSlots = RoomFormats.SeatsOf(4, -3, currentPlayers: 4, spectatorsPresent: 0);
        Assert.Equal(4, negativeSlots.PlayerSeats);
        Assert.True(negativeSlots.Full);

        var tooManyWatching = RoomFormats.SeatsOf(4, 1, currentPlayers: 4, spectatorsPresent: 9);
        Assert.Equal(1, tooManyWatching.Watching);
        Assert.Equal(3, tooManyWatching.PlayersSeated);
        Assert.True(tooManyWatching.Full);

        var moreSlotsThanSeats = RoomFormats.SeatsOf(2, 9, currentPlayers: 0, spectatorsPresent: 0);
        Assert.Equal(0, moreSlotsThanSeats.PlayerSeats);
        Assert.True(moreSlotsThanSeats.PlayingFull);

        var negativeHeads = RoomFormats.SeatsOf(4, 1, currentPlayers: -5, spectatorsPresent: -5);
        Assert.Equal(0, negativeHeads.PlayersSeated);
        Assert.Equal(0, negativeHeads.Watching);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. <b>With observing unavailable, a room whose game is full is
    /// full.</b>
    ///
    /// <para>This is the case the obvious way of hiding the feature gets wrong. A room with
    /// four playing seats taken and one watching seat free is NOT
    /// <see cref="RoomFormats.RoomSeats.Full"/> — so simply not drawing the Watch button
    /// lets the row fall through to Join, and the player presses a button for a seat that does
    /// not exist. The server answers <c>lobby_full</c> and the row looks broken.</para>
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_WithoutObservingAFullGameIsAFullRoom()
    {
        // 5 seats, 1 for watching, four players in: the game is full, the caster seat is not.
        var seats = RoomFormats.SeatsOf(5, 1, currentPlayers: 4, spectatorsPresent: 0);
        Assert.False(seats.Full);
        Assert.True(seats.CanWatch);

        // The seat exists, so it is offered - but only to somebody who can take it.
        Assert.Equal(RoomOffer.Watch, RoomFormats.OfferFor(seats, observersEnabled: true));
        Assert.Equal(RoomOffer.Full, RoomFormats.OfferFor(seats, observersEnabled: false));

        // And never Join, which is the answer that would put somebody through a refusal.
        Assert.NotEqual(RoomOffer.Join, RoomFormats.OfferFor(seats, observersEnabled: false));
    }

    /// <summary>
    /// A free playing seat is offered whatever the viewer can or cannot do about observers.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFreePlayingSeatIsAlwaysAJoin(bool observers)
    {
        Assert.Equal(RoomOffer.Join,
            RoomFormats.OfferFor(RoomFormats.SeatsOf(2, 0, 1, 0), observers));
        // Even with a watcher already seated and a watching seat still free.
        Assert.Equal(RoomOffer.Join,
            RoomFormats.OfferFor(RoomFormats.SeatsOf(6, 2, 3, 1), observers));
    }

    /// <summary>A room with nothing left reads Full for everybody.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARoomWithNothingLeftIsFullForEverybody(bool observers)
    {
        Assert.Equal(RoomOffer.Full,
            RoomFormats.OfferFor(RoomFormats.SeatsOf(5, 1, 5, 1), observers));
        Assert.Equal(RoomOffer.Full,
            RoomFormats.OfferFor(RoomFormats.SeatsOf(2, 0, 2, 0), observers));
    }

    /// <summary>
    /// A room with no observer seats never offers Watch, however the flag is set — which is
    /// every room that exists today.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARoomWithNoObserverSeatsNeverOffersWatch(bool observers)
    {
        var full = RoomFormats.SeatsOf(4, 0, currentPlayers: 4, spectatorsPresent: 0);
        Assert.Equal(RoomOffer.Full, RoomFormats.OfferFor(full, observers));

        var open = RoomFormats.SeatsOf(4, 0, currentPlayers: 2, spectatorsPresent: 0);
        Assert.Equal(RoomOffer.Join, RoomFormats.OfferFor(open, observers));
    }

    /// <summary>Observers never make a casual room competitive.</summary>
    [Theory]
    [InlineData(5, 1)]
    [InlineData(3, 1)]
    public void ObserversDoNotRateACasualRoom(int seats, int obs)
        => Assert.Equal(RoomFormat.Casual, RoomFormats.Resolve(competitive: false, seats, obs));

    /// <summary>
    /// THE ONE THAT MATTERS. A casual room of two is not a rated 1v1 — its size says nothing
    /// about how it will be played, and the competitive flag exists precisely to stop that claim
    /// being made for somebody.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ACasualRoomHasNoFormatWhateverItsSize(int seats)
        => Assert.Equal(RoomFormat.Casual, RoomFormats.Resolve(competitive: false, seats));

    /// <summary>
    /// A competitive room of a size no format names — one created before formats existed, or by a
    /// client that did not go through the dialog. It reads as Unknown and <b>never as 1v1</b>:
    /// falling back would hand it the abandonment rule and the 1v1 ladder on a guess.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ACompetitiveRoomOfAnImpossibleSizeIsNotGuessed(int seats)
    {
        var format = RoomFormats.Resolve(competitive: true, seats);

        Assert.Equal(RoomFormat.Unknown, format);
        Assert.NotEqual(RoomFormat.OneVOne, format);
        Assert.Equal(0, RoomFormats.PlayersFor(format));
    }

    /// <summary>
    /// Team-ness and the abandonment rule are asked separately, because they answer differently
    /// for everything that is not a named format — and a rule written as "not 1v1" would fire on
    /// a casual room and on an unknown one, which is how the launcher came to threaten a forfeit
    /// the server does not carry out.
    /// </summary>
    [Fact]
    public void OnlyRealTeamFormatsAreTeams_AndOnlyOneVOneForfeits()
    {
        Assert.True(RoomFormats.IsTeam(RoomFormat.TwoVTwo));
        Assert.True(RoomFormats.IsTeam(RoomFormat.ThreeVThree));
        Assert.False(RoomFormats.IsTeam(RoomFormat.OneVOne));
        Assert.False(RoomFormats.IsTeam(RoomFormat.Casual));
        Assert.False(RoomFormats.IsTeam(RoomFormat.Unknown));

        Assert.True(RoomFormats.AbandonmentApplies(RoomFormat.OneVOne));
        foreach (var other in Enum.GetValues<RoomFormat>().Where(f => f != RoomFormat.OneVOne))
            Assert.False(RoomFormats.AbandonmentApplies(other));
    }

    // ---------------- the promise the room made ----------------

    private static Dictionary<string, int> Sides(params int[] teamPerPlayer)
        => teamPerPlayer.Select((t, i) => (t, i))
                        .ToDictionary(x => "u" + x.i, x => x.t);

    [Fact]
    public void TeamsThatMatchTheDeclaredFormatAreKept()
    {
        Assert.True(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, Sides(0, 1, 0, 1)));
        Assert.True(RoomFormats.TeamsAgreeWithFormat(
            RoomFormat.ThreeVThree, Sides(0, 1, 0, 1, 0, 1)));
        Assert.True(RoomFormats.TeamsAgreeWithFormat(RoomFormat.OneVOne, null));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A room created as 2v2 and actually played 1v3 would otherwise write
    /// real-but-wrong sides into four people's history, with nothing downstream able to tell.
    /// </summary>
    [Fact]
    public void SidesThatContradictTheDeclaredFormatAreRefused()
    {
        Assert.False(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, Sides(0, 1, 1, 1)));
        Assert.False(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, Sides(0, 1, 2, 3)));
        Assert.False(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, Sides(0, 1)));
        Assert.False(RoomFormats.TeamsAgreeWithFormat(RoomFormat.TwoVTwo, null));
        Assert.False(RoomFormats.TeamsAgreeWithFormat(
            RoomFormat.ThreeVThree, Sides(0, 1, 0, 1)));
        // A 1v1 room that somehow produced sides is not this match.
        Assert.False(RoomFormats.TeamsAgreeWithFormat(RoomFormat.OneVOne, Sides(0, 1)));
    }

    /// <summary>
    /// A room that declared nothing cannot be contradicted — which is how a CASUAL team game
    /// still shows its sides in the history. Refusing here would silently drop teams from every
    /// unranked 2v2 people actually play.
    /// </summary>
    [Theory]
    [InlineData(RoomFormat.Casual)]
    [InlineData(RoomFormat.Unknown)]
    public void ARoomThatDeclaredNothingAcceptsWhateverWasPlayed(RoomFormat format)
    {
        Assert.True(RoomFormats.TeamsAgreeWithFormat(format, Sides(0, 1, 0, 1)));
        Assert.True(RoomFormats.TeamsAgreeWithFormat(format, null));
        Assert.True(RoomFormats.TeamsAgreeWithFormat(format, Sides(0, 0, 0, 1)));
    }

    /// <summary>
    /// Written against the enum rather than a list, so a format added later must be given a name
    /// on purpose instead of silently rendering as nothing.
    /// </summary>
    [Fact]
    public void EveryPlayableFormatHasAName()
    {
        foreach (var f in Enum.GetValues<RoomFormat>())
        {
            var key = RoomFormats.LabelKey(f);
            if (f is RoomFormat.Casual or RoomFormat.Unknown) Assert.Null(key);
            else Assert.False(string.IsNullOrWhiteSpace(key));
        }
    }
}
