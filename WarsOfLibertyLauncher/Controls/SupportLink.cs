using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The one way into the project's Discord, built once and dropped wherever a player is stuck.
///
/// <para><b>Why a shared builder rather than a button per dialog.</b> This appears in five
/// unrelated places — the brand menu, the diagnostics section, the antivirus dialog, the
/// compatibility-layer dialog and the Radmin assistant — and five copies of "glyph, label,
/// tooltip, open" is five things that drift. The tooltip in particular is not decoration: it
/// shows the FULL url, which is the same anti-phishing measure the mod link pills make, and it
/// is exactly the sort of detail a copy would quietly lose.</para>
///
/// <para><b>Where this goes is the whole point.</b> A support link in an About box asks the
/// player to remember it exists at the moment they least need it. These live where the launcher
/// has just told somebody that their antivirus ate a file, or that Windows put a compatibility
/// layer on their game, or has walked them to the end of the Radmin checklist and left them to
/// verify it themselves — screens that named a problem and offered nowhere to go.</para>
/// </summary>
internal static class SupportLink
{
    /// <summary>
    /// A pill that opens the Discord, styled exactly like the mod link pills in the Workshop so
    /// it reads as the same kind of thing.
    ///
    /// <para>The wording is deliberately "need help?" rather than an invitation: every place
    /// this appears is a place where something has already gone wrong, so the link is an answer,
    /// not an advert.</para>
    /// </summary>
    /// <param name="captionSize">
    /// The caption's size, and the glyphs scale with it so the pill stays proportional.
    ///
    /// <para>Null keeps the pill's own <c>ModLinkPill</c> size, which is what three of the
    /// four hosts want: there the pill sits alone on its line in a dialog running on the
    /// app-wide type scale. The fourth is the DIAGNOSTICS row of the mod properties window,
    /// where it stands beside two buttons on the smaller settings scale — it read a size and
    /// a half heavier than its neighbours, and that surplus width was also what pushed it
    /// off the edge of the card in Spanish. The size belongs to the HOST, not to this
    /// builder, which is why it is a parameter rather than a second builder.</para>
    /// </param>
    public static Button Build(double? captionSize = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = captionSize ?? Caption;

        // Keeps its own Foreground so it stays gold while the caption brightens on hover —
        // the same split the mod link pills use.
        content.Children.Add(new TextBlock
        {
            Text = ModLink.GlyphFor(ModLinkType.Discord),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = glyph,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Foreground = Brush("AccentBrush"),
        });

        // NO Foreground here on purpose: the ContentPresenter propagates the button's, which is
        // the only route the style's hover trigger has to reach this caption.
        content.Children.Add(new TextBlock
        {
            Text = Strings.Get("SupportDiscordHelpLabel"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        content.Children.Add(new TextBlock
        {
            Text = ModLink.ExternalGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = glyph - 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brush("TextSecondary"),
        });

        var button = new Button
        {
            Content = content,
            // Colours come from the style, never from here: a local Background or BorderBrush
            // beats the template's hover triggers and the pill goes dead. FontSize is safe to
            // set locally — no trigger drives it, so precedence costs nothing here.
            Style = (Style)Application.Current.FindResource("ModLinkPill"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            // The destination in full. A label can promise anything; showing where it actually
            // goes is the measure that matters.
            ToolTip = TooltipHelper.Wrap(LauncherConfig.SupportDiscordUrl),
        };
        if (captionSize is double size) button.FontSize = size;
        button.Click += (_, _) => Open();
        return button;
    }

    /// <summary>Open the Discord. Through <see cref="SafeUrl"/> like every other link.</summary>
    public static bool Open() => SafeUrl.TryOpen(LauncherConfig.SupportDiscordUrl);

    private static double Caption => (double)Application.Current.FindResource("FontSizeCaption");

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
