using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Services.Multiplayer;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Constructs the multiplayer windows that the startup smoke test never reaches.
///
/// <para>A green build does not prove a window loads: a <c>{StaticResource}</c> that
/// fails to resolve throws when the XAML is PARSED, and these windows are only parsed
/// once someone signs in and opens a room. The launcher's smoke test opens MainWindow
/// and nothing else, so a broken resource key here would ship unseen — which is exactly
/// the class of bug that has bitten this repo before (RadiusMd).</para>
///
/// <para>The resource dictionaries are merged by EXPLICIT pack:// URIs: App.xaml's own
/// Source values are relative and resolve against the entry assembly, which under a test
/// host is the test runner, not the launcher.</para>
/// </summary>
public class DialogXamlTests
{
    [Fact]
    public void CreateLobbyDialog_LoadsItsXaml()
    {
        var error = RunOnStaThread(() =>
        {
            var session = new MultiplayerSession(new LauncherConfig());
            var dlg = new CreateLobbyDialog(
                session,
                new List<ModProfile>(),
                null,
                _ => Task.FromResult("0123456789abcdef"),
                _ => new ModCopyInfo(false, false, Array.Empty<ModCopyChoice>()),
                _ => Task.CompletedTask);
            // Touching a named element proves the tree was really built, not just that
            // the constructor returned.
            Assert.NotNull(dlg.CreateButton);

            // The format row exists and starts HIDDEN, which is the state of everyone who does
            // not tick the competitive box — i.e. almost every room. It is revealed by the tick,
            // and the row it locks is the player-count one, so both must be here and both must
            // start the way a casual room needs them.
            Assert.NotNull(dlg.CompetitiveFormatRow);
            Assert.Equal(Visibility.Collapsed, dlg.CompetitiveFormatRow.Visibility);
            Assert.Equal(3, dlg.FormatRow.Children.Count);   // 1v1 / 2v2 / 3v3
            Assert.NotNull(dlg.MaxPlayersRow);
            Assert.True(dlg.MaxPlayersRow.Children.Count > 0);

            // And the note under it says nothing until a format is chosen — it carries either
            // the 1v1 forfeit clause or the team "does not rate yet" line, and neither applies
            // to a casual room.
            Assert.Equal(Visibility.Collapsed, dlg.CompetitiveSizeNote.Visibility);

            // And a casual room still opens at the full eight seats. The format row picks a
            // format at construction so the row is never revealed empty, and that pick moves the
            // size — so without the competitive guard on it, every room would have quietly
            // opened as a two-player one.
            var active = dlg.MaxPlayersRow.Children.OfType<Button>()
                .Where(b => (b.Tag as string) == "active")
                .Select(b => b.Content as string)
                .ToList();
            Assert.Equal(new[] { "8" }, active);
            Assert.All(dlg.MaxPlayersRow.Children.OfType<Button>(), b => Assert.True(b.IsEnabled));

            dlg.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The Create button must not inherit the ghost button's hover.
    ///
    /// <para>It did, and it took three reports to find. `MpFooterPrimary` is BasedOn
    /// `MpFooterGhost`, so it inherited the ghost's ControlTemplate — whose IsMouseOver
    /// trigger painted the template's Border BY NAME with `MpRowHighlight`. A trigger that
    /// targets a template element by name cannot be overridden by a derived style, so
    /// pointing at the filled blue Create button replaced its fill with #16263E: the
    /// primary action went dark at the exact moment the user was about to click it. It
    /// reads as "the interface eats the buttons", which is what it was reported as.</para>
    ///
    /// <para>The FIRST assertion is the general lesson — no state colour hardcoded by
    /// TargetName in a template other styles build on. The second pins this outcome.</para>
    /// </summary>
    [Fact]
    public void CreateButton_DoesNotInheritTheGhostButtonsHover()
    {
        var error = RunOnStaThread(() =>
        {
            var session = new MultiplayerSession(new LauncherConfig());
            var dlg = new CreateLobbyDialog(
                session,
                new List<ModProfile>(),
                null,
                _ => Task.FromResult("0123456789abcdef"),
                _ => new ModCopyInfo(false, false, Array.Empty<ModCopyChoice>()),
                _ => Task.CompletedTask);

            var button = dlg.CreateButton;
            Assert.NotNull(button.Template);

            // (1) Nothing in the shared template may paint a state by TargetName: that is
            //     the setter shape a derived style is powerless against.
            foreach (var trigger in button.Template.Triggers.OfType<Trigger>())
                foreach (var setter in trigger.Setters.OfType<Setter>())
                    Assert.True(string.IsNullOrEmpty(setter.TargetName),
                        $"Template trigger on {trigger.Property?.Name} sets {setter.Property?.Name} " +
                        $"through TargetName='{setter.TargetName}' — a derived style cannot override it.");

            // (2) The hover that actually applies is the primary's blue. Triggers merge
            //     base-first down the BasedOn chain and the last setter for a property wins,
            //     so walking the chain in that order leaves the effective one.
            var chain = new List<Style>();
            for (var style = button.Style; style != null; style = style.BasedOn)
                chain.Insert(0, style);

            object? hoverBackground = null;
            foreach (var style in chain)
                foreach (var trigger in style.Triggers.OfType<Trigger>())
                    if (trigger.Property == UIElement.IsMouseOverProperty)
                        foreach (var setter in trigger.Setters.OfType<Setter>())
                            if (setter.Property == Control.BackgroundProperty)
                                hoverBackground = setter.Value;

            var key = (hoverBackground as DynamicResourceExtension)?.ResourceKey as string;
            Assert.Equal("MpBlueHover", key);

            dlg.Close();
        });

        Assert.Null(error);
    }

    [Fact]
    public void LobbyWindow_LoadsItsXaml()
    {
        // The window with the most to lose: it is only ever parsed once someone signs in
        // AND enters a room, so nothing in the automated verification touches it. Both
        // remaining handoff screens (the lobby itself and the in-match panel) rewrite it.
        var error = RunOnStaThread(() =>
        {
            var window = new LobbyWindow(new MultiplayerSession(new LauncherConfig()));
            Assert.NotNull(window.StartButton);
            Assert.NotNull(window.MatchResultOverlay);

            // ApplyMatchPhaseUi COLLAPSES this container for the InGame and Result phases
            // so the lobby underneath cannot leak around the opaque overlays. Rename it and
            // that hiding stops happening SILENTLY — the overlays still show, the lobby
            // still peeks out at the edges, and nothing fails. This is the tripwire.
            Assert.NotNull(window.LobbyLeftColumn);
            Assert.NotNull(window.InGameOverlay);

            // The "before you start" checklist. The third item states the abandonment
            // penalty, which is the ONLY place a guest can read it — the create-room
            // dialog that spells it out is seen by the host alone. It starts Collapsed
            // and RefreshPreflightChecklist shows it for a competitive room, so a rename
            // here would leave every guest reading nothing, silently.
            Assert.NotNull(window.PreflightAbandonRow);
            Assert.NotNull(window.PreflightAbandonText);
            Assert.Equal(Visibility.Collapsed, window.PreflightAbandonRow.Visibility);

            window.Close();
        });

        Assert.Null(error);
    }

    [Fact]
    public void RenameRoomDialog_LoadsItsXaml()
    {
        // Added when this dialog moved off the launcher's gold styles onto the multiplayer
        // blue ones. It now resolves MpDialogField, MpSecondaryButton and MpPrimaryButton —
        // keys it had never referenced — and a StaticResource that fails to resolve throws at
        // RUNTIME, not compile. Nothing else opens this window: it needs a signed-in host
        // inside a room to press the button.
        var error = RunOnStaThread(() =>
        {
            var dlg = new RenameRoomDialog("Sala de prueba");
            Assert.NotNull(dlg.NameEntry);
            dlg.Close();
        });

        Assert.Null(error);
    }

    [Fact]
    public void PasswordPromptDialog_LoadsItsXaml()
    {
        // Same move, and the better-travelled of the two — it opens on every join of a private
        // room. Its PasswordBox is the riskier half: WPF ships no implicit style for that
        // control, so it depends entirely on MpDialogPasswordField resolving.
        var error = RunOnStaThread(() =>
        {
            var dlg = new PasswordPromptDialog();
            Assert.NotNull(dlg.PasswordEntry);
            dlg.Close();
        });

        Assert.Null(error);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void MatchResultCard_BuildsForEveryVerdict(double result)
    {
        // The card is built in code, so nothing about it is checked at compile time: a
        // resource key that does not resolve throws at BUILD time, and it is only built
        // once a real match has finished. All three verdicts take different branches —
        // the no-result one in particular reaches the footer's explanation, which no other
        // path does.
        var error = RunOnStaThread(() =>
        {
            var model = new MatchOutcomeView(
                MatchOutcomeView.Classify(result),
                "wol", "Texas", 1440, 2,
                RatingBefore: 1524, RatingAfter: 1542,
                RivalLogin: "someone", RivalRating: 1592,
                Wins: 4, Losses: 1, Rd: 60);
            var card = MatchResultCard.Build(
                model, new MatchResultCard.Actions(OnRematch: null, OnDismiss: () => { }));
            Assert.NotNull(card);
        });

        Assert.Null(error);
    }

    [Fact]
    public void MatchResultCard_BuildsWithNothingKnown()
    {
        // The degraded shape an older backend produces: no ratings, no map, no player
        // count, nothing decided. Every one of those is a branch that returns null or an
        // em dash instead of a value, and together they are the case most likely to throw.
        var error = RunOnStaThread(() =>
        {
            var model = new MatchOutcomeView(
                MatchVerdict.NoResult, null, null, 0, 0,
                RatingBefore: null, RatingAfter: null,
                RivalLogin: null, RivalRating: null,
                Wins: 0, Losses: 0, Rd: null);
            Assert.NotNull(MatchResultCard.Build(
                model, new MatchResultCard.Actions(null, null)));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The REPLAY cell, both ways.
    ///
    /// <para>It used to be a fixed label and is now the one cell that can become a BUTTON —
    /// a different branch of <c>Cell</c>, resolving a style by name (<c>MpLinkButton</c>) that
    /// nothing else in this card touches. A key that does not resolve throws only when a real
    /// match ends, which is the worst possible place to find out.</para>
    ///
    /// <para><b>The no-recording case is the one that matters.</b> Most matches have none, so
    /// that branch has to render exactly as it always did; if it ever starts throwing, every
    /// unrecorded match loses its whole card rather than losing a button.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\Users\someone\Documents\My Games\Wars of Liberty\Savegame\Record Game 1.age3Yrec")]
    public void MatchResultCard_BuildsWithAndWithoutARecording(string? recordingPath)
    {
        var error = RunOnStaThread(() =>
        {
            var model = new MatchOutcomeView(
                MatchVerdict.Win, "wol", "ESOC_Tibet", 916, 2,
                RatingBefore: 1500, RatingAfter: 1617,
                RivalLogin: "alucard", RivalRating: 1383,
                Wins: 1, Losses: 0, Rd: 110,
                RecordingPath: recordingPath);

            Assert.NotNull(MatchResultCard.Build(
                model, new MatchResultCard.Actions(null, () => { })));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// No derived style may declare a state that its INHERITED template silently stomps.
    ///
    /// <para>The generalisation of the Create-button bug, and the test that would have
    /// caught it — twice over. A <c>ControlTemplate</c> trigger that paints a template
    /// element BY NAME is unreachable from a derived style: the derived style can only set
    /// the control's own property, which the TargetName setter then beats on its way down
    /// the TemplateBinding. So a derived style that declares, say, a hover Background while
    /// its inherited template also sets that Background by name is writing code that reads
    /// as intent and does nothing.</para>
    ///
    /// <para>Two real cases existed when this was written: <c>MpFooterPrimary</c> (the Create
    /// button went grey on hover instead of blue) and <c>PrimaryButton</c>/<c>DangerButton</c>,
    /// whose gold and red hovers were dead in ~15 dialogs — every confirm and every
    /// destructive button in the launcher hovered neutral grey.</para>
    ///
    /// <para>A style that supplies its own <c>Template</c> is exempt: it inherits no template
    /// to clash with. That is how several styles here legitimately keep a TargetName trigger.</para>
    ///
    /// <para>Scope: the app-wide <c>Styles/*.xaml</c> dictionaries. A style declared inside a
    /// single window (as <c>MpFooterPrimary</c> is) is not reachable from here — that one has
    /// its own test above.</para>
    /// </summary>
    [Fact]
    public void NoDerivedStyleDeclaresAStateItsInheritedTemplateStomps()
    {
        var error = RunOnStaThread(() =>
        {
            var app = Application.Current!;
            var offenders = new List<string>();
            var examined = 0;

            foreach (var dict in app.Resources.MergedDictionaries)
                foreach (var key in dict.Keys.Cast<object>().ToList())
                {
                    if (dict[key] is not Style style || style.BasedOn == null) continue;

                    // Owns its template → inherits nothing that could stomp it.
                    if (style.Setters.OfType<Setter>().Any(s => s.Property == Control.TemplateProperty))
                        continue;

                    var template = InheritedTemplate(style.BasedOn);
                    if (template == null) continue;

                    var declared = style.Triggers.OfType<Trigger>()
                        .SelectMany(t => t.Setters.OfType<Setter>())
                        .Select(s => s.Property)
                        .Where(p => p != null)
                        .ToHashSet();
                    if (declared.Count == 0) continue;
                    examined++;

                    foreach (var t in template.Triggers.OfType<Trigger>())
                        foreach (var s in t.Setters.OfType<Setter>())
                            if (!string.IsNullOrEmpty(s.TargetName) && declared.Contains(s.Property))
                                offenders.Add(
                                    $"'{key}' declares {s.Property?.Name} on a trigger, but its inherited " +
                                    $"template sets {s.Property?.Name} via TargetName='{s.TargetName}' — " +
                                    "the declaration is dead. Move the template's state off TargetName, " +
                                    "or give this style its own Template.");
                }

            // A rule nothing is subject to is a rule that passes for the wrong reason. A few
            // styles match this shape today; if the count ever reaches zero it means the walk
            // stopped finding styles, not that the codebase became clean.
            Assert.True(examined > 0, "the audit examined no derived styles at all");
            Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        });

        Assert.Null(error);
    }

    /// <summary>The nearest Template setter walking up the BasedOn chain, or null.</summary>
    private static ControlTemplate? InheritedTemplate(Style? style)
    {
        for (; style != null; style = style.BasedOn)
        {
            var setter = style.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == Control.TemplateProperty);
            if (setter?.Value is ControlTemplate template) return template;
        }
        return null;
    }

    /// <summary>
    /// The support pill is assembled in code, so nothing checks it at compile time — the same
    /// reason MatchResultCard is built here. It resolves four resources by name
    /// (<c>ModLinkPill</c>, <c>AccentBrush</c>, <c>TextSecondary</c>, <c>FontSizeCaption</c>),
    /// and a rename of any of them would throw only when a player already had something go
    /// wrong, which is the worst possible moment to find out.
    /// </summary>
    [Fact]
    public void SupportLink_Builds()
    {
        var error = RunOnStaThread(() =>
        {
            var pill = SupportLink.Build();
            Assert.NotNull(pill.Style);
            // The full url in the tooltip is the anti-phishing measure, not decoration: a label
            // can claim anything, so the destination has to be visible.
            Assert.NotNull(pill.ToolTip);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The two post-match panels the guest sees are assembled in code, exactly like the card and
    /// the support pill, so a green build is no evidence either one can be shown.
    ///
    /// <para>They resolve <c>MpTextFaint</c>, <c>FontSizeCaption</c> and <c>MpSecondaryButton</c>
    /// by name, and they appear at the one moment where a throw is most expensive: the player has
    /// just finished a rated match and is waiting to be told who won. Nothing else would exercise
    /// them — the smoke launch only opens MainWindow, and these live in the lobby window, which is
    /// not built until somebody signs in and enters a room.</para>
    /// </summary>
    [Fact]
    public void ThePostMatchWaitingPanelsResolveTheirResources()
    {
        var error = RunOnStaThread(() =>
        {
            // Mirrors ShowResultWaitingForHost / ShowResultUnavailable. Those are private methods
            // on MultiplayerTab and need a live lobby window, so what is pinned here is the part
            // that can actually fail on its own: every resource they look up by name exists.
            var faint = Application.Current.FindResource("MpTextFaint");
            var caption = Application.Current.FindResource("FontSizeCaption");
            var secondary = Application.Current.FindResource("MpSecondaryButton");

            Assert.IsAssignableFrom<System.Windows.Media.Brush>(faint);
            Assert.IsType<double>(caption);
            var style = Assert.IsType<Style>(secondary);

            var button = new Button { Content = "x", Style = style };
            var text = new TextBlock
            {
                Text = "x",
                Foreground = (System.Windows.Media.Brush)faint,
                FontSize = (double)caption,
            };
            var stack = new StackPanel();
            stack.Children.Add(text);
            stack.Children.Add(button);

            Assert.Equal(2, stack.Children.Count);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The per-player line under a History row, built for real.
    ///
    /// <para>Assembled in code like <see cref="MatchResultCard"/>, so a resource key that
    /// does not resolve throws when it is BUILT — and the History subtab is not a surface
    /// the startup smoke test ever reaches. The three verdicts take different branches: only
    /// the decided ones paint a verdict at all, and each reaches a different brush.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void HistoryPlayerRow_BuildsForEveryVerdict(double result)
    {
        var error = RunOnStaThread(() =>
        {
            var line = MatchParticipantsView.Build(new List<MatchHistoryParticipant>
            {
                new()
                {
                    UserId = "me", DisplayName = "Gorgorito", DiscordUsername = "gorgorito",
                    Result = result, RatingBefore = 1617, RatingAfter = 1500,
                },
            }, "me").Single();

            Assert.NotNull(MultiplayerTab.BuildHistoryPlayerRow(line));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The degraded shape: somebody else, no avatar, no rating either side, nothing decided.
    /// Every one of those is a branch that omits an element rather than painting one, which
    /// makes this the row most likely to be built wrong and never noticed.
    /// </summary>
    [Fact]
    public void HistoryPlayerRow_BuildsWithNothingKnown()
    {
        var error = RunOnStaThread(() =>
        {
            var line = new MatchParticipantLine(
                "someone", "?", null, IsMe: false, MatchVerdict.NoResult, RatingDelta: null);

            Assert.NotNull(MultiplayerTab.BuildHistoryPlayerRow(line));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The install dialog, with and without a mod to copy settings from.
    ///
    /// <para>The dialog gained an optional row, and <b>the case that matters is the empty
    /// one</b>: somebody installing their first mod has nothing to copy from, so the row must
    /// collapse rather than offer an empty combo. Getting that backwards would put a dead
    /// control in front of every new player, in the one dialog they cannot avoid.</para>
    ///
    /// <para>It is also the only automated cover this window has — the startup smoke test never
    /// opens it, so a resource key that does not resolve would first be seen by someone trying
    /// to install.</para>
    ///
    /// <para><b>NO AoE3 source is passed, and that is not incidental — do not "improve" it by
    /// adding one.</b> With a source the constructor kicks <c>MeasureCloneSizeAsync</c>, whose
    /// continuation comes back through the captured SynchronizationContext. This harness runs a
    /// bare STA thread with no Dispatcher, so there is none: the continuation resumes on the
    /// thread pool, touches a TextBox it does not own, and the unhandled exception takes down the
    /// whole test host — every other test in the run disappears with it, and the run still
    /// reports success with a smaller total. Passing no source keeps that method synchronous,
    /// which is exactly the shape an in-place overlay install uses anyway.</para>
    /// </summary>
    [Fact]
    public void InstallFolderDialog_HidesTheCopySettingsRowWithNoSources()
    {
        var error = RunOnStaThread(() =>
        {
            var none = new InstallFolderDialog(
                @"C:\Games\Wars of Liberty", null, null,
                "Wars of Liberty", requiresAoe3Source: false, settingsSources: null);
            Assert.Equal(Visibility.Collapsed, none.CopySettingsRow.Visibility);
            none.Close();

            var some = new InstallFolderDialog(
                @"C:\Games\Wars of Liberty", null, null,
                "Wars of Liberty", requiresAoe3Source: false,
                settingsSources: new List<ModProfile>
                {
                    new() { Id = "improvement-mod", DisplayName = "Improvement Mod" },
                });
            Assert.Equal(Visibility.Visible, some.CopySettingsRow.Visibility);
            Assert.Single(some.CopySettingsCombo.Items);

            // Unticked, and the combo inert with it: the default has to be "don't copy". A row
            // that arrives already agreeing to write into the player's profile is not a question.
            Assert.False(some.CopySettingsCheck.IsChecked);
            Assert.False(some.CopySettingsCombo.IsEnabled);
            some.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The whole Multiplayer tab, parsed.
    ///
    /// <para>This is the broadest guard in the file and the cheapest: constructing the
    /// control runs <c>InitializeComponent</c>, which parses every <c>{StaticResource}</c>
    /// in the largest XAML in the launcher, and then <c>ApplyStrings</c>, which touches
    /// dozens of named elements. A key that does not resolve throws HERE instead of on a
    /// player's screen.</para>
    ///
    /// <para>It is the only automated cover for resources reached by the XAML alone and by
    /// no code-built card — <c>MpActivityHeadlineSize</c> is one. The tab does live inside
    /// MainWindow, so the startup smoke test would also catch it, but that test cannot run
    /// while a launcher is already open: the single-instance guard makes the second process
    /// exit successfully without parsing anything.</para>
    ///
    /// <para>Safe to construct with no session: the constructor only lays itself out and
    /// wires handlers. Everything that needs a backend waits for <c>Attach</c>.</para>
    /// </summary>
    [Fact]
    public void MultiplayerTab_ParsesItsWholeXaml()
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.NotNull(tab.Content);

            // The strip's own pieces, by name: renaming one of these breaks
            // LayOutActivityColumns SILENTLY at runtime, since it finds them by field.
            Assert.NotNull(tab.ActivityStrip);
            Assert.NotNull(tab.ActivityColRecent);
            Assert.NotNull(tab.ActivityColMiddle);
            Assert.NotNull(tab.ActivityColPeak);
            Assert.NotNull(tab.ActivityDividerLeft);
            Assert.NotNull(tab.ActivityDividerRight);
            Assert.NotNull(tab.ActivityMiddleCard);
            Assert.NotNull(tab.ActivityTotalsList);
            Assert.NotNull(tab.ActivityRankingEmpty);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The community-activity strip's two code-built rows.
    ///
    /// <para>Same reason as the card above: they resolve their brushes and sizes by name at
    /// BUILD time, and the strip lives at the bottom of the Rooms subtab, which the startup
    /// smoke test never opens. They also reach the three sizes this panel does not share
    /// with the rest of the tab — <c>MpActivityTitleSize</c>, <c>MpActivityBodySize</c>,
    /// <c>MpActivityHeadlineSize</c> — so a mistyped key would be invisible until a player
    /// with a signed-in session opened the tab.</para>
    ///
    /// <para>Both branches are exercised: a decided duel writes "X beat Y" in one brush, an
    /// unreadable match writes the mod and the map in another.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, 0.5)]
    public void CommunityMatchRow_BuildsForDecidedAndUndecided(double a, double b)
    {
        var error = RunOnStaThread(() =>
        {
            var match = new CommunityMatch
            {
                Id = "m1",
                ModId = "wol",
                MapName = "ESOC_Fertile Crescent",
                ReportedAt = DateTime.UtcNow.AddHours(-2).ToString("s"),
            };
            match.Participants.Add(new MatchHistoryParticipant
            {
                UserId = "u1", DisplayName = "Alucard", Result = a,
            });
            match.Participants.Add(new MatchHistoryParticipant
            {
                UserId = "u2", DisplayName = "Gorgorito", Result = b,
            });

            Assert.NotNull(MultiplayerTab.BuildCommunityMatchRow(match));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The degraded shape: no map, no participants, and a timestamp that does not parse —
    /// every segment of the meta line absent at once, which is the row most likely to be
    /// built wrong and never seen.
    /// </summary>
    [Fact]
    public void CommunityMatchRow_BuildsWithNothingKnown()
    {
        var error = RunOnStaThread(() =>
        {
            Assert.NotNull(MultiplayerTab.BuildCommunityMatchRow(new CommunityMatch()));
            Assert.NotNull(MultiplayerTab.BuildTotalsLine("47 partidas \u00b7 30 d"));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The "you are running as another Windows account" notice.
    ///
    /// <para>Worth a case of its own because of WHERE it opens: only on a machine whose accounts
    /// are already tangled. Without this, an unresolved <c>{StaticResource}</c> in it would first
    /// be seen by the one person least able to report what happened — and the launcher would have
    /// thrown while explaining why their recordings went missing.</para>
    ///
    /// <para>The second half is the half that matters. The other account's folder is resolved
    /// exactly or not at all (see <c>RunningAccount.ProfileFolderOf</c>), so "not at all" is a
    /// normal outcome, and the caption has to disappear WITH the box — a heading over an empty
    /// field reads as a bug in the dialog rather than as a path we could not confirm.</para>
    /// </summary>
    [Fact]
    public void CrossUserAccountDialog_CollapsesTheOtherFolderWhenThereIsNone()
    {
        var error = RunOnStaThread(() =>
        {
            var info = new WarsOfLibertyLauncher.Services.RunningAccount.AccountInfo(
                "a-admin", "Miro", Elevated: true, Mismatch: true);

            var both = new CrossUserAccountDialog(
                info,
                @"C:\Users\a-admin\Documents\My Games\Wars of Liberty",
                @"C:\Users\Miro\Documents\My Games");
            Assert.Equal(Visibility.Visible, both.OtherLabel.Visibility);
            Assert.Equal(Visibility.Visible, both.OtherPathText.Visibility);
            // Both accounts are named, or the reader cannot tell which folder is which.
            Assert.Contains("a-admin", both.BodyText.Text);
            Assert.Contains("Miro", both.BodyText.Text);
            both.Close();

            var unresolved = new CrossUserAccountDialog(
                info, @"C:\Users\a-admin\Documents", null);
            Assert.Equal(Visibility.Collapsed, unresolved.OtherLabel.Visibility);
            Assert.Equal(Visibility.Collapsed, unresolved.OtherPathText.Visibility);
            unresolved.Close();
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on an STA thread with the launcher's resource
    /// dictionaries loaded, and returns the exception it threw (null when it didn't).
    /// </summary>
    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureResources();
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Generous: the first WPF touch in a process pays for the framework's own
        // initialisation. A hang is a failure too, so it is bounded.
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the STA thread did not finish");
        return captured;
    }

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application();
        if (app.Resources.MergedDictionaries.Count > 0) return;
        foreach (var name in new[] { "Tokens", "Colors", "Chrome", "Buttons", "Inputs" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/Aoe3ModLauncher;component/Styles/{name}.xaml",
                    UriKind.Absolute),
            });
        }
        // App.xaml's inline resources (the FontSize scale and the font families) are not
        // in any dictionary file, so they are recreated here rather than parsed.
        app.Resources["FontSizeCaption"] = 13.0;
        app.Resources["FontSizeBody"] = 14.0;
        app.Resources["FontSizeBodyStrong"] = 15.0;
        app.Resources["FontSizeSubtitle"] = 16.0;
        app.Resources["FontSizeTitle"] = 18.0;
        app.Resources["FontSizeHeading"] = 24.0;
        app.Resources["FontSizeDisplay"] = 34.0;
        app.Resources["DisplayFont"] = new System.Windows.Media.FontFamily("Cambria, Georgia");
        app.Resources["BodyFont"] = new System.Windows.Media.FontFamily("Segoe UI, Tahoma");
        app.Resources["MonoFont"] = new System.Windows.Media.FontFamily("Consolas, Courier New");
    }
}
