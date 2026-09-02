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
using WarsOfLibertyLauncher.Localization;
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

            // The format row is ALWAYS on screen now — it used to be revealed by the tick, which
            // made one decision take two steps and jumped the dialog's height.
            Assert.NotNull(dlg.CompetitiveFormatRow);
            Assert.Equal(Visibility.Visible, dlg.CompetitiveFormatRow.Visibility);
            Assert.Equal(3, dlg.FormatRow.Children.Count);   // 1v1 / 2v2 / 3v3

            // AND NOTHING IS LIT, which is the invariant that being visible put at risk. A
            // casual room has declared no format, and showing 1v1 highlighted would both
            // contradict the "Max players: 8" just above it and assert the one thing this model
            // refuses to assert — that a two-seat casual room IS a 1v1 (see RoomFormats).
            Assert.All(dlg.FormatRow.Children.OfType<Button>(),
                b => Assert.Null(b.Tag as string));
            Assert.NotNull(dlg.MaxPlayersRow);
            Assert.True(dlg.MaxPlayersRow.Children.Count > 0);

            // And the note under it says nothing until a format is chosen — it carries either
            // the 1v1 forfeit clause or the team "does not rate yet" line, and neither applies
            // to a casual room.
            Assert.Equal(Visibility.Collapsed, dlg.CompetitiveSizeNote.Visibility);

            // And a casual room still opens at the full eight seats. The format row picks a
            // format at construction — so that ticking the box lands on something rather than on
            // nothing — and that pick moves the size, so without the competitive guard on it
            // every room would have quietly opened as a two-player one. Making the row visible
            // did not touch that guard, and this is what proves it.
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
    /// PICKING A FORMAT DECLARES THE ROOM COMPETITIVE — the whole point of the row being on
    /// screen for a casual room, and the half that compiles perfectly while doing nothing.
    ///
    /// <para>The format only means something for a competitive room, so a click that lit a
    /// segment and left the box unticked would be a control that looks broken: you would pick
    /// 2v2, watch nothing else move, and still have to go find the checkbox. One click has to
    /// carry the whole decision — the box, the format, and the four seats that format IS.</para>
    ///
    /// <para>Asserted through the real Click event rather than by calling the handler, because
    /// what is being pinned is the wiring: the handler could be perfect and simply not attached.
    /// </para>
    /// </summary>
    [Fact]
    public void PickingAFormatMakesTheRoomCompetitive()
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

            // Starts casual and eight-seat, as the test above pins in detail.
            Assert.NotEqual(true, dlg.CompetitiveCheck.IsChecked);

            // 1v1 / 2v2 / 3v3, in that order — so this is 2v2, chosen because its four seats
            // differ from BOTH the eight the room opens on and the two a 1v1 would give: a
            // handler that did nothing, and one that fell back to the default format, both fail.
            var twoVTwo = dlg.FormatRow.Children.OfType<Button>().ElementAt(1);
            twoVTwo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.True(dlg.CompetitiveCheck.IsChecked);
            Assert.Equal("active", twoVTwo.Tag as string);

            var active = dlg.MaxPlayersRow.Children.OfType<Button>()
                .Where(b => (b.Tag as string) == "active")
                .Select(b => b.Content as string)
                .ToList();
            Assert.Equal(new[] { "4" }, active);

            // The seat row belongs to the format now, and the note says what a team match needs.
            Assert.All(dlg.MaxPlayersRow.Children.OfType<Button>(), b => Assert.False(b.IsEnabled));
            Assert.Equal(Visibility.Visible, dlg.CompetitiveSizeNote.Visibility);

            // Unticking gives the seats back and leaves NO format showing — the room stopped
            // being one that has a format, so the row must stop claiming it has one.
            dlg.CompetitiveCheck.IsChecked = false;
            Assert.All(dlg.FormatRow.Children.OfType<Button>(), b => Assert.Null(b.Tag as string));
            Assert.All(dlg.MaxPlayersRow.Children.OfType<Button>(), b => Assert.True(b.IsEnabled));
            Assert.Equal(Visibility.Collapsed, dlg.CompetitiveSizeNote.Visibility);

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
    /// The subtitle's civilization segment, in all three states it can be in.
    ///
    /// <para><b>The both-null case is the one that matters</b> — it is every match stored before
    /// civilizations were reported and every match of a mod that ships no loose civ list, so it
    /// has to render byte for byte what it always did. And the mine-only case exists because
    /// <c>Strings.Format</c> would happily print a matchup with an empty second half; the card
    /// is supposed to fall back to the bare name instead.</para>
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("Chinese", null)]
    [InlineData("Chinese", "Colombians")]
    public void MatchResultCard_BuildsWithAndWithoutCivilizations(string? mine, string? theirs)
    {
        var error = RunOnStaThread(() =>
        {
            var model = new MatchOutcomeView(
                MatchVerdict.Win, "wol", "ESOC_Tibet", 916, 2,
                RatingBefore: 1500, RatingAfter: 1617,
                RivalLogin: "alucard", RivalRating: 1383,
                Wins: 1, Losses: 0, Rd: 110,
                MyCiv: mine, RivalCiv: theirs);

            Assert.NotNull(MatchResultCard.Build(
                model, new MatchResultCard.Actions(null, () => { })));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A row of the CIVS table on the Clasificación page, and the same percentage rule the
    /// Profile card follows — the two surfaces must never disagree about when there is enough
    /// behind a civilization to state a rate.
    ///
    /// <para>It also pins that a row's columns match the table definition. Header and rows are
    /// built by one method so drift is structurally impossible today; this is what notices if
    /// somebody inlines one of them.</para>
    /// </summary>
    [Theory]
    [InlineData(2, 1, 1, false)]    // two decided games: no rate
    [InlineData(9, 6, 3, true)]     // past the bar
    public void CivRow_ShowsAPercentageOnlyWhenThereIsEnoughBehindIt(
        int played, int wins, int losses, bool expectPercent)
    {
        var error = RunOnStaThread(() =>
        {
            var row = (Border)MultiplayerTab.BuildCivRow(new CivStatEntry
            {
                ModId = "wol",
                ModVersion = "abc123",
                Civ = "Chinese",
                Played = played,
                Wins = wins,
                Losses = losses,
                AvgSeconds = 900,
            });

            var grid = (Grid)row.Child;
            Assert.Equal(CivTableLayout.All.Count, grid.ColumnDefinitions.Count);

            var cells = grid.Children.OfType<TextBlock>().ToList();
            Assert.Equal("Chinese", RevealText.PlainTextOf(cells.Single(t => Grid.GetColumn(t) == 0)));
            Assert.Equal($"{wins}-{losses}",
                RevealText.PlainTextOf(cells.Single(t => Grid.GetColumn(t) == 2)));

            var percent = RevealText.PlainTextOf(cells.Single(t => Grid.GetColumn(t) == 3));
            Assert.Equal(expectPercent, percent.Contains('%'));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A row of the Profile's YOUR CIVILIZATIONS card, above and below the percentage bar.
    ///
    /// <para><b>The thin row is the one that matters</b> — for months almost every civilization
    /// will have two or three matches, so that is the shape most players see, and it must draw
    /// NOTHING where the percentage would go rather than an em dash or a 0.</para>
    /// </summary>
    [Theory]
    [InlineData(1, 0, false)]    // one decided game: no rate
    [InlineData(6, 2, true)]     // past the bar
    public void ProfileCivRow_ShowsAPercentageOnlyWhenThereIsEnoughBehindIt(
        int wins, int losses, bool expectPercent)
    {
        var error = RunOnStaThread(() =>
        {
            var row = MultiplayerTab.BuildProfileCivRow(
                new CivStatRow("Chinese", wins + losses, wins, losses));

            Assert.NotNull(row);

            var cells = ((Grid)row).Children.OfType<TextBlock>().ToList();
            Assert.Equal("Chinese", RevealText.PlainTextOf(cells[0]));

            var percent = cells.Single(t => Grid.GetColumn(t) == 2);
            Assert.Equal(expectPercent, RevealText.PlainTextOf(percent).Contains('%'));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A card on the STATISTICS tab, in the three shapes the store really holds.
    ///
    /// <para><b>The zeroed case is the one that matters</b> — every game but the newest in a
    /// personality file has its totals wiped by AoE3, so most imported games arrive with real
    /// unit counts and no resources at all. That card must build, and it must not print
    /// "0 shipments", which would be a statement rather than an absence.</para>
    ///
    /// <para>Nothing else in the launcher builds this and the smoke test never opens the tab, so
    /// a resource looked up by name would fail for the first time in front of a player.</para>
    /// </summary>
    [Theory]
    [InlineData(true, 664331, 42)]     // the newest game: everything recorded
    [InlineData(false, 0, 0)]          // an older game: units only
    [InlineData(null, 0, 0)]           // a block with no result at all
    public void ModPropertiesAiGameCard_BuildsForEveryShape(bool? won, int score, int shipments)
    {
        var error = RunOnStaThread(() =>
        {
            var game = new AiGameRecord
            {
                Personality = "wolMenelik",
                ModId = "wol",
                PlayerName = "Gorgorito",
                DurationMs = 1067806,
                Won = won,
                Score = score,
                Shipments = shipments,
                Gold = score > 0 ? 300820 : 0,
                Units = new Dictionary<string, int> { ["gwtank"] = 56, ["hussar"] = 31 },
            };

            var card = ModPropertiesDialog.BuildAiGameCard(
                game, new Dictionary<string, string> { ["gwtank"] = "Tank" });

            Assert.NotNull(card);

            // The unresolved proto falls back to its internal name — unlike a civilization,
            // which must go blank rather than print a number nobody can read.
            var text = string.Join(" ", ((StackPanel)card.Child).Children
                .OfType<TextBlock>()
                .Select(RevealText.PlainTextOf));
            Assert.Contains("Tank", text, StringComparison.Ordinal);
            Assert.Contains("hussar", text, StringComparison.Ordinal);
            Assert.Equal(shipments > 0, text.Contains("42", StringComparison.Ordinal));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A deck card on the STATISTICS tab, built from the game's own home city file.
    ///
    /// <para><b>The order is the assertion that matters.</b> A deck is a sequence the player
    /// arranged, and it is the one thing this file carries that nothing else does — a sort would
    /// still print every card correctly and destroy it silently. So the card is checked for the
    /// cards appearing in the file's order, not merely for appearing.</para>
    ///
    /// <para>Nothing else in the launcher builds this and the smoke test never opens the tab, so
    /// a resource looked up by name would fail for the first time in front of a player.</para>
    /// </summary>
    [Fact]
    public void ModPropertiesDeckCard_BuildsAndKeepsTheDeckOrder()
    {
        var error = RunOnStaThread(() =>
        {
            var profile = new HomeCityProfile { Civ = "Chinese", CityName = "Beijing", Level = 16 };
            var deck = new HomeCityDeckEntry
            {
                Name = "Static Deck",
                Cards =
                {
                    new HomeCityCard { Slot = 0, Dbid = 4128, InternalName = "YPHCExpandedTradingPost" },
                    new HomeCityCard { Slot = 1, Dbid = 2212, InternalName = "HCShipWoodCrates3" },
                    new HomeCityCard { Slot = 2, Dbid = 52905, InternalName = "WOLHCShipTigermen2" },
                },
            };

            var card = ModPropertiesDialog.BuildDeckCard(
                profile, deck,
                new Dictionary<string, string> { ["HCShipWoodCrates3"] = "3 Wood Crates" });

            Assert.NotNull(card);

            var text = string.Join(" ", ((StackPanel)card.Child).Children
                .OfType<TextBlock>()
                .Select(RevealText.PlainTextOf));

            Assert.Contains("Chinese", text, StringComparison.Ordinal);
            Assert.Contains("Beijing", text, StringComparison.Ordinal);
            Assert.Contains("Static Deck", text, StringComparison.Ordinal);

            // The resolved name replaces the internal one; an unresolved card keeps its own,
            // which still identifies it to anyone who mods.
            Assert.Contains("3 Wood Crates", text, StringComparison.Ordinal);
            Assert.Contains("WOLHCShipTigermen2", text, StringComparison.Ordinal);

            // ...and in the deck's order, which is not alphabetical and not by id.
            var first = text.IndexOf("YPHCExpandedTradingPost", StringComparison.Ordinal);
            var second = text.IndexOf("3 Wood Crates", StringComparison.Ordinal);
            var third = text.IndexOf("WOLHCShipTigermen2", StringComparison.Ordinal);
            Assert.True(first < second && second < third,
                $"the deck was reordered: {first}, {second}, {third}");
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A roster line under a history row, with and without a civilization.
    ///
    /// <para>The civ is appended as a <c>Run</c> of the name's own TextBlock rather than given a
    /// column, so this checks the two things that arrangement can break: that the row still
    /// builds, and that the name is still the FIRST thing in it — the ellipsis eats from the
    /// end, and whose name it is matters more than what they played.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Colombians")]
    public void HistoryPlayerRow_BuildsWithAndWithoutACivilization(string? civ)
    {
        var error = RunOnStaThread(() =>
        {
            var row = MultiplayerTab.BuildHistoryPlayerRow(new MatchParticipantLine(
                "u-1", "Gorgorito", null, IsMe: true, MatchVerdict.Win, RatingDelta: 117,
                Team: 0, Civ: civ));

            Assert.NotNull(row);

            // Column 1 is the name cell — the only one the civilization is allowed to join.
            var name = ((Grid)row).Children.OfType<TextBlock>()
                .Single(t => Grid.GetColumn(t) == 1);
            var text = RevealText.PlainTextOf(name);

            Assert.StartsWith("Gorgorito", text, StringComparison.Ordinal);
            Assert.Equal(civ != null, text.Contains("Colombians", StringComparison.Ordinal));
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
            EnsureResources();
            var pill = SupportLink.Build();
            Assert.NotNull(pill.Style);
            // The full url in the tooltip is the anti-phishing measure, not decoration: a label
            // can claim anything, so the destination has to be visible.
            Assert.NotNull(pill.ToolTip);

            // The optional size exists so ONE host — the diagnostics row, whose neighbours run
            // on the smaller settings scale — can match its row without a second builder. It
            // must reach the button, or that host silently keeps the default and the Spanish
            // caption goes back over the edge of the card.
            var sized = SupportLink.Build(11.5);
            Assert.Equal(11.5, sized.FontSize);
            Assert.NotNull(sized.Style);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The DIAGNOSTICS row of the mod properties window has to fit its three actions at the
    /// window's narrowest, in the widest language.
    ///
    /// <para>It did not: "¿Necesitas ayuda? Pregunta en Discord" is 36 characters against the
    /// English 25, and a horizontal <c>StackPanel</c> measures its children with INFINITE
    /// width, so nothing negotiates — the pill asked for its full width, was arranged at it,
    /// and the card clipped it mid-word. Nothing in that row trims and a Button cannot
    /// ellipsise its own caption. Same failure, same shape, as the rooms toolbar and the
    /// Workshop's filter chips.</para>
    ///
    /// <para><b>Spanish explicitly</b>: in English the row fits with room to spare and this
    /// test passes over a broken layout.</para>
    /// </summary>
    [Fact]
    public void TheDiagnosticsRowFitsAtTheNarrowestWindow()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");

                // The structural half. Measuring alone cannot catch this class of defect —
                // DesiredSize is clamped to the constraint, so an overflow reports as a fit —
                // which is why the panel TYPE is what is asserted.
                var pill = SupportLink.Build(11.5);
                var row = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var caption in new[]
                         {
                             Strings.Get("ModPropViewLogs"),
                             Strings.Get("ModPropShareDiagnostics"),
                         })
                {
                    row.Children.Add(new Button
                    {
                        Content = caption,
                        Style = (Style)Application.Current!.Resources["SetActionButtonLg"],
                        Width = double.NaN,
                        MinWidth = 120,
                        Padding = new Thickness(14, 0, 14, 0),
                        Margin = new Thickness(0, 0, 10, 8),
                    });
                }
                row.Children.Add(pill);

                // The narrowest the row can be: the mod window's MinWidth, minus its rail,
                // the content padding, the card border and the row padding.
                const double narrowest = 780 - 206 - 40 - 2 - 28;
                row.Measure(new Size(narrowest, double.PositiveInfinity));
                row.Arrange(new Rect(0, 0, narrowest, row.DesiredSize.Height));
                row.UpdateLayout();

                // Every child inside the row's own bounds. A WrapPanel that has to wrap is a
                // pass; a child hanging past the right edge is the reported defect.
                foreach (FrameworkElement child in row.Children)
                {
                    var origin = child.TranslatePoint(new Point(0, 0), row);
                    // Name the offender in words: the pill's Content is a StackPanel of three
                    // TextBlocks, so printing it says nothing about which control ran over.
                    var who = (child as ContentControl)?.Content as string ?? "the support pill";
                    Assert.True(origin.X + child.ActualWidth <= narrowest + 0.5,
                        $"'{who}' ends at {origin.X + child.ActualWidth:0} in a {narrowest:0} px "
                        + "row, so it is clipped by the card. A horizontal StackPanel measures at "
                        + "infinity and nothing here can trim — the row has to wrap.");
                }
            }
            finally
            {
                Strings.SetLanguage(previous);
            }
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
    /// One row of the Clasificación table, built for real, in both of its states.
    ///
    /// <para>Same reason as the player line below: it is assembled in code, so a resource key
    /// that does not resolve throws when it is BUILT and nothing at compile time can see it —
    /// and this table is only ever drawn after somebody signs in and opens a subtab the
    /// startup smoke test never reaches. The two branches paint different brushes (first place
    /// is gold, the viewer's own row is tinted and blue) and different bar lengths.</para>
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(7, true)]
    public void RankingRow_Builds(int rank, bool isMe)
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var row = new LeaderboardRow
            {
                Rank = rank,
                UserId = "me",
                DisplayName = "Gorgorito12",
                DiscordUsername = "gorgorito_12",
                Rating = 1383,
                Rd = 286,
                GamesPlayed = 6,
                Wins = 2,
                Losses = 4,
            };

            Assert.NotNull(tab.BuildLeaderboardRow(row, 1383, 1604, isMe));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A history card, in the two shapes that take different branches: one that counted, with
    /// a roster and a delta, and one that did not — which is the one that also builds the
    /// amber note and the neutral tag, and is the majority of stored matches.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HistoryCard_Builds(bool rated)
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            var row = new MatchHistoryRow
            {
                Id = "m1",
                ModId = "wol",
                MapName = "ESOC_Fertile_Crescent",
                StartedAt = "2026-08-29T17:50:00Z",
                EndedAt = "2026-08-29T19:09:00Z",
                Result = rated ? 0.0 : 0.5,
                Rated = rated,
                UnratedReason = rated ? null : "not_competitive",
                RatingBefore = rated ? 1500 : null,
                RatingAfter = rated ? 1383 : null,
                Participants = new List<MatchHistoryParticipant>
                {
                    new() { UserId = "me", DisplayName = "Gorgorito12", Result = rated ? 0.0 : 0.5 },
                    new() { UserId = "alu", DisplayName = "Aluclown", Result = rated ? 1.0 : 0.5 },
                },
            };

            Assert.NotNull(tab.BuildHistoryRow(row, "me"));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The profile header — the one card that carries a gradient, a rounded-square avatar and
    /// the 30-px rating, none of which appears anywhere else in the launcher.
    ///
    /// <para>Built with NO standing on purpose: that is the state a player sees while the
    /// fetch is in flight, and it is the branch that omits elements rather than painting
    /// them, which makes it the one most likely to be wrong and never noticed.</para>
    /// </summary>
    [Fact]
    public void ProfileHeader_BuildsWithNoStandingYet()
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            Assert.NotNull(tab.BuildProfileHeader(new LobbyUserSummary
            {
                Id = "me",
                DisplayName = "Gorgorito12",
                DiscordUsername = "gorgorito_12",
                CreatedAt = "2026-08-01T00:00:00Z",
            }));
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
            Assert.NotNull(tab.ActivityColPeak);
            Assert.NotNull(tab.ActivityColRecent);
            Assert.NotNull(tab.ActivityColMiddle);
            // The gaps replaced the vertical rules when the strip went to the handoff's
            // three cards, and they collapse with their card for the same reason the
            // columns do — so losing one of these names breaks the layout just as quietly.
            Assert.NotNull(tab.ActivityGapLeft);
            Assert.NotNull(tab.ActivityGapRight);
            Assert.NotNull(tab.ActivityMiddleCard);
            Assert.NotNull(tab.ActivityStripTotals);
            Assert.NotNull(tab.ActivityRankingEmpty);
            Assert.NotNull(tab.ActivityRankingSeeAll);
            Assert.NotNull(tab.ActivityPeakLine);

            // NONE of the three cards may stretch. They share one grid row, where a Border
            // fills the row by default — so the shortest card was drawn as tall as the
            // tallest, which painted the ranking as a ~200-px empty box under two lines of
            // text. Measured on this very tree: stretched, all three came out at 297 px;
            // top-aligned they are 129 / 225 / 110. Losing this property costs no build
            // error and no test but the one, and looks like the panel grew back.
            foreach (var card in new[]
                     { tab.ActivityPeakCard, tab.ActivityRecentCard, tab.ActivityMiddleCard })
            {
                Assert.Equal(VerticalAlignment.Top, card.VerticalAlignment);
            }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The rooms top bar's two groups — the subtabs and the tool cluster — must FIT side by side
    /// at the narrowest window the app allows.
    ///
    /// <para>They share one 48-px row as `*` + `Auto`, and NEITHER has TextTrimming. So the Auto
    /// cluster takes its full width first and the star strip is arranged at its desired size and
    /// then clipped at the column edge, with the cluster painting over the same pixels. The
    /// symptom is the CLASIFICACION tab reading "CLAS" with the room-code box sitting on top of
    /// it, which is what shipped: adding that field cost ~154 px this row did not have.</para>
    ///
    /// <para><b>Measured in Spanish, which is the wide language</b> (CLASIFICACION vs RANKING is
    /// 93 px against 58). And measured against a FIXED budget rather than the window, because
    /// UiScale lays this tab out at a scaled logical size: the transform pins the logical bar at
    /// ~1072 px for every window between the 900-px minimum and the 1100-px default, so that is
    /// simultaneously the worst case and the common one — making the window smaller does not
    /// make this worse, and the default size is already it.</para>
    ///
    /// <para>Nothing else can catch this. It is not an overflow (a star column that shrinks
    /// reports nothing, the same blindness the tab's own overflow diagnostic has), it throws
    /// nothing, and it looks fine on a wide monitor.</para>
    ///
    /// <para><b>AND IT WAS ITSELF BLIND FOR A WHILE, which is worth knowing before trusting a
    /// number this test reports.</b> The three text buttons in the cluster take their size from
    /// MpSecondaryButton / MpPrimaryButton, whose Setter said <c>{StaticResource FontSizeBody}</c>
    /// — and in this harness that reference did not resolve, so they measured at the WPF default
    /// of 12 while the shipped app painted them at 14. The bar was therefore ~22 px over budget
    /// in reality and passing here. Moving every font size to <c>{DynamicResource}</c> for the
    /// text-size setting (see <c>TextScaleTests</c>) fixed the harness, the overlap appeared, and
    /// the cluster's captions were taken down to the multiplayer scale they should always have
    /// been on. A green result here means what it says only because the harness now measures the
    /// same sizes the app paints.</para>
    /// </summary>
    /// <summary>
    /// The three rebuilt multiplayer pages FILL the window — and the ladder's flexible column
    /// is the one that can absorb the surplus.
    ///
    /// <para><b>Both halves belong in one test because either alone can be satisfied by
    /// breaking the other.</b> Filling the window is what was asked for, three rounds running;
    /// what makes it safe is that RATING grows and PLAYER is capped, so a wide window lengthens
    /// the comparative bar instead of stranding a name 1500 px from its own rating. Flip the
    /// flexible column back to PLAYER — the obvious reading of the handoff's fixed-width mockup
    /// — and the pages still "fill the window" while reproducing the exact defect the rebuild
    /// started from, with a green build and no error anywhere.</para>
    ///
    /// <para>The page assertions are one XAML attribute each, which is the other reason: a
    /// tidy-up that puts a MaxWidth back reads as harmless in a diff.</para>
    /// </summary>
    [Fact]
    public void TheMultiplayerPagesFillTheWindowAndTheLadderGrowsByItsBar()
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();

            foreach (var (name, page) in new (string, FrameworkElement)[]
                     {
                         ("Ranking", tab.RankingPage),
                         ("Profile", tab.ProfileBody),
                     })
            {
                Assert.True(double.IsPositiveInfinity(page.MaxWidth),
                    $"{name} is bounded to {page.MaxWidth}: these pages fill the window now, "
                    + "and the bounding is what left more than half of it empty.");

                Assert.True(page.HorizontalAlignment == HorizontalAlignment.Stretch,
                    $"{name} is {page.HorizontalAlignment}, not Stretch, so it cannot fill "
                    + "the width it is given.");
            }

            var flexible = RankingTableLayout.All.Where(c => c.FixedWidth == null).ToList();
            Assert.Equal(2, flexible.Count);

            var player = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Player);
            var rating = RankingTableLayout.All.Single(c => c.Column == RankingColumn.Rating);

            Assert.True(player.MaxWidth is > 0,
                "PLAYER has no cap, so on a wide window the name takes the whole surplus and "
                + "its rating ends up an arm's length away — the defect this table was rebuilt "
                + "to fix.");
            Assert.True(rating.FixedWidth == null && rating.MaxWidth == null,
                "RATING is not the column that grows. Its cell holds the comparative bar, "
                + "which is the only thing here that gets MORE useful with more width.");
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The Workshop's filter strip cannot be painted over by the sort cluster.
    ///
    /// <para><b>Why this exists.</b> That row is <c>* | Auto</c> — the sort cluster takes its
    /// width first and whatever is in the star column is arranged at its own desired size and
    /// clipped at the column edge, with the cluster drawing over the same pixels. Nothing in
    /// the strip trims and a Button cannot ellipsise its caption, so as a horizontal
    /// StackPanel the chips had no way to give ground. It was invisible because a UiScale
    /// LayoutTransform pinned the whole Workshop's logical width at ~1100 px for every window
    /// from 900 up; with that gone the row gets the real width — about 852 px at the minimum
    /// window — and any text size above 100 % pushes it over.</para>
    ///
    /// <para><b>The type assertion is the load-bearing half, and the numbers cannot replace
    /// it.</b> <c>Measure</c> clamps <c>DesiredSize</c> to the constraint it is given, so a
    /// StackPanel that overflows reports a width that fits — the overflow is simply not
    /// visible from here. What IS checkable is that the strip is a panel that WRAPS, so it
    /// cannot overflow by construction, and that the Auto cluster still leaves the first line
    /// room for the label and the widest chip.</para>
    /// </summary>
    [Fact]
    public void TheWorkshopFiltersRowFitsAtTheNarrowestWindow()
    {
        var error = RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                // Spanish is the wide language here: "Actualizaciones", "No instalados".
                Strings.SetLanguage("es");
                var browser = new ModsBrowser();

                // The captions come from MainWindow, so the harness has to supply them or
                // every chip measures as an empty button.
                browser.FiltersLabelText = Strings.Get("ModsBrowserFiltersLabel");
                browser.SortLabelText = Strings.Get("ModsBrowserSortLabel");
                browser.SetFilterLabels(
                    Strings.Get("ModsBrowserFilterAll"),
                    Strings.Get("ModsBrowserFilterInstalled"),
                    Strings.Get("ModsBrowserFilterNotInstalled"),
                    Strings.Get("ModsBrowserFilterUpdates"),
                    Strings.Get("ModsBrowserFilterCompatible"));

                var strip = LogicalTreeHelper.GetParent(browser.FilterAll);
                Assert.True(strip is WrapPanel,
                    $"the filter strip is a {strip?.GetType().Name}, not a WrapPanel. In a "
                    + "`* | Auto` row nothing else can give ground: the chips do not trim and "
                    + "cannot ellipsise, so on a narrow window they go UNDER the sort box. "
                    + "Measuring will not catch this — DesiredSize is clamped to the "
                    + "constraint, so the overflow reports as a fit.");

                var sort = (FrameworkElement)LogicalTreeHelper.GetParent(browser.SortBox);
                sort.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var label = browser.FiltersLabel;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var widestChip = 0.0;
                foreach (var chip in new[]
                         {
                             browser.FilterAll, browser.FilterInstalled,
                             browser.FilterNotInstalled, browser.FilterUpdates,
                             browser.FilterCompatible,
                         })
                {
                    chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    widestChip = Math.Max(widestChip, chip.DesiredSize.Width);
                }

                // The 900-px minimum window, less the header's own 24-px side padding. NOT
                // divided by any scale factor — that is exactly what changed when the
                // Workshop's LayoutTransform was removed.
                const double budget = 900 - 48;
                var firstLine = sort.DesiredSize.Width + label.DesiredSize.Width + widestChip;

                Assert.True(firstLine <= budget,
                    $"the sort cluster ({sort.DesiredSize.Width:F0}), the label "
                    + $"({label.DesiredSize.Width:F0}) and the widest chip ({widestChip:F0}) "
                    + $"need {firstLine:F0} px of the {budget:F0} the narrowest window gives "
                    + "this row — so not even one chip fits beside the sort box and wrapping "
                    + "cannot save it. Take the width out of the sort box or a chip caption.");
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    [Fact]
    public void TheRoomsTopBarFitsAtTheNarrowestWindow()
    {
        var error = RunOnStaThread(() =>
        {
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var tab = new MultiplayerTab();

                var tabs = (FrameworkElement)LogicalTreeHelper.GetParent(tab.SubtabFriends);
                var cluster = (FrameworkElement)LogicalTreeHelper.GetParent(tab.CreateRoomButton);
                tabs.Measure(new Size(double.PositiveInfinity, 48));
                cluster.Measure(new Size(double.PositiveInfinity, 48));

                // The worst case, and it is NOT the smallest window. UiScale scales this tab by
                // min(w/1100, h/560) with a 0.82 floor, so the LOGICAL width is 1100 at the
                // 1100-px default and 900/0.82 = 1097.6 at the 900-px minimum — i.e. the bar is
                // ~1098 logical px wide across that whole range, and shrinking the window does not
                // shrink it further. Less the bar's own 10-px side padding.
                const double budget = 1097.6 - 20;
                var need = tabs.DesiredSize.Width + cluster.DesiredSize.Width;

                Assert.True(need <= budget,
                    $"the top bar needs {need:F0} px and has {budget:F0}: the subtab strip will be "
                    + "painted over by the tool cluster. Take the width out of padding, a caption, "
                    + "or the search box — but NOT out of the Radmin help button's word, which is "
                    + "a documented refusal.");
            }
            finally { Strings.SetLanguage(previous); }
        });

        Assert.Null(error);
    }

    /// <summary>
    /// The rooms list may NOT have a viewport of its own.
    ///
    /// <para>It had one, and on a short window that is what reduced it to a single 64-px row.
    /// The join-by-code box and the activity strip below it are Auto rows that take their
    /// height first, so the star row holding the list absorbed the whole shortfall while the
    /// strip kept every pixel: the list scrolled inside about one row, and the page did not
    /// scroll at all.</para>
    ///
    /// <para>Both halves are pinned because both fail silently. Re-adding a ScrollViewer
    /// around <c>RoomsListPanel</c> builds clean and looks right on a big monitor; so does
    /// removing the page one. And the header strip has to sit in the SAME viewport as the
    /// rows — that is what makes the old scrollbar-gutter compensation unnecessary, and
    /// re-adding that compensation now would push the header left of the rows it labels.</para>
    /// </summary>
    [Fact]
    public void TheRoomsListScrollsWithThePageAndNeverOnItsOwn()
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();

            static IEnumerable<DependencyObject> Ancestors(DependencyObject d)
            {
                for (var p = LogicalTreeHelper.GetParent(d); p != null;
                     p = LogicalTreeHelper.GetParent(p))
                    yield return p;
            }

            var overRows = Ancestors(tab.RoomsListPanel).OfType<ScrollViewer>().ToList();
            Assert.Single(overRows);
            Assert.Same(tab.RoomsPageScroll, overRows[0]);

            // ...and it is the same one for every part of the page: the column headers (or
            // the gutter compensation comes back), the footer and the strip. The join-by-code
            // field used to be here too and is deliberately NOT any more — it lives in the
            // toolbar now, outside the scroller, which is the point of having moved it.
            foreach (FrameworkElement part in new FrameworkElement[]
                     {
                         tab.RoomsHeaderStrip, tab.RoomsShowingCount, tab.ActivityStrip,
                     })
            {
                Assert.Same(tab.RoomsPageScroll,
                    Ancestors(part).OfType<ScrollViewer>().Single());
            }

            // The rows' left inset is the header's: 16 here plus 14 of row padding makes the
            // 30 the strip is inset by. It was the deleted scroller's Padding.
            Assert.Equal(16, tab.RoomsListPanel.Margin.Left);
            Assert.Equal(16, tab.RoomsListPanel.Margin.Right);
        });

        Assert.Null(error);
    }

    /// <summary>
    /// On a window too short for everything, the ROOMS keep the height and the join box and
    /// the strip go below the fold — not the other way round.
    ///
    /// <para>Measured, not eyeballed, and deliberately not a pixel count: the claim is that
    /// the block is as tall as the rows it holds, whatever that comes to. Ten rows against a
    /// 420-px window is the reported screenshot, where the block was handed about one row.
    /// It fails on the layout this replaced, and it fails again the moment anyone divides a
    /// fixed height between a star row and an Auto one here.</para>
    /// </summary>
    [Fact]
    public void AShortWindowShrinksThePageAndNotTheRoomsList()
    {
        var error = RunOnStaThread(() =>
        {
            var tab = new MultiplayerTab();
            const int rows = 10, rowHeight = 64;
            for (var i = 0; i < rows; i++)
                tab.RoomsListPanel.Children.Add(new Border { Height = rowHeight });
            // Collapsed until its data lands; visible is the case that hurt.
            tab.ActivityStrip.Visibility = Visibility.Visible;

            // Laid out DIRECTLY, not through the tab: nobody is signed in on a bare
            // MultiplayerTab, so the sign-in gate collapses everything under it and laying
            // out the tab measures nothing at all (every height comes back 0). 420 is the
            // viewport the reported short window gives this column.
            tab.RoomsPageScroll.Measure(new Size(1100, 420));
            tab.RoomsPageScroll.Arrange(new Rect(0, 0, 1100, 420));
            tab.RoomsPageScroll.UpdateLayout();

            Assert.True(
                tab.RoomsBlock.ActualHeight >= rows * rowHeight,
                $"the rooms block was squeezed to {tab.RoomsBlock.ActualHeight:0} px for "
                + $"{rows} rows: something below it is taking the height first");
            Assert.True(tab.RoomsPageScroll.ScrollableHeight > 0, "the page did not scroll");
            // And nothing re-adds the scrollbar gutter: the header is in the same viewport as
            // the rows, so it loses the same width and its inset stays a flat 30.
            Assert.Equal(30, tab.RoomsHeaderStrip.Margin.Right);
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
    /// THE AGE LABEL REGISTERS ITSELF, and the label it registers is the one actually in the
    /// row. Both halves matter and both fail silently.
    ///
    /// <para>Those "31 min ago" labels were computed once, when the row was built, and the row
    /// is only rebuilt by a fetch that got past a 60-second gate — so a tab left open kept
    /// saying 31 min for as long as anybody looked at it. They are ticked in place now, from
    /// the rooms ping timer, which only works if the builder hands the TextBlock back.</para>
    ///
    /// <para>Registering a block that is NOT in the row would tick something nobody can see,
    /// and nothing would look wrong — the same shape as the roster health dots, which were
    /// found by structure and silently stopped updating when that structure moved. Hence the
    /// reference check against the row's own children.</para>
    /// </summary>
    [Fact]
    public void ACommunityMatchRowHandsBackItsAgeLabel()
    {
        var error = RunOnStaThread(() =>
        {
            var cells = new List<(TextBlock Text, DateTime ReportedUtc)>();
            var reported = DateTime.UtcNow.AddMinutes(-31);
            var match = new CommunityMatch
            {
                Id = "m1",
                ModId = "wol",
                MapName = "ESOC_Fertile Crescent",
                ReportedAt = reported.ToString("s"),
            };

            var row = MultiplayerTab.BuildCommunityMatchRow(match, cells);

            var cell = Assert.Single(cells);
            Assert.Contains("31", cell.Text.Text);
            // Within a second of what was asked: the row parses the stamp itself, and a cell
            // registered against a different instant would drift away from its own label.
            Assert.True((cell.ReportedUtc - reported).Duration() < TimeSpan.FromSeconds(1));

            var grid = Assert.IsType<Grid>(row);
            Assert.Contains(grid.Children.Cast<UIElement>(), c => ReferenceEquals(c, cell.Text));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// A row whose timestamp does not parse registers NOTHING. There is no label to tick, and a
    /// cell holding a fabricated instant would invent an age for a match that never reported one
    /// — the same refusal the row already makes by omitting the column entirely.
    /// </summary>
    [Fact]
    public void AnUnreadableTimestampRegistersNoAgeLabel()
    {
        var error = RunOnStaThread(() =>
        {
            var cells = new List<(TextBlock Text, DateTime ReportedUtc)>();
            MultiplayerTab.BuildCommunityMatchRow(new CommunityMatch { ReportedAt = "no" }, cells);
            Assert.Empty(cells);
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
    /// THE REVEAL IS BUILT FOR REAL, because it is assembled entirely in code and nothing else
    /// in the app would ever notice if a piece of it stopped resolving.
    ///
    /// <para><c>RevealTextTests</c> pins the decisions; this pins the object. It measures a
    /// genuinely truncated line with <c>FormattedText</c> against a real arranged width, looks
    /// up <c>RevealTooltip</c> from <c>Styles/Text.xaml</c> by name, and computes the offset
    /// that puts the revealed first glyph on top of the original's. A renamed style resolves to
    /// nothing and the reveal silently reverts to the app's ordinary tooltip — a shadowed,
    /// differently-padded box in the wrong place — which is not a crash and not a build error.
    /// </para>
    ///
    /// <para>The negative half is in the same test on purpose: the same block, given room to
    /// fit, must build NOTHING. With the behaviour armed on every trimming TextBlock in the
    /// launcher, a measurement that answered "cut" too readily would put a box over text that
    /// was perfectly legible, everywhere, at once.</para>
    /// </summary>
    [Fact]
    public void TheRevealBuildsInPlaceAndOnlyWhenTheTextIsActuallyCut()
    {
        var error = RunOnStaThread(() =>
        {
            const string full = "Mapa mas jugado: ESOC Fertile Crescent";

            var text = new TextBlock
            {
                Text = full,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = 12,
                Padding = new Thickness(3, 1, 0, 0),
            };
            // A card with a real fill, so the backdrop walk has something to find — the reveal
            // sits directly on top of the original text and must not be see-through.
            var card = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("MpPanel"),
                Child = text,
            };

            // The implicit style in Styles/Text.xaml armed it, without the call site asking.
            Assert.True(RevealText.GetEnabled(text),
                "the implicit TextBlock style did not arm the behaviour");

            // Narrow: the line cannot fit, which is the case the feature exists for.
            card.Measure(new Size(90, 40));
            card.Arrange(new Rect(0, 0, 90, 40));
            // Loaded is the hook that arms it in the real app (SizeChanged carries it from
            // there). Raised by hand because a detached tree is never loaded and never runs a
            // real layout pass — what is pinned is that the handler is wired to it and does
            // the right thing with a laid-out block, which is exactly what was wrong.
            text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            // THE REGRESSION, and the whole reason this test exists in this shape: the tooltip
            // has to be IN PLACE once the block has been laid out, with no mouse involved. The
            // first version computed it on hover and therefore never showed anything at all —
            // WPF's tooltip service inspects an element when the mouse ENTERS it, from a class
            // handler that runs before any instance handler, so a tooltip assigned during hover
            // is always assigned a moment too late.
            var tip = Assert.IsType<ToolTip>(text.ToolTip);

            // Same words, in full, and wrapped rather than trimmed — a reveal that trimmed
            // again would show exactly what the user was already looking at.
            var revealed = Assert.IsType<TextBlock>(tip.Content);
            // Read through PlainTextOf, never revealed.Text: a TextBlock whose content is runs
            // reports "" from that property, which is the trap the helper exists for.
            Assert.Equal(full, RevealText.PlainTextOf(revealed));
            Assert.Equal(TextWrapping.Wrap, revealed.TextWrapping);
            Assert.Equal(text.FontSize, revealed.FontSize);

            // Its own chrome resolved. Without the style it would inherit the app-wide tooltip
            // template, shadow and all.
            Assert.NotNull(tip.Style);
            Assert.False(tip.HasDropShadow);

            // NO DELAY, and on the TEXTBLOCK rather than on the balloon — the service reads it
            // from the owner, so set on the ToolTip it would silently do nothing and the reveal
            // would go back to WPF's stock second of waiting.
            Assert.Equal(0, ToolTipService.GetInitialShowDelay(text));

            // Placed back by its own border and padding, less whatever inset the original gives
            // its text: the two first glyphs land on the same pixel. THAT is what makes it read
            // as the same sentence continuing.
            Assert.Equal(-(RevealText.PadX + 1 - text.Padding.Left), tip.HorizontalOffset, 3);
            Assert.Equal(-(RevealText.PadY + 1 - text.Padding.Top), tip.VerticalOffset, 3);
            Assert.Equal(text, tip.PlacementTarget);

            // And the same block with room to spare reveals nothing at all — and takes its own
            // tooltip back off, so it stops shadowing whatever an ancestor might have to say.
            card.Measure(new Size(600, 40));
            card.Arrange(new Rect(0, 0, 600, 40));
            text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Null(text.ToolTip);

            // The delay comes off with it: it was ours to impose only while our own tooltip was
            // the one on this element.
            Assert.NotEqual(0, ToolTipService.GetInitialShowDelay(text));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// HOVERING AN UNCHANGED BLOCK MUST NOT REBUILD ITS TOOLTIP, and this is the other half of
    /// why the reveal took seconds to appear.
    ///
    /// <para>By the time the MouseEnter handler runs, WPF's tooltip service has already
    /// inspected the element from a class handler and scheduled the show. Clearing the ToolTip
    /// property cancels that, and re-assigning it does not reschedule — the timer only starts on
    /// entry — so the reveal needed a SECOND inspection to appear, one full delay later. The
    /// handler was rebuilding on every hover, unconditionally.</para>
    ///
    /// <para><b>The assertion has to be by REFERENCE.</b> A rebuilt tooltip holds the same words
    /// in the same font at the same offset: compared by value it passes, looks right in a
    /// screenshot, and is exactly the bug.</para>
    /// </summary>
    [Fact]
    public void HoveringAnUnchangedBlockLeavesItsRevealAlone()
    {
        var error = RunOnStaThread(() =>
        {
            var text = new TextBlock
            {
                Text = "Mapa mas jugado: ESOC Fertile Crescent",
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                FontSize = 12,
            };
            var card = new Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("MpPanel"),
                Child = text,
            };
            card.Measure(new Size(90, 40));
            card.Arrange(new Rect(0, 0, 90, 40));
            text.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var armed = Assert.IsType<ToolTip>(text.ToolTip);

            // Two hovers, nothing changed in between.
            text.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
            text.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

            Assert.Same(armed, text.ToolTip);

            // But a block whose TEXT changed without its width changing — a room's age ticking
            // in a fixed column — is still refreshed. That case is the whole reason the handler
            // exists, and a "never touch it" fix would have silently dropped it.
            text.Text = "Mapa mas jugado: ESOC Yucatan y algo mucho mas largo todavia";
            text.RaiseEvent(new System.Windows.Input.MouseEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

            var refreshed = Assert.IsType<ToolTip>(text.ToolTip);
            Assert.NotSame(armed, refreshed);
            Assert.Equal(text.Text, RevealText.PlainTextOf(Assert.IsType<TextBlock>(refreshed.Content)));
        });

        Assert.Null(error);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on an STA thread with the launcher's resource
    /// dictionaries loaded, and returns the exception it threw (null when it didn't).
    /// </summary>
    /// <summary>
    /// Every shared settings control in <c>Styles/Controls.xaml</c> applies, and the
    /// brushes it names actually resolve.
    ///
    /// <para>These styles are the handoff's "make it ONCE" set — the switch, the card,
    /// the row, the badge, the notice box and the fixed-width action button — and they
    /// are consumed from three different windows. Nothing checks them at compile time:
    /// a style with an unresolvable <c>{StaticResource}</c> throws only when it is
    /// APPLIED, and one with a dead <c>{DynamicResource}</c> throws nothing at all and
    /// simply paints the WPF default, which on a dark surface is invisible rather than
    /// obviously wrong. The smoke launch cannot see any of it either: it opens
    /// MainWindow, and none of these styles is used there.</para>
    /// </summary>
    [Fact]
    public void EverySharedSettingsControlAppliesAndItsBrushesResolve()
    {
        var ex = RunOnStaThread(() =>
        {
            EnsureResources();
            var res = Application.Current!.Resources;

            // -- text roles ------------------------------------------------
            foreach (var key in new[]
                     {
                         "SetSectionTitle", "SetGroupLabel", "SetRowTitle",
                         "SetRowDesc", "SetMonoValue", "SetActionQuiet",
                     })
            {
                var style = res[key] as Style;
                Assert.True(style != null, $"{key} is missing from Styles/Controls.xaml");
                var tb = new TextBlock { Style = style, Text = "x" };
                MeasureSharedControl(tb);
                Assert.True(tb.Foreground != null, $"{key} resolved no Foreground");
                Assert.True(tb.FontSize > 0, $"{key} resolved no FontSize");
            }

            // -- card + rows -----------------------------------------------
            foreach (var key in new[]
                     {
                         "SetCard", "SetCardDim", "SetRow", "SetRowLast",
                         "SetActionRow", "SetActionRowLast",
                         "SetBadge", "SetNoticeAmber", "SetNoticeDanger",
                     })
            {
                var style = res[key] as Style;
                Assert.True(style != null, $"{key} is missing from Styles/Controls.xaml");
                var border = new Border { Style = style, Child = new TextBlock { Text = "x" } };
                MeasureSharedControl(border);
            }

            // The seam between rows is the ONLY thing separating them — the handoff
            // forbids separating them by margin — so a SetRow that resolved no
            // BorderBrush would silently merge every row in every card into one block.
            var row = new Border { Style = (Style)res["SetRow"] };
            MeasureSharedControl(row);
            Assert.True(row.BorderBrush != null, "SetRow resolved no seam brush");
            Assert.True(row.BorderThickness.Bottom > 0, "SetRow has no bottom seam");
            var last = new Border { Style = (Style)res["SetRowLast"] };
            MeasureSharedControl(last);
            Assert.Equal(0d, last.BorderThickness.Bottom);

            // -- badge variants --------------------------------------------
            // Tag drives the colour. A typo'd Tag falls through to neutral rather than
            // throwing, so assert that each variant actually CHANGES the fill.
            var neutral = BadgeFill(res, null);
            foreach (var tag in new[] { "ok", "warn", "info", "danger", "private" })
            {
                var fill = BadgeFill(res, tag);
                Assert.True(fill != null, $"SetBadge[{tag}] resolved no Background");
                Assert.True(fill!.ToString() != neutral!.ToString(),
                    $"SetBadge[{tag}] painted the neutral fill — the trigger did not fire");
            }

            // -- toggle switch ---------------------------------------------
            // The one control the whole redesign hangs on: it replaces every checkbox
            // in both settings windows. Off must read as "no colour" and on as blue,
            // or a column of eleven of them cannot be scanned at a glance, which is the
            // entire reason they stopped being checkboxes.
            var toggleStyle = res["SetToggle"] as Style;
            Assert.True(toggleStyle != null, "SetToggle is missing from Styles/Controls.xaml");

            var off = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = toggleStyle,
                IsChecked = false,
            };
            MeasureSharedControl(off);
            off.ApplyTemplate();
            Assert.Equal(34d, off.Width);
            Assert.Equal(20d, off.Height);
            var offFill = off.Background;
            Assert.True(offFill != null, "SetToggle resolved no off-track brush");

            var on = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = toggleStyle,
                IsChecked = true,
            };
            MeasureSharedControl(on);
            on.ApplyTemplate();
            Assert.True(on.Background != null, "SetToggle resolved no on-track brush");
            Assert.True(on.Background!.ToString() != offFill!.ToString(),
                "SetToggle looks identical on and off — the IsChecked trigger did not fire");

            // -- fixed-width action buttons --------------------------------
            // The width must NOT follow the label: a column whose width tracks its text
            // zigzags down the section, which is the defect the fixed widths exist to
            // stop. So the same style with a long and a short caption must measure the
            // same, and the three sizes must actually differ from each other.
            var widths = new Dictionary<string, double>();
            foreach (var (key, expected) in new[]
                     {
                         ("SetActionButtonSm", 88d),
                         ("SetActionButton", 112d),
                         ("SetActionButtonLg", 132d),
                         ("SetActionButtonPrimary", 112d),
                     })
            {
                var style = res[key] as Style;
                Assert.True(style != null, $"{key} is missing from Styles/Controls.xaml");

                var shortBtn = new Button { Style = style, Content = "Ok" };
                var longBtn = new Button { Style = style, Content = "Reparar la instalación completa" };
                MeasureSharedControl(shortBtn);
                MeasureSharedControl(longBtn);
                shortBtn.ApplyTemplate();

                Assert.Equal(expected, shortBtn.Width);
                Assert.Equal(shortBtn.DesiredSize.Width, longBtn.DesiredSize.Width);
                Assert.True(shortBtn.Foreground != null, $"{key} resolved no Foreground");
                widths[key] = expected;
            }
            Assert.Equal(3, widths.Values.Distinct().Count());
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// The launcher settings window parses, its five sections resolve, and the footer
    /// counts what is pending.
    ///
    /// <para>The smoke launch opens MainWindow and nothing else, so this window's XAML —
    /// which is where the whole shared-control set is first consumed — would ship with a
    /// broken <c>{StaticResource}</c> unseen. Constructing it here is the only automated
    /// cover it has.</para>
    ///
    /// <para>The section mapping is the part worth pinning: the redesign merged seven
    /// rail entries into five, so ADVANCED shows three panels and MODS AND UPDATES shows
    /// two. Nothing about that is visible in a build — a section that forgot a panel
    /// simply renders a shorter page.</para>
    /// </summary>
    [Fact]
    public void TheSettingsWindowLoadsAndItsFiveSectionsMapToTheRightPanels()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var previous = Strings.Language;
            try
            {
                Strings.SetLanguage("es");
                var dlg = new LauncherSettingsDialog(new LauncherConfig());

                // Opens on GENERAL, and the title above the content is the same string as
                // the rail entry — they are one name, not a name and a header.
                Assert.Equal(Visibility.Visible, dlg.GeneralPanel.Visibility);
                Assert.Equal("active", dlg.TabGeneralBtn.Tag);
                Assert.Equal(Strings.Get("DlgLauncherSettingsSectionGeneral"), dlg.SectionTitleText.Text);

                // The two recording settings moved OUT of General into GAMES — they are
                // what decides whether a match can be rated, and they were the least
                // findable things on the page.
                Assert.Equal(Visibility.Collapsed, dlg.GamesPanel.Visibility);
                dlg.TabGamesBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(Visibility.Visible, dlg.GamesPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, dlg.GeneralPanel.Visibility);

                // MODS AND UPDATES = the old Updates + Catalog, both at once.
                dlg.TabModsBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(Visibility.Visible, dlg.UpdatesPanel.Visibility);
                Assert.Equal(Visibility.Visible, dlg.CatalogPanel.Visibility);

                // ADVANCED = the old Maintenance + Privacy + Developer, all three at once.
                // The developer block is PRESENT whatever the switch says — folded shut
                // with a line telling you how to open it — because hiding the panel
                // outright left nothing on screen to say the tools existed. What the
                // switch decides is DevTools.
                dlg.DeveloperModeCheck.IsChecked = false;
                dlg.TabAdvancedBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(Visibility.Visible, dlg.MaintenancePanel.Visibility);
                Assert.Equal(Visibility.Visible, dlg.PrivacyPanel.Visibility);
                Assert.Equal(Visibility.Visible, dlg.TranslationsPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, dlg.DevTools.Visibility);
                Assert.Equal(Visibility.Visible, dlg.DevOffHint.Visibility);

                dlg.DeveloperModeCheck.IsChecked = true;
                Assert.Equal(Visibility.Visible, dlg.DevTools.Visibility);
                Assert.Equal(Visibility.Collapsed, dlg.DevOffHint.Visibility);

                dlg.Close();
            }
            finally
            {
                Strings.SetLanguage(previous);
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// An untouched settings window offers nothing to save, and one changed setting
    /// counts as one.
    ///
    /// <para>This is what lets the footer stop being a permanent Cancel/Save pair. It
    /// also pins the property that makes the count honest rather than merely
    /// impressive: a switch flipped twice is back where it started, so it counts as
    /// nothing — the footer compares against what the dialog OPENED with, not against
    /// how many times something moved.</para>
    ///
    /// <para>The handlers are attached to the content root as class handlers, so this
    /// also covers the wiring: a setting that stopped being counted would look exactly
    /// like a setting nobody changed.</para>
    /// </summary>
    [Fact]
    public void TheSettingsFooterAppliesInstantlyAndOnlyCountsWhatCanStillBeRefused()
    {
        var error = RunOnStaThread(() =>
        {
            EnsureResources();
            var config = new LauncherConfig();
            var dlg = new LauncherSettingsDialog(config);

            // Untouched: the line states the contract, without the amber dot, and there
            // is nothing to save.
            Assert.Equal(Visibility.Visible, dlg.UnsavedIndicator.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.UnsavedIndicatorDot.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.SaveButton.Visibility);
            Assert.Equal(Strings.Get("DlgSettingsAppliesInstantly"), dlg.UnsavedText.Text);

            // An INSTANT setting reaches the config the moment it is touched, and is
            // never pending — which is the whole claim the footer line makes.
            bool wasOn = dlg.SoundsCheck.IsChecked == true;
            dlg.SoundsCheck.IsChecked = !wasOn;
            Assert.Equal(!wasOn, config.EnableSounds);
            Assert.Equal(Visibility.Collapsed, dlg.UnsavedIndicatorDot.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.SaveButton.Visibility);
            Assert.Equal(Strings.Get("DlgSettingsAppliesInstantly"), dlg.UnsavedText.Text);

            // A DEFERRED setting is the opposite: it can be refused, so it waits for Save
            // and the footer counts it meanwhile.
            dlg.CatalogCustomRadio.IsChecked = true;
            Assert.Equal(Visibility.Visible, dlg.UnsavedIndicatorDot.Visibility);
            Assert.Equal(Visibility.Visible, dlg.SaveButton.Visibility);
            Assert.Equal(Strings.Get("DlgSettingsUnsavedOne"), dlg.UnsavedText.Text);
            // ...and it has NOT been written.
            Assert.Equal("", config.ModsCatalogRepo);

            // Two of them, counted as two.
            bool bgWas = dlg.StartWithWindowsCheck.IsChecked == true;
            dlg.StartWithWindowsCheck.IsChecked = !bgWas;
            Assert.Equal(Strings.Format("DlgSettingsUnsavedMany", 2), dlg.UnsavedText.Text);

            // Put both back and the footer forgets them.
            dlg.CatalogDefaultRadio.IsChecked = true;
            dlg.StartWithWindowsCheck.IsChecked = bgWas;
            Assert.Equal(Visibility.Collapsed, dlg.UnsavedIndicatorDot.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.SaveButton.Visibility);
            Assert.Equal(Strings.Get("DlgSettingsAppliesInstantly"), dlg.UnsavedText.Text);

            dlg.Close();
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The redirect in <c>TestDataDirectory</c> is what stops the assertion above from
    /// overwriting the developer's real config: the dialog PERSISTS on every change now,
    /// so a test that flips a control writes a file. Pinned here because the failure mode
    /// is silent and expensive — it destroys installed-mod state on the machine running
    /// the suite, and nothing else in the run would report it.
    /// </summary>
    [Fact]
    public void TheTestRunNeverWritesToTheRealLauncherDataDirectory()
    {
        var real = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AoE3ModLauncher");
        Assert.NotEqual(
            System.IO.Path.GetFullPath(real).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(WarsOfLibertyLauncher.Services.AppPaths.DataDir)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar));
    }

    private static System.Windows.Media.Brush? BadgeFill(ResourceDictionary res, string? tag)
    {
        var border = new Border { Style = (Style)res["SetBadge"], Tag = tag };
        MeasureSharedControl(border);
        return border.Background;
    }

    private static void MeasureSharedControl(FrameworkElement el)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        el.Arrange(new Rect(el.DesiredSize));
        el.UpdateLayout();
    }

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
        // Text BEFORE Buttons, as App.xaml merges them: SidebarNavLabel is BasedOn the
        // implicit TextBlock style that lives in Text.xaml, and a StaticResource in a
        // merged dictionary can only see dictionaries merged before it.
        foreach (var name in new[] { "Tokens", "Colors", "Text", "Chrome", "Buttons", "Inputs", "Controls" })
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
