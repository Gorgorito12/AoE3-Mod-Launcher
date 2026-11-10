using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The Tournaments page (design 80). <see cref="TournamentPage"/> decides what it says and where
/// things go; the geometry is arithmetic and the wording is rules, so most of this needs no window.
/// </summary>
[Collection("wpf-and-language")]
public class TournamentPageTests
{
    private const string Me = TournamentDemoData.MeUserId;

    private static void Spanish(Action body)
    {
        var previous = Strings.Language;
        Strings.SetLanguage(Strings.LangEs);
        try { body(); }
        finally { Strings.SetLanguage(previous); }
    }

    private static void OnSta(Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() => Spanish(body));
        Assert.Null(error);
    }

    private static TournamentSummary Summary(string id, string status, string? owner = null, int? pending = null) => new()
    {
        Id = id, Name = id, Status = status, OwnerUserId = owner, Format = "1v1", PendingCount = pending,
    };

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Logical(child)) yield return below;
        }
    }

    [Fact]
    public void TheListGroupsPlayingThenOrganisingThenTheRest_AskingFirst() => Spanish(() =>
    {
        var list = new TournamentListResponse
        {
            Tournaments = new()
            {
                Summary("other", "running"),
                Summary("mine", "registration", Me, pending: 3),
                Summary("quiet", "running", Me),
                Summary("in", "running"),
                Summary("turn", "running"),
            },
            MyEntries = new() { new() { TournamentId = "in" }, new() { TournamentId = "turn", Alert = "play" } },
        };
        var rows = TournamentPage.Rows(list, Me, null, "all");
        Assert.Equal(new[] { "turn", "in", "mine", "quiet", "other" }, rows.Select(r => r.Summary.Id));
        Assert.Equal(Strings.Get("TpAlertPlay"), rows[0].Alert);
        Assert.Equal(Strings.Format("TpAlertRequests", 3), rows[2].Alert);
        Assert.True(rows[2].AlertIsRequests);
        Assert.True(rows[2].Mine);
    });

    /// <summary>A server that says nothing about entries still knows the tournament on screen.</summary>
    [Fact]
    public void WithoutMyEntriesOnlyTheOpenOneIsKnown() => Spanish(() =>
    {
        var open = TournamentDemoData.Running();
        var list = new TournamentListResponse { Tournaments = new() { open, Summary("other", "running") } };
        var rows = TournamentPage.Rows(list, Me, open, "all");
        Assert.Equal(TournamentRowGroup.Playing, rows[0].Group);
        Assert.Equal(Strings.Get("TpAlertPlay"), rows[0].Alert);
        Assert.Equal(TournamentRowGroup.Others, rows[1].Group);
    });

    [Fact]
    public void AFilterKeepsOneStatus() => Spanish(() =>
    {
        var list = new TournamentListResponse { Tournaments = new() { Summary("a", "running"), Summary("b", "finished") } };
        Assert.Equal("b", Assert.Single(TournamentPage.Rows(list, Me, null, "finished")).Summary.Id);
    });

    [Theory]
    [InlineData("draft", 0)]
    [InlineData("registration", 1)]
    [InlineData("ready", 2)]
    [InlineData("running", 3)]
    public void TheStepBeingLivedIsTheOneMarked(string status, int now) => Spanish(() =>
    {
        var steps = TournamentPage.Steps(status);
        Assert.Equal(5, steps.Count);
        Assert.Equal(TournamentStepState.Now, steps[now].State);
        Assert.All(steps.Take(now), s => Assert.Equal(TournamentStepState.Done, s.State));
        Assert.All(steps.Skip(now + 1), s => Assert.Equal(TournamentStepState.Next, s.State));
    });

    [Fact]
    public void AFinishedTournamentHasEveryStepDoneAndACancelledOneNone() => Spanish(() =>
    {
        Assert.All(TournamentPage.Steps("finished"), s => Assert.Equal(TournamentStepState.Done, s.State));
        Assert.Empty(TournamentPage.Steps("cancelled"));
    });

    [Fact]
    public void TheMetaLineSaysPlacesEnteredRoundAndOrganiser() => Spanish(() =>
    {
        var t = TournamentDemoData.Big32();
        Assert.Equal("1v1 · 32 plazas · 29 inscritos · ronda 2 de 5 · organiza Kaiser", TournamentPage.Meta(t, Me));
    });

    [Fact]
    public void ThePrimaryIsTheFurthestMove_NeverReopeningAClosedList() => Spanish(() =>
    {
        var t = TournamentDemoData.Registration();
        Assert.Equal(TournamentPrimary.CloseRegistration, TournamentPage.Call(t, t.OwnerUserId).Primary);

        t.Status = "ready";
        foreach (var e in t.Entrants!.Where(x => x.Status == "confirmed")) e.Seed = null;
        Assert.Equal(TournamentPrimary.Seed, TournamentPage.Call(t, t.OwnerUserId).Primary);

        int seed = 1;
        foreach (var e in t.Entrants!.Where(x => x.Status == "confirmed")) e.Seed = seed++;
        Assert.Equal(TournamentPrimary.Start, TournamentPage.Call(t, t.OwnerUserId).Primary);
    });

    [Fact]
    public void MyMatchOutranksEverythingAndSaysWhoIsNext() => Spanish(() =>
    {
        var t = TournamentDemoData.Big32();
        var call = TournamentPage.Call(t, Me);
        Assert.Equal(TournamentPrimary.OpenRoom, call.Primary);
        Assert.Equal(Strings.Get("MpTournamentPlayMyMatch"), call.PrimaryText);
        Assert.Equal(Strings.Format("TpCallPlayKicker", "OCTAVOS"), call.Kicker);
        Assert.Equal("en Cuartos contra Aluclown.", call.Next);
    });

    [Fact]
    public void NobodyWithNothingToDoGetsACard() => Spanish(() =>
    {
        var call = TournamentPage.Call(TournamentDemoData.Big32(), "a-stranger");
        Assert.Equal(TournamentPrimary.None, call.Primary);
        Assert.Null(call.Kicker);
        Assert.Null(call.Title);
    });

    [Fact]
    public void AFinishedTournamentNamesItsChampion()
        => Assert.True(TournamentPage.Call(TournamentDemoData.Finished(), Me).Champion);

    [Fact]
    public void ACardSitsMidwayBetweenTheTwoThatFeedIt()
    {
        var t = TournamentDemoData.Big32();
        var sheet = TournamentPage.Sheet(t);
        double Mid(int round, int position)
        {
            var box = sheet.Cards.Single(b => b.Match.Round == round && b.Match.Position == position);
            return box.Y + box.Height / 2;
        }
        Assert.Equal(31, sheet.Cards.Count);
        foreach (var m in t.Matches!.Where(x => x.Round > 1))
            Assert.Equal((Mid(m.Round - 1, 2 * m.Position) + Mid(m.Round - 1, 2 * m.Position + 1)) / 2, Mid(m.Round, m.Position), 3);
    }

    /// <summary>A full draw of the largest size the design offers, every side known.</summary>
    private static TournamentDetail Draw64()
    {
        var t = TournamentDemoData.Big32();
        t.Capacity = 64;
        t.Entrants = Enumerable.Range(1, 64).Select(i => new TournamentEntrant
        {
            Id = $"e{i}", Kind = "solo", DisplayName = $"Jugador {i}", Seed = i, Status = "confirmed",
            CaptainUserId = $"u{i}", MemberIds = new List<string> { $"u{i}" },
        }).ToList();
        var matches = new List<TournamentMatch>();
        for (int round = 1, count = 32; count >= 1; round++, count /= 2)
            for (int position = 0; position < count; position++)
                matches.Add(new TournamentMatch
                {
                    Id = $"r{round}p{position}", Round = round, Position = position, Status = "pending",
                    Entrant1Id = round == 1 ? $"e{2 * position + 1}" : null,
                    Entrant2Id = round == 1 ? $"e{2 * position + 2}" : null,
                });
        t.Matches = matches;
        return t;
    }

    [Fact]
    public void ASixtyFourDrawHasSixRoundsAndNoCardOnAnother()
    {
        var sheet = TournamentPage.Sheet(Draw64());
        Assert.Equal(63, sheet.Cards.Count);
        Assert.Equal(6, sheet.Cards.Select(b => b.Match.Round).Distinct().Count());
        foreach (var round in sheet.Cards.GroupBy(b => b.Match.Round))
        {
            var column = round.OrderBy(b => b.Y).ToList();
            Assert.Single(column.Select(b => b.X).Distinct());
            for (int i = 1; i < column.Count; i++)
                Assert.True(column[i].Y >= column[i - 1].Y + column[i - 1].Height);
        }
        Assert.True(sheet.Width > 0 && sheet.Height > 0);
    }

    [Fact]
    public void ASixtyFourDrawBuildsOnceAndTheZoomKeepsTheSameCards()
    {
        OnSta(() =>
        {
            var tab = new MultiplayerTab();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var content = (FrameworkElement)typeof(MultiplayerTab).GetMethod("BuildTournamentContent", flags)!.Invoke(tab, new object?[] { Draw64(), null })!;

            Canvas Sheet() => Logical(content).OfType<Canvas>().OrderByDescending(c => c.Children.Count).First();
            var canvas = Sheet();
            Assert.True(canvas.Children.Count >= 63);
            var first = canvas.Children[0];

            typeof(MultiplayerTab).GetMethod("SetBracketZoom", flags)!.Invoke(tab, new object[] { 60d });
            Assert.Same(canvas, Sheet());
            Assert.Same(first, canvas.Children[0]);
        });
    }

    [Fact]
    public void ARoundsColumnStartsWhereTheHeaderSaysAndFourLinesJoinEachCard()
    {
        var sheet = TournamentPage.Sheet(TournamentDemoData.Big32());
        Assert.Equal(TournamentPage.Pad, sheet.Cards.Where(b => b.Match.Round == 1).Min(b => b.X));
        Assert.Equal(TournamentPage.Pad + 2 * (TournamentPage.ColumnWidthSolo + TournamentPage.Gutter), sheet.Cards.Where(b => b.Match.Round == 3).Min(b => b.X));
        Assert.Equal((31 - 16) * 4, sheet.Lines.Count);
    }

    [Fact]
    public void ATeamDrawUsesWiderTallerCards()
    {
        var sheet = TournamentPage.Sheet(TournamentDemoData.Teams());
        Assert.All(sheet.Cards, b => Assert.Equal(TournamentPage.ColumnWidthTeam, b.Width));
        Assert.Equal(TournamentPage.CardHeightTeam, sheet.CardHeight);
    }

    [Fact]
    public void AByeIsHalfACard()
    {
        var bye = TournamentPage.Sheet(TournamentDemoData.Big32()).Cards.First(b => b.Match.Status == "bye");
        Assert.Equal(TournamentPage.CardHeightSolo / 2 + 2, bye.Height);
    }

    [Fact]
    public void ParticipantsSayWhereEachOneFell() => Spanish(() =>
    {
        var t = TournamentDemoData.Big32();
        EntrantFate Fate(string name) => TournamentPage.FateOf(t, t.Entrants!.First(e => e.DisplayName == name))!;
        Assert.Equal("Sigue · Octavos", Fate("Gorgo").Text);
        Assert.Equal("No se presentó · Dieciseisavos", Fate("Granadero").Text);
        Assert.Equal("caution", Fate("Granadero").Tone);
        Assert.Equal("Fuera en Dieciseisavos", Fate("Kaiser").Text);
    });

    [Fact]
    public void AFinishedDrawNamesChampionAndFinalist() => Spanish(() =>
    {
        var t = TournamentDemoData.Finished();
        var final = t.Matches!.OrderByDescending(m => m.Round).First();
        var loser = final.WinnerEntrantId == final.Entrant1Id ? final.Entrant2Id : final.Entrant1Id;
        Assert.Equal(Strings.Get("TpFateChampion"), TournamentPage.FateOf(t, t.Entrants!.First(e => e.Id == t.WinnerEntrantId))!.Text);
        Assert.Equal(Strings.Get("TpFateFinalist"), TournamentPage.FateOf(t, t.Entrants!.First(e => e.Id == loser))!.Text);
    });

    [Fact]
    public void BeforeABracketNobodyHasAFate()
        => Assert.All(TournamentDemoData.Registration().Entrants!, e => Assert.Null(TournamentPage.FateOf(TournamentDemoData.Registration(), e)));

    [Fact]
    public void ASearchFindsARosterToo()
    {
        var e = new TournamentEntrant { DisplayName = "Los Andes", Members = new() { new() { UserId = "1", DisplayName = "Cuzco" } } };
        Assert.True(TournamentPage.Matches(e, "andes"));
        Assert.True(TournamentPage.Matches(e, "cuz"));
        Assert.False(TournamentPage.Matches(e, "mariscal"));
    }

    [Fact]
    public void RoundNamesAreOrdinaryWordsInASentence() => Spanish(()
        => Assert.Equal("Octavos", TournamentPage.RoundName(TournamentDemoData.Big32(), 2)));

    // ---------------------------------------------------------------- the two panels

    [Fact]
    public void FlexLineGivesTheSpringWhatIsLeftAndWrapsTheRest() => OnSta(() =>
    {
        var line = new FlexLine { Gap = 10, LineGap = 4 };
        Border Box(double w) => new() { Width = w, Height = 20 };
        var a = Box(100);
        var spring = new Border();
        FlexLine.SetSpring(spring, true);
        var b = Box(100);
        var c = Box(300);
        line.Children.Add(a);
        line.Children.Add(spring);
        line.Children.Add(b);
        line.Children.Add(c);
        line.Measure(new Size(350, double.PositiveInfinity));
        line.Arrange(new Rect(0, 0, 350, line.DesiredSize.Height));
        Assert.Equal(250, b.TranslatePoint(new Point(0, 0), line).X, 3);
        Assert.Equal(24, c.TranslatePoint(new Point(0, 0), line).Y, 3);
        Assert.Equal(44, line.DesiredSize.Height, 3);
    });

    [Fact]
    public void AutoFillPanelFitsAsManyEqualColumnsAsItCan() => OnSta(() =>
    {
        var panel = new AutoFillPanel { MinColumn = 220, Gap = 8 };
        for (int i = 0; i < 5; i++) panel.Children.Add(new Border { Height = i == 1 ? 60 : 40 });
        panel.Measure(new Size(700, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));
        var second = (Border)panel.Children[1];
        var fourth = (Border)panel.Children[3];
        Assert.Equal((700 - 16) / 3.0 + 8, second.TranslatePoint(new Point(0, 0), panel).X, 3);
        Assert.Equal(68, fourth.TranslatePoint(new Point(0, 0), panel).Y, 3);
    });

    // ---------------------------------------------------------------- the page

    [Fact]
    public void ThePageOpensOnItsTabWithTheOtherTwoBuiltAndHidden() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoTournaments("registration");
        var texts = Logical(tab.TournamentDetailPanel).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();
        Assert.Contains(Strings.Get("MpTournamentGroupRequests"), texts);
        Assert.Contains(Strings.Get("TpOrgManagers"), texts);
        Assert.Contains(Strings.Get("TpNoBracket"), texts);
    });

    [Fact]
    public void THE_ONE_THAT_MATTERS_AnEntrantTileStaysInsideItsCard() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoTournaments("registration");
        var t = tab.TournamentPreview.Detail(TournamentDemoData.RegistrationId)!;
        var list = (FrameworkElement)tab.BuildEntrantsList(t, t.OwnerUserId);

        // The narrowest a column can get: the window's MinWidth, minus the list and the margins.
        const double narrowest = 900 - 300 - 56;
        list.Measure(new Size(narrowest, double.PositiveInfinity));
        list.Arrange(new Rect(0, 0, narrowest, list.DesiredSize.Height));
        list.UpdateLayout();

        var buttons = Logical(list).OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            var at = button.TranslatePoint(new Point(0, 0), list);
            Assert.True(at.X + button.ActualWidth <= narrowest + .5, $"'{button.Content}' ends at {at.X + button.ActualWidth:0} in {narrowest:0}.");
        }
        var captions = buttons.Select(b => b.Content as string ?? "").ToList();
        Assert.Contains(Strings.Get("TpAcceptShort"), captions);
        Assert.Contains(Strings.Get("MpTournamentReject"), captions);
    });

    [Fact]
    public void AnEntryAlreadyOutOffersNothing() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        var t = new TournamentDetail
        {
            Id = "c", Name = "Copa", Status = "registration", OwnerUserId = "owner", Capacity = 8, Format = "1v1",
            Entrants = new() { new() { Id = "out", Kind = "solo", DisplayName = "Gorgo", Status = "withdrawn", CaptainUserId = "me", MemberIds = new() { "me" } } },
        };
        Assert.Empty(Logical((DependencyObject)tab.BuildEntrantsList(t, "me")).OfType<Button>());
    });

    [Fact]
    public void TheOrganisersPowersAreInThePanel_EachOneAsking() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoTournaments("organiser");
        var t = tab.TournamentPreview.Detail(TournamentDemoData.OrganiserId)!;
        var live = t.Matches!.First(m => m.Lobby != null);
        tab.ClickBracketCellForPreview(live.Id);
        var captions = Logical(tab.TournamentDetailPanel).OfType<Button>().Select(b => b.Content as string ?? "").ToList();
        Assert.Contains(Strings.Format("MpTournamentAwardTo", t.Entrants!.First(e => e.Id == live.Entrant1Id).DisplayName), captions);
        Assert.Contains(Strings.Format("MpTournamentAwardTo", t.Entrants!.First(e => e.Id == live.Entrant2Id).DisplayName), captions);
        Assert.Contains(Strings.Get("MpTournamentReplay"), captions);
        Assert.Contains(Strings.Get("TpDisqualify"), captions);
        Assert.Contains(Strings.Get("MpTournamentWatchRoom"), captions);
    });

    /// <summary>The whole of "the zoom is a transform": the same cards, nothing built again.</summary>
    [Fact]
    public void ZoomingRebuildsNoCard() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoTournaments("big32");
        var before = Logical(tab.TournamentDetailPanel).OfType<Canvas>().Single();
        var plus = Logical(tab.TournamentDetailPanel).OfType<Button>().First(b => (b.Content as string) == "−");
        plus.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, plus));
        var after = Logical(tab.TournamentDetailPanel).OfType<Canvas>().Single();
        Assert.Same(before, after);
        Assert.Contains("80 %", Logical(tab.TournamentDetailPanel).OfType<TextBlock>().Select(t => t.Text));
    });

    [Fact]
    public void TheListFoldsTo72() => OnSta(() =>
    {
        var tab = new MultiplayerTab();
        tab.ShowDemoTournaments("big32");
        tab.TournamentsFoldButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, tab.TournamentsFoldButton));
        Assert.Equal(72, tab.TournamentsListColumn.Width.Value);
        Assert.Equal(Visibility.Visible, tab.TournamentsListClosedView.Visibility);
        Assert.NotEmpty(tab.TournamentsCollapsedPanel.Children);
    });

    [Theory]
    [InlineData("big32")]
    [InlineData("teams")]
    [InlineData("finished")]
    public void EverySampleHasACardlessBracketInBothLanguages(string sample) => OnSta(() =>
    {
        foreach (var language in new[] { Strings.LangEs, Strings.LangEn })
        {
            Strings.SetLanguage(language);
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments(sample);
            var canvas = Logical(tab.TournamentDetailPanel).OfType<Canvas>().Single();
            Assert.Empty(Logical(canvas).OfType<ButtonBase>());
        }
    });
}
