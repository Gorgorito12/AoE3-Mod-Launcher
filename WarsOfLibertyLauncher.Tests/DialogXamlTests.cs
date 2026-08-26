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
    /// Runs <paramref name="action"/> on an STA thread with the launcher's resource
    /// dictionaries loaded, and returns the exception it threw (null when it didn't).
    /// </summary>
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
