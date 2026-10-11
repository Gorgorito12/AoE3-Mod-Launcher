using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// TEAM tournaments, end to end on the launcher's side.
///
/// <para>Before this, no 2v2 or 3v3 tournament could be played from the launcher: the Enter
/// button posted an empty body (refused for every registered and ad-hoc tournament), a draft
/// could never be turned into teams, and there was no screen for teams at all. What is pinned
/// here is each of the three ways in, the server's new refusals as the preview reproduces them,
/// and the windows that collect the answers.</para>
/// </summary>
[Collection("wpf-and-language")]
public class TeamTournamentTests
{
    private const string Me = TournamentDemoData.MeUserId;

    private static string Create(TournamentSimulator s, string format, string source)
    {
        var id = s.Create(JsonSerializer.SerializeToElement(new
        {
            name = "Copa de equipos",
            mod_id = "wol",
            format,
            team_source = source,
            entry_mode = "open",
            capacity = 8,
        })).Id;
        s.OpenRegistration(id);
        return id;
    }

    private static JsonElement Body(object o) => JsonSerializer.SerializeToElement(o);

    private static LobbyApiException Refusal(Action act)
        => Assert.Throws<LobbyApiException>(act);

    // ---------------------------------------------------------------- the rules, ported

    [Fact]
    public void ChooseRegisteredLineup_MirrorsTheServer()
    {
        var team = new[] { "cap", "b", "c", "reserve" };
        Assert.Equal(new[] { "cap", "b" }, TournamentRules.ChooseRegisteredLineup(team, null, "2v2").MemberIds);
        Assert.Equal(new[] { "cap", "reserve" },
            TournamentRules.ChooseRegisteredLineup(team, new[] { "cap", "reserve" }, "2v2").MemberIds);
        Assert.Equal("not_team_member",
            TournamentRules.ChooseRegisteredLineup(team, new[] { "cap", "stranger" }, "2v2").Refusal);
    }

    [Fact]
    public void IncompleteRosters_ADraftPlayerStillAloneBlocksTheDraw()
    {
        var incomplete = TournamentRules.IncompleteRosters("2v2", new (string, IReadOnlyList<string>)[]
        {
            ("team", new[] { "a", "b" }),
            ("alone", new[] { "c" }),
        });
        Assert.Equal(new[] { "alone" }, incomplete);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_MergeRefusal_AWithdrawnEntrantCannotBeMergedTwice()
    {
        var free = new TournamentEntrant { Id = "x", Kind = "solo", Status = "confirmed" };
        var merged = new TournamentEntrant { Id = "y", Kind = "solo", Status = "withdrawn" };
        Assert.Equal("not_available", TournamentRules.MergeRefusal("2v2", new[] { "x", "y" }, new[] { free, merged }));
        Assert.Null(TournamentRules.MergeRefusal("2v2", new[] { "x", "x2" },
            new[] { free, new TournamentEntrant { Id = "x2", Kind = "solo", Status = "waitlist" } }));
        Assert.Equal("wrong_count", TournamentRules.MergeRefusal("3v3", new[] { "x" }, new[] { free }));
        Assert.Equal("unknown_entrant", TournamentRules.MergeRefusal("2v2", new[] { "x", "z" }, new TournamentEntrant?[] { free, null }));
    }

    // ---------------------------------------------------------------- entering, as the server would answer

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnEmptyEntryToARegisteredTeamTournamentIsStillRefused()
    {
        // What every launcher before this sent. The server refuses it, and so does the preview.
        var s = new TournamentSimulator();
        var id = Create(s, "2v2", "registered");
        Assert.Equal("bad_request", Refusal(() => s.Enter(id)).Code);
    }

    [Fact]
    public void ACaptainEntersWithHisTeam_AndMayChooseWhoPlays()
    {
        var s = new TournamentSimulator();
        var team = s.MyTeams().Teams!.Single(t => t.OwnerUserId == Me);
        Assert.Equal(3, team.Members!.Count);
        var reserve = team.Members![2].UserId;

        var id = Create(s, "2v2", "registered");
        var entry = s.Enter(id, Body(new { team_id = team.Id, member_ids = new[] { Me, reserve } }));
        Assert.Equal(2, entry.RosterSize);
        Assert.Equal(new[] { Me, reserve }, entry.Members);

        var mine = s.Detail(id)!.Entrants!.Single(e => e.Id == entry.EntrantId);
        Assert.Equal("team", mine.Kind);
        Assert.Equal(team.Name, mine.DisplayName);
        Assert.Equal(new[] { Me, reserve }, mine.MemberIds);
    }

    [Fact]
    public void WithoutAChoiceTheRegisteredLineUpIsCaptainFirst()
    {
        var s = new TournamentSimulator();
        var team = s.MyTeams().Teams!.Single(t => t.OwnerUserId == Me);
        var id = Create(s, "2v2", "registered");
        var entry = s.Enter(id, Body(new { team_id = team.Id, member_ids = (string[]?)null }));
        Assert.Equal(team.Members!.Take(2).Select(m => m.UserId), entry.Members);
    }

    [Fact]
    public void AChoiceNamingSomebodyOutsideTheTeamIsRefusedWithItsReason()
    {
        var s = new TournamentSimulator();
        var team = s.MyTeams().Teams!.Single(t => t.OwnerUserId == Me);
        var id = Create(s, "2v2", "registered");
        var ex = Refusal(() => s.Enter(id, Body(new { team_id = team.Id, member_ids = new[] { Me, "nobody" } })));
        Assert.Equal("roster_invalid", ex.Code);
        Assert.Equal("not_team_member", TournamentRefusals.Text(ex.Details, "reason"));
        Assert.Equal(Strings.Get("MpTournamentErrRosterNotMember"), MultiplayerTab.TournamentErrorText(ex));
    }

    [Fact]
    public void ACaptainEntersAnAdhocLineUp_AndTheCaptainMustBeInIt()
    {
        var s = new TournamentSimulator();
        var id = Create(s, "2v2", "adhoc");

        Assert.Equal("bad_request",
            Refusal(() => s.Enter(id, Body(new { member_ids = new[] { "sim-mate-1", "sim-mate-2" }, display_name = "X" }))).Code);

        var entry = s.Enter(id, Body(new { member_ids = new[] { Me, "sim-mate-1" }, display_name = "Los Dos" }));
        var e = s.Detail(id)!.Entrants!.Single(x => x.Id == entry.EntrantId);
        Assert.Equal("Los Dos", e.DisplayName);
        Assert.Equal(new[] { Me, "sim-mate-1" }, e.MemberIds);
    }

    // ---------------------------------------------------------------- draft

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheOrganiserFormsDraftTeamsBeforeTheBracketCanBeDrawn()
    {
        var s = new TournamentSimulator();
        var id = Create(s, "2v2", "draft");
        s.Enter(id);
        s.SignUp(id, 3);
        s.CloseRegistration(id);

        // Four players alone: no bracket can be drawn from them.
        var ex = Refusal(() => s.Seed(id));
        Assert.Equal("tournament_roster_incomplete", ex.Code);
        Assert.Equal(4, TournamentRefusals.Count(ex.Details, "entrant_ids"));
        Assert.Contains("4", MultiplayerTab.TournamentErrorText(ex));

        var t = s.Detail(id)!;
        Assert.True(TournamentPermissions.CanFormDraftTeams(t, Me));
        Assert.All(t.Entrants!, e => Assert.True(TournamentPermissions.NeedsTeam(t, e)));
        var solos = MultiplayerTab.DraftPlayersWithoutTeam(t).Select(e => e.Id).ToList();
        Assert.Equal(4, solos.Count);

        s.MergeEntrants(id, new[] { solos[0], solos[1] }, "Equipo Uno");
        // A player already folded into a team cannot be folded into a second one.
        Assert.Equal("entrant_not_available", Refusal(() => s.MergeEntrants(id, new[] { solos[0], solos[2] }, null)).Code);
        s.MergeEntrants(id, new[] { solos[2], solos[3] }, null);

        t = s.Detail(id)!;
        Assert.Empty(MultiplayerTab.DraftPlayersWithoutTeam(t));
        var merged = t.Entrants!.Single(e => e.Id == solos[0]);
        Assert.Equal("Equipo Uno", TournamentPermissions.MergedInto(t, merged));

        s.Seed(id);
        s.Start(id);
        Assert.Equal("running", s.Detail(id)!.Status);
        Assert.False(TournamentPermissions.CanFormDraftTeams(s.Detail(id), Me));
    }

    [Fact]
    public void CanStartIsFalseWhileAnEntrantLacksAFullLineUp()
    {
        var t = new TournamentDetail
        {
            Id = "t", OwnerUserId = Me, Format = "2v2", TeamSource = "draft", Status = "ready",
            Entrants = new List<TournamentEntrant>
            {
                new() { Id = "a", Kind = "team", Status = "confirmed", Seed = 1, MemberIds = new() { "1", "2" } },
                new() { Id = "b", Kind = "solo", Status = "confirmed", Seed = 2, MemberIds = new() { "3" } },
            },
        };
        Assert.False(TournamentPermissions.CanStart(t, Me));
        t.Entrants[1].MemberIds!.Add("4");
        Assert.True(TournamentPermissions.CanStart(t, Me));
    }

    [Fact]
    public void RosterSizeOf_PrefersTheServersNumber_AndFallsBackToTheFormat()
    {
        Assert.Equal(3, TournamentPermissions.RosterSizeOf(new TournamentSummary { Format = "3v3" }));
        Assert.Equal(2, TournamentPermissions.RosterSizeOf(new TournamentSummary { Format = "3v3", RosterSize = 2 }));
        Assert.Equal(1, TournamentPermissions.RosterSizeOf(new TournamentSummary { Format = "1v1" }));
    }

    [Fact]
    public void ARosterTheServerDidNotSendIsNeverCalledShort()
    {
        var t = new TournamentDetail { Format = "2v2" };
        Assert.False(TournamentPermissions.NeedsTeam(t, new TournamentEntrant { Status = "confirmed", MemberIds = null }));
    }

    // ---------------------------------------------------------------- teams

    [Fact]
    public void Teams_CreateInviteAcceptLeaveDisband()
    {
        var s = new TournamentSimulator();
        var start = s.MyTeams();
        Assert.Single(start.Invites!);

        // The waiting invitation, accepted: I am now in that team.
        s.AcceptTeamInvite(start.Invites![0].Id);
        var after = s.MyTeams();
        Assert.Empty(after.Invites!);
        var joined = after.Teams!.Single(t => t.Id == start.Invites[0].TeamId);
        Assert.Contains(joined.Members!, m => m.UserId == Me);

        // Leaving it again.
        s.RemoveTeamMember(joined.Id, Me);
        Assert.DoesNotContain(s.MyTeams().Teams!, t => t.Id == joined.Id);

        // A new team: mine, with me as its captain, and an invitation that waits.
        var created = s.CreateTeam(Body(new { name = "Nuevos", tag = "NEW" }));
        Assert.Equal(Me, created.OwnerUserId);
        s.InviteToTeam(created.Id, "sim-mate-1");
        Assert.Equal("bad_request", Refusal(() => s.InviteToTeam(created.Id, Me)).Code);
        Assert.Equal("not_found", Refusal(() => s.InviteToTeam(created.Id, "nobody-at-all")).Code);

        s.DisbandTeam(created.Id);
        Assert.DoesNotContain(s.MyTeams().Teams!, t => t.Id == created.Id);
    }

    [Fact]
    public void SearchFindsPlayersByName_NeverTheOneLooking()
    {
        var s = new TournamentSimulator();
        var found = s.SearchPlayers("arca").Users!;
        Assert.Contains(found, u => u.UserId == "sim-mate-1");
        Assert.DoesNotContain(found, u => u.UserId == Me);
        Assert.Empty(s.SearchPlayers("a").Users!);
    }

    // ---------------------------------------------------------------- the room title

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheRoomTitleNeverCutsAnEntrantsName()
    {
        var s = new TournamentSimulator();
        var t = s.Detail(TournamentDemoData.RunningId)!;
        var m = t.Matches!.First(x => !string.IsNullOrEmpty(x.Entrant1Id) && !string.IsNullOrEmpty(x.Entrant2Id));
        var e2 = t.Entrants!.Single(e => e.Id == m.Entrant2Id).DisplayName!;
        var title = s.RoomTitle(t, m);
        Assert.EndsWith(e2, title);
        Assert.True(title.Length <= 160);
    }

    // ---------------------------------------------------------------- what the screen says

    [Fact]
    public void EveryPendingReportStateHasItsOwnSentence_AndAnUnknownOneSaysNothing()
    {
        foreach (var state in new[] { "awaiting_confirmation", "teams_mismatch", "lineups_do_not_match", "undecided" })
        {
            var key = TournamentRefusals.PendingReportKey(state);
            Assert.NotNull(key);
            Assert.NotEqual(key, Strings.Get(key!));
        }
        Assert.Null(TournamentRefusals.PendingReportKey("something_new"));
        Assert.Null(TournamentRefusals.PendingReportKey(null));
    }

    [Fact]
    public void RefusalDetailsReadTheSameFromTheServerAndFromThePreview()
    {
        var fromServer = new Dictionary<string, object?>
        {
            ["reason"] = JsonSerializer.SerializeToElement("already_entered"),
            ["entrant_ids"] = JsonSerializer.SerializeToElement(new[] { "a", "b", "c" }),
        };
        var fromPreview = new Dictionary<string, object?>
        {
            ["reason"] = "already_entered",
            ["entrant_ids"] = new List<string> { "a", "b", "c" },
        };
        foreach (var d in new[] { fromServer, fromPreview })
        {
            Assert.Equal("MpTournamentErrRosterTaken", TournamentRefusals.RosterReasonKey(TournamentRefusals.Text(d, "reason")));
            Assert.Equal(3, TournamentRefusals.Count(d, "entrant_ids"));
        }
    }

    [Fact]
    public void ATournamentRoomSaysItsSidesAreLocked_OnlyWhenTheServerSaysSo()
    {
        Assert.True(JsonSerializer.Deserialize<WsRoomState>("{\"lobby_id\":\"x\",\"teams_locked\":true}")!.TeamsLocked);
        Assert.Null(JsonSerializer.Deserialize<WsRoomState>("{\"lobby_id\":\"x\"}")!.TeamsLocked);
    }

    // ---------------------------------------------------------------- the windows

    [Fact]
    public void ThePickerConfirmsOnlyAtExactlyTheRequiredCount_AndTheCaptainCannotBeUnticked()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var language in new[] { "es", "en" })
            {
                var previous = Strings.Language;
                Strings.SetLanguage(language);
                try
                {
                    var dlg = new PlayerPickerDialog(
                        "t", "intro", "PLAYERS",
                        new[]
                        {
                            new PickCandidate("me", "Gorgo", Locked: true),
                            new PickCandidate("a", "Ana"),
                            new PickCandidate("b", "Luis"),
                            new PickCandidate("c", "Pedro", Disabled: true, DisabledReason: "ya está"),
                        },
                        required: 3,
                        okLabel: "OK",
                        nameLabel: "NAME");

                    Assert.False(dlg.CanConfirm);
                    dlg.Toggle("me", false);              // the captain stays
                    dlg.Toggle("c", true);                // a disabled one is not taken
                    dlg.Toggle("a", true);
                    Assert.False(dlg.CanConfirm);
                    dlg.Toggle("b", true);
                    Assert.False(dlg.CanConfirm);         // three, but no name yet
                    dlg.NameEntry.Text = "Los Tres";
                    Assert.True(dlg.CanConfirm);
                    dlg.Toggle("b", false);
                    Assert.False(dlg.CanConfirm);
                    Assert.Contains(Strings.Format("MpPickCount", 2, 3), dlg.CountText.Text);
                    dlg.Close();
                }
                finally { Strings.SetLanguage(previous); }
            }
        });
        Assert.Null(error);
    }

    [Fact]
    public void ChoosingExactlyOneIsARadio()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var dlg = new PlayerPickerDialog("t", "intro", "TEAMS",
                new[] { new PickCandidate("x", "X"), new PickCandidate("y", "Y") },
                required: 1, okLabel: "OK");
            dlg.Toggle("x", true);
            dlg.Toggle("y", true);
            Assert.True(dlg.CanConfirm);
            dlg.Close();
        });
        Assert.Null(error);
    }

    [Fact]
    public void TheNewTeamDialogLoads_AndWantsANameOfTwo()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var dlg = new CreateTeamDialog();
            Assert.False(dlg.CanConfirm);
            dlg.NameEntry.Text = "A";
            Assert.False(dlg.CanConfirm);
            dlg.NameEntry.Text = "AB";
            Assert.True(dlg.CanConfirm);
            dlg.Close();
        });
        Assert.Null(error);
    }

    [Fact]
    public void MyTeamsShowsTheInvitationAndMyTeam_InBothLanguages()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var language in new[] { "es", "en" })
            {
                var previous = Strings.Language;
                Strings.SetLanguage(language);
                try
                {
                    var api = new PreviewTournamentApi(new TournamentSimulator(), () => null, null);
                    var w = new MyTeamsWindow(api, Me, () => Array.Empty<PickCandidate>(), null);
                    w.LoadAsync().GetAwaiter().GetResult();
                    Assert.NotNull(w.Data);
                    var texts = Logical(w.ContentPanel).OfType<TextBlock>().Select(t => t.Text).ToList();
                    Assert.Contains(Strings.Get("MpTeamsInvitesHeading"), texts);
                    Assert.Contains(texts, t => t.StartsWith("Los Libertadores", StringComparison.Ordinal));
                    Assert.Single(Logical(w.ContentPanel).OfType<FrameworkElement>(), e => Equals(e.Tag, "TeamCard"));
                    w.Close();
                }
                finally { Strings.SetLanguage(previous); }
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// A team match that WAS played and has not moved says why, in the bar of the selected tie:
    /// waiting for the other side's confirmation. It used to just sit there undecided.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_APlayedTeamMatchThatHasNotMovedSaysWhy()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var language in new[] { "es", "en" })
            {
                var previous = Strings.Language;
                Strings.SetLanguage(language);
                try
                {
                    var tab = new MultiplayerTab();
                    tab.ShowDemoTournaments("teams");
                    var mid = tab.SelectedBracketMatchId;
                    Assert.NotNull(mid);
                    Assert.DoesNotContain(Logical(tab.TournamentDetailPanel).OfType<FrameworkElement>(),
                        e => Equals(e.Tag, MultiplayerTab.PendingReportTag));

                    tab.TournamentPreview.ReportAwaitingConfirmation(TournamentDemoData.TeamsId, mid!);
                    tab.ShowDemoTournaments("teams");

                    var note = Logical(tab.TournamentDetailPanel).OfType<TextBlock>()
                        .Single(e => Equals(e.Tag, MultiplayerTab.PendingReportTag));
                    Assert.Equal(Strings.Get("MpTournamentReportAwaiting"), note.Text);
                }
                finally { Strings.SetLanguage(previous); }
            }
        });
        Assert.Null(error);
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var deeper in Logical(child)) yield return deeper;
        }
    }
}
