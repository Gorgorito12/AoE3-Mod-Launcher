using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The enrol window (design 82). <see cref="EnrollView"/> decides, the window paints, and the
/// refusals are what matter: a team that cannot enter stays on screen with its reason, and nothing
/// is sent while something is missing.
/// </summary>
[Collection("wpf-and-language")]
public class EnrollDialogTests
{
    private const string Me = "me";

    private static TeamMember P(string id, string name) => new() { UserId = id, DisplayName = name };

    private static TournamentDetail T(string format = "1v1", string source = "solo", string entry = "open") => new()
    {
        Id = "t", Name = "Copa", Format = format, TeamSource = source, EntryMode = entry, Status = "registration",
        Capacity = 8, ConfirmedCount = 5, RosterSize = format == "3v3" ? 3 : format == "2v2" ? 2 : 1,
        Entrants = new(),
    };

    private static TeamSummary Team(string id, string owner, params string[] members) => new()
    {
        Id = id, Name = id, OwnerUserId = owner, Members = members.Select(m => P(m, m)).ToList(),
    };

    private static TournamentEntrant Entrant(string kind, string captain, params string[] members) => new()
    {
        Id = "e", Kind = kind, Status = "confirmed", DisplayName = "Los Andes", CaptainUserId = captain,
        MemberIds = members.ToList(),
    };

    [Theory]
    [InlineData("1v1", "solo", EnrollMode.Solo)]
    [InlineData("3v3", "draft", EnrollMode.Solo)]
    [InlineData("2v2", "adhoc", EnrollMode.Build)]
    public void EachTournamentAsksInItsOwnShape(string format, string source, EnrollMode mode)
        => Assert.Equal(mode, EnrollView.ModeOf(T(format, source), Me, null));

    [Fact]
    public void A_SavedTeamTournamentNeedsATeamThatCanEnter()
    {
        var t = T("3v3", "registered");
        Assert.Equal(EnrollMode.NoTeams, EnrollView.ModeOf(t, Me, null));
        Assert.Equal(EnrollMode.NoTeams, EnrollView.ModeOf(t, Me, new[] { Team("short", Me, Me, "a") }));
        Assert.Equal(EnrollMode.NoTeams, EnrollView.ModeOf(t, Me, new[] { Team("theirs", "x", "x", Me, "a") }));
        Assert.Equal(EnrollMode.Saved, EnrollView.ModeOf(t, Me, new[] { Team("ok", Me, Me, "a", "b") }));
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_ATeamThatCannotEnterStaysInViewWithItsReason()
    {
        var rows = EnrollView.TeamsOf(T("3v3", "registered"), Me, new[]
        {
            Team("short", Me, Me, "a"),
            Team("theirs", "x", "x", Me, "a"),
            Team("ok", Me, Me, "a", "b"),
        });

        Assert.Equal("ok", rows[0].Team.Id);
        Assert.True(rows[0].CanEnter);
        Assert.Equal(3, rows.Count);
        Assert.All(rows.Skip(1), r => Assert.False(r.CanEnter));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Line)));
    }

    [Fact]
    public void ADisbandedTeamIsNotOffered()
    {
        var gone = Team("gone", Me, Me, "a", "b");
        gone.Disbanded = true;
        Assert.Empty(EnrollView.TeamsOf(T("3v3", "registered"), Me, new[] { gone }));
    }

    [Theory]
    [InlineData("solo", Me, EnrollMode.Enrolled)]
    [InlineData("team", Me, EnrollMode.TeamCaptain)]
    [InlineData("team", "rio", EnrollMode.TeamMember)]
    public void SomebodyInSeesTheirEntry(string kind, string captain, EnrollMode mode)
    {
        var t = T("2v2", "adhoc");
        t.Entrants!.Add(kind == "solo" ? Entrant(kind, captain, Me) : Entrant(kind, captain, Me, "rio"));
        Assert.Equal(mode, EnrollView.ModeOf(t, Me, null));
    }

    [Fact]
    public void AnOrdinaryMemberIsToldWhoCanWithdrawTheTeam()
    {
        var t = T("2v2", "adhoc");
        var e = Entrant("team", "rio", "rio", Me);
        e.Members = new() { new() { UserId = "rio", DisplayName = "Rioplatense" }, new() { UserId = Me, DisplayName = "Gorgo" } };
        t.Entrants!.Add(e);

        var last = EnrollView.Facts(t, Me, EnrollMode.TeamMember).Last();
        Assert.Equal("EnFactLeave", last.LabelKey);
        Assert.Contains("Rioplatense", last.Text);
    }

    [Fact]
    public void WhoEnteredCannotBePicked()
    {
        var t = T("2v2", "adhoc");
        t.Entrants!.Add(Entrant("team", "x", "x", "y"));
        var gone = Entrant("team", "z", "z");
        gone.Status = "withdrawn";
        t.Entrants.Add(gone);

        var taken = EnrollView.TakenPlayers(t);
        Assert.Contains("y", taken);
        Assert.DoesNotContain("z", taken);
    }

    [Fact]
    public void TheListGoesFromWhoYouKnowToWhoSearchFinds()
    {
        var recent = new[] { new EnrollPlayer("a", "Ana", true, "2 d") };
        var online = new[] { new EnrollPlayer("a", "Ana", true), new EnrollPlayer("b", "Beto", true), new EnrollPlayer(Me, "Yo", true) };
        var found = new[] { new EnrollPlayer("c", "Betina", false) };

        var plain = EnrollView.Groups(recent, online, found, "", Me);
        Assert.Equal(new[] { "EnGroupRecent", "EnGroupOnline" }, plain.Select(g => g.TitleKey));
        Assert.Equal(new[] { "b" }, plain[1].Players.Select(p => p.Id));

        var search = EnrollView.Groups(recent, online, found, "bet", Me);
        Assert.Equal(new[] { "EnGroupOnline", "EnGroupOther" }, search.Select(g => g.TitleKey));
    }

    [Theory]
    [InlineData(3, 3, false)]
    [InlineData(2, 3, true)]
    [InlineData(4, 3, true)]
    public void TheLineupMustBeExact(int chosen, int size, bool blocked)
        => Assert.Equal(blocked, EnrollView.LineupBlock(chosen, size) != null);

    [Fact]
    public void ABuiltTeamNeedsItsMatesAndItsName()
    {
        Assert.NotNull(EnrollView.BuildBlock(0, 2, "Los Andes"));
        Assert.NotNull(EnrollView.BuildBlock(1, 2, " "));
        Assert.Null(EnrollView.BuildBlock(1, 2, "Los Andes"));
    }

    [Theory]
    [InlineData("Los Andes", "LA")]
    [InlineData("Compañía de Indias", "CI")]
    [InlineData("Kaiser", "KA")]
    public void ACrestIsTwoLetters(string name, string crest) => Assert.Equal(crest, EnrollView.CrestOf(name));

    [Fact]
    public void ACapacityTheServerDidNotSendIsNotInvented()
    {
        var t = T();
        t.Capacity = null;
        Assert.DoesNotContain("/", EnrollView.Meta(t).Replace("1v1", ""));
        Assert.Contains(EnrollView.Facts(t, Me, EnrollMode.Solo), f => f.LabelKey == "EnFactPlaces");
    }

    private static EnrollActions Actions(System.Func<object?, Task<TournamentEntryResponse>> enter, TournamentDetail? fresh = null) => new()
    {
        Enter = enter,
        Withdraw = _ => Task.CompletedTask,
        Reload = () => Task.FromResult(fresh ?? new TournamentDetail()),
        ErrorText = ex => ex.Message,
    };

    private static void Dialog(TournamentDetail t, EnrollActions actions, System.Action<EnrollDialog> body,
        IReadOnlyList<TeamSummary>? teams = null, IReadOnlyList<EnrollPlayer>? online = null)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                body(new EnrollDialog(t, Me, null, true, teams, System.Array.Empty<EnrollPlayer>(), online ?? System.Array.Empty<EnrollPlayer>(), actions));
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    [Fact]
    public void ThePrimarySaysWhatItDoes()
    {
        Dialog(T(), Actions(_ => Task.FromResult(new TournamentEntryResponse())), d =>
            Assert.Equal(Strings.Get("EnOkSolo"), d.OkButton.Content));
        Dialog(T(entry: "approval"), Actions(_ => Task.FromResult(new TournamentEntryResponse())), d =>
        {
            Assert.Equal(Strings.Get("EnOkSend"), d.OkButton.Content);
            Assert.Equal(Visibility.Visible, d.ApprovalNote.Visibility);
        });
        Dialog(T("3v3", "registered"), Actions(_ => Task.FromResult(new TournamentEntryResponse())), d =>
            Assert.Equal(Strings.Format("EnOkTeam", "andes"), d.OkButton.Content),
            new[] { Team("andes", Me, Me, "a", "b") });
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_NothingIsSentWhileSomethingIsMissing()
    {
        var sent = 0;
        Dialog(T("2v2", "adhoc"), Actions(_ =>
        {
            sent++;
            return Task.FromResult(new TournamentEntryResponse());
        }), d =>
        {
            Assert.False(d.OkButton.IsEnabled);
            Assert.Equal(Visibility.Visible, d.BlockText.Visibility);
            d.SubmitAsync().GetAwaiter().GetResult();
        });
        Assert.Equal(0, sent);
    }

    [Fact]
    public void ALostConnectionSaysYouAreNotInAndOffersARetry()
    {
        Dialog(T(), Actions(_ => throw new HttpRequestException("down")), d =>
        {
            d.SubmitAsync().GetAwaiter().GetResult();
            Assert.Equal(Visibility.Visible, d.ErrorNote.Visibility);
            Assert.Equal(Strings.Get("EnRetry"), d.OkButton.Content);
            Assert.True(d.OkButton.IsEnabled);
            Assert.False(d.Changed);
        });
    }

    [Theory]
    [InlineData("confirmed", "EnDoneTitle")]
    [InlineData("pending", "EnSentTitle")]
    [InlineData("waitlist", "EnWaitTitle")]
    public void TheWindowStaysToSayWhatHappened(string status, string title)
    {
        Dialog(T(), Actions(_ => Task.FromResult(new TournamentEntryResponse { Status = status })), d =>
        {
            d.SubmitAsync().GetAwaiter().GetResult();
            Assert.True(d.Changed);
            Assert.Equal(Strings.Get(title), d.DoneTitle.Text);
            Assert.Equal(Strings.Get("EnDone"), d.OkButton.Content);
            Assert.Equal(Visibility.Collapsed, d.CancelButton.Visibility);
        });
    }

    [Fact]
    public void AMemberGetsNoButtonTheyCannotUse()
    {
        var t = T("2v2", "adhoc");
        t.Entrants!.Add(Entrant("team", "rio", "rio", Me));
        Dialog(t, Actions(_ => Task.FromResult(new TournamentEntryResponse())), d =>
        {
            Assert.Equal(EnrollMode.TeamMember, d.Mode);
            Assert.Equal(Visibility.Collapsed, d.LeaveButton.Visibility);
            Assert.Equal(Strings.Get("EnClose"), d.OkButton.Content);
        });
    }

    [Fact]
    public void ACaptainCanWithdrawTheTeam()
    {
        var t = T("2v2", "adhoc");
        t.Entrants!.Add(Entrant("team", Me, Me, "rio"));
        Dialog(t, Actions(_ => Task.FromResult(new TournamentEntryResponse())), d =>
        {
            Assert.Equal(Visibility.Visible, d.LeaveButton.Visibility);
            Assert.Equal(Strings.Get("EnLeaveTeam"), d.LeaveButton.Content);
        });
    }
}
