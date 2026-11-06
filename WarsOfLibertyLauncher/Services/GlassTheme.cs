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

    /// <summary>Colours of one level. The title bar is #0b1422 at <paramref name="BarAlpha"/>.</summary>
    public readonly record struct Spec(Color Panel, Color Tile, Color Menu, Color Footer, double BarAlpha);

    /// <summary>An unknown or missing value reads as the default, Media.</summary>
    public static string Normalize(string? level) => level switch
    {
        Solid => Solid,
        Maximum => Maximum,
        _ => Medium,
    };

    public static Spec For(string? level) => Normalize(level) switch
    {
        Solid => new Spec(Rgba(12, 22, 38, .86), Rgba(12, 22, 38, .8), Rgba(12, 22, 38, .95), Rgba(12, 22, 38, .94), 1),
        Maximum => new Spec(Rgba(10, 19, 33, .28), Rgba(10, 19, 33, .28), Rgba(12, 22, 38, .6), Rgba(10, 19, 33, .42), .3),
        _ => new Spec(Rgba(10, 19, 33, .5), Rgba(10, 19, 33, .28), Rgba(12, 22, 38, .74), Rgba(10, 19, 33, .62), .42),
    };

    /// <summary>The title bar of a window of one mod: its colour at 22 % in #0b1422 (BASE.md).</summary>
    public static Color ModBar(Color accent, string? level)
    {
        var mixed = ModAccent.Mix(Color.FromRgb(11, 20, 34), accent, .22);
        return Color.FromArgb((byte)System.Math.Round(For(level).BarAlpha * 255), mixed.R, mixed.G, mixed.B);
    }

    public static void Apply(string? level)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;
        var spec = For(level);
        Set(resources, PanelKey, spec.Panel);
        Set(resources, TileKey, spec.Tile);
        Set(resources, MenuKey, spec.Menu);
        Set(resources, FooterKey, spec.Footer);
        Set(resources, TitleBarKey, Rgba(11, 20, 34, spec.BarAlpha));
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
