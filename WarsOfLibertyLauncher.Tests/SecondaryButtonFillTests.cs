using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The secondary settings buttons carry a fill of their own (docs/design_botones_secundarios,
/// variant 52a). Every property pinned here fails SILENTLY when it breaks: a dead
/// <c>{DynamicResource}</c> paints nothing and the button falls back to a bare rim, a derived
/// style that forgets to undo the base's disabled Opacity dims a SOLID button that keeps its
/// colour rule, and a local <c>Foreground</c> on a button beats every trigger of its style. None
/// of it throws, builds red or looks wrong on a wide monitor.
/// </summary>
[Collection("wpf-and-language")]
public class SecondaryButtonFillTests
{
    /// <summary>Every neutral secondary style paints the 52a fill, not the card through a rim.</summary>
    [Fact]
    public void TheSecondaryButtonsHaveAFillOfTheirOwn()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var (style, fill) in new[]
                     {
                         ("SetActionButton", "UiButtonFill"),
                         ("SetActionButtonSm", "UiButtonFill"),
                         ("SetActionButtonLg", "UiButtonFill"),
                         ("SetGhostButton", "UiButtonFill"),
                         ("SetFooterGhostButton", "UiButtonFill"),
                         ("SetFooterBarGhostButton", "UiButtonFooterFill"),
                         ("SetAccentOutlineButton", "UiButtonAccentFill"),
                         ("SetDangerOutlineButton", "UiButtonDangerFill"),
                         ("SetActionButtonDanger", "UiButtonDangerFill"),
                         ("SetActionCard", "UiButtonFill"),
                     })
            {
                var button = Applied(style, enabled: true);
                Assert.True(button.Background is SolidColorBrush,
                    $"{style} paints no fill — it is a bare rim again");
                Assert.Equal(ColorOf(fill), ((SolidColorBrush)button.Background).Color);
                Assert.True(((SolidColorBrush)button.Background).Color.A == 255,
                    $"{style} paints a translucent fill, which shows the card through it");
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// Pressed is never lighter than hover — the handoff's one rule about the states — and the
    /// hover actually moves the fill, or the button has no answer to the pointer.
    /// </summary>
    [Fact]
    public void PressedIsNeverLighterThanHover()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var style in new[]
                     {
                         "SetActionButton", "SetGhostButton", "SetFooterGhostButton",
                         "SetFooterBarGhostButton", "SetAccentOutlineButton",
                         "SetDangerOutlineButton", "SetActionButtonDanger", "SetActionCard",
                     })
            {
                var normal = Applied(style, enabled: true).Background as SolidColorBrush;
                var hover = TriggerColor(style, UIElement.IsMouseOverProperty, true);
                var pressed = TriggerColor(style, Button.IsPressedProperty, true);

                Assert.True(normal != null, $"{style} has no normal fill");
                Assert.True(hover != normal!.Color, $"{style}'s hover does not change the fill");
                Assert.True(Luminance(pressed) <= Luminance(hover),
                    $"{style}: pressed {pressed} is lighter than hover {hover}");
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A disabled secondary button keeps its fill at half opacity (the
    /// maintainer's call, against the launcher-wide "disabled is a colour" rule) — and the SOLID
    /// buttons derived from the same base must NOT inherit that. They keep their colour rule at
    /// full opacity; a missing reset in any of them would dim Verify, Share diagnostics or a
    /// dialog's primary button with nothing on screen to explain why.
    /// </summary>
    [Fact]
    public void ADisabledSecondaryButtonDimsAndASolidOneDoesNot()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            foreach (var style in new[]
                     {
                         "SetActionButton", "SetGhostButton", "SetFooterBarGhostButton",
                         "SetAccentOutlineButton", "SetDangerOutlineButton", "SetActionButtonDanger",
                         "SetActionCard",
                     })
            {
                var button = Applied(style, enabled: false);
                Assert.Equal(0.5, button.Opacity);
                Assert.True(button.Background is SolidColorBrush,
                    $"a disabled {style} fell back to an empty rim");
            }

            foreach (var style in new[]
                     {
                         "SetActionButtonPrimary", "SetSolidButton", "SetDiagButton",
                         "SetFooterPrimaryButton", "SetFooterSolidButton", "SetFooterDangerButton",
                         "SetInlinePillButton",
                     })
            {
                var button = Applied(style, enabled: false);
                Assert.True(button.Opacity == 1.0,
                    $"a disabled {style} is dimmed by the secondary buttons' opacity rule");
            }
        });
        Assert.Null(error);
    }

    /// <summary>
    /// The Copy-both pill is a chip, not a secondary button: it keeps its own tint and the
    /// pressed fill it had, whatever the base it derives from now does.
    /// </summary>
    [Fact]
    public void ThePillKeepsItsOwnLook()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var pill = Applied("SetInlinePillButton", enabled: true);
            Assert.Equal(ColorOf("UiActionPillBg"), ((SolidColorBrush)pill.Background).Color);
            Assert.Equal(ColorOf("MpField"), TriggerColor("SetInlinePillButton", Button.IsPressedProperty, true));
        });
        Assert.Null(error);
    }

    /// <summary>
    /// "Uninstall from my PC" uses the red style and sets no colour of its own. It used to be a
    /// neutral button painted red by local values, which beat the style's triggers — so its hover
    /// was the neutral one, and after this change it would have kept the neutral FILL under red
    /// text.
    /// </summary>
    [Fact]
    public void TheUninstallButtonIsRedThroughItsStyleAlone()
    {
        var doc = XDocument.Load(RepoFile("LauncherSettingsDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "UninstallButton");

        Assert.Equal("{StaticResource SetActionButtonDanger}", (string?)button.Attribute("Style"));
        Assert.Null(button.Attribute("Foreground"));
        Assert.Null(button.Attribute("BorderBrush"));
        Assert.Null(button.Attribute("Background"));
    }

    /// <summary>
    /// 52d: the two large LOCAL FILES card-buttons wear the 52a fill through their STYLE, and
    /// their icon squares and text point at the 52d brushes. A local Background on either card
    /// would beat the style and bring the bare rim back; a typo'd brush key paints nothing and
    /// builds clean.
    /// </summary>
    [Fact]
    public void TheLocalFilesCardsWearTheFillAndTheLighterTiles()
    {
        var doc = XDocument.Load(RepoFile("ModPropertiesDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) =>
            doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);

        foreach (var (card, tile, glyph) in new[]
                 {
                     ("InstallNewCopyBtn", "{DynamicResource UiButtonIconTileAccent}", "{DynamicResource UiButtonIconGlyphAccent}"),
                     ("AddExistingFolderBtn", (string?)null, "{DynamicResource UiButtonIconGlyph}"),
                 })
        {
            var button = Named(card);
            Assert.Equal("{StaticResource SetActionCard}", (string?)button.Attribute("Style"));
            Assert.Null(button.Attribute("Background"));
            Assert.Null(button.Attribute("BorderBrush"));

            var tileBorder = button.Descendants()
                .Single(e => (string?)e.Attribute("Style") == "{StaticResource SetIconTile}");
            // The folder tile takes the style's own background, which is the 52d brush.
            Assert.Equal(tile, (string?)tileBorder.Attribute("Background"));
            Assert.Equal(glyph, (string?)tileBorder.Elements().Single().Attribute("Foreground"));
        }

        foreach (var title in new[] { "InstallNewCopyTitle", "AddExistingFolderTitle" })
            Assert.Equal("{DynamicResource MpTextHeading}", (string?)Named(title).Attribute("Foreground"));
        foreach (var desc in new[] { "InstallNewCopyDesc", "AddExistingFolderDesc" })
            Assert.Equal("{DynamicResource UiButtonCardDesc}", (string?)Named(desc).Attribute("Foreground"));

        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            var tileStyle = (Style)Application.Current!.Resources["SetIconTile"];
            var folderTile = new Border { Style = tileStyle };
            folderTile.Measure(new Size(40, 40));
            Assert.Equal(ColorOf("UiButtonIconTile"), ((SolidColorBrush)folderTile.Background).Color);
            foreach (var key in new[]
                     {
                         "UiButtonIconTile", "UiButtonIconGlyph", "UiButtonIconTileAccent",
                         "UiButtonIconGlyphAccent", "UiButtonCardDesc", "MpTextHeading",
                     })
                ColorOf(key);
        });
        Assert.Null(error);
    }

    // ── helpers ──

    private static Button Applied(string styleKey, bool enabled)
    {
        var style = Application.Current!.Resources[styleKey] as Style;
        Assert.True(style != null, $"{styleKey} is missing from Styles/Controls.xaml");
        var button = new Button { Style = style, Content = "x", IsEnabled = enabled };
        button.Measure(new Size(400, 100));
        button.Arrange(new Rect(0, 0, 400, 100));
        button.UpdateLayout();
        return button;
    }

    /// <summary>
    /// The Background a style's trigger sets, read from the style chain — the most derived
    /// declaration wins, which is how WPF merges triggers down <c>BasedOn</c>. A pointer state
    /// cannot be simulated on an unshown button, so the setter is resolved instead.
    /// </summary>
    private static Color TriggerColor(string styleKey, DependencyProperty property, object value)
    {
        for (var style = Application.Current!.Resources[styleKey] as Style; style != null; style = style.BasedOn)
        {
            foreach (var trigger in style.Triggers.OfType<Trigger>())
            {
                if (trigger.Property != property || !Equals(trigger.Value, value)) continue;
                var setter = trigger.Setters.OfType<Setter>()
                    .FirstOrDefault(s => s.Property == Control.BackgroundProperty);
                if (setter == null) continue;
                var brush = setter.Value switch
                {
                    DynamicResourceExtension d => Application.Current.Resources[d.ResourceKey],
                    StaticResourceExtension s => Application.Current.Resources[s.ResourceKey],
                    var v => v,
                };
                Assert.True(brush is SolidColorBrush, $"{styleKey}'s {property.Name} trigger resolves no brush");
                return ((SolidColorBrush)brush!).Color;
            }
        }
        throw new Xunit.Sdk.XunitException($"{styleKey} has no {property.Name} trigger that sets a Background");
    }

    private static Color ColorOf(string key)
    {
        var brush = Application.Current!.Resources[key] as SolidColorBrush;
        Assert.True(brush != null, $"{key} is not a brush in Styles/Colors.xaml");
        return brush!.Color;
    }

    private static double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

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
        throw new DirectoryNotFoundException("WarsOfLibertyLauncher/App.xaml not found above the test output.");
    }
}
