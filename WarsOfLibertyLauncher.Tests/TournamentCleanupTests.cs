using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Ended tournaments, deleting them, and the faces in a bracket (2026-11-11). The refusals are the
/// point: a tournament with a game played, a live one, somebody else's, and one from a server that
/// cannot delete never offer Delete.
/// </summary>
[Collection("wpf-and-language")]
public class TournamentCleanupTests
{
    private const string Me = TournamentDemoData.MeUserId;

    private static TournamentDetail Detail(string status, bool? hasGames, string? owner = Me) => new()
    {
        Id = "t", Name = "Copa", Status = status, OwnerUserId = owner, Format = "1v1", HasGames = hasGames,
    };

    private static void BothLanguages(Action body)
    {
        var previous = Strings.Language;
        try
        {
            foreach (var lang in new[] { Strings.LangEn, Strings.LangEs })
            {
                Strings.SetLanguage(lang);
                body();
            }
        }
        finally { Strings.SetLanguage(previous); }
    }

    [Fact]
    public void THE_ONE_THAT_MATTERS_OnlyTheOwnerOfAnUnplayedEndedOneMayDelete()
    {
        Assert.True(TournamentPermissions.CanDelete(Detail("cancelled", false), Me));
        Assert.True(TournamentPermissions.CanDelete(Detail("abandoned", false), Me));
        Assert.True(TournamentPermissions.CanDelete(Detail("draft", false), Me));

        Assert.False(TournamentPermissions.CanDelete(Detail("cancelled", true), Me), "a game was played");
        Assert.False(TournamentPermissions.CanDelete(Detail("cancelled", null), Me), "a server that cannot delete");
        Assert.False(TournamentPermissions.CanDelete(Detail("cancelled", false, owner: "someone"), Me), "not mine");
        foreach (var live in new[] { "registration", "ready", "running", "finished" })
            Assert.False(TournamentPermissions.CanDelete(Detail(live, false), Me), live);
    }

    [Fact]
    public void EveryStatusHasWordsOnItsListRow() => BothLanguages(() =>
    {
        // The row of a cancelled or abandoned tournament used to read "· 1v1": no status at all.
        foreach (var status in new[] { "draft", "registration", "ready", "running", "finished", "cancelled", "abandoned" })
        {
            var list = new TournamentListResponse { Tournaments = new() { new TournamentSummary { Id = status, Name = status, Status = status, Format = "1v1", Capacity = 8 } } };
            var row = TournamentPage.Rows(list, Me, null, "all").Single();
            Assert.False(row.Sub.StartsWith(" · ", StringComparison.Ordinal), $"{status}: '{row.Sub}'");
            Assert.NotEqual("1v1", row.Sub);
        }
    });

    [Fact]
    public void TheEndedCardOffersDeleteOnlyWhenItCan() => BothLanguages(() =>
    {
        var mine = TournamentPage.Call(Detail("cancelled", false), Me);
        Assert.Equal(TournamentPrimary.Delete, mine.Primary);
        Assert.False(string.IsNullOrEmpty(mine.Title));

        var played = TournamentPage.Call(Detail("cancelled", true), Me);
        Assert.Equal(TournamentPrimary.None, played.Primary);
        Assert.Equal(Strings.Get("TpCallEndedGamesTitle"), played.Title);

        var someoneElses = TournamentPage.Call(Detail("abandoned", false, owner: "x"), Me);
        Assert.Equal(TournamentPrimary.None, someoneElses.Primary);
        Assert.Equal(Strings.Get("TpCallAbandonedTitle"), someoneElses.Title);

        var oldServer = TournamentPage.Call(Detail("cancelled", null), Me);
        Assert.Equal(TournamentPrimary.None, oldServer.Primary);
    });

    [Fact]
    public void AnUnopenedDraftOffersDeleteBesideOpening()
    {
        var draft = Detail("draft", false);
        draft.Capacity = 8;
        var call = TournamentPage.Call(draft, Me);
        Assert.Equal(TournamentPrimary.OpenRegistration, call.Primary);
        Assert.Equal(TournamentPrimary.Delete, call.Secondary);

        draft.HasGames = null;
        Assert.NotEqual(TournamentPrimary.Delete, TournamentPage.Call(draft, Me).Secondary);
    }

    [Fact]
    public void TheMineListJoinsTheRowsOnce()
    {
        var shared = new TournamentSummary { Id = "a", Name = "a", Status = "cancelled", OwnerUserId = Me };
        var list = new TournamentListResponse
        {
            Tournaments = new() { shared },
            Mine = new() { shared, new TournamentSummary { Id = "b", Name = "b", Status = "finished" } },
        };
        Assert.Equal(new[] { "a", "b" }, TournamentPage.Listed(list).Select(t => t.Id).ToArray());
        Assert.Equal(2, TournamentPage.Rows(list, Me, null, "all").Count);
    }

    [Fact]
    public void TheSimulatorDeletesLikeTheServer()
    {
        var s = new TournamentSimulator();
        var body = JsonSerializer.SerializeToElement(new { name = "Prueba", mod_id = "wol", format = "1v1", team_source = "solo", entry_mode = "open", capacity = 8 });
        var draft = s.Create(body).Id;
        Assert.True(TournamentPermissions.CanDelete(s.Detail(draft), s.ViewerOf(draft)));
        s.Delete(draft);
        Assert.False(s.Contains(draft));

        s.ViewerChoice = TournamentPreviewViewer.Organiser;
        Assert.Throws<LobbyApiException>(() => s.Delete(TournamentDemoData.FinishedId));   // games were played
        Assert.Throws<LobbyApiException>(() => s.Delete(TournamentDemoData.RunningId));    // live
        Assert.True(s.Contains(TournamentDemoData.FinishedId));
    }

    [Fact]
    public void ASoloEntrantShowsItsPhotoAndATeamKeepsItsCrest()
    {
        var solo = new TournamentEntrant { Members = new() { new TournamentEntrantMember { UserId = "u", AvatarUrl = "https://cdn.discordapp.com/embed/avatars/1.png" } } };
        Assert.Equal("https://cdn.discordapp.com/embed/avatars/1.png", MultiplayerTab.PhotoOf(solo, team: false));
        Assert.Null(MultiplayerTab.PhotoOf(solo, team: true));
        Assert.Null(MultiplayerTab.PhotoOf(new TournamentEntrant(), team: false));

        // A real picture on disk: a test must not depend on reaching Discord's CDN.
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aoe3ml-face-{Guid.NewGuid():N}.png");
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var pixels = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pixels));
            using (var stream = System.IO.File.Create(file)) encoder.Save(stream);

            var plain = MultiplayerTab.LetterWithPhoto(new TextBlock { Text = "A" }, null, 20, 10);
            Assert.IsType<TextBlock>(plain);
            var withPhoto = MultiplayerTab.LetterWithPhoto(new TextBlock { Text = "A" }, new Uri(file).AbsoluteUri, 20, 10);
            var grid = Assert.IsType<Grid>(withPhoto);
            // The letter stays underneath, so a photo that fails to load leaves the disc as it was.
            Assert.IsType<TextBlock>(grid.Children[0]);
            Assert.IsType<Rectangle>(grid.Children[1]);
        });
        try { System.IO.File.Delete(file); } catch { /* best-effort */ }
        Assert.Null(error);
    }

    [Fact]
    public void TheSamplesWearFacesAndPlacesButSomebodyIsStillBeingPlaced()
    {
        var s = new TournamentSimulator();
        var t = s.Detail(TournamentDemoData.Big32Id)!;
        var players = t.Entrants!.SelectMany(e => e.Members ?? new()).ToList();
        Assert.Contains(players, m => m.AvatarUrl != null);
        Assert.Contains(players, m => m.AvatarUrl == null);
        Assert.Contains(players, m => m.LadderRank is > 0);
        Assert.Contains(players, m => m.LadderRank == null);
    }
}
