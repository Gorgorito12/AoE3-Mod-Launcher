using System.Windows.Media;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The tournament's mod colour on the page's CHROME (2026-11-18, over design 106's "only what is
/// yours"): the active tab and filter, the mod chip, the panels' rims, the bracket's lines, the
/// round strip's rule, the cards on your way through the draw and the zoom box. As a spectator the
/// page used to wear the mod nowhere but the selected list row.
///
/// <para>What it never touches is the point: a STATE keeps <see cref="StateColors"/> (registration
/// green, being played blue, the champion gold, results, tags, the legend), so a red mod never
/// reads as an error and a gold one never steals the champion's colour. Every value is the accent
/// at an alpha over the page's navy, through <see cref="ModAccent"/>, so the launcher's "use the
/// mod's colour" setting (off → launcher blue) reaches all of it through the one accent.</para>
/// </summary>
public readonly record struct TournamentAmbient(
    Color ActiveFill, Color ActiveRim,
    Color ChipFill, Color ChipRim, Color ChipText,
    Color PanelRimTop, Color PanelRimBottom,
    Color Line, Color Hairline, Color PathRim, Color ToolRim, Color Glow)
{
    public static TournamentAmbient For(Color accent)
    {
        var text = ModAccent.TextOn(accent);
        return new TournamentAmbient(
            ActiveFill: ModAccent.SelectionFill(accent),
            ActiveRim: WithAlpha(accent, .60),
            // TextOn is measured against the navy; the chip is lighter than that, so its name goes
            // a step further towards white to stay readable on it.
            ChipFill: ModAccent.Mix(accent, ModAccent.Navy, .78),
            ChipRim: WithAlpha(accent, .55),
            ChipText: ModAccent.Mix(text, Colors.White, .35),
            PanelRimTop: WithAlpha(accent, .50),
            PanelRimBottom: WithAlpha(accent, .14),
            Line: WithAlpha(text, .24),
            Hairline: WithAlpha(accent, .35),
            PathRim: WithAlpha(accent, .40),
            ToolRim: WithAlpha(accent, .45),
            Glow: WithAlpha(accent, .40));
    }

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)System.Math.Round(255 * System.Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
}
