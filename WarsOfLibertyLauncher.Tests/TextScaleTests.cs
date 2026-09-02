using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="TextScale"/> — the launcher-wide text size — and the one structural
/// condition the whole feature rests on.
///
/// <para><b>The floor of 1.0 was questioned and kept.</b> It came up because a default of
/// "Automatic" scales UP only — so with the floor where it is, a small or ordinary screen gets
/// the smallest type in the launcher while a 32" desktop gets 15 % more. Raising the floor was
/// considered on the machine-by-machine table below (<c>SmallAndOrdinaryScreensStayAtOne</c>
/// and <c>ALaptopStuckAt100PercentIsBumped</c> ARE that table) and deliberately not done: a
/// laptop at the display scale Windows actually configures for it is not the case that hurts,
/// and the one that is — a dense panel left at 100 % — is already picked up by the density
/// bump. Don't "fix" the floor without re-reading those two.</para>
///
/// <para>Two very different kinds of test live here on purpose. The first half argues with
/// the Automatic curve, which is a judgement call and should be arguable in a test rather
/// than on a screenshot. The second half is the important one: it walks the XAML and
/// asserts that no font size is still a <c>{StaticResource}</c>. That is not a style rule —
/// a StaticResource is resolved when the XAML is parsed, so one that slips through does not
/// fail the build, does not throw, and simply stops scaling in silence while everything
/// around it moves.</para>
/// </summary>
public class TextScaleTests
{
    // ------------------------------------------------------------------ Recommend

    /// <summary>
    /// The case the whole setting was asked for, and the only number on this curve that was set
    /// by LOOKING at it rather than by arithmetic.
    ///
    /// <para>It was 1.15, derived to put the reverted multiplayer tokens back at the size that
    /// screen had been showing (11.5 x 1.15 = 13.2). The derivation was tidy and the result was
    /// wrong: shipped to the very machine the reports came from, 13.2 read as too big. 1.10
    /// gives 12.65. Every earlier version of this curve was argued in the abstract, which is
    /// why this one is worth more than the number it changed.</para>
    /// </summary>
    [Fact]
    public void The32InchDesktopIsTheAnchor()
    {
        Assert.Equal(1.10, TextScale.Recommend(32, 2560, 1440, 1.0));
    }

    /// <summary>
    /// The top band, which until now NOTHING pinned — it was only ever brushed by the
    /// stays-in-range check, so the number could have moved with no test noticing. It came down
    /// a step with the 32" band, to keep the ladder proportioned rather than because anybody on
    /// a panel that size complained; nobody has one to report from.
    /// </summary>
    [Fact]
    public void TheLargestPanelsGetTheTopOfTheCurve()
    {
        Assert.Equal(1.20, TextScale.Recommend(34, 3840, 2160, 1.5));
        Assert.Equal(1.20, TextScale.Recommend(43, 3840, 2160, 1.5));
    }

    /// <summary>
    /// THE CASE THAT MATTERS MOST. A panel that does not report its size must change
    /// nothing — a remote session, a virtual display and a projector all land here, and in
    /// every one of them a guess would be worse than leaving the user alone.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-14.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnknownOrImpossibleScreenChangesNothing(double? diagonal)
    {
        Assert.Equal(1.0, TextScale.Recommend(diagonal, 2560, 1440, 1.0));
    }

    /// <summary>
    /// A laptop, and an ordinary desktop monitor, are left alone. You sit close to a laptop,
    /// and it is the case where growing the text costs the most — there is the least room to
    /// give. Each is passed the display scale Windows actually configures for it, which is
    /// the whole reason the density bump does not fire here.
    /// </summary>
    [Theory]
    [InlineData(13.3, 1920, 1080, 1.5)]
    [InlineData(15.6, 1920, 1080, 1.25)]
    [InlineData(24.0, 1920, 1080, 1.0)]
    [InlineData(24.0, 3840, 2160, 1.5)]
    [InlineData(25.9, 2560, 1440, 1.0)]
    public void SmallAndOrdinaryScreensStayAtOne(double diagonal, int w, int h, double dpi)
    {
        Assert.Equal(1.0, TextScale.Recommend(diagonal, w, h, dpi));
    }

    /// <summary>
    /// The other side of the same coin, and it is deliberate rather than a leak: a laptop
    /// panel left at 100 % renders everything physically tiny, Windows is not compensating,
    /// and the diagonal alone would read it as a small screen that needs nothing.
    /// </summary>
    [Theory]
    [InlineData(13.3)]
    [InlineData(15.6)]
    public void ALaptopStuckAt100PercentIsBumped(double diagonal)
    {
        Assert.Equal(1.10, TextScale.Recommend(diagonal, 1920, 1080, 1.0));
    }

    /// <summary>
    /// Bigger panel, never smaller text. Measured on the diagonal ALONE (no screen metrics),
    /// because the density bump is a second, independent axis: holding the pixel count fixed
    /// while the diagonal grows makes the panel less dense, so the bump correctly drops away
    /// and the total is allowed to fall.
    /// </summary>
    [Fact]
    public void TheCurveNeverGoesBackwards()
    {
        var previous = 0.0;
        for (var d = 10.0; d <= 60.0; d += 0.5)
        {
            var factor = TextScale.Recommend(d, 0, 0, 0);
            Assert.True(factor >= previous,
                $"A {d}\" panel recommended {factor}, less than the {previous} before it.");
            previous = factor;
        }
    }

    /// <summary>Nothing may leave the range the settings dropdown offers.</summary>
    [Fact]
    public void EveryAnswerStaysInsideTheOfferedRange()
    {
        foreach (var d in new[] { 5.0, 21.5, 27.0, 32.0, 43.0, 85.0 })
        foreach (var dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var factor = TextScale.Recommend(d, 3840, 2160, dpi);
            Assert.InRange(factor, TextScale.MinFactor, TextScale.MaxFactor);
        }
    }

    /// <summary>
    /// The DPI safety net. A high-resolution panel run at 100 % renders everything
    /// physically small and Windows is not compensating, which is the one case the diagonal
    /// alone would miss — a 27" 4K at 100 % packs ~163 logical pixels to the inch.
    ///
    /// <para>And the mirror: the SAME panel at the display scale Windows would normally
    /// choose must not be bumped, because WPF's DIPs have already absorbed it. Getting this
    /// backwards would double-count the correction on every properly configured machine.</para>
    /// </summary>
    [Fact]
    public void ADensePanelAt100PercentIsBumped_AndTheSameOneAt150IsNot()
    {
        // 27" 4K: 163 logical PPI at 100 %, 109 at the 150 % Windows would pick.
        Assert.Equal(1.15, TextScale.Recommend(27, 3840, 2160, 1.0));
        Assert.Equal(1.05, TextScale.Recommend(27, 3840, 2160, 1.5));
    }

    /// <summary>Missing screen metrics fall back to the diagonal alone, never to a throw.</summary>
    [Fact]
    public void NoScreenMetricsStillAnswersFromTheDiagonal()
    {
        Assert.Equal(1.10, TextScale.Recommend(32, 0, 0, 0));
    }

    // -------------------------------------------------------------------- Resolve

    /// <summary>An explicit percentage is obeyed and never re-derived from the screen.</summary>
    [Theory]
    [InlineData("100", 1.00)]
    [InlineData("110", 1.10)]
    [InlineData("125", 1.25)]
    public void AnExplicitPercentageWins(string setting, double expected)
    {
        // A 32" screen would recommend 1.15; the setting has to beat it in both directions.
        Assert.Equal(expected, TextScale.Resolve(setting, 32, 2560, 1440, 1.0));
    }

    /// <summary>
    /// Every value that is not a percentage means Automatic — including the ones a
    /// hand-edited config produces. A config is user-writable, so "unrecognised" is a state
    /// this has to have an answer for rather than a state it can rule out.
    /// </summary>
    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("enormous")]
    [InlineData("1.5x")]
    public void AnythingThatIsNotAPercentageMeansAutomatic(string? setting)
    {
        Assert.Equal(TextScale.Recommend(32, 2560, 1440, 1.0),
                     TextScale.Resolve(setting, 32, 2560, 1440, 1.0));
    }

    /// <summary>A percentage outside the range is clamped, not honoured.</summary>
    [Theory]
    [InlineData("400")]
    [InlineData("-50")]
    [InlineData("0")]
    public void AnAbsurdPercentageIsClamped(string setting)
    {
        Assert.InRange(TextScale.Resolve(setting, null, 1920, 1080, 1.0),
                       TextScale.MinFactor, TextScale.MaxFactor);
    }

    /// <summary>
    /// THE STARTUP PATH AND THE CONFIG CANNOT DISAGREE ABOUT THE DEFAULT, and this is the one
    /// assertion that survived the default itself moving twice.
    ///
    /// <para><c>App.OnStartup</c> reads this single setting straight out of the config JSON —
    /// it runs before MainWindow and must not fire four migrations for one string — so it has
    /// its own fallback for a config with no <c>textScale</c> key, which is every config
    /// written before the setting existed. That fallback and the property's initialiser were
    /// two separate literals once, and the moment one moved the other went on answering the
    /// old value: the default was changed in the only place that could not see it, and the
    /// launcher kept scaling with nothing in the config saying so. They are one constant now.
    /// </para>
    ///
    /// <para>Deliberately says nothing about WHICH value it is — that has been 100 and
    /// Automatic and may move again. What must never come back is the divergence.</para>
    /// </summary>
    [Fact]
    public void TheStartupPathAndTheConfigAgreeOnTheDefault()
    {
        var fresh = new WarsOfLibertyLauncher.Models.LauncherConfig();

        Assert.Equal(WarsOfLibertyLauncher.Models.LauncherConfig.DefaultTextScale, fresh.TextScale);
        Assert.Contains(fresh.TextScale, TextScale.Choices);

        // And whatever it is, it has to resolve to something usable rather than throwing or
        // landing outside the range the dropdown can even represent.
        Assert.InRange(
            TextScale.Resolve(fresh.TextScale, 32, 2560, 1440, 1.0),
            TextScale.MinFactor, TextScale.MaxFactor);
    }

    /// <summary>
    /// A VALUE NOBODY CHOSE IS NOT A SETTING, and this is the test for the bug that made the
    /// default unreachable.
    ///
    /// <para>Every property on <c>LauncherConfig</c> is serialised on the first <c>Save()</c>,
    /// and Save runs constantly — a mod switch, a game launch — so whatever the default happens
    /// to be is stamped into the file within minutes of a first launch and looks exactly like a
    /// deliberate choice from then on. When the default moved to 100 for one round, every
    /// machine that ran that build got <c>"textScale": "100"</c> written into its config, and
    /// moving the default back changed nothing for any of them: the setting looked broken while
    /// doing precisely what it said.</para>
    ///
    /// <para>So the stored value is obeyed only once somebody has actually picked it.</para>
    /// </summary>
    [Fact]
    public void AStoredValueNobodyPickedFollowsTheDefault()
    {
        Assert.Equal(WarsOfLibertyLauncher.Models.LauncherConfig.DefaultTextScale,
                     WarsOfLibertyLauncher.Models.LauncherConfig.ResolveTextScale("100", explicitlyChosen: false));

        // Including a value that IS the default: nothing here depends on which one it is.
        Assert.Equal(WarsOfLibertyLauncher.Models.LauncherConfig.DefaultTextScale,
                     WarsOfLibertyLauncher.Models.LauncherConfig.ResolveTextScale("125", explicitlyChosen: false));
    }

    /// <summary>An actual choice is obeyed, which is the entire point of having the setting.</summary>
    [Fact]
    public void AChoiceIsObeyed()
    {
        Assert.Equal("125", WarsOfLibertyLauncher.Models.LauncherConfig.ResolveTextScale("125", explicitlyChosen: true));
        Assert.Equal("100", WarsOfLibertyLauncher.Models.LauncherConfig.ResolveTextScale(" 100 ", explicitlyChosen: true));
    }

    /// <summary>
    /// A blank falls back even when the flag says otherwise — a hand-edited or half-written
    /// config must land on the default rather than on an empty string that resolves to nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankAlwaysFallsBack(string? stored)
    {
        Assert.Equal(WarsOfLibertyLauncher.Models.LauncherConfig.DefaultTextScale,
                     WarsOfLibertyLauncher.Models.LauncherConfig.ResolveTextScale(stored, explicitlyChosen: true));
    }

    /// <summary>
    /// The dropdown opens on what the user actually has, so its first entry is the default.
    /// Compared against the constant rather than a literal, so this keeps telling the truth
    /// the next time the default moves — which it has, twice.
    /// </summary>
    [Fact]
    public void TheDropdownLeadsWithTheDefault()
    {
        Assert.Equal(WarsOfLibertyLauncher.Models.LauncherConfig.DefaultTextScale,
                     TextScale.Choices[0]);
    }

    /// <summary>
    /// Every value the settings dropdown offers has to survive the round trip, or the combo
    /// could show a choice the resolver does not recognise and silently answer Automatic.
    /// </summary>
    [Fact]
    public void EveryOfferedChoiceResolves()
    {
        foreach (var choice in TextScale.Choices)
        {
            var factor = TextScale.Resolve(choice, null, 1920, 1080, 1.0);
            Assert.InRange(factor, TextScale.MinFactor, TextScale.MaxFactor);
        }
    }

    // ----------------------------------------------------------------- the tokens

    /// <summary>
    /// The scaled set must be font sizes and nothing else. A height or a padding in there
    /// would quietly turn the setting into the zoom it exists to avoid — the one that was
    /// already tried through UiScale and rejected as "gigante".
    /// </summary>
    [Fact]
    public void OnlyFontSizesAreScaled()
    {
        var offenders = TextScale.ScaledKeys
            .Where(k => k.Contains("Height", StringComparison.Ordinal)
                     || k.Contains("Width", StringComparison.Ordinal)
                     || k.Contains("Padding", StringComparison.Ordinal)
                     || k.Contains("Radius", StringComparison.Ordinal)
                     || k.Contains("Space", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These are measurements, not type sizes, and scaling them makes this a zoom: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Every scaled key has to actually exist in the token dictionaries. A renamed token
    /// silently drops out of the scale otherwise — Apply skips a key it cannot find, by
    /// design, because a missing resource must not fail a launch.
    /// </summary>
    [Fact]
    public void EveryScaledKeyIsDeclaredSomewhere()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { Path.Combine("Styles", "Tokens.xaml"), "App.xaml" })
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(RepoFile(file)),
                                              @"<sys:Double x:Key=""([A-Za-z0-9]+)"""))
            {
                declared.Add(m.Groups[1].Value);
            }
        }

        var missing = TextScale.ScaledKeys.Where(k => !declared.Contains(k)).ToList();
        Assert.True(missing.Count == 0,
            "TextScale scales tokens that are not declared in Tokens.xaml or App.xaml, so "
            + "they do nothing: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The Workshop and the Multiplayer tab have SEPARATE type scales that happen to hold the
    /// same numbers, and the separation is the point.
    ///
    /// <para>The Workshop ran on the app-wide scale and read a size and a half heavier than
    /// the tab beside it; the fix gave it <c>Ws*</c> tokens matching the <c>Mp*</c> values.
    /// The tempting tidy-up is to delete the Ws set and point the Workshop at the Mp one —
    /// and that chains two surfaces together, so the next person tuning the multiplayer scale
    /// moves the Workshop without knowing they have. Same call <c>MpActivity*Size</c> already
    /// documents for the community strip.</para>
    ///
    /// <para>The values are asserted EQUAL as well, because that is the state today and a
    /// silent drift between two scales nobody meant to differ is the other way this goes
    /// wrong. If they ever should differ, this test is where you say so.</para>
    ///
    /// <para><b>The settings family is here for a reason that is not symmetry.</b> It was
    /// declared equal to multiplayer rung for rung and still rendered visibly smaller,
    /// because it was absent from <see cref="TextScale.ScaledKeys"/> and so was the only
    /// one of the three not being multiplied. Equal VALUES were never the guarantee anyone
    /// thought they were; equal values plus membership in that list is.</para>
    /// </summary>
    [Fact]
    public void TheWorkshopAndMultiplayerScalesAreSeparateButEqual()
    {
        var tokens = File.ReadAllText(RepoFile(Path.Combine("Styles", "Tokens.xaml")));

        double Value(string key)
        {
            var m = Regex.Match(tokens,
                @"<sys:Double x:Key=""" + key + @""">([0-9.]+)</sys:Double>");
            Assert.True(m.Success, $"{key} is not declared in Tokens.xaml.");
            return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var (workshop, multiplayer) in new[]
                 {
                     ("WsBodyStrongSize", "MpBodySize"),
                     ("WsBodySize", "MpMetaSize"),
                     ("WsLabelSize", "MpLabelSize"),
                     ("WsHeadingSize", "MpPageTitleSize"),
                     ("WsMonoSize", "SetMonoSize"),
                     ("WsBadgeSize", "SetBadgeSize"),
                 })
        {
            Assert.Equal(Value(multiplayer), Value(workshop));
            Assert.Contains(workshop, TextScale.ScaledKeys);
        }

        // The settings surface is the THIRD family, and it is the one that made this
        // question concrete: it was declared rung for rung equal to multiplayer and still
        // rendered smaller, because only one of the two was being multiplied. Asserting
        // the values EQUAL says the design intent; asserting membership in ScaledKeys says
        // the intent actually reaches the screen. Neither alone would have caught it.
        foreach (var (settings, multiplayer) in new[]
                 {
                     ("SetSectionTitleSize", "MpPageTitleSize"),
                     ("SetBodySize", "MpBodySize"),
                     ("SetControlSize", "MpMetaSize"),
                     ("SetDescSize", "MpLabelSize"),
                     ("SetGroupLabelSize", "MpPillSize"),
                     ("SetTinySize", "MpSectionLabelSize"),
                 })
        {
            Assert.Equal(Value(multiplayer), Value(settings));
            Assert.Contains(settings, TextScale.ScaledKeys);
        }
    }

    /// <summary>
    /// THE STRUCTURAL GUARD. A <c>{StaticResource}</c> font size is baked in when the XAML
    /// is parsed and never looks at the dictionary again, so one left behind does not fail
    /// the build and does not throw — that one piece of text just stops scaling while
    /// everything around it moves. There is no way to see that except by looking for it.
    ///
    /// <para>The two chrome tokens are the deliberate exception, and they are asserted the
    /// other way round below.</para>
    /// </summary>
    [Fact]
    public void NoFontSizeTokenIsStillAStaticResource()
    {
        var pattern = new Regex(
            @"\{StaticResource (" + string.Join("|", TextScale.ScaledKeys) + @")\}");

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(RepoFile("."), "*.xaml",
                                                      SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;
            var text = File.ReadAllText(file);
            foreach (Match m in pattern.Matches(text))
                offenders.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        }

        // A pass because nothing was read is not a pass. This test has one job and it is
        // invisible if the walk quietly finds no files.
        Assert.True(scanned > 20, $"Only {scanned} XAML files were scanned; the walk is wrong.");

        Assert.True(offenders.Count == 0,
            "These font sizes are still StaticResource, so they will not scale with the "
            + "text-size setting and nothing will say so: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The title bar is deliberately NOT scaled, and it has to stay that way. Its height
    /// comes from a fixed token, and App.ApplyWindowChrome derives
    /// WindowChrome.CaptionHeight from that SAME token — text growing inside a caption
    /// region that cannot grow with it is the documented silent-breakage zone, where the
    /// bar simply stops dragging the window over part of its height.
    /// </summary>
    [Fact]
    public void TheChromeIsNotScaled()
    {
        foreach (var key in TextScale.UnscaledChromeKeys)
            Assert.DoesNotContain(key, TextScale.ScaledKeys);

        // Named individually as well: that list is what the XAML walk below consults, so an
        // entry quietly dropped from it would take a chrome token with it — and the walk
        // would then demand that token be SCALED, which is the opposite of this rule.
        Assert.Contains("TitleBarTitleSize", TextScale.UnscaledChromeKeys);
        Assert.Contains("TitleBarGlyphSize", TextScale.UnscaledChromeKeys);
        Assert.Contains("ChromeVersionSize", TextScale.UnscaledChromeKeys);
    }

    /// <summary>
    /// THE OTHER STRUCTURAL GUARD, and the one that was missing.
    ///
    /// <para>Every other check in this file runs LIST-FIRST — take
    /// <see cref="TextScale.ScaledKeys"/> and ask something of each entry. None of them can
    /// see a token that is ABSENT from the list, and absent is the failure that shipped: the
    /// whole <c>Set*Size</c> family dressed both settings windows, was consumed as
    /// <c>{DynamicResource}</c> everywhere, was documented as following the setting — and
    /// was never multiplied. Those two windows sat at 100 % beside a multiplayer tab running
    /// at 110 %, and since the two scales are declared identically rung for rung, that
    /// omission WAS the whole visible difference. Nothing threw and nothing failed.</para>
    ///
    /// <para><b>It reads the markup and the code, not the token names.</b> Guessing by name
    /// would be wrong in both directions: <c>SetToggleThumbSize</c> and <c>DiscSizeSm</c>
    /// end in "Size" and are geometry, while <c>SidebarNavTextSize</c> and
    /// <c>NavTabTextSize</c> do not follow the <c>*Size</c> shape at all. What makes a token
    /// a FONT size is that something binds it to a FontSize, so that is what is searched
    /// for.</para>
    ///
    /// <para>The code-behind half rides on a fact worth stating, because it is what makes
    /// that half exact rather than a heuristic: <b>every <c>(double)FindResource(...)</c> in
    /// this repository reads a font size</b>. Geometry comes back through a different cast
    /// (<c>(CornerRadius)</c>, <c>(Thickness)</c>) or a different property, so the cast
    /// alone identifies the call. Without this pass a token used only from code — which
    /// <c>WsMonoSize</c> is — would be invisible to the walk.</para>
    /// </summary>
    [Fact]
    public void EveryFontSizeTokenTheXamlBindsIsScaled()
    {
        // The two shapes a font size takes in this repo's XAML: an attribute on an element,
        // and a Style setter — including the TextElement.FontSize form a container uses to
        // reach the text inside it.
        var patterns = new[]
        {
            new Regex(@"FontSize=""\{(?:Dynamic|Static)Resource ([A-Za-z0-9]+)\}"""),
            new Regex(@"Property=""(?:TextElement\.)?FontSize""\s+Value=""\{(?:Dynamic|Static)Resource ([A-Za-z0-9]+)\}"""),
        };
        var fromCode = new Regex(@"\(double\)(?:Application\.Current\.)?FindResource\(""([A-Za-z0-9]+)""\)");

        var bound = new SortedSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        var scannedCode = 0;
        foreach (var file in Directory.EnumerateFiles(RepoFile("."), "*.*",
                                                      SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var isXaml = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            var isCode = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (!isXaml && !isCode) continue;

            var text = File.ReadAllText(file);
            if (isXaml)
            {
                scanned++;
                foreach (var pattern in patterns)
                    foreach (Match m in pattern.Matches(text))
                        bound.Add(m.Groups[1].Value);
            }
            else
            {
                scannedCode++;
                foreach (Match m in fromCode.Matches(text))
                    bound.Add(m.Groups[1].Value);
            }
        }

        // A pass because nothing was read is not a pass — the same protection the
        // StaticResource walk carries, for the same reason.
        Assert.True(scanned > 20, $"Only {scanned} XAML files were scanned; the walk is wrong.");
        Assert.True(scannedCode > 20, $"Only {scannedCode} .cs files were scanned; the walk is wrong.");
        Assert.True(bound.Count > 20,
                    $"Only {bound.Count} font-size tokens were found bound in XAML; the "
                    + "patterns no longer match how this repo writes them.");

        // The families this exists to protect, named so the walk cannot pass by finding
        // only the app-wide sizes and none of the per-surface scales.
        foreach (var known in new[] { "FontSizeBody", "MpLabelSize", "WsLabelSize", "SetDescSize" })
            Assert.Contains(known, bound);

        var unscaled = bound
            .Where(k => !TextScale.ScaledKeys.Contains(k))
            .Where(k => !TextScale.UnscaledChromeKeys.Contains(k))
            .ToList();

        Assert.True(unscaled.Count == 0,
            "These tokens dress text but are in neither TextScale.ScaledKeys nor "
            + "UnscaledChromeKeys, so they silently ignore the text-size setting. Add them "
            + "to one list or the other, on purpose: " + string.Join(", ", unscaled));
    }

    /// <summary>
    /// Walks up from the test assembly to the launcher project, the same way
    /// <c>StringTableSourceTests</c> does — by looking for a file that has to be there, so a
    /// layout change fails loudly here instead of quietly skipping the checks.
    /// </summary>
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var project = Path.Combine(dir.FullName, "WarsOfLibertyLauncher");
            if (File.Exists(Path.Combine(project, "App.xaml")))
                return Path.GetFullPath(Path.Combine(project, relative));
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the WarsOfLibertyLauncher project above " + AppContext.BaseDirectory);
    }
}
