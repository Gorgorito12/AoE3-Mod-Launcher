using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Private tournaments (2026-11-13). The server decides who sees one; what the launcher must get
/// right is never OFFERING "private" to a server that would make it public in silence, keeping a
/// tournament opened with its code on screen, and carrying the code to the detail and the entry.
/// </summary>
[Collection("wpf-and-language")]
public class PrivateTournamentTests
{
    // ---------------------------------------------------------------- the wire

    [Fact]
    public void TheListSaysWhetherTheServerKnowsPrivateTournaments()
    {
        var newer = JsonSerializer.Deserialize<TournamentListResponse>(
            """{ "tournaments": [ { "id": "A", "name": "A", "visibility": "private" } ], "private_tournaments": true }""")!;
        Assert.True(newer.PrivateTournaments);
        Assert.True(newer.Tournaments![0].IsPrivate);

        // An older server says nothing: no flag, and every tournament is public.
        var older = JsonSerializer.Deserialize<TournamentListResponse>(
            """{ "tournaments": [ { "id": "B", "name": "B" } ] }""")!;
        Assert.Null(older.PrivateTournaments);
        Assert.False(older.Tournaments![0].IsPrivate);
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_TheEntryCarriesTheCodeThatOpenedTheTournament()
    {
        var withBody = LiveTournamentApi.WithInviteCode(new { member_ids = new[] { "u1" } }, "K7N9P2XA");
        var node = Assert.IsType<JsonObject>(withBody);
        Assert.Equal("K7N9P2XA", (string?)node["invite_code"]);
        Assert.NotNull(node["member_ids"]);

        // A 1v1 entry sends no body at all; the code still has to travel.
        var bare = Assert.IsType<JsonObject>(LiveTournamentApi.WithInviteCode(null, "K7N9P2XA"));
        Assert.Equal("K7N9P2XA", (string?)bare["invite_code"]);

        // Without a code the body is left exactly as it was.
        var body = new { team_id = "t" };
        Assert.Same(body, LiveTournamentApi.WithInviteCode(body, null));
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public void ATournamentOpenedWithItsCodeKeepsItsRowUntilThePlayerIsIn()
    {
        var list = new TournamentListResponse { Tournaments = new() { new TournamentSummary { Id = "PUB", Name = "PUB", Status = "registration" } } };
        var open = new TournamentDetail { Id = "PRIV", Name = "PRIV", Status = "registration", Visibility = "private" };

        var ids = TournamentPage.Listed(list, open).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "PUB", "PRIV" }, ids);

        // Once the server lists it (the player entered) it appears once, not twice.
        list.Mine = new() { new TournamentSummary { Id = "PRIV", Name = "PRIV", Visibility = "private" } };
        Assert.Single(TournamentPage.Listed(list, open), t => t.Id == "PRIV");

        // A PUBLIC tournament on screen but absent from the list is not invented into it.
        var publicOpen = new TournamentDetail { Id = "GONE", Name = "GONE", Status = "finished" };
        Assert.DoesNotContain(TournamentPage.Listed(list, publicOpen), t => t.Id == "GONE");
    }

    // ---------------------------------------------------------------- the preview's server

    private static string Create(TournamentSimulator s, string visibility)
        => s.Create(JsonSerializer.SerializeToElement(new
        {
            name = "Copa privada", mod_id = "wol", format = "1v1", team_source = "solo",
            entry_mode = "open", capacity = 8, visibility,
        })).Id;

    [Fact]
    public void ThePreviewKeepsAPrivateTournamentOutOfThePublicList()
    {
        var s = new TournamentSimulator();
        var id = Create(s, "private");
        s.OpenRegistration(id);

        var list = s.List(id);
        Assert.True(list.PrivateTournaments);
        Assert.DoesNotContain(list.Tournaments!, t => t.Id == id);
        Assert.Contains(list.Mine!, t => t.Id == id && t.IsPrivate);

        var code = s.Detail(id)!.InviteCode;
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}$", code);
        Assert.Equal(id, s.FindByCode(code!.ToLowerInvariant())?.Id);
        Assert.Null(s.FindByCode("ZZZZZZZZ"));
    }

    [Fact]
    public void APublicTournamentHasNoCode()
    {
        var s = new TournamentSimulator();
        var id = Create(s, "public");
        Assert.Null(s.Detail(id)!.InviteCode);
        Assert.False(s.Detail(id)!.IsPrivate);
    }

    // ---------------------------------------------------------------- the windows

    [Fact]
    public void THE_ONE_THAT_MATTERS_PrivateIsOfferedOnlyToAServerThatKnowsIt()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var older = new CreateTournamentDialog();
            Assert.Equal(Visibility.Collapsed, older.VisibilityBlock.Visibility);

            var newer = new CreateTournamentDialog(privateSupported: true);
            Assert.Equal(Visibility.Visible, newer.VisibilityBlock.Visibility);
            Assert.Equal(2, newer.VisibilityRow.Buttons.Count());
            Assert.Equal(Strings.Get("NtWhyPublic"), newer.VisibilityWhy.Text);
            older.Close();
            newer.Close();
        });
        Assert.Null(error);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TheCodeDialogLoadsAndWaitsForACode(string language)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            Strings.SetLanguage(language);
            try
            {
                DialogXamlTests.EnsureResources();
                var dlg = new JoinTournamentCodeDialog(_ => System.Threading.Tasks.Task.FromResult<(string?, string?)>((null, null)));
                Assert.False(dlg.PrimaryAction.IsEnabled);
                dlg.CodeEntry.Text = " k7n9 p2xa ";
                Assert.True(dlg.PrimaryAction.IsEnabled);
                Assert.Equal("K7N9P2XA", dlg.EnteredCode);
                Assert.Equal(Strings.Get("TpJoinCodeOpen"), dlg.PrimaryAction.Content);
                dlg.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }
}
