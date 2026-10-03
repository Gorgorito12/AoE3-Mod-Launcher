using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The INTERACTIVE tournament preview, driven through the tab's own buttons.
///
/// <para>The preview's promise is that what gets judged is the real interface: the real
/// confirmations, the real error notices, the real refresh — with a simulated server behind
/// them. These tests hold it to that by clicking. A test that called the simulator directly
/// would pass over a tab whose buttons still did nothing, which is exactly the state this
/// replaced.</para>
///
/// <para>Clicks are <c>RaiseEvent(ClickEvent)</c> on the very Button the tab built, found by
/// its caption or by the preview's automation id. The preview's server answers with completed
/// tasks, so each click is settled before the next line runs; an overlay that appears is
/// answered the same way, by clicking its button.</para>
/// </summary>
[Collection("wpf-and-language")]
public class TournamentPreviewTests
{
    private const string Prefix = MultiplayerTab.TournamentPreviewIdPrefix;

    /// <summary>
    /// THE ONE THAT MATTERS: a sample played from where it stands to a champion, by the
    /// preview's own buttons, with nothing refused and nothing "inert" along the way.
    /// </summary>
    [Fact]
    public void THE_ONE_THAT_MATTERS_ASampleIsPlayedToAChampionByItsOwnButtons()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("running");
            var sim = tab.TournamentPreview;

            PlayToChampion(tab, TournamentDemoData.RunningId);

            var done = sim.Detail(TournamentDemoData.RunningId)!;
            Assert.Equal("finished", done.Status);
            Assert.False(string.IsNullOrEmpty(done.WinnerEntrantId));

            // The champion line the finished tournament draws, on screen.
            var champion = Strings.Format("MpTournamentChampion",
                done.Entrants!.First(e => e.Id == done.WinnerEntrantId).DisplayName ?? "");
            Assert.Contains(champion, Texts(tab.TournamentDetailPanel));

            // Nothing left to simulate, so the preview stops offering to.
            Assert.Empty(PreviewButtons(tab).Where(b => IdOf(b).StartsWith(Prefix + "Wins:")));
            Assert.Null(PreviewButton(tab, "PlayRound"));
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// A tournament still taking sign-ups, run by its organiser's REAL buttons from closing the
    /// list to a champion — and the step that was unreachable in production is the pin.
    ///
    /// <para>Opening registration is allowed from "ready" too, and it used to be checked first,
    /// so a closed list offered "open registration" for ever and no tournament could be
    /// started from the launcher. After closing, the forward move must be SEED.</para>
    /// </summary>
    [Fact]
    public void TheOrganiserRunsATournamentFromClosingTheListToAChampion()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("registration");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.RegistrationId;

            // Its owner is the person the sample was written for, so it opens as organiser.
            Assert.Equal(TournamentPreviewViewer.Organiser, sim.RoleOf(id));

            Click(Captioned(tab, "MpTournamentCloseRegistration"));
            AssertNoOverlay(tab);
            Assert.Equal("ready", sim.Detail(id)!.Status);

            // THE PIN: seed is offered, and reopening is not competing with it.
            Assert.NotNull(CaptionedOrNull(tab, "MpTournamentSeed"));
            Assert.Null(CaptionedOrNull(tab, "MpTournamentOpenRegistration"));

            Click(Captioned(tab, "MpTournamentSeed"));
            AssertNoOverlay(tab);
            Click(Captioned(tab, "MpTournamentStart"));
            AssertNoOverlay(tab);
            Assert.Equal("running", sim.Detail(id)!.Status);

            PlayToChampion(tab, id);
            Assert.Equal("finished", sim.Detail(id)!.Status);
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// Deciding a match by hand ASKS, exactly as it does for real — and the answer is obeyed
    /// both ways. Before, the preview skipped the question and said "this is a preview".
    /// </summary>
    [Fact]
    public void TheOrganisersQuestionsAreAskedForRealAndObeyedBothWays()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("organiser");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.OrganiserId;

            var live = sim.Detail(id)!.Matches!.First(m => m.Lobby != null && m.Status == "pending");
            var winnerName = NameOf(sim.Detail(id)!, live.Entrant1Id);
            tab.ClickBracketCellForPreview(live.Id);

            // "No": nothing is sent.
            ClickMenuItem(AwardItem(tab, winnerName));
            var question = Overlay(tab);
            Assert.Equal(Strings.Get("MpTournamentAwardConfirmTitle"), question.Title);
            Click(question.Buttons.First(b => (b.Content as string) == Strings.Get("MpAlertCancel")));
            AssertNoOverlay(tab);
            Assert.Equal("pending", MatchOf(sim, id, live.Id).Status);

            // "Yes": the simulated server decides it, as a walkover for the side chosen.
            ClickMenuItem(AwardItem(tab, winnerName));
            question = Overlay(tab);
            Click(question.Buttons.First(
                b => (b.Content as string) == Strings.Get("MpTournamentAwardConfirmYes")));
            AssertNoOverlay(tab);

            var decided = MatchOf(sim, id, live.Id);
            Assert.Equal("done", decided.Status);
            Assert.Equal("walkover", decided.Outcome);
            Assert.Equal(live.Entrant1Id, decided.WinnerEntrantId);
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// A refusal reaches the player in the launcher's words for the server's code, through the
    /// same notice production shows — here, somebody else closing the list while the "Enter"
    /// button was on screen.
    /// </summary>
    [Fact]
    public void ARefusalIsTheRealNoticeWithTheServersReason()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("registration");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.RegistrationId;

            Click(PreviewButton(tab, "ViewAs:Spectator")!);
            var enter = Captioned(tab, "MpTournamentEnter");

            // The organiser closes the list behind the spectator's back.
            sim.ViewerChoice = TournamentPreviewViewer.Organiser;
            sim.CloseRegistration(id);
            sim.ViewerChoice = TournamentPreviewViewer.Spectator;

            Click(enter);
            var notice = Overlay(tab);
            Assert.Equal(Strings.Get("MpTournamentActionFailed"), notice.Title);
            Assert.Equal(Strings.Get("MpTournamentErrClosed"), notice.Body);
            Assert.NotEqual(Strings.Get("MpTournamentDemoInertTitle"), notice.Title);

            Click(notice.Buttons.Single());
            AssertNoOverlay(tab);
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// A push that lands while the preview is open refreshes the SAMPLES. The listing is public,
    /// so a real fetch succeeds even signed out — and it used to replace the preview's data with
    /// whatever the real server had.
    /// </summary>
    [Fact]
    public void APushRefreshesTheSamplesAndNeverReplacesThem()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("registration");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.RegistrationId;

            var before = sim.Detail(id)!.Entrants!.Select(e => e.DisplayName).ToHashSet();

            // Somebody signs up, unseen: no repaint has happened yet.
            sim.SignUp(id);
            var newcomer = sim.Detail(id)!.Entrants!
                .Select(e => e.DisplayName)
                .First(n => !before.Contains(n));
            Assert.DoesNotContain(Texts(tab.TournamentDetailPanel), s => s.Contains(newcomer!));

            tab.HandleTournamentUpdateFrame(
                "{\"kind\":\"entry_accepted\",\"tournament_id\":\"" + id
                + "\",\"tournament_name\":\"x\"}");

            // Refreshed from the simulated server: the sign-up is on screen, and the samples
            // are still the samples.
            Assert.Contains(Texts(tab.TournamentDetailPanel), s => s.Contains(newcomer!));
            Assert.Contains(Strings.Get("MpTournamentDemoRunningName"), Texts(tab.TournamentsListPanel));
            Assert.NotNull(Logical(tab.TournamentDetailPanel).OfType<FrameworkElement>()
                .FirstOrDefault(e => IdOf(e) == Prefix + "Banner"));
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// "View as" changes what the bar offers, and opening another tournament goes back to that
    /// tournament's own viewer — the choice was about the bracket being looked at.
    /// </summary>
    [Fact]
    public void ViewAsChangesWhatTheBarOffers()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("organiser");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.OrganiserId;

            var live = sim.Detail(id)!.Matches!.First(m => m.Lobby != null && m.Status == "pending");
            tab.ClickBracketCellForPreview(live.Id);
            Assert.NotNull(BarOverflow(tab));
            Assert.Equal("active", PreviewButton(tab, "ViewAs:Organiser")!.Tag as string);

            Click(PreviewButton(tab, "ViewAs:Spectator")!);
            Assert.Null(BarOverflow(tab));
            Assert.Equal("active", PreviewButton(tab, "ViewAs:Spectator")!.Tag as string);
            Assert.Null(PreviewButton(tab, "ViewAs:Organiser")!.Tag as string);
            Assert.Equal(live.Id, tab.SelectedBracketMatchId);

            Click(PreviewButton(tab, "ViewAs:Organiser")!);
            Assert.NotNull(BarOverflow(tab));

            // Another tournament opens as the person IT was written for.
            Click(PreviewButton(tab, "ViewAs:Spectator")!);
            tab.OpenTournamentForPreview(TournamentDemoData.RunningId);
            Assert.Equal(TournamentPreviewViewer.Player, sim.RoleOf(TournamentDemoData.RunningId));
            Assert.Equal("active", PreviewButton(tab, "ViewAs:Player")!.Tag as string);
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// "Play my match" opens the room on the simulated server and never the real path — the
    /// join, and the countdown that launches the game. The bar then offers the way back in.
    /// </summary>
    [Fact]
    public void PlayingMyMatchOpensTheRoomOnTheSimulatedServer()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            tab.ShowDemoTournaments("running");
            var sim = tab.TournamentPreview;
            const string id = TournamentDemoData.RunningId;

            var mine = tab.SelectedBracketMatchId;
            Assert.NotNull(mine);

            Click(Captioned(tab, "MpTournamentPlayMyMatch"));
            AssertNoOverlay(tab);

            var room = MatchOf(sim, id, mine!).Lobby;
            Assert.NotNull(room);
            Assert.Equal(TournamentDemoData.MeUserId, room!.HostUserId);
            Assert.NotNull(CaptionedOrNull(tab, "MpTournamentReturnToRoom"));
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// The sides box is where the people about to play a TEAM match read it: in the bar. It was
    /// built for the card and lost its only caller when the actions left the card.
    /// </summary>
    [Fact]
    public void TheSidesWarningIsInTheBarForATeamMatchAndNowhereForA1v1()
    {
        var error = DialogXamlTests.RunOnStaThread(() => InSpanish(() =>
        {
            var tab = new MultiplayerTab();
            var prefix = Strings.Get("MpTournamentSidesWarningTeam").Split("{0}")[0];

            tab.ShowDemoTournaments("teams");
            Assert.NotNull(tab.SelectedBracketMatchId);
            Assert.Contains(Texts(tab.TournamentDetailPanel), s => s.StartsWith(prefix, StringComparison.Ordinal));

            tab.OpenTournamentForPreview(TournamentDemoData.RunningId);
            Assert.NotNull(tab.SelectedBracketMatchId);
            Assert.DoesNotContain(Texts(tab.TournamentDetailPanel), s => s.StartsWith(prefix, StringComparison.Ordinal));
        }));
        Assert.Null(error);
    }

    /// <summary>
    /// The preview's own controls, in both languages, in every sample: a real caption (a key
    /// shown raw is how a missing translation looks), no menu of their own, and never inside
    /// the bracket — two other tests walk the bracket for buttons and menus and would find them.
    /// </summary>
    [Fact]
    public void ThePreviewsControlsAreCaptionedAndNeverInsideTheBracket()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var language in new[] { "es", "en" })
            {
                InLanguage(language, () =>
                {
                    var tab = new MultiplayerTab();
                    foreach (var sample in new[]
                             { "running", "teams", "myroom", "waiting", "organiser", "registration", "finished" })
                    {
                        tab.ShowDemoTournaments(sample);

                        var banner = Logical(tab.TournamentDetailPanel).OfType<FrameworkElement>()
                            .First(e => IdOf(e) == Prefix + "Banner");
                        Assert.Contains(Strings.Get("MpTournamentDemoBanner"), Texts(banner));

                        var buttons = PreviewButtons(tab);
                        Assert.Contains(buttons, b => IdOf(b) == Prefix + "Reset");
                        foreach (var b in buttons)
                        {
                            var caption = b.Content as string;
                            Assert.False(string.IsNullOrWhiteSpace(caption), $"{sample}/{language}: {IdOf(b)} has no caption");
                            Assert.DoesNotContain("MpTournament", caption!);
                            Assert.Null(b.ContextMenu);
                        }

                        // Not inside any scroller of the detail pane — the bracket's is the only one.
                        foreach (var scroller in Logical(tab.TournamentDetailPanel).OfType<ScrollViewer>())
                        {
                            Assert.DoesNotContain(Logical(scroller).OfType<FrameworkElement>(),
                                e => IdOf(e).StartsWith(Prefix, StringComparison.Ordinal));
                        }
                    }
                });
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// No tournament call goes around the swappable server. One that did would reach the REAL
    /// server from inside the preview — and the preview would look like it worked.
    /// </summary>
    [Fact]
    public void NoTournamentCallGoesAroundTheSwappableServer()
    {
        var dir = FindRepoDirectory("WarsOfLibertyLauncher", "Controls");
        var files = Directory.GetFiles(dir, "MultiplayerTab*.cs");
        Assert.NotEmpty(files);

        var around = new Regex(
            @"\.Api[!?]?\.(\w*Tournament\w*|AcceptEntrantAsync|RejectEntrantAsync|"
            + @"DisqualifyEntrantAsync|ReplayMatchAsync|AwardWalkoverAsync)\(");
        int routed = 0;
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (Match m in around.Matches(source))
            {
                Assert.Fail($"{Path.GetFileName(file)} calls the session's client directly: {m.Value}");
            }
            routed += Regex.Matches(source, @"\bTournamentApi\.").Count
                      + Regex.Matches(source, @"\bapi\.\w*Tournament\w*Async\(").Count;
        }

        // Not vacuous: the calls exist, and they go the right way.
        Assert.True(routed >= 18, $"only {routed} tournament calls found through TournamentApi");
    }

    /// <summary>Every kind of push the server sends — and the preview reproduces — has words of
    /// its own. <c>match_replay</c> used to fall through to the generic "Tournaments".</summary>
    [Fact]
    public void EveryPushHasItsOwnToastTitle()
    {
        var generic = Strings.Get("MpSubtabTournaments");
        foreach (var kind in new[] { "match_ready", "room_opened", "match_done", "entry_accepted",
                                     "entry_promoted", "match_replay" })
        {
            Assert.NotEqual(generic, MultiplayerTab.TournamentToastTitle(new TournamentUpdateNotice { Kind = kind }));
        }
        Assert.Equal(Strings.Get("MpTournamentToastReplay"),
            MultiplayerTab.TournamentToastTitle(new TournamentUpdateNotice { Kind = "match_replay" }));
    }

    /// <summary>
    /// Pictures of the preview at each step, for looking at — only when
    /// <c>AOE3ML_TOURNAMENT_PREVIEW_SNAPSHOTS</c> names a folder, like the replay corpus test.
    /// </summary>
    [Fact]
    public void Snapshots()
    {
        var folder = Environment.GetEnvironmentVariable("AOE3ML_TOURNAMENT_PREVIEW_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            // ONE window for both languages: closing the last window ends the test application,
            // and a second window shown after that never lays out.
            var window = new Window
            {
                Width = 1440,
                Height = 1000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -30000,
                Top = -30000,
            };
            window.Show();
            try
            {
                foreach (var language in new[] { "es", "en" })
                {
                    InLanguage(language, () => ShootTheSteps(window, folder, language));
                }
            }
            finally
            {
                window.Close();
            }
        });
        Assert.Null(error);
    }

    private static void ShootTheSteps(Window window, string folder, string language)
    {
        var tab = new MultiplayerTab();
        window.Content = tab;

        void Shot(string name)
        {
            // Everything the dispatcher still has queued — layout passes, Loaded handlers —
            // before the picture, or it shows a half-drawn tree.
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();

            var target = (FrameworkElement)window.Content;
            var bmp = new RenderTargetBitmap(
                (int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(target);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(folder, $"{language}-{name}.png"));
            png.Save(fs);
        }

        tab.ShowDemoTournaments("running");
        Shot("01-start");

        // The first side of the selected tie, which in this sample is "me".
        var win = PreviewButtons(tab).First(b => IdOf(b).StartsWith(Prefix + "Wins:"));
        Click(win);
        Shot("02-after-my-win");

        tab.ShowDemoTournaments("organiser");
        var live = tab.TournamentPreview.Detail(TournamentDemoData.OrganiserId)!
            .Matches!.First(m => m.Lobby != null && m.Status == "pending");
        tab.ClickBracketCellForPreview(live.Id);
        Shot("03-organiser");

        Click(PreviewButton(tab, "ViewAs:Spectator")!);
        Shot("04-spectator");

        tab.ShowDemoTournaments("registration");
        Shot("05-registration");

        tab.ShowDemoTournaments("running");
        PlayToChampion(tab, TournamentDemoData.RunningId);
        Shot("06-champion");

        tab.ResetTournamentPreview();
        Shot("07-reset");

        tab.ShowDemoTournaments("teams");
        Shot("08-teams");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Press the strip's winner buttons — or "play this round" when no match is
    /// selected — until the tournament has a champion. Bounded: a loop that never ends is a
    /// failure too.</summary>
    private static void PlayToChampion(MultiplayerTab tab, string id)
    {
        var sim = tab.TournamentPreview;
        for (int step = 0; step < 80; step++)
        {
            if (sim.Detail(id)!.Status == "finished") return;
            AssertNoOverlay(tab);

            var win = PreviewButtons(tab).FirstOrDefault(b => IdOf(b).StartsWith(Prefix + "Wins:"));
            if (win != null) Click(win);
            else Click(PreviewButton(tab, "PlayRound")
                       ?? throw new Xunit.Sdk.XunitException("neither a winner button nor 'play this round' is offered"));
        }
        Assert.Fail($"{id} did not finish in 80 steps");
    }

    private static void InSpanish(Action body) => InLanguage("es", body);

    private static void InLanguage(string language, Action body)
    {
        var previous = Strings.Language;
        try
        {
            Strings.SetLanguage(language);
            body();
        }
        finally { Strings.SetLanguage(previous); }
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var grand in Logical(child)) yield return grand;
        }
    }

    private static string IdOf(DependencyObject e) => AutomationProperties.GetAutomationId(e) ?? "";

    private static List<Button> PreviewButtons(MultiplayerTab tab)
        => Logical(tab.TournamentDetailPanel).OfType<Button>()
            .Where(b => IdOf(b).StartsWith(Prefix, StringComparison.Ordinal))
            .ToList();

    private static Button? PreviewButton(MultiplayerTab tab, string action)
        => PreviewButtons(tab).FirstOrDefault(b => IdOf(b) == Prefix + action);

    private static Button? CaptionedOrNull(MultiplayerTab tab, string key)
        => Logical(tab.TournamentDetailPanel).OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == Strings.Get(key));

    private static Button Captioned(MultiplayerTab tab, string key)
        => CaptionedOrNull(tab, key)
           ?? throw new Xunit.Sdk.XunitException($"no button reads '{Strings.Get(key)}'");

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

    private static void ClickMenuItem(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));

    /// <summary>The ⋯ of the bracket's action bar: the one Button whose menu decides matches.</summary>
    private static Button? BarOverflow(MultiplayerTab tab)
        => Logical(tab.TournamentDetailPanel).OfType<Button>()
            .FirstOrDefault(b => b.ContextMenu != null
                                 && b.ContextMenu.Items.OfType<MenuItem>()
                                     .Any(i => (i.Header as string)?.Contains(
                                         Strings.Get("MpTournamentReplay")) == true));

    private static MenuItem AwardItem(MultiplayerTab tab, string winnerName)
        => (BarOverflow(tab) ?? throw new Xunit.Sdk.XunitException("the bar has no ⋯"))
            .ContextMenu!.Items.OfType<MenuItem>()
            .First(i => (i.Header as string) == Strings.Format("MpTournamentAwardTo", winnerName));

    private static List<string> Texts(DependencyObject root)
        => Logical(root).OfType<TextBlock>().Select(RevealText.PlainTextOf).ToList();

    private static string NameOf(TournamentDetail t, string? entrantId)
        => t.Entrants!.First(e => e.Id == entrantId).DisplayName ?? "";

    private static TournamentMatch MatchOf(TournamentSimulator sim, string id, string matchId)
        => sim.Detail(id)!.Matches!.First(m => m.Id == matchId);

    private sealed record AlertCard(string Title, string Body, List<Button> Buttons);

    /// <summary>The alert card on top of the tab, if one is up. <c>MpAlertOverlay</c> adds the
    /// scrim, the shadow and the card as the host's last three children.</summary>
    private static AlertCard? OverlayOrNull(MultiplayerTab tab)
    {
        var host = tab.TabRootGrid;
        if (host.Children.Count == 0) return null;
        if (host.Children[^1] is not Border { Child: Border card }) return null;
        if (card.Child is not StackPanel stack || stack.Children.Count < 3) return null;
        if (stack.Children[0] is not StackPanel header) return null;

        var title = header.Children.OfType<TextBlock>().Skip(1).FirstOrDefault()?.Text ?? "";
        var body = (stack.Children[1] as TextBlock)?.Text ?? "";
        var buttons = Logical(stack.Children[2]).OfType<Button>().ToList();
        if (stack.Children[2] is Panel row) buttons = row.Children.OfType<Button>().ToList();
        return new AlertCard(title, body, buttons);
    }

    private static AlertCard Overlay(MultiplayerTab tab)
        => OverlayOrNull(tab) ?? throw new Xunit.Sdk.XunitException("no notice or question is on screen");

    private static void AssertNoOverlay(MultiplayerTab tab)
    {
        var card = OverlayOrNull(tab);
        Assert.True(card == null, card == null ? "" : $"an alert is on screen: {card.Title} — {card.Body}");
    }

    private static string FindRepoDirectory(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not find {string.Join('/', parts)} above {AppContext.BaseDirectory}");
    }
}
