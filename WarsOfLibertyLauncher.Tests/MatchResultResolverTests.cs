using System.Collections.Generic;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="MatchResultResolver"/> — who gets credited with winning.
///
/// <para>This is the only code in the launcher where being wrong takes rating points off one real
/// person and gives them to another, and until now it had no tests at all. The refusals are half
/// the point: a result that cannot be established is reported as 0.5 for everyone, which the
/// backend and <see cref="PlayerStanding"/> both read as "not known" rather than "draw".</para>
/// </summary>
public class MatchResultResolverTests
{
    private const string Host = "host-user-id";
    private const string Rival = "rival-user-id";

    private static List<string> Duel() => new() { Host, Rival };

    [Fact]
    public void AClean1v1WithTheHostPresent_CarriesTheResult()
    {
        Assert.Equal(1.0, MatchResultResolver.ResolveHostResult(1.0, Duel(), Host).Result);
        Assert.Equal(0.0, MatchResultResolver.ResolveHostResult(0.0, Duel(), Host).Result);
    }

    [Fact]
    public void NoResultInTheRecording_IsNotKnown()
        => Assert.Null(MatchResultResolver.ResolveHostResult(null, Duel(), Host).Result);

    /// <summary>
    /// The recording names one loser. In a team game that says nothing about the other three
    /// players, so the whole match has to go down as unknown rather than have three scores
    /// invented around one real one.
    /// </summary>
    [Fact]
    public void MoreThanTwoParticipants_IsNotKnown()
    {
        var teamGame = new List<string> { Host, Rival, "third", "fourth" };

        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, teamGame, Host).Result);
    }

    [Fact]
    public void FewerThanTwoParticipants_IsNotKnown()
    {
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, new List<string> { Host }, Host).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, new List<string>(), Host).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, null, Host).Result);
    }

    /// <summary>
    /// The reporter is the player whose recording was read. If they are not among the people being
    /// reported, the room's roster and the file disagree — so nothing here can be trusted to name
    /// the other player either.
    /// </summary>
    [Fact]
    public void HostMissingFromTheParticipants_IsNotKnown()
    {
        var strangers = new List<string> { "someone", Rival };

        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, strangers, Host).Result);
    }

    [Fact]
    public void NoHostId_IsNotKnown()
    {
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, Duel(), null).Result);
        Assert.Null(MatchResultResolver.ResolveHostResult(1.0, Duel(), "").Result);
    }

    /// <summary>
    /// Every refusal has to name its cause. Before this, a match that silently went down as a draw
    /// looked identical whether it was skipped, refused or had simply never recorded — which made
    /// "my game didn't count" undiagnosable from a log.
    /// </summary>
    [Fact]
    public void EveryRefusalNamesItsReason()
    {
        Assert.Contains("recording", MatchResultResolver.ResolveHostResult(null, Duel(), Host).Reason);
        Assert.Contains("not 2", MatchResultResolver
            .ResolveHostResult(1.0, new List<string> { Host }, Host).Reason);
        Assert.Contains("host", MatchResultResolver
            .ResolveHostResult(1.0, new List<string> { "someone", Rival }, Host).Reason);
        Assert.NotEmpty(MatchResultResolver.ResolveHostResult(1.0, Duel(), Host).Reason);
    }

    [Fact]
    public void TheOpponentGetsTheMirrorImage()
    {
        Assert.Equal(0.0, MatchResultResolver.ParticipantResult(1.0, isHost: false));
        Assert.Equal(1.0, MatchResultResolver.ParticipantResult(0.0, isHost: false));
        Assert.Equal(0.5, MatchResultResolver.ParticipantResult(0.5, isHost: false));
        Assert.Equal(1.0, MatchResultResolver.ParticipantResult(1.0, isHost: true));
    }

    /// <summary>
    /// THE ONE THAT MATTERS. The backend rejects a report whose scores don't sum to half the
    /// player count, and Glicko takes them at face value — so if this ever goes red, either every
    /// match is refused or two players are credited for the same win.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void TheTwoScoresAlwaysSumToOne(double hostResult)
    {
        var host = MatchResultResolver.ParticipantResult(hostResult, isHost: true);
        var rival = MatchResultResolver.ParticipantResult(hostResult, isHost: false);

        Assert.Equal(1.0, host + rival, precision: 10);
    }
}
