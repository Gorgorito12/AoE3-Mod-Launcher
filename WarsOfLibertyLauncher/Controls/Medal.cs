using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// One medal (design 121, docs/design_logros): a coin with a metal rim, an enamel the colour of
/// its group, a gloss, its pictogram, and two ribbons hanging below. The same builder draws every
/// size the design uses — 34 under the player's name, 54 in the small notice, 66 among the latest,
/// 68 on a tile, 132 in the celebration — so the proportions are the prototype's at each.
///
/// <para><b>The metal is a CONIC gradient</b>, which WPF has no brush for. It is computed once per
/// metal and size into a small bitmap (<see cref="Conic"/>) and painted as an image: the same
/// stops, the same 200° start as the prototype's <c>conic-gradient(from 200deg, …)</c>. A medal not
/// yet earned shows its progress on the rim instead: a pie from twelve o'clock, as the prototype's
/// two-stop conic draws it.</para>
///
/// <para>No Effect sits over anything with text: the glow is a sibling disc behind the coin.</para>
/// </summary>
public static class Medal
{
    /// <summary>The enamels, by group: [light, base, dark]. The first seven are design 121's table; the
    /// eighth, a dark steel, is the platinum feats' (2026-11-26), so their rim stands out on it.</summary>
    internal static readonly string[][] Enamels =
    {
        new[] { "#E0605A", "#A32D2D", "#4C1012" },
        new[] { "#5B9AF0", "#2456B0", "#0D2350" },
        new[] { "#F2BD5C", "#B47A14", "#4A2C04" },
        new[] { "#EE7FB4", "#A72D6E", "#48102E" },
        new[] { "#4FC48F", "#1F7A52", "#0A3323" },
        new[] { "#A987EE", "#6238B0", "#2A154F" },
        new[] { "#4CC3D6", "#187A8C", "#08343C" },
        new[] { "#8FA3BA", "#3B4B61", "#121A26" },
    };

    /// <summary>The conic stops of each metal, and the colour its name and dot are drawn in.</summary>
    internal static (string[] Stops, string Main) MetalOf(string? metal) => metal switch
    {
        "g" => (new[] { "#8A6420", "#F6DF9E", "#C4973D", "#FFF3CF", "#A87B2A", "#F6DF9E", "#8A6420" }, "#F3D995"),
        "s" => (new[] { "#6F7F93", "#F4F8FC", "#A9B8C9", "#FFFFFF", "#8696AA", "#E6EDF5", "#6F7F93" }, "#E6EDF5"),
        // Platinum (the feats): white with a cold cyan and lavender sheen, so it never reads as silver.
        "p" => (new[] { "#4B6A8F", "#E8F6FF", "#8FD3F0", "#FFFFFF", "#9C8FE0", "#D6F0FF", "#4B6A8F" }, "#BFE6FF"),
        _ => (new[] { "#6E3D1A", "#F0B98A", "#A8602C", "#FFD9B8", "#8A4C22", "#E3A271", "#6E3D1A" }, "#E3A271"),
    };

    public enum RimKind { Metal, Age, Progress, Faint }

    /// <summary>What one medal shows.</summary>
    public sealed record Look(
        string Icon,
        int Enamel,
        bool Earned,
        string Metal = "b",
        RankAge? Age = null,
        double Progress = -1);

    /// <summary>A medal whose coin is <paramref name="size"/> across.</summary>
    public static FrameworkElement Build(Look look, double size)
    {
        double height = Math.Round(size * 164 / 132);
        var root = new Grid
        {
            Width = size,
            Height = height,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };

        var enamel = Enamels[Math.Clamp(look.Enamel, 0, Enamels.Length - 1)];
        var c0 = Hex(enamel[0]);
        var c1 = Hex(enamel[1]);
        var c2 = Hex(enamel[2]);

        // ---- the ribbons, behind the coin
        double rw = Math.Round(size * 36 / 132);
        double rh = Math.Round(size * 66 / 132);
        double rx = Math.Round(size * 26 / 132);
        var ribbonFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(c0, 0), new GradientStop(c1, 0.6), new GradientStop(c2, 1) },
        };
        ribbonFill.Freeze();
        foreach (var left in new[] { true, false })
        {
            var ribbon = new Polygon
            {
                Points = new PointCollection { new(0, 0), new(rw, 0), new(rw, rh), new(rw / 2, rh * 0.76), new(0, rh) },
                Fill = ribbonFill,
                Opacity = look.Earned ? 1 : 0.16,
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = left ? new Thickness(rx, 0, 0, 0) : new Thickness(0, 0, rx, 0),
                RenderTransformOrigin = new Point(0.5, 0),
                RenderTransform = new RotateTransform(left ? 9 : -9),
            };
            root.Children.Add(ribbon);
        }

        // ---- the coin
        var coin = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };

        if (look.Earned)
        {
            // The glow: a blurred disc behind, never an Effect on the coin itself.
            var glowColour = look.Age is { } a ? Hex(RankAgeColors.Of(a).Main) : Hex(MetalOf(look.Metal).Main);
            var glow = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x66, glowColour.R, glowColour.G, glowColour.B)),
                Effect = new BlurEffect { Radius = Math.Max(4, size * 0.14) },
                Margin = new Thickness(size * 0.04),
                CacheMode = new BitmapCache(),
            };
            coin.Children.Add(glow);
        }

        var rim = new Ellipse();
        switch (RimOf(look))
        {
            case RimKind.Metal:
                rim.Fill = new ImageBrush(Conic(MetalOf(look.Metal).Stops, 200, PixelsFor(size)));
                break;
            case RimKind.Age:
                var (main, light) = RankAgeColors.Of(look.Age!.Value);
                rim.Fill = new ImageBrush(Conic(new[] { main, light, main, light, main }, 200, PixelsFor(size)));
                break;
            default:
                rim.Fill = new SolidColorBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF));
                break;
        }
        coin.Children.Add(rim);

        if (RimOf(look) == RimKind.Progress)
        {
            var pie = ProgressPie(size, Math.Clamp(look.Progress, 0, 1));
            if (pie != null) coin.Children.Add(pie);
        }

        double inset = Math.Max(3, Math.Round(size * 0.09));
        var face = new Ellipse
        {
            Margin = new Thickness(inset),
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            Fill = look.Earned
                ? new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.34, 0.26),
                    Center = new Point(0.34, 0.26),
                    RadiusX = 0.8,
                    RadiusY = 0.8,
                    GradientStops = { new GradientStop(c0, 0), new GradientStop(c1, 0.46), new GradientStop(c2, 1) },
                }
                : new SolidColorBrush(Hex(("#101D30"))),
        };
        coin.Children.Add(face);

        var gloss = new Ellipse
        {
            Margin = new Thickness(inset),
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0.25, 0.07),
                EndPoint = new Point(0.75, 0.93),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(look.Earned ? 92 : 13), 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.44),
                },
            },
        };
        coin.Children.Add(gloss);

        double iconSize = Math.Round(size * (size <= 54 ? 0.44 : 0.42));
        var icon = Pictogram(look.Icon, iconSize, look.Earned ? "#FFF8E1" : "#8C9CB1");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        coin.Children.Add(icon);

        root.Children.Add(coin);
        return root;
    }

    /// <summary>Which rim a medal wears: its metal (or its age) once earned, its progress while a
    /// count is under way, a faint ring otherwise.</summary>
    internal static RimKind RimOf(Look look)
        => look.Earned ? (look.Age != null ? RimKind.Age : RimKind.Metal)
         : look.Progress >= 0 ? RimKind.Progress : RimKind.Faint;

    /// <summary>The pictogram the server names, drawn as one round-capped stroke; the shield for a
    /// name this launcher does not know.</summary>
    public static FrameworkElement Pictogram(string? icon, double size, string ink)
    {
        var data = Application.Current?.TryFindResource(IconKey(icon)) as Geometry
                ?? Application.Current?.TryFindResource("AchvIconShield") as Geometry
                ?? Geometry.Empty;
        var path = new Path
        {
            Data = data,
            Stroke = new SolidColorBrush(Hex(ink)),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var canvas = new Canvas { Width = 20, Height = 20 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    /// <summary>"AchvIconTrophy" for "trophy"; Promotion's coin is the shield, as the prototype draws it.</summary>
    internal static string IconKey(string? icon)
    {
        string name = string.IsNullOrWhiteSpace(icon) || icon == "promotion" ? "shield" : icon!;
        return "AchvIcon" + char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The filled share of a rim, from twelve o'clock clockwise, in the progress blue.</summary>
    private static Shape? ProgressPie(double size, double fraction)
    {
        if (fraction <= 0) return null;
        var fill = new SolidColorBrush(Hex("#8FB8E8"));
        if (fraction >= 0.999) return new Ellipse { Fill = fill };
        double r = size / 2;
        double angle = fraction * 2 * Math.PI;
        var end = new Point(r + r * Math.Sin(angle), r - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(r, r), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(r, 0), true));
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry { Figures = { figure } };
        geometry.Freeze();
        return new Path { Data = geometry, Fill = fill, Width = size, Height = size };
    }

    private static readonly ConcurrentDictionary<string, BitmapSource> s_conics = new();

    /// <summary>
    /// A conic gradient as a square bitmap: <paramref name="stops"/> spread evenly round the
    /// circle starting <paramref name="fromDeg"/> clockwise from twelve o'clock, CSS's convention.
    /// Cached and frozen, so a page of forty medals computes each metal once per size.
    /// </summary>
    internal static BitmapSource Conic(string[] stops, double fromDeg, int pixels)
    {
        string key = string.Join(",", stops) + "|" + fromDeg + "|" + pixels;
        return s_conics.GetOrAdd(key, _ =>
        {
            var colours = stops.Select(Hex).ToArray();
            int n = pixels;
            var data = new byte[n * n * 4];
            double c = (n - 1) / 2.0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // Angle clockwise from twelve o'clock, then the start offset, as CSS measures it.
                    double deg = Math.Atan2(x - c, c - y) * 180 / Math.PI;
                    double t = ((deg - fromDeg) % 360 + 360) % 360 / 360.0;
                    double pos = t * (colours.Length - 1);
                    int i = Math.Min((int)pos, colours.Length - 2);
                    double f = pos - i;
                    var a = colours[i];
                    var b = colours[i + 1];
                    int o = (y * n + x) * 4;
                    data[o] = (byte)(a.B + (b.B - a.B) * f);
                    data[o + 1] = (byte)(a.G + (b.G - a.G) * f);
                    data[o + 2] = (byte)(a.R + (b.R - a.R) * f);
                    data[o + 3] = 255;
                }
            }
            var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, data, n * 4);
            bitmap.Freeze();
            return bitmap;
        });
    }

    /// <summary>The bitmap size for a coin: twice its width for a sharp rim on a 200 % screen.</summary>
    private static int PixelsFor(double size) => Math.Clamp((int)Math.Ceiling(size * 2), 16, 300);

    internal static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
