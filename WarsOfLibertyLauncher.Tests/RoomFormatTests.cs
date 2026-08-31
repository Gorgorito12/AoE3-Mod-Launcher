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
