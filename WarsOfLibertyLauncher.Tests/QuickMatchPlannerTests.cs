using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The quick match, phase 0 (docs/DISENO-PARTIDA-RAPIDA.md): join a waiting competitive 1v1 room
/// of the same mod, or open one — with no server help, so every rule is here.
///
/// <para>The refusals are most of the file, on purpose. A room the search should not join but
/// does is a player dropped into a 2v2, a private room, a tournament slot or another version of
/// the mod; a room it should join but skips is two players waiting for each other a room apart.
/// The second half of the file is the rule that stops that: two searchers who opened rooms at
/// the same moment must end up in ONE of them.</para>
/// </summary>
public class QuickMatchPlannerTests
{
    private const string Me = "user-me";
    private const string Mod = "wol";
    private const string Hash = "abc123";

    private static LobbySummary Room(
        string id,
        string createdAt = "2026-10-28T15:00:00Z",
        string hostId = "user-host",
        bool competitive = true,
        bool isPrivate = false,
        string status = "open",
        string? tournamentMatchId = null,
        string modId = Mod,
        string modHash = Hash,
        int maxPlayers = 2,
        int currentPlayers = 1,
        int spectatorSlots = 0,
        int spectatorsPresent = 0,
        double? hostRating = null,
        bool? hostInPlacement = null)
        => new()
        {
            Id = id,
            CreatedAt = createdAt,
            Competitive = competitive,
            IsPrivate = isPrivate,
            Status = status,
            TournamentMatchId = tournamentMatchId,
            ModId = modId,
            ModCombinedHash = modHash,
            MaxPlayers = maxPlayers,
            CurrentPlayers = currentPlayers,
            SpectatorSlots = spectatorSlots,
            SpectatorsPresent = spectatorsPresent,
            Host = new LobbyHost { Id = hostId, Rating = hostRating, InPlacement = hostInPlacement },
        };

    // ---------------------------------------------------------------- who may be joined

    [Fact]
    public void AWaitingRankedOneVOneOfMyModIsACandidate()
    {
        Assert.True(QuickMatchPlanner.IsCandidate(Room("r1"), Me, Mod, Hash));
    }

    /// <summary>
    /// Every way a room can be wrong for a quick match, each on its own so a rule that stops
    /// refusing names itself.
    /// </summary>
    [Theory]
    [InlineData("casual")]
    [InlineData("private")]
    [InlineData("in game")]
    [InlineData("tournament slot")]
    [InlineData("other mod")]
    [InlineData("other version")]
    [InlineData("2v2")]
    [InlineData("3v3")]
    [InlineData("seat taken")]
    [InlineData("my own room")]
    public void THE_REFUSALS_ARE_THE_POINT_ARoomThatIsNotAWaitingOneVOneIsNeverJoined(string why)
    {
        var room = why switch
        {
            "casual" => Room("r", competitive: false),
            "private" => Room("r", isPrivate: true),
            "in game" => Room("r", status: "in_game"),
            "tournament slot" => Room("r", tournamentMatchId: "m-1"),
            "other mod" => Room("r", modId: "improvement-mod"),
            "other version" => Room("r", modHash: "different"),
            "2v2" => Room("r", maxPlayers: 4),
            "3v3" => Room("r", maxPlayers: 6),
            "seat taken" => Room("r", currentPlayers: 2),
            "my own room" => Room("r", hostId: Me),
            _ => throw new ArgumentOutOfRangeException(nameof(why)),
        };
        Assert.False(QuickMatchPlanner.IsCandidate(room, Me, Mod, Hash), why);
    }

    [Fact]
    public void NothingAndNobodyAreNotCandidates()
    {
        Assert.False(QuickMatchPlanner.IsCandidate(null, Me, Mod, Hash));
        Assert.False(QuickMatchPlanner.IsCandidate(Room("r"), Me, "", Hash));
    }

    /// <summary>
    /// The playing seat decides, not the head count: a 1v1 with an observer seat is still a 1v1,
    /// joinable while its second PLAYER seat is free, and full once it is taken even though the
    /// observer seat is empty — that room would answer <c>lobby_full</c> to a player.
    /// </summary>
    [Fact]
    public void AnObserverSeatNeitherMakesItAnotherFormatNorAFreeSeat()
    {
        var waiting = Room("r", maxPlayers: 3, spectatorSlots: 1, currentPlayers: 1);
        var playingFull = Room("r", maxPlayers: 3, spectatorSlots: 1, currentPlayers: 2);

        Assert.True(QuickMatchPlanner.IsCandidate(waiting, Me, Mod, Hash));
        Assert.False(QuickMatchPlanner.IsCandidate(playingFull, Me, Mod, Hash));
    }

    /// <summary>The ids and fingerprints come from different code paths and may differ in case.</summary>
    [Fact]
    public void ModAndFingerprintCompareWithoutCase()
    {
        Assert.True(QuickMatchPlanner.IsCandidate(Room("r", modId: "WoL", modHash: "ABC123"), Me, Mod, Hash));
    }

    [Fact]
    public void ASignedOutSearcherHostsNothing()
    {
        Assert.False(QuickMatchPlanner.IsHostedBy(Room("r", hostId: ""), null));
        Assert.False(QuickMatchPlanner.IsHostedBy(Room("r", hostId: Me), ""));
        Assert.True(QuickMatchPlanner.IsHostedBy(Room("r", hostId: Me), Me));
    }

    // ---------------------------------------------------------------- which room first

    [Fact]
    public void OlderIsTheEarlierTimeThenTheSmallerId()
    {
        var early = Room("z", createdAt: "2026-10-28T15:00:00Z");
        var late = Room("a", createdAt: "2026-10-28T15:00:05Z");
        Assert.True(QuickMatchPlanner.CompareAge(early, late) < 0);
        Assert.True(QuickMatchPlanner.CompareAge(late, early) > 0);

        // Same second: the id decides, so the order is total.
        var a = Room("a1", createdAt: "2026-10-28T15:00:00Z");
        var b = Room("b1", createdAt: "2026-10-28T15:00:00Z");
        Assert.True(QuickMatchPlanner.CompareAge(a, b) < 0);
        Assert.True(QuickMatchPlanner.CompareAge(b, a) > 0);
        Assert.Equal(0, QuickMatchPlanner.CompareAge(a, a));
    }

    /// <summary>A time that cannot be read counts as the NEWEST, so such a room never draws a mover.</summary>
    [Fact]
    public void AnUnreadableTimeCountsAsTheNewest()
    {
        var unreadable = Room("a", createdAt: "not a date");
        var readable = Room("z", createdAt: "2026-10-28T15:00:00Z");
        Assert.True(QuickMatchPlanner.CompareAge(readable, unreadable) < 0);
    }

    /// <summary>
    /// The closest rating first, then age — and nobody is left out for being far away: in a small
    /// community a room 400 points off is still a match, where no room is none.
    /// </summary>
    [Fact]
    public void TheClosestRatingGoesFirstAndNoRatingIsEverExcluded()
    {
        var rooms = new[]
        {
            Room("far", createdAt: "2026-10-28T15:00:00Z", hostRating: 1900),
            Room("close", createdAt: "2026-10-28T15:00:09Z", hostRating: 1520),
            Room("middle", createdAt: "2026-10-28T15:00:05Z", hostRating: 1650),
        };

        var order = QuickMatchPlanner.RoomsToJoin(rooms, Me, Mod, Hash, myRating: 1500).Select(r => r.Id);

        Assert.Equal(new[] { "close", "middle", "far" }, order);
    }

    /// <summary>
    /// A host still being placed has no rating anybody may see, so he cannot jump the queue on
    /// it: he sorts after every visible rating, then by age like everyone else.
    /// </summary>
    [Fact]
    public void APlacingHostSortsAfterTheVisibleRatings()
    {
        var rooms = new[]
        {
            Room("placing", createdAt: "2026-10-28T15:00:00Z", hostRating: 1500, hostInPlacement: true),
            Room("withheld", createdAt: "2026-10-28T15:00:01Z"),
            Room("visible", createdAt: "2026-10-28T15:00:09Z", hostRating: 1800),
        };

        var order = QuickMatchPlanner.RoomsToJoin(rooms, Me, Mod, Hash, myRating: 1500).Select(r => r.Id);

        Assert.Equal(new[] { "visible", "placing", "withheld" }, order);
    }

    /// <summary>A searcher with no visible rating of his own simply takes the oldest room first.</summary>
    [Fact]
    public void WithoutMyRatingTheOldestGoesFirst()
    {
        var rooms = new[]
        {
            Room("new", createdAt: "2026-10-28T15:00:09Z", hostRating: 1500),
            Room("old", createdAt: "2026-10-28T15:00:00Z", hostRating: 2100),
        };

        var order = QuickMatchPlanner.RoomsToJoin(rooms, Me, Mod, Hash, myRating: null).Select(r => r.Id);

        Assert.Equal(new[] { "old", "new" }, order);
    }

    [Fact]
    public void OnlyCandidatesAreOffered()
    {
        var rooms = new[]
        {
            Room("ok"),
            Room("full", currentPlayers: 2),
            Room("mine", hostId: Me),
            Room("old-build", modHash: "different"),
        };

        Assert.Equal(new[] { "ok" }, QuickMatchPlanner.RoomsToJoin(rooms, Me, Mod, Hash, 1500).Select(r => r.Id));
        Assert.Empty(QuickMatchPlanner.RoomsToJoin(null, Me, Mod, Hash, 1500));
    }

    // ---------------------------------------------------------------- convergence

    /// <summary>
    /// THE ONE THAT MATTERS. Two players press the button in the same second, both find nothing
    /// and both open a room — created in the SAME second, so the time cannot tell them apart.
    /// Each then looks from his own room. Exactly one of them must move, and into the other's
    /// room; if both moved they would swap rooms for ever, and if neither did each would wait
    /// alone for half an hour while a rival sat one room away.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_TwoSearchersWhoCreatedAtOnceEndInTheSameRoom()
    {
        const string ana = "user-ana", ben = "user-ben";
        var anasRoom = Room("lobby-7f", createdAt: "2026-10-28T15:00:00Z", hostId: ana);
        var bensRoom = Room("lobby-2c", createdAt: "2026-10-28T15:00:00Z", hostId: ben);
        var list = new[] { anasRoom, bensRoom };

        var anaMoves = QuickMatchPlanner.PickToMoveTo(list, anasRoom.Id, ana, Mod, Hash);
        var benMoves = QuickMatchPlanner.PickToMoveTo(list, bensRoom.Id, ben, Mod, Hash);

        // Exactly one moves…
        Assert.True((anaMoves == null) != (benMoves == null), "exactly one of the two must move");
        // …and into the room the other one is waiting in.
        var (mover, target, stayersRoom) = anaMoves != null
            ? ("ana", anaMoves, bensRoom)
            : ("ben", benMoves!, anasRoom);
        Assert.Equal(stayersRoom.Id, target.Id);
        // The same second, so the id decided: the smaller one is the older and stays.
        Assert.Equal("ana", mover);
    }

    /// <summary>
    /// Three at once: everyone who moves heads for the ONE oldest room, so the search converges
    /// on a single room instead of pairing off at random. (Only one of the movers will get the
    /// seat; the loser of that race simply looks again.)
    /// </summary>
    [Fact]
    public void ThreeSearchersAllHeadForTheOldestRoom()
    {
        var a = Room("a", createdAt: "2026-10-28T15:00:00Z", hostId: "u-a");
        var b = Room("b", createdAt: "2026-10-28T15:00:02Z", hostId: "u-b");
        var c = Room("c", createdAt: "2026-10-28T15:00:04Z", hostId: "u-c");
        var list = new[] { c, a, b };

        Assert.Null(QuickMatchPlanner.PickToMoveTo(list, "a", "u-a", Mod, Hash));
        Assert.Equal("a", QuickMatchPlanner.PickToMoveTo(list, "b", "u-b", Mod, Hash)?.Id);
        Assert.Equal("a", QuickMatchPlanner.PickToMoveTo(list, "c", "u-c", Mod, Hash)?.Id);
    }

    /// <summary>
    /// The oldest room wins even when a younger one has the closer rating: convergence needs one
    /// answer every waiter agrees on, and only age gives one.
    /// </summary>
    [Fact]
    public void MovingFollowsAgeNotRating()
    {
        var mine = Room("mine", createdAt: "2026-10-28T15:00:09Z", hostId: Me, hostRating: 1500);
        var oldest = Room("oldest", createdAt: "2026-10-28T15:00:00Z", hostRating: 2200);
        var closer = Room("closer", createdAt: "2026-10-28T15:00:05Z", hostId: "user-other", hostRating: 1510);

        Assert.Equal("oldest",
            QuickMatchPlanner.PickToMoveTo(new[] { mine, closer, oldest }, "mine", Me, Mod, Hash)?.Id);
    }

    [Fact]
    public void ANewerRoomOrOneThatCannotBeJoinedIsNoReasonToMove()
    {
        var mine = Room("mine", createdAt: "2026-10-28T15:00:05Z", hostId: Me);
        var newer = Room("newer", createdAt: "2026-10-28T15:00:09Z");
        var olderButFull = Room("full", createdAt: "2026-10-28T15:00:00Z", currentPlayers: 2);
        var olderOtherVersion = Room("other", createdAt: "2026-10-28T15:00:01Z", modHash: "different");

        Assert.Null(QuickMatchPlanner.PickToMoveTo(
            new[] { mine, newer, olderButFull, olderOtherVersion }, "mine", Me, Mod, Hash));
    }

    /// <summary>
    /// My own room missing from the list means the list is out of date; moving on it could send
    /// two waiters past each other, so the answer is to stay and look again next time.
    /// </summary>
    [Fact]
    public void MyRoomMissingFromTheListMeansStay()
    {
        var older = Room("older", createdAt: "2026-10-28T15:00:00Z");
        Assert.Null(QuickMatchPlanner.PickToMoveTo(new[] { older }, "mine", Me, Mod, Hash));
        Assert.Null(QuickMatchPlanner.PickToMoveTo(null, "mine", Me, Mod, Hash));
    }

    // ---------------------------------------------------------------- what is said

    /// <summary>
    /// "Nothing found" has a reason a player can act on when rooms of his mod are waiting on
    /// another version: those are counted, and only those.
    /// </summary>
    [Fact]
    public void OtherVersionsAreCountedAndNothingElse()
    {
        var rooms = new[]
        {
            Room("v-old-1", modHash: "old"),
            Room("v-old-2", modHash: "older"),
            Room("same", modHash: Hash),
            Room("full-old", modHash: "old", currentPlayers: 2),
            Room("team-old", modHash: "old", maxPlayers: 4),
            Room("mine-old", modHash: "old", hostId: Me),
            Room("other-mod", modId: "improvement-mod", modHash: "old"),
        };

        Assert.Equal(2, QuickMatchPlanner.OtherVersionCount(rooms, Me, Mod, Hash));
        Assert.Equal(0, QuickMatchPlanner.OtherVersionCount(null, Me, Mod, Hash));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(133, "02:13")]
    [InlineData(4500, "75:00")]
    [InlineData(-5, "00:00")]
    public void TheElapsedTimeIsMinutesAndSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, QuickMatchPlanner.Elapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ASearchGivesUpAfterHalfAnHour()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), QuickMatchPlanner.GiveUpAfter);
    }
}
