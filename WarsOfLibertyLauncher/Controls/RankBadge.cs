using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The rank badge: a heraldic shield in the colours of an AoE3 age, with the ladder position
/// inside it. ONE builder for the three screens that show it — the Ranking table (24 px, 28 for
/// first place), the rooms row (17 px, in the avatar's place) and the room's player panel (24 px)
/// — so the three cannot drift apart. See <c>docs/design_insignias_rango</c>.
///
/// <para><b>Every layer draws the one shield declared in <c>Styles/Badges.xaml</c></b>
/// (<c>RankShieldGeometry</c>), stretched onto the badge's own box. The shield touches all four
/// sides of its 100 x 100 design box, so <see cref="Stretch.Fill"/> maps it onto any badge with
/// no seam between the plate and what is drawn over it. The facets are that same shield cut in
/// two, and the sheens are held inside it by an <see cref="UIElement.OpacityMask"/> made of it.</para>
///
/// <para><b>No <c>Clip</c>, anywhere.</b> The outer edge sits 2.5 px outside the plate, the aura
/// and the Sovereign's halo and flags reach further, and some of its stars fall outside the
/// shield on purpose. Everything is laid out in a Grid whose own size is the plate's, so the
/// badge takes exactly its nominal room in a table cell and the light simply paints past it.</para>
///
/// <para><b>The numeral is monospaced with aligned figures</b> (<c>MonoFont</c>, the same font
/// the ranking's numbers use), never the display serif, whose old-style figures make "1" and
/// "14" dance in height down a column. Its glow is a SIBLING blurred copy under the crisp text,
/// never an <see cref="Effect"/> on the numeral or anything above it.</para>
///
/// <para><b>The light</b> (<see cref="RankBadgeTiming"/>) rises with the age and only transforms
/// and opacities move — never a brush out of a dictionary, which is frozen and would throw.
/// Animations run only while the badge is on screen, and not at all when Windows' animations are
/// switched off, in which case the badge keeps its colours and stays still.</para>
/// </summary>
public static class RankBadge
{
    /// <summary>Width-to-height of the shield: the prototype draws it 36 x 42.</summary>
    public const double AspectHeight = 7.0 / 6.0;

    /// <summary>The prototype's reference width; the light's pixel sizes scale from it.</summary>
    private const double ReferenceWidth = 36;

    /// <summary>The outer edge sits this far outside the plate, on Industrial and up.</summary>
    private const double EdgeOutset = 2.5;

    /// <summary>The veil over the plate is inset this far, so the plate shows as a rim.</summary>
    private const double VeilInset = 1.5;

    /// <summary>
    /// Forces the light on or off regardless of the system setting. Tests only: the real answer
    /// is Windows' own <see cref="SystemParameters.ClientAreaAnimation"/>.
    /// </summary>
    internal static bool? AnimationsOverride { get; set; }

    private static bool AnimationsEnabled => AnimationsOverride ?? SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// A badge for a ladder position as the server numbered it. <paramref name="position"/> 0 (or
    /// less) is Discovery: not on the ladder, drawn with no number.
    /// </summary>
    public static FrameworkElement ForPosition(int position, double width, string seedKey, string? tooltip = null)
    {
        var age = RankAges.For(position);
        return Build(age, position > 0 ? position.ToString() : null, width, seedKey, tooltip);
    }

    /// <summary>
    /// The hover text for a badge. Discovery names the entry bar when the server told us what it
    /// is (<paramref name="minDecided"/>), and says only that the player is not ranked yet when it
    /// did not — never an invented number.
    /// </summary>
    public static string TooltipFor(RankAge age, int position, int? minDecided)
    {
        var name = Strings.Get(RankAges.NameKey(age));
        if (age == RankAge.Discovery)
            return minDecided is > 0
                ? Strings.Format("MpRankBadgeTipDiscovery", minDecided.Value)
                : Strings.Get("MpRankBadgeTipDiscoveryNoBar");
        return Strings.Format("MpRankBadgeTip", name, position);
    }

    /// <summary>
    /// Builds a badge. <paramref name="numeral"/> null draws an empty plate — Discovery's, which
    /// carries no position. <paramref name="seedKey"/> is what the sparks' pattern is drawn from
    /// (a player id or the position), never a counter: the pages that show badges are rebuilt on
    /// every payload, and a pattern that changed on each refresh would flicker.
    /// </summary>
    public static FrameworkElement Build(RankAge age, string? numeral, double width, string seedKey, string? tooltip = null)
    {
        var height = Math.Round(width * AspectHeight, 1);
        var k = width / ReferenceWidth;
        var animate = AnimationsEnabled && age != RankAge.Discovery;
        var light = RankBadgeTiming.For(age);
        var anims = new List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Timeline)>();

        var root = new Grid
        {
            Width = width,
            Height = height,
            // Transparent, not null: a null background is not hit-testable, and the tooltip
            // belongs to the whole shield rather than to the few pixels of the numeral.
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Tag = age,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // ── Behind the shield: the Sovereign's counter-rotating halo and its side flags ──
        if (animate && light.HaloSeconds > 0)
        {
            // Alphas below the prototype's .92/.60: its conic sweep is a thin wedge, and the linear
            // gradient standing in for it covers far more of the ring at the same alpha.
            AddHalo(root, width, height, 6, 0.55, 3.5, light.HaloSeconds, clockwise: true, anims);
            AddHalo(root, width, height, 4, 0.38, 2.5, light.HaloReverseSeconds, clockwise: false, anims);
            AddFlags(root, age, k, light.AuraSeconds, anims);
        }

        // ── Aura: a blurred copy of the shield in the age's light colour, breathing ──
        if (animate && light.AuraSeconds > 0)
        {
            var aura = Shield(Res($"RankInk{age}"), -2);
            aura.Effect = new BlurEffect { Radius = light.AuraBlur * 1.3 };
            aura.Opacity = 0.2;
            anims.Add((aura, UIElement.OpacityProperty,
                Keyframes(light.AuraSeconds, RankBadgeTiming.EaseInOut, (0, 0.2), (0.5, 0.66), (1, 0.2))));
            root.Children.Add(aura);
        }

        // 1. Outer edge — Industrial and up. The first thing a higher age adds.
        if (age >= RankAge.Industrial)
            root.Children.Add(Shield(Res($"RankEdge{age}"), -EdgeOutset));

        // 2. The plate.
        root.Children.Add(Shield(Res($"RankPlate{age}"), 0));

        // 2b. Edge light: the plate brightening and dimming (CSS brightness 1 → 1.3).
        if (animate && light.EdgeLightSeconds > 0)
        {
            var glint = Shield(Brushes.White, 0);
            glint.Opacity = 0;
            anims.Add((glint, UIElement.OpacityProperty,
                Keyframes(light.EdgeLightSeconds, RankBadgeTiming.EaseInOut, (0, 0), (0.5, 0.23), (1, 0))));
            root.Children.Add(glint);
        }

        // 3. The veil over it, which retires as the age rises. Discovery gets a flat one.
        if (age == RankAge.Discovery)
        {
            root.Children.Add(Shield(Res("RankVeilDiscovery"), VeilInset));
        }
        else
        {
            var veil = age >= RankAge.Imperial ? "RankVeilThin" : "RankVeilDense";
            root.Children.Add(Shield(Res(veil), VeilInset));
            root.Children.Add(Shield(Res("RankVeilShade"), VeilInset));
            root.Children.Add(Shield(Res("RankVeilGloss"), VeilInset));
            if (age == RankAge.Colonial)
                root.Children.Add(Shield(Res("RankTintColonial"), VeilInset));
        }

        // 4. Facets — the shield cut in two at 40-50 %, each catching the light in turn.
        if (animate && light.FacetSeconds > 0)
        {
            var glow = ((SolidColorBrush)Res($"RankGlow{age}")).Color;
            root.Children.Add(Facet("RankShieldFacetUpper", glow, 0.5, width, height, light.FacetSeconds,
                new[] { (0.0, 0.16), (0.34, 0.62), (0.52, 0.2), (1.0, 0.16) }, anims));
            root.Children.Add(Facet("RankShieldFacetLower", glow, 0.34, width, height, light.FacetSeconds,
                new[] { (0.0, 0.1), (0.52, 0.5), (0.72, 0.14), (1.0, 0.1) }, anims));
        }

        // 5. Inside the shield: the rotating inner shine and the two sheens, held in by a mask
        //    of the shield itself (a mask, not a Clip).
        if (animate && light.BroadSheenSeconds > 0)
        {
            var inside = new Grid { OpacityMask = ShieldMask(width, height), IsHitTestVisible = false };

            if (light.InnerShineSeconds > 0)
            {
                var glow = ((SolidColorBrush)Res($"RankGlow{age}")).Color;
                var spin = new RotateTransform(0, 0.5, 0.5);
                inside.Children.Add(new Rectangle
                {
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops =
                        {
                            new GradientStop(Colors.Transparent, 0.2),
                            new GradientStop(Color.FromArgb((byte)(age == RankAge.Sovereign ? 153 : 115), glow.R, glow.G, glow.B), 0.5),
                            new GradientStop(Colors.Transparent, 0.8),
                        },
                        RelativeTransform = spin,
                    },
                });
                anims.Add((spin, RotateTransform.AngleProperty, Spin(light.InnerShineSeconds, clockwise: true)));
            }

            inside.Children.Add(Sheen(width, height, light.BroadSheenSeconds, RankBadgeTiming.BroadSheenTravel,
                RankBadgeTiming.EaseInOut, light.BroadSheenAlpha, sharp: false, delay: 0, anims));
            if (light.SharpSheenSeconds > 0)
                inside.Children.Add(Sheen(width, height, light.SharpSheenSeconds, light.SharpSheenTravel,
                    RankBadgeTiming.SharpSheenCurve, light.SharpSheenAlpha, sharp: true,
                    delay: RankBadgeTiming.SharpSheenDelaySeconds, anims));

            root.Children.Add(inside);
        }

        // 6. The dark well the numeral sits in, centred a little above the middle (46 %).
        var well = width * 1.07;
        root.Children.Add(new Ellipse
        {
            Width = well,
            Height = well,
            Fill = Res("RankNumeralWell"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-(well - width) / 2, height * 0.46 - well / 2, -(well - width) / 2, 0),
            IsHitTestVisible = false,
        });

        // 7. The numeral, with its glow as a blurred sibling underneath. Both share one scale:
        //    the number grows 5 % exactly as the sharp sheen crosses the centre.
        if (!string.IsNullOrEmpty(numeral))
        {
            var fontSize = Math.Max(7, Math.Round(width * 0.43 * 2) / 2);
            var mono = (FontFamily)Application.Current.FindResource("MonoFont");
            var weight = age == RankAge.Discovery ? FontWeights.SemiBold : FontWeights.Bold;
            var numeralMargin = new Thickness(0, 0, 0, height * 0.08);
            ScaleTransform? pulse = null;
            if (animate && light.PulseKeys is { } keys)
            {
                pulse = new ScaleTransform(1, 1);
                var timeline = Keyframes(light.SharpSheenSeconds, RankBadgeTiming.EaseInOut,
                    (0, 1), (keys[0], 1), (keys[1], RankBadgeTiming.PulseScale), (keys[2], 1), (1, 1));
                timeline.BeginTime = TimeSpan.FromSeconds(RankBadgeTiming.SharpSheenDelaySeconds);
                anims.Add((pulse, ScaleTransform.ScaleXProperty, timeline));
                anims.Add((pulse, ScaleTransform.ScaleYProperty, timeline.Clone()));
            }

            if (age >= RankAge.Fortress)
            {
                root.Children.Add(new TextBlock
                {
                    Text = numeral,
                    FontFamily = mono,
                    FontSize = fontSize,
                    FontWeight = weight,
                    Foreground = Res($"RankGlow{age}"),
                    Opacity = 0.9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = numeralMargin,
                    Effect = new BlurEffect { Radius = age == RankAge.Sovereign ? 6 : 4 },
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = pulse,
                    IsHitTestVisible = false,
                });
            }

            root.Children.Add(new TextBlock
            {
                Text = numeral,
                FontFamily = mono,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = Res($"RankInk{age}"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = numeralMargin,
                TextAlignment = TextAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = pulse,
                IsHitTestVisible = false,
            });
        }

        // 8. Sparks — never two badges alike, never in step, and the same badge alike every
        //    time it is rebuilt: everything is drawn from the seed.
        if (animate && light.Sparks != RankSparkKind.None)
        {
            var canvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
            foreach (var spark in RankBadgeTiming.Sparks(seedKey, age, width))
                canvas.Children.Add(Spark(spark, light.Sparks, age, width, height, k, anims));
            root.Children.Add(canvas);
        }

        if (!string.IsNullOrEmpty(tooltip))
            root.ToolTip = TooltipHelper.Wrap(tooltip);

        if (anims.Count > 0)
        {
            SetAnimations(root, anims);
            root.Loaded += (_, _) => { if (root.IsVisible) Start(root); };
            root.Unloaded += (_, _) => Stop(root);
            root.IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Start(root); else Stop(root); };
        }

        return root;
    }

    // ── Animation bookkeeping ────────────────────────────────────────────

    private sealed class AnimationSet
    {
        public required List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Timeline)> Items { get; init; }
        public bool Running { get; set; }
    }

    private static readonly DependencyProperty AnimationsProperty = DependencyProperty.RegisterAttached(
        "RankBadgeAnimations", typeof(AnimationSet), typeof(RankBadge));

    private static void SetAnimations(DependencyObject root, List<(IAnimatable, DependencyProperty, AnimationTimeline)> items)
        => root.SetValue(AnimationsProperty, new AnimationSet { Items = items });

    /// <summary>How many animations the badge carries. Tests only.</summary>
    internal static int AnimationCount(DependencyObject badge)
        => (badge.GetValue(AnimationsProperty) as AnimationSet)?.Items.Count ?? 0;

    /// <summary>Whether the badge's light is running right now. Tests only.</summary>
    internal static bool IsRunning(DependencyObject badge)
        => (badge.GetValue(AnimationsProperty) as AnimationSet)?.Running ?? false;

    /// <summary>Starts the light. Idempotent: a badge that is already running is left alone, so
    /// Loaded and IsVisibleChanged arriving together do not restart its phase.</summary>
    internal static void Start(DependencyObject badge)
    {
        if (badge.GetValue(AnimationsProperty) is not AnimationSet set || set.Running) return;
        foreach (var (target, property, timeline) in set.Items)
            target.BeginAnimation(property, timeline);
        set.Running = true;
    }

    /// <summary>Stops the light, leaving every property at its resting value.</summary>
    internal static void Stop(DependencyObject badge)
    {
        if (badge.GetValue(AnimationsProperty) is not AnimationSet set || !set.Running) return;
        foreach (var (target, property, _) in set.Items)
            target.BeginAnimation(property, null);
        set.Running = false;
    }

    // ── Layers ───────────────────────────────────────────────────────────

    /// <summary>
    /// One layer of the shield: the shared geometry, stretched onto the badge inset by
    /// <paramref name="inset"/> pixels on every side (negative reaches outside it).
    /// </summary>
    private static Path Shield(Brush fill, double inset) => new()
    {
        Data = (Geometry)Application.Current.FindResource("RankShieldGeometry"),
        Stretch = Stretch.Fill,
        Fill = fill,
        Margin = new Thickness(inset),
        IsHitTestVisible = false,
    };

    /// <summary>
    /// The shield as a mask, in the badge's own pixels. Absolute units on purpose: a mask sized
    /// to its element's bounding box would be stretched over the sheens' overflow instead of over
    /// the badge.
    /// </summary>
    private static Brush ShieldMask(double width, double height) => new DrawingBrush
    {
        Drawing = new GeometryDrawing(Brushes.Black, null,
            (Geometry)Application.Current.FindResource("RankShieldGeometry")),
        Stretch = Stretch.Fill,
        ViewboxUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, 100, 100),
        ViewportUnits = BrushMappingMode.Absolute,
        Viewport = new Rect(0, 0, width, height),
        TileMode = TileMode.None,
    };

    /// <summary>A facet: one half of the shared shield, scaled onto the veil's box.</summary>
    private static Path Facet(string key, Color glow, double alpha, double width, double height, double seconds,
        (double At, double Opacity)[] keys, List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims)
    {
        var facet = new Path
        {
            Data = (Geometry)Application.Current.FindResource(key),
            Stretch = Stretch.None,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), glow.R, glow.G, glow.B)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(VeilInset),
            // LayoutTransform, not RenderTransform: an unstretched 100 x 100 path arranged into a
            // smaller cell is clipped by LAYOUT before a render transform ever scales it, which
            // cut the facets off in a straight line. Scaled in layout it simply fits the cell.
            LayoutTransform = new ScaleTransform((width - 2 * VeilInset) / 100, (height - 2 * VeilInset) / 100),
            Opacity = keys[0].Opacity,
            IsHitTestVisible = false,
        };
        anims.Add((facet, UIElement.OpacityProperty, Keyframes(seconds, RankBadgeTiming.EaseInOut, keys)));
        return facet;
    }

    /// <summary>
    /// A sheen: a band as wide as the badge, crossing it from −160 % to +260 % of its width over
    /// <paramref name="travel"/> of the cycle and then waiting off-shield for the rest of it.
    /// </summary>
    private static Rectangle Sheen(double width, double height, double seconds, double travel,
        (double X1, double Y1, double X2, double Y2) curve, double alpha, bool sharp, double delay,
        List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims)
    {
        var stops = new GradientStopCollection();
        if (sharp)
        {
            stops.Add(new GradientStop(White(0), 0.08));
            stops.Add(new GradientStop(White(alpha * 0.22), 0.26));
            stops.Add(new GradientStop(White(alpha * 0.70), 0.42));
            stops.Add(new GradientStop(White(alpha), 0.50));
            stops.Add(new GradientStop(White(alpha * 0.70), 0.58));
            stops.Add(new GradientStop(White(alpha * 0.22), 0.74));
            stops.Add(new GradientStop(White(0), 0.92));
        }
        else
        {
            stops.Add(new GradientStop(White(0), 0.04));
            stops.Add(new GradientStop(White(alpha * 0.4), 0.28));
            stops.Add(new GradientStop(White(alpha), 0.50));
            stops.Add(new GradientStop(White(alpha * 0.4), 0.72));
            stops.Add(new GradientStop(White(0), 0.96));
        }

        var move = new TranslateTransform(RankBadgeTiming.SheenFrom * width, 0);
        var band = new Rectangle
        {
            Width = width,
            Height = height * 1.4,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -height * 0.2, 0, 0),
            // 103deg: almost left-to-right, leaning slightly.
            Fill = new LinearGradientBrush(stops, new Point(0, 0.39), new Point(1, 0.61)),
            RenderTransform = move,
            IsHitTestVisible = false,
        };

        var cycle = TimeSpan.FromSeconds(seconds);
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = cycle,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delay),
        };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(RankBadgeTiming.SheenFrom * width, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(RankBadgeTiming.SheenTo * width,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * travel)), Spline(curve)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(RankBadgeTiming.SheenTo * width, KeyTime.FromTimeSpan(cycle)));
        anims.Add((move, TranslateTransform.XProperty, anim));
        return band;
    }

    /// <summary>
    /// One ring of the Sovereign's halo: an ellipse around the shield whose gradient rotates,
    /// blurred. WPF has no conic gradient, so a rotating linear one stands in for the CSS
    /// <c>conic-gradient</c> sweep — a declared deviation from the prototype.
    /// </summary>
    private static void AddHalo(Grid root, double width, double height, double outset, double alpha, double blur,
        double seconds, bool clockwise, List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims)
    {
        var glow = ((SolidColorBrush)Res("RankGlowSovereign")).Color;
        var spin = new RotateTransform(clockwise ? 0 : 180, 0.5, 0.5);
        root.Children.Add(new Ellipse
        {
            Margin = new Thickness(-outset),
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0.55),
                    new GradientStop(Color.FromArgb((byte)(alpha * 255), glow.R, glow.G, glow.B), 0.85),
                    new GradientStop(Colors.Transparent, 1.0),
                },
                RelativeTransform = spin,
            },
            Effect = new BlurEffect { Radius = blur * 1.3 },
            IsHitTestVisible = false,
        });
        anims.Add((spin, RotateTransform.AngleProperty, Spin(seconds, clockwise)));
    }

    /// <summary>The Sovereign's banners: three bars each side of the shield, breathing with the aura.</summary>
    private static void AddFlags(Grid root, RankAge age, double k, double auraSeconds,
        List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims)
    {
        var glow = ((SolidColorBrush)Res($"RankGlow{age}")).Color;
        foreach (var left in new[] { true, false })
        {
            var bars = new StackPanel
            {
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = left ? new Thickness(-12 * k, 8 * k, 0, 0) : new Thickness(0, 8 * k, -12 * k, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(left ? -1 : 1, 1),
                IsHitTestVisible = false,
                Opacity = 0.2,
            };
            foreach (var (length, indent) in new[] { (13.0, 0.0), (9.0, 1.5), (6.0, 3.0) })
            {
                bars.Children.Add(new Rectangle
                {
                    Width = length * k,
                    Height = Math.Max(1, 2 * k),
                    Margin = new Thickness(indent * k, 0, 0, 1.5 * k),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(64, glow.R, glow.G, glow.B), 0),
                });
            }
            anims.Add((bars, UIElement.OpacityProperty,
                Keyframes(auraSeconds, RankBadgeTiming.EaseInOut, (0, 0.2), (0.5, 0.66), (1, 0.2))));
            root.Children.Add(bars);
        }
    }

    /// <summary>
    /// A spark, placed and timed by <see cref="RankBadgeTiming.Sparks"/>. They spend most of
    /// their cycle invisible, which is what keeps a row of badges calm.
    /// </summary>
    private static FrameworkElement Spark(RankSpark spark, RankSparkKind kind, RankAge age, double width, double height,
        double k, List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims)
    {
        var size = Math.Max(4, spark.Size * k);
        var glow = ((SolidColorBrush)Res($"RankGlow{age}")).Color;
        FrameworkElement shape = kind switch
        {
            RankSparkKind.Ember => new Ellipse
            {
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Colors.White, 0),
                        new GradientStop(Color.FromArgb(220, glow.R, glow.G, glow.B), 0.3),
                        new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1),
                    },
                },
            },
            RankSparkKind.Mote => new Path
            {
                Data = Geometry.Parse("M50,0 L60,40 100,50 60,60 50,100 40,60 0,50 40,40Z"),
                Stretch = Stretch.Fill,
                Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xF3, 0xC0), Color.FromRgb(0xFF, 0xD7, 0x6B), 45),
            },
            _ => new Path
            {
                Data = Geometry.Parse(spark.EightPoints
                    ? "M50,0 L53,40 72,28 60,47 100,50 60,53 72,72 53,60 50,100 47,60 28,72 40,53 0,50 40,47 28,28 47,40Z"
                    : "M50,0 L54,46 100,50 54,54 50,100 46,54 0,50 46,46Z"),
                Stretch = Stretch.Fill,
                Fill = Brushes.White,
            },
        };
        shape.Width = size;
        shape.Height = size;
        shape.IsHitTestVisible = false;
        shape.Opacity = 0;
        shape.RenderTransformOrigin = new Point(0.5, 0.5);
        Canvas.SetLeft(shape, spark.X * width - size / 2);
        Canvas.SetTop(shape, spark.Y * height - size / 2);

        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        var rise = new TranslateTransform(0, 0);
        shape.RenderTransform = new TransformGroup { Children = { scale, rotate, rise } };

        var curve = RankBadgeTiming.SharpSheenCurve;
        var d = spark.DurationSeconds;
        (double, double)[] opacity, scales, turns;
        switch (kind)
        {
            case RankSparkKind.Ember:
                opacity = new[] { (0.0, 0.0), (0.10, 1.0), (0.58, 0.0), (1.0, 0.0) };
                scales = new[] { (0.0, 0.5), (0.10, 1.0), (0.58, 0.35), (1.0, 0.5) };
                turns = Array.Empty<(double, double)>();
                var climb = -0.35 * height;
                AddTimed(anims, rise, TranslateTransform.YProperty,
                    Keyframes(d, curve, (0, 0), (0.10, 0), (0.58, climb), (1.0, 0)), spark.DelaySeconds);
                break;
            case RankSparkKind.Mote:
                opacity = new[] { (0.0, 0.0), (0.56, 0.0), (0.70, 1.0), (0.84, 0.15), (1.0, 0.0) };
                scales = new[] { (0.0, 0.3), (0.56, 0.3), (0.70, 1.0), (0.84, 0.7), (1.0, 0.3) };
                turns = new[] { (0.0, 0.0), (0.56, 0.0), (0.70, 22.0), (0.84, 38.0), (1.0, 0.0) };
                break;
            default:
                opacity = new[] { (0.0, 0.0), (0.52, 0.0), (0.63, 1.0), (0.72, 0.9), (0.86, 0.0), (1.0, 0.0) };
                scales = new[] { (0.0, 0.2), (0.52, 0.2), (0.63, 1.3), (0.72, 1.0), (0.86, 0.55), (1.0, 0.2) };
                turns = new[] { (0.0, 0.0), (0.52, 0.0), (0.63, 18.0), (0.72, 28.0), (0.86, 46.0), (1.0, 0.0) };
                break;
        }

        AddTimed(anims, shape, UIElement.OpacityProperty, Keyframes(d, curve, opacity), spark.DelaySeconds);
        AddTimed(anims, scale, ScaleTransform.ScaleXProperty, Keyframes(d, curve, scales), spark.DelaySeconds);
        AddTimed(anims, scale, ScaleTransform.ScaleYProperty, Keyframes(d, curve, scales), spark.DelaySeconds);
        if (turns.Length > 0)
            AddTimed(anims, rotate, RotateTransform.AngleProperty, Keyframes(d, curve, turns), spark.DelaySeconds);
        return shape;
    }

    private static void AddTimed(List<(IAnimatable, DependencyProperty, AnimationTimeline)> anims, IAnimatable target,
        DependencyProperty property, AnimationTimeline timeline, double delaySeconds)
    {
        timeline.BeginTime = TimeSpan.FromSeconds(delaySeconds);
        anims.Add((target, property, timeline));
    }

    // ── Timelines ────────────────────────────────────────────────────────

    /// <summary>
    /// A looping key-frame animation whose segments each follow <paramref name="curve"/> — the
    /// way a CSS <c>@keyframes</c> applies its timing function between every pair of stops.
    /// Keys are (fraction of the cycle, value).
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Keyframes(double seconds, (double X1, double Y1, double X2, double Y2) curve,
        params (double At, double Value)[] keys)
    {
        var cycle = TimeSpan.FromSeconds(seconds);
        var anim = new DoubleAnimationUsingKeyFrames { Duration = cycle, RepeatBehavior = RepeatBehavior.Forever };
        var spline = Spline(curve);
        for (var i = 0; i < keys.Length; i++)
        {
            var at = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * keys[i].At));
            anim.KeyFrames.Add(i == 0
                ? new DiscreteDoubleKeyFrame(keys[i].Value, at)
                : new SplineDoubleKeyFrame(keys[i].Value, at, spline));
        }
        return anim;
    }

    private static DoubleAnimation Spin(double seconds, bool clockwise) => new()
    {
        By = clockwise ? 360 : -360,
        Duration = TimeSpan.FromSeconds(seconds),
        RepeatBehavior = RepeatBehavior.Forever,
    };

    private static KeySpline Spline((double X1, double Y1, double X2, double Y2) c)
        => new(c.X1, c.Y1, c.X2, c.Y2);

    private static Color White(double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 255, 255, 255);

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
