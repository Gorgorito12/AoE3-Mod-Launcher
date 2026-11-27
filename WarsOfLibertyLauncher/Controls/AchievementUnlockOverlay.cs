using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// "Achievement unlocked" (design 121b): a card in the middle of the window. The medal spins in, a
/// shine crosses it once, and slow golden rays turn behind it with six twinkling sparks. It says
/// what was won, of which metal and how many the player holds, offers "See my medals", and closes
/// by itself after <see cref="AchievementCelebration.CardSeconds"/> — or on Close, Escape, or a
/// click outside it.
///
/// <para><b>Animation, as the design's note asks:</b> the card rises 26 px in 0.4 s; the medal
/// goes from 0.15 and −18° to 1.16 and +4° in 0.6 s and settles at 1; a white shine crosses it in
/// 1.2 s; eighteen gold wedges at 20 % turn once every 36 s, faded outwards; six four-pointed
/// sparks twinkle every 2.6 s, out of step. The looping two are clocks this class keeps and STOPS
/// through their controller on close (detaching alone keeps a clock drawing for nobody, see
/// <see cref="RankBadge"/>), capped at <see cref="RankBadge.FrameRate"/>, and paused whenever
/// <see cref="RankBadge.MayRunOn"/> says nobody can see them. With the effects reduced nothing
/// moves: no rays, no shine, no spin, the card simply appears.</para>
/// </summary>
public static class AchievementUnlockOverlay
{
    /// <summary>Looping clocks of every open card, for the tests: closing must bring it back to 0.</summary>
    internal static int LiveClocks;

    /// <summary>What the card says.</summary>
    public sealed record Model(Medal.Look Medal, string Name, string Line, string MedalLine, Color TierDot);

    /// <summary>
    /// Shows the card over <paramref name="host"/> (a full-window grid) and returns what closes it.
    /// <paramref name="onSeeMedals"/> runs after the card closes.
    /// </summary>
    public static Action Show(Grid host, Model model, Action onSeeMedals, bool reducedEffects, TimeSpan? stayFor = null)
    {
        var clocks = new List<(AnimationClock Clock, Action Detach)>();
        var root = new Grid { Tag = "AchievementUnlockOverlay" };
        var accent = Accent.For(model.Medal.Metal);

        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xCC, 8, 14, 24)) };
        root.Children.Add(scrim);

        // ---- the card, its outer ring and its shadow (siblings: no Effect over its text)
        var stage = new Grid { Width = 560, Height = 428, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stage.Children.Add(new Border
        {
            Margin = new Thickness(-6),
            CornerRadius = new CornerRadius(26),
            Background = new SolidColorBrush(accent.Ring),
            IsHitTestVisible = false,
        });
        stage.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = Brushes.Black,
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 30, Direction = 270, BlurRadius = 80, Opacity = 0.6 },
        });

        var fill = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        fill.GradientStops.Add(new GradientStop(accent.Top, 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0xF7, 15, 24, 40), 0.46));
        var card = new Border
        {
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(accent.Rim),
            Background = fill,
        };
        stage.Children.Add(card);

        var layers = new Grid { ClipToBounds = true };
        layers.Clip = new RectangleGeometry(new Rect(0, 0, 558, 426), 19, 19);
        card.Child = layers;

        // ---- the rays and the sparks, behind the words
        var decor = new Canvas { IsHitTestVisible = false };
        layers.Children.Add(decor);
        if (!reducedEffects)
        {
            var rays = Rays(620, accent.Ray);
            Canvas.SetLeft(rays, 279 - 310);
            Canvas.SetTop(rays, 146 - 310);
            decor.Children.Add(rays);
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(36)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, RankBadge.FrameRate);
            Start(clocks, rays.RenderTransform as RotateTransform, RotateTransform.AngleProperty, spin);

            foreach (var (x, y, size, delay) in new[]
                     {
                         (118.0, 70.0, 14.0, 0.2), (430, 54, 10, 0.9), (160, 190, 9, 1.5),
                         (402, 176, 15, 0.5), (92, 138, 8, 1.1), (462, 122, 9, 1.8),
                     })
            {
                var spark = Spark(size, accent);
                Canvas.SetLeft(spark, x);
                Canvas.SetTop(spark, y);
                decor.Children.Add(spark);
                AddTwinkle(clocks, spark, TimeSpan.FromSeconds(delay));
            }
        }

        // ---- the words and the medal
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        layers.Children.Add(column);

        var label = new TextBlock
        {
            Text = Strings.Get("AchvUnlocked"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(accent.Label),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 0),
        };
        column.Children.Add(label);

        var medalHolder = new Grid
        {
            Width = 132,
            Height = 164,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.4),
        };
        medalHolder.Children.Add(Medal.Build(model.Medal, 132));
        column.Children.Add(medalHolder);

        // The shine: a white band crossing the coin once, clipped to the coin.
        Rectangle? shine = null;
        if (!reducedEffects)
        {
            var coinClip = new Grid { Width = 132, Height = 132, VerticalAlignment = VerticalAlignment.Top, Clip = new EllipseGeometry(new Point(66, 66), 66, 66), IsHitTestVisible = false };
            var band = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.3));
            band.GradientStops.Add(new GradientStop(Color.FromArgb(0xBF, 255, 255, 255), 0.5));
            band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7));
            shine = new Rectangle
            {
                Width = 132,
                Height = 132,
                Fill = band,
                RenderTransform = new TransformGroup { Children = { new SkewTransform(-20, 0), new TranslateTransform(-185, 0) } },
            };
            coinClip.Children.Add(shine);
            medalHolder.Children.Add(coinClip);
        }

        var name = new TextBlock
        {
            Text = model.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 34,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 520,
            Margin = new Thickness(0, 8, 0, 0),
        };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        column.Children.Add(name);

        var line = new TextBlock
        {
            Text = model.Line,
            FontSize = 14.5,
            Foreground = new SolidColorBrush(Medal.Hex("#DCE7F5")),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 520,
            Margin = new Thickness(0, 8, 0, 0),
        };
        column.Children.Add(line);

        var pillRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pillRow.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(model.TierDot), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        pillRow.Children.Add(new TextBlock
        {
            Text = model.MedalLine,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Medal.Hex("#DCE7F5")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        column.Children.Add(new Border
        {
            Height = 26,
            Padding = new Thickness(13, 0, 13, 0),
            CornerRadius = new CornerRadius(13),
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            Child = pillRow,
            MaxWidth = 520,
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        var closeButton = CardButton(Strings.Get("AchvClose"), gold: false);
        var seeButton = CardButton(Strings.Get("AchvSeeMedals"), gold: true);
        seeButton.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(closeButton);
        buttons.Children.Add(seeButton);
        column.Children.Add(buttons);

        root.Children.Add(stage);
        host.Children.Add(root);
        host.Visibility = Visibility.Visible;

        // ---- closing
        bool closed = false;
        var timer = new DispatcherTimer { Interval = stayFor ?? TimeSpan.FromSeconds(AchievementCelebration.CardSeconds) };
        void SyncClocks()
        {
            bool run = RankBadge.MayRunOn(stage);
            foreach (var (clock, _) in clocks)
            {
                if (run) clock.Controller?.Resume();
                else clock.Controller?.Pause();
            }
        }
        void Keys(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }
        void Close()
        {
            if (closed) return;
            closed = true;
            timer.Stop();
            host.PreviewKeyDown -= Keys;
            RankBadge.RunConditionsChanged -= SyncClocks;
            foreach (var (clock, detach) in clocks)
            {
                clock.Controller?.Stop();
                detach();
                LiveClocks--;
            }
            clocks.Clear();
            host.Children.Remove(root);
            if (host.Children.Count == 0) host.Visibility = Visibility.Collapsed;
        }
        timer.Tick += (_, _) => Close();
        timer.Start();
        scrim.MouseLeftButtonDown += (_, _) => Close();
        closeButton.Click += (_, _) => Close();
        seeButton.Click += (_, _) =>
        {
            Close();
            try { onSeeMedals(); } catch (Exception ex) { Services.DiagnosticLog.Write($"Achievements: see medals failed: {ex.Message}"); }
        };
        host.PreviewKeyDown += Keys;
        RankBadge.RunConditionsChanged += SyncClocks;
        root.Loaded += (_, _) => { seeButton.Focus(); SyncClocks(); };

        // ---- the entrance: the card rises, the medal spins in, the shine crosses once
        if (!reducedEffects)
        {
            var rise = new TranslateTransform(0, 26);
            var grow = new ScaleTransform(0.96, 0.96);
            stage.RenderTransformOrigin = new Point(0.5, 0.5);
            stage.RenderTransform = new TransformGroup { Children = { grow, rise } };
            stage.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var cardTime = TimeSpan.FromMilliseconds(400);
            rise.BeginAnimation(TranslateTransform.YProperty, Finite(new DoubleAnimation(26, 0, cardTime) { EasingFunction = ease }));
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, Finite(new DoubleAnimation(0.96, 1, cardTime) { EasingFunction = ease }));
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, Finite(new DoubleAnimation(0.96, 1, cardTime) { EasingFunction = ease }));
            stage.BeginAnimation(UIElement.OpacityProperty, Finite(new DoubleAnimation(0, 1, cardTime)));

            var pop = new ScaleTransform(0.15, 0.15);
            var turn = new RotateTransform(-18);
            medalHolder.RenderTransform = new TransformGroup { Children = { pop, turn } };
            medalHolder.Opacity = 0;
            var scale = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1260) };
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scale.KeyFrames.Add(new SplineDoubleKeyFrame(1.16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new KeySpline(0.2, 0.9, 0.3, 1)));
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(0.97, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980))));
            scale.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1260))));
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, Finite(scale));
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, Finite(scale.Clone()));
            var angle = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(980) };
            angle.KeyFrames.Add(new LinearDoubleKeyFrame(-18, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            angle.KeyFrames.Add(new SplineDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), new KeySpline(0.2, 0.9, 0.3, 1)));
            angle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980))));
            turn.BeginAnimation(RotateTransform.AngleProperty, Finite(angle));
            medalHolder.BeginAnimation(UIElement.OpacityProperty, Finite(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))));

            if (shine?.RenderTransform is TransformGroup { Children: [_, TranslateTransform slide] })
            {
                var sweep = new DoubleAnimation(-185, 211, TimeSpan.FromMilliseconds(1200))
                {
                    BeginTime = TimeSpan.FromMilliseconds(1120),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.HoldEnd,
                };
                Timeline.SetDesiredFrameRate(sweep, RankBadge.FrameRate);
                slide.BeginAnimation(TranslateTransform.XProperty, sweep);
            }
        }

        return Close;
    }

    private static T Finite<T>(T animation) where T : AnimationTimeline
    {
        animation.FillBehavior = FillBehavior.HoldEnd;
        Timeline.SetDesiredFrameRate(animation, RankBadge.FrameRate);
        return animation;
    }

    /// <summary>
    /// The card's light: design 121's gold, or a cold white for a platinum feat (2026-11-26), so the
    /// rarest medals do not arrive in the same light as a gold one.
    /// </summary>
    internal sealed record Accent(Color Ring, Color Top, Color Rim, Color Ray, Color Spark, Color Glow, Color Label)
    {
        internal static readonly Accent Gold = new(
            Color.FromArgb(0x14, 0xE6, 0xC0, 0x6A), Color.FromArgb(0xF0, 40, 32, 14), Color.FromArgb(0x8C, 0xE6, 0xC0, 0x6A),
            Color.FromArgb(0x33, 0xF6, 0xDF, 0x9E), Medal.Hex("#FBEAB8"), Medal.Hex("#F6DF9E"), Medal.Hex("#F3D995"));

        internal static readonly Accent Platinum = new(
            Color.FromArgb(0x18, 0xB8, 0xE2, 0xFF), Color.FromArgb(0xF0, 20, 34, 54), Color.FromArgb(0x8C, 0xB8, 0xDC, 0xF6),
            Color.FromArgb(0x33, 0xD8, 0xEE, 0xFF), Medal.Hex("#EEF8FF"), Medal.Hex("#BFE6FF"), Medal.Hex("#CFEAFF"));

        internal static Accent For(string? metal) => metal == "p" ? Platinum : Gold;
    }

    /// <summary>Eighteen wedges in the card's light at 20 %, 7° each every 20°, faded outwards.</summary>
    private static FrameworkElement Rays(double size, Color fill)
    {
        var geometry = new StreamGeometry();
        double r = size / 2;
        using (var g = geometry.Open())
        {
            for (int i = 0; i < 18; i++)
            {
                double a0 = (i * 20) * Math.PI / 180;
                double a1 = (i * 20 + 7) * Math.PI / 180;
                g.BeginFigure(new Point(r, r), true, true);
                g.LineTo(new Point(r + r * Math.Sin(a0), r - r * Math.Cos(a0)), false, false);
                g.ArcTo(new Point(r + r * Math.Sin(a1), r - r * Math.Cos(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            }
        }
        geometry.Freeze();
        var mask = new RadialGradientBrush();
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.66));
        return new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(fill),
            OpacityMask = mask,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(0),
            CacheMode = new BitmapCache(),
        };
    }

    /// <summary>A four-pointed spark with its glow.</summary>
    private static FrameworkElement Spark(double size, Accent accent)
    {
        var star = new Polygon
        {
            Points = new PointCollection
            {
                new(0.5 * size, 0), new(0.6 * size, 0.4 * size), new(size, 0.5 * size), new(0.6 * size, 0.6 * size),
                new(0.5 * size, size), new(0.4 * size, 0.6 * size), new(0, 0.5 * size), new(0.4 * size, 0.4 * size),
            },
            Fill = new SolidColorBrush(accent.Spark),
            Effect = new DropShadowEffect { Color = accent.Glow, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.8 },
            Width = size,
            Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(0, 0), new RotateTransform(0) } },
            Opacity = 0,
        };
        return star;
    }

    /// <summary>The twinkle: 0 → full and 45° at 40 %, 0.6 and 90° at 70 %, gone at 100 %, every 2.6 s.</summary>
    private static void AddTwinkle(List<(AnimationClock Clock, Action Detach)> clocks, FrameworkElement spark, TimeSpan delay)
    {
        var cycle = TimeSpan.FromSeconds(2.6);
        DoubleAnimationUsingKeyFrames Track(double a, double b, double c)
        {
            var k = new DoubleAnimationUsingKeyFrames { Duration = cycle, BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever };
            k.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            k.KeyFrames.Add(new SplineDoubleKeyFrame(a, KeyTime.FromPercent(0.4), new KeySpline(0.4, 0, 0.6, 1)));
            k.KeyFrames.Add(new SplineDoubleKeyFrame(b, KeyTime.FromPercent(0.7), new KeySpline(0.4, 0, 0.6, 1)));
            k.KeyFrames.Add(new SplineDoubleKeyFrame(c, KeyTime.FromPercent(1), new KeySpline(0.4, 0, 0.6, 1)));
            Timeline.SetDesiredFrameRate(k, RankBadge.FrameRate);
            return k;
        }
        if (spark.RenderTransform is not TransformGroup { Children: [ScaleTransform scale, RotateTransform turn] }) return;
        Start(clocks, scale, ScaleTransform.ScaleXProperty, Track(1, 0.6, 0));
        Start(clocks, scale, ScaleTransform.ScaleYProperty, Track(1, 0.6, 0));
        Start(clocks, turn, RotateTransform.AngleProperty, Track(45, 90, 90));
        Start(clocks, spark, UIElement.OpacityProperty, Track(1, 0.6, 0));
    }

    /// <summary>A forever clock this card keeps, so closing it can STOP it (not merely detach it).</summary>
    private static void Start(List<(AnimationClock Clock, Action Detach)> clocks, IAnimatable? target,
        DependencyProperty property, AnimationTimeline timeline)
    {
        if (target == null) return;
        var clock = timeline.CreateClock();
        target.ApplyAnimationClock(property, clock);
        clocks.Add((clock, () => target.ApplyAnimationClock(property, null)));
        LiveClocks++;
    }

    /// <summary>Close (dark navy, a blue rim) or "See my medals" (gold, dark text): 36 high, radius 8.</summary>
    private static Button CardButton(string text, bool gold)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Medal.Hex(gold ? "#2B1F06" : "#DCE7F5")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Brush face;
        if (gold)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            g.GradientStops.Add(new GradientStop(Medal.Hex("#F6DF9E"), 0));
            g.GradientStops.Add(new GradientStop(Medal.Hex("#E6C06A"), 0.55));
            g.GradientStops.Add(new GradientStop(Medal.Hex("#D2A54A"), 1));
            face = g;
        }
        else face = new SolidColorBrush(Medal.Hex("#1A2C48"));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, face);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(gold ? 0 : 1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x38, 130, 175, 255)));
        border.SetValue(Border.PaddingProperty, new Thickness(gold ? 18 : 16, 0, gold ? 18 : 16, 0));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var button = new Button
        {
            Content = caption,
            Height = 36,
            Cursor = Cursors.Hand,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = border },
            Style = new Style(typeof(Button)),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, gold ? "AchievementSeeMedals" : "AchievementClose");
        return button;
    }
}
