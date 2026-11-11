using System;
using System.Windows;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The three glass levels of the design base (docs/design_base_launcher/BASE.md): how much of the
/// mod's image shows through the panels. It rewrites a handful of brushes in
/// <c>Application.Resources</c>, the same way <see cref="TextScale"/> rewrites the type tokens, so
/// everything bound with <c>{DynamicResource}</c> follows at once and a setting needs no restart.
///
/// <para>The numbers are the handoff's. Colors.xaml declares the same brushes at the default level
/// so a window built without this running, as the tests do, still resolves them.</para>
/// </summary>
public static class GlassTheme
{
    public const string Solid = "solid";
    public const string Medium = "mid";
    public const string Maximum = "max";

    public const string PanelKey = "GlassPanel";
    public const string TileKey = "GlassTile";
    public const string MenuKey = "GlassMenu";
    public const string FooterKey = "GlassFooter";
    public const string TitleBarKey = "GlassTitleBar";

    /// <summary>What every menu, popover, toast and floating panel is painted with. Denser than
    /// the design's .95 / .74 / .6, whose numbers come with a blur of 18 to 32 px because these
    /// surfaces sit over text. WPF cannot blur what is behind a popup, and at .74 without it the
    /// text behind reads through the menu's own (seen on screen).</summary>
    public static readonly string[] FloatingKeys = { "GlassMenu", "ChromePopupBg", "LibraryMenuBg" };

    /// <summary>Colours of one level. The title bar is #0b1422 at <paramref name="BarAlpha"/>.</summary>
    public readonly record struct Spec(Color Panel, Color Tile, Color Menu, Color Footer, double BarAlpha, Color Floating);

    /// <summary>An unknown or missing value reads as the default, Media.</summary>
    public static string Normalize(string? level) => level switch
    {
        Solid => Solid,
        Maximum => Maximum,
        _ => Medium,
    };

    public static Spec For(string? level) => Normalize(level) switch
    {
        Solid => new Spec(Rgba(12, 22, 38, .86), Rgba(12, 22, 38, .8), Rgba(12, 22, 38, .95), Rgba(12, 22, 38, .94), 1, Rgba(12, 22, 38, .97)),
        Maximum => new Spec(Rgba(10, 19, 33, .28), Rgba(10, 19, 33, .28), Rgba(12, 22, 38, .6), Rgba(10, 19, 33, .42), .3, Rgba(12, 22, 38, .88)),
        _ => new Spec(Rgba(10, 19, 33, .5), Rgba(10, 19, 33, .28), Rgba(12, 22, 38, .74), Rgba(10, 19, 33, .62), .42, Rgba(12, 22, 38, .93)),
    };

    /// <summary>The title bar of a window of one mod: its colour at 22 % in #0b1422 (BASE.md).</summary>
    public static Color ModBar(Color accent, string? level)
    {
        var mixed = ModAccent.Mix(Color.FromRgb(11, 20, 34), accent, .22);
        return Color.FromArgb((byte)System.Math.Round(For(level).BarAlpha * 255), mixed.R, mixed.G, mixed.B);
    }

    /// <summary>
    /// The glass that is not a panel: bars, controls, chips and footers that the launcher painted
    /// with an alpha of their own, so they stayed put while the panels beside them followed the
    /// level (reported: the list changed and the tab bar, the search box and the chips did not).
    /// Each keeps the value it already had at Media, which is the handoff's; the other two levels
    /// move it the way the panel moves (<see cref="Follow"/>). Adding a translucent surface means
    /// adding its key here, or it will not follow.
    /// </summary>
    public static readonly (string Key, Color Base, double MediumAlpha, bool Inset)[] Followers =
    {
        ("LibraryNavBg", Color.FromRgb(10, 19, 33), .45, false),
        ("RmSubBarBg", Color.FromRgb(10, 19, 33), .32, false),
        ("LibraryControl", Color.FromRgb(10, 19, 33), .55, false),
        ("LibraryGhost", Color.FromRgb(10, 19, 33), .5, false),
        ("LibraryCard", Color.FromRgb(10, 19, 33), .66, false),
        ("ModChipFill", Color.FromRgb(10, 19, 33), .55, false),
        ("ModDeckDetailFill", Color.FromRgb(10, 19, 33), .55, false),
        ("ModDeckTileFill", Color.FromRgb(10, 19, 33), .45, false),
        ("GlassWell", Color.FromRgb(5, 10, 18), .4, true),
        ("RmSearchBg", Color.FromRgb(5, 10, 18), .4, true),
        ("ProfilePillFill", Color.FromRgb(5, 10, 18), .4, true),
        ("ProfileTabsFill", Color.FromRgb(5, 10, 18), .4, true),
        ("WindowFooter", Color.FromRgb(8, 15, 27), .72, false),
    };

    /// <summary>The navy everything sits on when nothing shows through (BASE.md's #0f1c2e).</summary>
    public static readonly Color PageNavy = Color.FromRgb(15, 28, 46);

    /// <summary>A follower's alpha at a level: Solid never lets less through than the panel does
    /// (.86), Maximum lets through as much more as the panel does (.28 / .5). An INSET (a dark well
    /// inside a card) keeps its own alpha at Solid: there it darkens an opaque card, and at .86 it
    /// would turn the search box black.</summary>
    public static double Follow(double mediumAlpha, string? level, bool inset = false) => Normalize(level) switch
    {
        Solid => inset ? mediumAlpha : Math.Max(mediumAlpha, .86),
        Maximum => mediumAlpha * .56,
        _ => mediumAlpha,
    };

    /// <summary>
    /// "Effects: reduced" (or a machine that cannot draw them) asks for solid backgrounds
    /// (BASE.md): every glass colour becomes what it would look like over the page's navy, opaque.
    /// </summary>
    public static Color Settle(Color glass, bool reducedEffects)
    {
        if (!reducedEffects || glass.A == 255) return glass;
        var mixed = ModAccent.Mix(PageNavy, Color.FromRgb(glass.R, glass.G, glass.B), glass.A / 255.0);
        return Color.FromRgb(mixed.R, mixed.G, mixed.B);
    }

    /// <summary>The one rule for "reduced effects": the setting, or Automatic on a machine that
    /// has turned animations off or renders below tier 2.</summary>
    public static bool IsReduced(string? effectsMode) => effectsMode switch
    {
        "low" => true,
        "full" => false,
        _ => !SystemParameters.ClientAreaAnimation || RenderCapability.Tier >> 16 < 2,
    };

    public static void Apply(string? level, bool reducedEffects = false)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;
        var spec = For(level);
        Set(resources, PanelKey, Settle(spec.Panel, reducedEffects));
        Set(resources, TileKey, Settle(spec.Tile, reducedEffects));
        foreach (var key in FloatingKeys) Set(resources, key, Settle(spec.Floating, reducedEffects));
        Set(resources, FooterKey, Settle(spec.Footer, reducedEffects));
        Set(resources, TitleBarKey, Settle(Rgba(11, 20, 34, spec.BarAlpha), reducedEffects));
        foreach (var (key, color, alpha, inset) in Followers)
            Set(resources, key, Settle(Color.FromArgb((byte)Math.Round(Follow(alpha, level, inset) * 255), color.R, color.G, color.B), reducedEffects));
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static Color Rgba(byte r, byte g, byte b, double alpha)
        => Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b);
}
