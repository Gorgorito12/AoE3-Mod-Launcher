using System;
using System.Linq;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The room previews. Like <see cref="TournamentDemoDataTests"/>, what is pinned is what
/// each sample is FOR rather than what it contains: a fixture like this does not fail by
/// throwing, it fails by decaying into four identical rooms while everything still renders,
/// and then the preview quietly stops being able to show the thing it exists for.
/// </summary>
public class RoomDemoDataTests
{
    [Fact]
    public void TheOneTheHandoffIsAboutHasExactlyOneFreeSeat()
    {
        // The reported room: a competitive 1v1 with the host alone in it. One player and one
        // empty seat is the whole case — the panel used to draw a sliced row and no seat.
        var s = RoomDemoData.OneVOne();
        Assert.True(s.Competitive);
        Assert.Equal(2, s.Seats);
        Assert.Single(s.Players);
        Assert.Equal(1, s.FreeSeats);
        Assert.True(s.Players[0].IsHost);
        Assert.NotNull(s.Players[0].Rating);
        Assert.False(string.IsNullOrWhiteSpace(s.Code));
    }

    [Fact]
    public void EverySampleShowsSomethingTheOthersDoNot()
    {
        var all = RoomDemoData.All();

        // No two samples are the same room. Seats and fill alone would call 1v1 and
        // long-name duplicates - they draw the same TWO rows on purpose, and what one shows
        // that the other cannot is the name that does not fit, so the key has to carry it.
        var shapes = all
            .Select(s => (s.Seats, s.Players.Count, Longest: s.Players.Max(p => p.Login.Length)))
            .ToList();
        Assert.Equal(shapes.Count, shapes.Distinct().Count());

        // A room that is FULL — no free seat to draw — and one with free seats. Both paths
        // have to be reachable or half the roster's job is invisible.
        Assert.Contains(all, s => s.FreeSeats == 0);
        Assert.Contains(all, s => s.FreeSeats > 0);

        // A room where "an opponent" is the wrong word.
        Assert.Contains(all, s => s.Seats > 2);

        // Somebody with no rating: the second line is shorter then, and a sample where
        // everyone happens to have one would hide that.
        Assert.Contains(all, s => s.Players.Any(p => p.Rating == null));

        // A name nobody can fit, because the row's layout rests on the name being the one
        // thing that trims.
        Assert.Contains(all, s => s.Players.Any(p => p.Login.Length > 40));

        // Every sample is selectable by name, and the names are unique.
        Assert.Equal(all.Count, all.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var s in all) Assert.Equal(s.Name, RoomDemoData.ByName(s.Name).Name);
    }

    [Fact]
    public void EverySampleIsCoherentEnoughToDraw()
    {
        foreach (var s in RoomDemoData.All())
        {
            // AoE 3 caps a room at eight, and the panel is built on never having to scroll
            // below that.
            Assert.InRange(s.Seats, 2, 8);
            Assert.True(s.Players.Count <= s.Seats, $"{s.Name}: more players than seats");

            // Exactly one host, or the roster's ordering (host first) has nothing to sort by.
            Assert.Equal(1, s.Players.Count(p => p.IsHost));

            // Ids are what the roster dictionary is keyed on; a duplicate silently drops a row.
            Assert.Equal(s.Players.Count, s.Players.Select(p => p.UserId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(s.Players, p => Assert.False(string.IsNullOrWhiteSpace(p.Login)));
        }
    }

    [Fact]
    public void AnUnknownScenarioFallsBackToTheRoomTheRedesignIsAbout()
    {
        Assert.Equal("1v1", RoomDemoData.ByName(null).Name);
        Assert.Equal("1v1", RoomDemoData.ByName("").Name);
        Assert.Equal("1v1", RoomDemoData.ByName("no-such-room").Name);

        // Spelled the way somebody would type it on a command line.
        Assert.Equal("long-name", RoomDemoData.ByName("LONG-NAME").Name);
        Assert.Equal("2v2", RoomDemoData.ByName("  2v2  ").Name);
    }
}
