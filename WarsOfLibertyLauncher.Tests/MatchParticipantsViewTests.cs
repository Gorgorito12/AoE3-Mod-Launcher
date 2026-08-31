using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for the roster drawn under a History row — who played, and who won.
///
/// <para><b>The rejections are the point.</b> The feature exists because a history row could
/// only ever say "2 players", but the way it can go wrong is by saying too much: a match whose
/// outcome nobody could read must not acquire a winner on the way to the screen, and a rating
/// nobody reported must not turn into "+0".</para>
///
/// <para>The other half is the empty list. Every backend older than this feature sends no
/// participants at all, and that has to render as the row always did rather than as a match
/// with no players in it.</para>
/// </summary>
public class MatchParticipantsViewTests
{
    private const string Me = "user-me";
    private const string Rival = "user-rival";

    private static MatchHistoryParticipant P(
        string id, double result, string? name = null,
        double? before = null, double? after = null)
        => new()
        {
            UserId = id,
            DisplayName = name ?? id,
            DiscordUsername = id + "#0001",
            Result = result,
            RatingBefore = before,
            RatingAfter = after,
        };

    /// <summary>
    /// The real shape: the match against Alucard, lost, from the reporter's own history.
    /// Winner on top, both named, the delta on each side.
    /// </summary>
    [Fact]
    public void ADecidedMatchNamesBothPlayersAndPutsTheWinnerFirst()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            P(Me, 0.0, "Gorgorito", before: 1617, after: 1500),
            P(Rival, 1.0, "Alucard", before: 1500, after: 1617),
        }, Me);

        Assert.Equal(2, lines.Count);

        Assert.Equal("Alucard", lines[0].Name);
        Assert.Equal(MatchVerdict.Win, lines[0].Verdict);
        Assert.Equal(117, lines[0].RatingDelta);
        Assert.False(lines[0].IsMe);

        Assert.Equal("Gorgorito", lines[1].Name);
        Assert.Equal(MatchVerdict.Loss, lines[1].Verdict);
        Assert.Equal(-117, lines[1].RatingDelta);
        Assert.True(lines[1].IsMe);
    }

    /// <summary>
    /// <b>The one that matters.</b> Most stored matches are all-0.5 — the outcome could not be
    /// read — and the roster must name the players without promoting either of them. A 0.5 is
    /// "nobody knows", never a draw and never a win.
    /// </summary>
    [Fact]
    public void AMatchNobodyCouldReadGivesNeitherPlayerAVerdict()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            P(Me, 0.5),
            P(Rival, 0.5),
        }, Me);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(MatchVerdict.NoResult, l.Verdict));
    }

    /// <summary>
    /// And it keeps the order it was given, because with every score equal there is nothing to
    /// sort by — a roster that reshuffled between two visits to the tab would read as a bug.
    /// </summary>
    [Fact]
    public void AnUnreadableMatchKeepsTheOrderItArrivedIn()
    {
        var incoming = new List<MatchHistoryParticipant>
        {
            P("b", 0.5, "Beto"), P("a", 0.5, "Ana"), P("c", 0.5, "Caro"),
        };

        var names = MatchParticipantsView.Build(incoming, Me).Select(l => l.Name);

        Assert.Equal(new[] { "Beto", "Ana", "Caro" }, names);
    }

    /// <summary>
    /// The winner is found by SCORE, not by the position the server sent — the client must not
    /// quietly depend on an ORDER BY it cannot see.
    /// </summary>
    [Fact]
    public void TheWinnerRisesEvenWhenTheServerSendsThemLast()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            P(Me, 0.0, "Gorgorito"),
            P(Rival, 1.0, "Alucard"),
        }, Me);

        Assert.Equal("Alucard", lines[0].Name);
    }

    /// <summary>
    /// No rating reported, no delta shown. "+0" would say the match moved nothing, which is a
    /// different claim from not knowing what it moved — the same refusal the result card makes.
    /// </summary>
    [Fact]
    public void AnUnknownRatingShowsNoDeltaRatherThanZero()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            P(Rival, 1.0, "Alucard"),                       // neither end known
            P(Me, 0.0, "Gorgorito", before: 1500),          // only one end known
        }, Me);

        Assert.Null(lines[0].RatingDelta);
        Assert.Null(lines[1].RatingDelta);
    }

    /// <summary>
    /// An older backend sends no participants, so the row falls back to what it always drew —
    /// including its "N players" chip, which the caller keeps precisely when this is empty.
    /// </summary>
    [Fact]
    public void AnOlderBackendYieldsNoLinesAtAll()
    {
        Assert.Empty(MatchParticipantsView.Build(new List<MatchHistoryParticipant>(), Me));
        Assert.Empty(MatchParticipantsView.Build(null, Me));
    }

    /// <summary>
    /// Nobody is "you" when nobody is signed in — the marker keys off a real id, and an empty
    /// one must not match a participant whose id is also empty.
    /// </summary>
    [Fact]
    public void SignedOutMarksNobody()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            P(Me, 1.0), P("", 0.0),
        }, null);

        Assert.All(lines, l => Assert.False(l.IsMe));
        Assert.All(MatchParticipantsView.Build(new List<MatchHistoryParticipant> { P("", 0.0) }, ""),
            l => Assert.False(l.IsMe));
    }

    /// <summary>
    /// The name falls back the way the backend's own does — display name, then the Discord
    /// handle — so one person is not called two different things in two places.
    /// </summary>
    [Fact]
    public void TheNameFallsBackToTheDiscordHandle()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = Rival, DisplayName = "   ", DiscordUsername = "alucard", Result = 1.0 },
            new() { UserId = Me, DisplayName = "", DiscordUsername = "", Result = 0.0 },
        }, Me);

        Assert.Equal("alucard", lines[0].Name);
        Assert.Equal("?", lines[1].Name);   // nothing to show, and the avatar disc accepts it
    }

    /// <summary>
    /// A blank avatar url is null, not an empty string: the disc builder switches on null to
    /// fall back to the monogram, and "" would send it down the image path with no image.
    /// </summary>
    [Fact]
    public void ABlankAvatarBecomesNull()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = Me, DisplayName = "Gorgorito", AvatarUrl = "", Result = 0.5 },
        }, Me);

        Assert.Null(lines[0].AvatarUrl);
    }

    /// <summary>
    /// A 1v1 has no sides to draw, and that is what keeps the history looking exactly as it
    /// always has.
    ///
    /// <para><b>This is the invariant the team work must not break.</b> Every match stored
    /// before teams existed carries team 0 for everybody, as does every 1v1 after — so if
    /// <c>HasTeams</c> ever answered true for them, every row in everyone's history would grow
    /// a "Team 1" heading over a single player. The caller's grouping hangs entirely off this
    /// one predicate.</para>
    /// </summary>
    [Fact]
    public void AOneVersusOneHasNoTeamsToDraw()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = Me, DisplayName = "Gorgorito", Result = 1.0 },
            new() { UserId = "u2", DisplayName = "Alucard", Result = 0.0 },
        }, Me);

        Assert.False(MatchParticipantsView.HasTeams(lines));
        Assert.All(lines, l => Assert.Equal(0, l.Team));
    }

    [Fact]
    public void TwoSidesAreCarriedThroughAndRecognised()
    {
        var lines = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
        {
            new() { UserId = Me, DisplayName = "Gorgorito", Team = 0, Result = 0.5 },
            new() { UserId = "u2", DisplayName = "Alucard", Team = 1, Result = 0.5 },
            new() { UserId = "u3", DisplayName = "Geaf", Team = 0, Result = 0.5 },
            new() { UserId = "u4", DisplayName = "Zelda", Team = 1, Result = 0.5 },
        }, Me);

        Assert.True(MatchParticipantsView.HasTeams(lines));
        Assert.Equal(2, lines.Count(l => l.Team == 0));
        Assert.Equal(2, lines.Count(l => l.Team == 1));
    }

    [Fact]
    public void NothingAtAllIsNotATeamGame()
    {
        Assert.False(MatchParticipantsView.HasTeams(null));
        Assert.False(MatchParticipantsView.HasTeams(new List<MatchParticipantLine>()));
    }
}
