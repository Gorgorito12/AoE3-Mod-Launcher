using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The floating notice: a small, NON-modal, auto-dismissing card that slides into the bottom-right
/// corner — of the window, or of the desktop through <see cref="DesktopToastWindow"/> — and stacks.
/// Distinct from the persistent bell and from <see cref="MpAlertOverlay"/> (modal, centred).
///
/// <para><b>ONE drawing for every notice (design 105, docs/design_avisos_flotantes).</b> There used
/// to be three cards built in three batches — the plain one, the recordings' (handoff 63) and the
/// teams' (83l) — and the plain one took its icon as a CHARACTER, which Windows' symbol font paints
/// black: "⚔" on near-black, with a green button on every primary action whatever it did. Now a
/// notice states a <see cref="ToastTone"/> and a <see cref="ToastIcon"/>, never a character, and
/// <see cref="Build"/> derives everything else from them: the tile, the glow in the corner, the
/// badge on a mod's icon, the rim of an important notice and the time bar.</para>
///
/// <para><b>Nothing over the text carries an Effect.</b> The shadow and the important notice's halo
/// are siblings UNDER the card, in a host clipped to everything but the card's rounded rectangle —
/// an Effect on an ancestor of the text would cost every glyph ClearType, and the glass is
/// translucent, so an unclipped shadow would show through it.</para>
///
/// <para>The countdown pauses while the pointer is on the card, bar and clock together
/// (<see cref="ToastCountdown"/>). All work is best-effort: a notice must never take the app down.</para>
/// </summary>
public static class AppToast
{
    /// <summary>What kind of news it is: the tile, the corner glow, the important rim and the time
    /// bar all take this colour. Fixed colours of the design base, never a mod's.</summary>
    public enum ToastTone
    {
        /// <summary>Green: good news, something ready.</summary>
        Ok,
        /// <summary>Blue: information, an invitation.</summary>
        Info,
        /// <summary>Amber: a problem, something missing.</summary>
        Warn,
        /// <summary>Grey: something stopped, and that is all.</summary>
        Neutral,
    }

    /// <summary>The pictogram in the tile (or in a mod icon's badge): a stroke from Styles/Toasts.xaml.</summary>
    public enum ToastIcon { Swords, Check, Plus, Stop, Clock, Warn, Info, Offline, Up, Download }

    /// <summary>
    /// The class of a button. ONE coloured button per notice (<see cref="ButtonKinds"/>): blue when
    /// the notice asks you to act, green only for "Join", solid for everything else.
    /// </summary>
    public enum ToastButton { Solid, Action, Join }

    /// <summary>One action button on a notice.</summary>
    /// <param name="KeepOpen">
    /// Run the action and leave the card where it is. For an action that is not the card's
    /// answer — copying a list next to a Repair button: closing the card on Copy took the
    /// Repair button with it, and that was the one worth pressing next.
    /// </param>
    /// <param name="DoneLabel">What the button says after it has been pressed, so an action that
    /// happens off screen (the clipboard) still shows that it happened.</param>
    public sealed record ToastAction(
        string Label, ToastButton Kind, Action OnClick, bool KeepOpen = false, string? DoneLabel = null);

    /// <summary>What to show.</summary>
    /// <param name="PreferDesktop">
    /// Put this card on the DESKTOP even when the user is looking at the launcher — for the notices
    /// that expire and carry a button worth pressing. It does not add a second surface, and it does
    /// not override the game-is-running suppression (see <see cref="Services.ToastRouting"/>).
    /// </param>
    /// <param name="Subtitle">The context line under the title (today only the recordings use it:
    /// "Aluclown vs Geaf_Argento · ESOC Indonesia"), trimmed rather than wrapped.</param>
    /// <param name="Cause">Why the card exists, which is what "Do not disturb" reads.</param>
    /// <param name="Crest">The team's shield instead of the pictogram (team invitations).</param>
    /// <param name="ModId">The notice is about this mod's room or this mod: its icon fills the tile and
    /// <paramref name="Icon"/> becomes the badge on it. A mod with no icon falls back to the pictogram.</param>
    /// <param name="Important">The rim and halo in the tone. ONLY the two "your rival is in" notices,
    /// which are what the player was waiting for.</param>
    public sealed record ToastOptions(
        ToastTone Tone,
        ToastIcon Icon,
        string Title,
        string? Body,
        IReadOnlyList<ToastAction> Actions,
        int AutoDismissMs = 9000,
        bool PreferDesktop = false,
        string? Subtitle = null,
        Services.ToastCause Cause = Services.ToastCause.News,
        ToastCrest? Crest = null,
        string? ModId = null,
        bool Important = false);

    /// <summary>The team's shield (design 83l): its initials on the crest colour.</summary>
    public sealed record ToastCrest(string Initials);

    /// <summary>Max cards visible at once; the oldest is evicted past this.</summary>
    private const int MaxVisible = 4;

    /// <summary>The card's width and corner, on both surfaces.</summary>
    /// Compact by the maintainer's call (2026-11-12): the design draws 420 and 14, and with its
    /// 16-px margins, 44-px tile and 32-px buttons a two-line notice stood ~170 tall.
    internal const double CardWidth = 360;
    internal const double CardRadius = 12;
    internal const double TileSize = 36;
    internal const double TileRadius = 9;

    /// <summary>The <c>Tag</c> of the close button and of the time bar, for the tests.</summary>
    internal const string ToastCloseTag = "ToastClose";
    internal const string ToastBarTag = "ToastBar";

    /// <summary>
    /// The class each action is drawn in, in order: the FIRST coloured one keeps its colour and any
    /// other coloured one is drawn solid, so a notice can never wear two colours.
    /// </summary>
    internal static IReadOnlyList<ToastButton> ButtonKinds(IReadOnlyList<ToastAction> actions)
    {
        var coloured = false;
        var kinds = new List<ToastButton>(actions.Count);
        foreach (var a in actions)
        {
            if (a.Kind == ToastButton.Solid || coloured) kinds.Add(ToastButton.Solid);
            else { kinds.Add(a.Kind); coloured = true; }
        }
        return kinds;
    }

    /// <summary>The coloured button goes last: the primary sits at the right end of the row.</summary>
    internal static IReadOnlyList<(ToastAction Action, ToastButton Kind)> ButtonOrder(IReadOnlyList<ToastAction> actions)
    {
        var kinds = ButtonKinds(actions);
        return actions.Select((a, i) => (a, kinds[i]))
            .OrderBy(x => x.Item2 == ToastButton.Solid ? 0 : 1)
            .ToList();
    }

    internal static string ToneKey(ToastTone tone) => "ToastTone" + tone;

    internal static string IconKey(ToastIcon icon) => "ToastIcon" + icon;

    /// <summary>
    /// Build a card and add it to <paramref name="host"/> (newest on top). Slides and fades in,
    /// counts down <see cref="ToastOptions.AutoDismissMs"/> (paused under the pointer), and closes
    /// when an action runs or the ✕ is pressed. <paramref name="desktop"/>: the card stands on the
    /// desktop (<see cref="DesktopToastWindow"/>), solid and saying whose it is.
    /// </summary>
    public static void Show(Panel host, ToastOptions opts, bool desktop = false)
    {
        if (host == null || opts == null) return;
        try
        {
            // Evict oldest cards beyond the cap (host stacks newest at index 0).
            while (host.Children.Count >= MaxVisible)
                host.Children.RemoveAt(host.Children.Count - 1);

            Grid? shell = null;
            ToastCountdown? countdown = null;
            void Close()
            {
                if (shell == null) return;
                countdown?.Stop();
                var closing = shell;
                shell = null;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (_, _) => { try { host.Children.Remove(closing); } catch { } };
                closing.BeginAnimation(UIElement.OpacityProperty, fade);
                closing.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(28, TimeSpan.FromMilliseconds(160)));
            }

            var card = Build(opts, Close, desktop);
            shell = card;
            shell.Opacity = 0;
            shell.RenderTransform = new TranslateTransform(28, 0);
            host.Children.Insert(0, shell);
            shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            shell.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

            if (opts.AutoDismissMs > 0)
            {
                var bar = FindBarFill(card);
                countdown = new ToastCountdown(TimeSpan.FromMilliseconds(opts.AutoDismissMs), Close, bar,
                    animate: !RankBadge.ReducedEffects && SystemParameters.ClientAreaAnimation);
                card.MouseEnter += (_, _) => countdown.Pause();
                card.MouseLeave += (_, _) => countdown.Resume();
                countdown.Start();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AppToast.Show failed: {ex.Message}");
        }
    }

    private static Border? FindBarFill(Grid shell)
    {
        Border? found = null;
        void Walk(DependencyObject d)
        {
            if (found != null) return;
            if (d is Border b && Equals(b.Tag, ToastBarTag)) { found = b; return; }
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
        }
        Walk(shell);
        return found;
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static double F(string key) => (double)Application.Current.FindResource(key);

    private static Color ColorOf(string key) => Res(key) is SolidColorBrush s ? s.Color : Colors.Gray;

    private static SolidColorBrush Alpha(Color c, double a)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// The card (design 105, compact): 360 wide, radius 12; the floating glass (or, on the desktop, solid
    /// <c>#12213A</c>), a white sheen and the tone's glow from the top-left corner; a 1-px white rim
    /// with a 1-px light along the top; a 0 12 30 shadow. Columns: the 36-px tile, 12, the text, 10,
    /// the 22-px ✕. Buttons under it at the right; the time bar along the bottom edge.
    /// </summary>
    internal static Grid Build(ToastOptions opts, Action close, bool desktop = false)
    {
        var toneKey = ToneKey(opts.Tone);
        var tone = ColorOf(toneKey);
        var toneText = Res(toneKey + "Text");
        var important = opts.Important;

        var shell = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = CardWidth,
        };

        // ---- Under the card: the shadow, and the important notice's halo, clipped to the outside ----
        var under = new Grid { IsHitTestVisible = false };
        under.Children.Add(new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(CardRadius),
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, ShadowDepth = 12, Direction = 270, BlurRadius = 30, Opacity = 0.5,
            },
        });
        if (important)
        {
            // CSS "0 0 34px -6px": the shape pulled in by 6, then blurred by 34.
            under.Children.Add(new Border
            {
                Margin = new Thickness(6),
                Background = new SolidColorBrush(tone),
                CornerRadius = new CornerRadius(CardRadius),
                CacheMode = new BitmapCache(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = tone, ShadowDepth = 0, BlurRadius = 34, Opacity = 0.5,
                },
            });
        }
        shell.SizeChanged += (_, e) =>
        {
            // Everything but the card's own rounded rectangle: the shadow must not show through the glass.
            var outside = new RectangleGeometry(new Rect(-80, -80, e.NewSize.Width + 160, e.NewSize.Height + 160));
            var inside = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), CardRadius, CardRadius);
            under.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, outside, inside);
        };
        shell.Children.Add(under);

        // ---- The card ----
        var card = new Border
        {
            CornerRadius = new CornerRadius(CardRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = important ? Alpha(tone, .55) : Res("ToastRim"),
            ClipToBounds = true,
        };
        // The floating glass follows the transparency level (GlassTheme.FloatingKeys); the desktop
        // card is a window over anything, so it is solid.
        card.SetResourceReference(Border.BackgroundProperty, desktop ? "ToastDesktopBg" : "LibraryMenuBg");
        shell.Children.Add(card);

        var inner = CardRadius - 1;
        var layers = new Grid();
        // Clipped to the inner rounded rectangle: the time bar runs along the bottom edge and must
        // follow the corners, which ClipToBounds (a plain rectangle) does not.
        layers.SizeChanged += (_, e) => layers.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), inner, inner);
        card.Child = layers;
        layers.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(inner),
            Background = Res("ToastSheen"),
            IsHitTestVisible = false,
        });
        var glow = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(0, 0),
            GradientOrigin = new Point(0, 0),
            RadiusX = 275,
            RadiusY = 145,
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(.22 * 255), tone.R, tone.G, tone.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, tone.R, tone.G, tone.B), .72));
        glow.Freeze();
        layers.Children.Add(new Border { CornerRadius = new CornerRadius(inner), Background = glow, IsHitTestVisible = false });
        layers.Children.Add(new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(inner, 0, inner, 0),
            Background = Res(important ? "ToastEdgeLightImportant" : "ToastEdgeLight"),
            IsHitTestVisible = false,
        });

        var body = new StackPanel { Margin = new Thickness(14, desktop ? 10 : 13, 12, 14) };
        layers.Children.Add(body);

        if (desktop) body.Children.Add(BuildOwnerLine());

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileSize) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        body.Children.Add(row);

        row.Children.Add(BuildTile(opts, tone, toneText));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        var title = new TextBlock
        {
            Text = opts.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("ToastTitle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "ToastTitleSize");
        text.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(opts.Subtitle))
        {
            var context = new TextBlock
            {
                Text = opts.Subtitle,
                Foreground = Res("ToastContext"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            };
            context.SetResourceReference(TextBlock.FontSizeProperty, "ToastContextSize");
            text.Children.Add(context);
        }
        if (!string.IsNullOrWhiteSpace(opts.Body))
        {
            // 12.5 / 1.45, THREE lines at most: the design caps two at 420, and at the compact 360 the
            // longest bodies (Radmin, the missing recording) need three. The cap leaves half a line of
            // slack past the third: a fixed line height only fits whole lines, and a cap of exactly
            // N lines lost the last one to rounding at 125 % (the body stopped after one line).
            var size = F("ToastBodySize");
            var lineHeight = Math.Round(size * 1.45, 1);
            var bodyText = new TextBlock
            {
                Text = opts.Body,
                Foreground = Res("ToastBody"),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = lineHeight,
                MaxHeight = lineHeight * 3.5,
                FontSize = size,
                Margin = new Thickness(0, 2, 0, 0),
            };
            text.Children.Add(bodyText);
        }

        var closeBtn = new Button
        {
            Style = (Style)Application.Current.FindResource("ToastCloseButton"),
            VerticalAlignment = VerticalAlignment.Top,
            Tag = ToastCloseTag,
            ToolTip = TooltipHelper.Wrap(Strings.Get("DlgClose")),
        };
        System.Windows.Automation.AutomationProperties.SetName(closeBtn, Strings.Get("DlgClose"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(closeBtn, "ToastClose");
        closeBtn.Click += (_, _) => close();
        Grid.SetColumn(closeBtn, 4);
        row.Children.Add(closeBtn);

        if (opts.Actions is { Count: > 0 })
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            foreach (var (action, kind) in ButtonOrder(opts.Actions))
            {
                var btn = new Button
                {
                    Content = action.Label,
                    Style = (Style)Application.Current.FindResource(kind switch
                    {
                        ToastButton.Action => "ToastButtonAction",
                        ToastButton.Join => "ToastButtonJoin",
                        _ => "ToastButtonSolid",
                    }),
                    Margin = new Thickness(8, 0, 0, 0),
                    Tag = kind,
                };
                System.Windows.Automation.AutomationProperties.SetAutomationId(btn, "ToastAction:" + action.Label);
                var act = action.OnClick;
                var keepOpen = action.KeepOpen;
                var doneLabel = action.DoneLabel;
                btn.Click += (_, _) =>
                {
                    if (!keepOpen) close();
                    try { act?.Invoke(); } catch (Exception ex) { DiagnosticLog.Write($"AppToast action failed: {ex.Message}"); }
                    if (keepOpen && !string.IsNullOrEmpty(doneLabel)) btn.Content = doneLabel;
                };
                buttons.Children.Add(btn);
            }
            body.Children.Add(buttons);
        }

        // The time bar: 3 px along the bottom edge, clipped by the card's radius. Only on a card that
        // closes by itself; it starts full and empties to the left.
        if (opts.AutoDismissMs > 0)
        {
            var fill = new Border
            {
                Background = new SolidColorBrush(tone),
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Tag = ToastBarTag,
            };
            layers.Children.Add(new Border
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Res("ToastBarTrack"),
                IsHitTestVisible = false,
                Child = fill,
            });
        }
        return shell;
    }

    /// <summary>"AoE3 Mod Launcher" with its 14-px icon: on the desktop, whose notice this is.</summary>
    private static FrameworkElement BuildOwnerLine()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        line.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Aoe3ModLauncher;component/AppIcon.png")),
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(line.Children[0], BitmapScalingMode.HighQuality);
        var name = new TextBlock
        {
            Text = "AoE3 Mod Launcher",
            Foreground = Res("ToastContext"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "ToastOwnerSize");
        line.Children.Add(name);
        return line;
    }

    /// <summary>
    /// The 36 x 36 tile, radius 9: the team's shield, the mod's icon with the pictogram as a badge,
    /// or the pictogram on the tone (white on a neutral card). Never a character.
    /// </summary>
    private static FrameworkElement BuildTile(ToastOptions opts, Color tone, Brush toneText)
    {
        var tile = new Grid { Width = TileSize, Height = TileSize, VerticalAlignment = VerticalAlignment.Top, Tag = "ToastTile" };
        if (opts.Crest is { } crest)
        {
            var initials = new TextBlock
            {
                Text = crest.Initials,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tile.Children.Add(new Border { CornerRadius = new CornerRadius(TileRadius), Background = Res("ToastCrestBg"), Child = initials });
            return tile;
        }

        var modIcon = string.IsNullOrWhiteSpace(opts.ModId) ? null : ModIconConverter.Resolve(opts.ModId);
        if (modIcon != null)
        {
            tile.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(TileRadius),
                Background = modIcon,
                BorderBrush = Res("ToastModTileRim"),
                BorderThickness = new Thickness(1),
                Tag = "ToastModIcon",
            });
            // The badge: 18 across, 4 past the tile's corner, the tone with a 2-px ring, and the
            // pictogram at 11 in the dark ink.
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(tone),
                BorderBrush = Res("ToastBadgeRing"),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                Child = Pictogram(opts.Icon, 11, 2.8, Res("ToastBadgeInk")),
                Tag = "ToastBadge",
            };
            tile.Children.Add(badge);
            return tile;
        }

        var neutral = opts.Tone == ToastTone.Neutral;
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(TileRadius),
            Background = neutral ? Res("ToastNeutralTileBg") : Alpha(tone, .16),
            BorderBrush = neutral ? Res("ToastNeutralTileRim") : Alpha(tone, .45),
            BorderThickness = new Thickness(1),
            Child = Pictogram(opts.Icon, 20, 1.7, toneText),
        });
        return tile;
    }

    /// <summary>A pictogram from its 20 x 20 stroke, drawn at <paramref name="size"/> with a stroke of
    /// <paramref name="stroke"/> at that size: the geometry is scaled, the pen is not.</summary>
    internal static Path Pictogram(ToastIcon icon, double size, double stroke, Brush ink)
    {
        var geometry = ((Geometry)Application.Current.FindResource(IconKey(icon))).Clone();
        geometry.Transform = new ScaleTransform(size / 20, size / 20);
        geometry.Freeze();
        return new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stroke = ink,
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = icon,
        };
    }
}

/// <summary>
/// A notice's countdown: the close timer and the time bar, paused together while the pointer is on
/// the card and resumed where they were when it leaves. With reduced effects the bar stays full and
/// only the timer runs. The bar's clock is STOPPED through its controller when the notice closes —
/// detaching alone would leave it ticking (see the animation-clock rule in CLAUDE.md).
/// </summary>
internal sealed class ToastCountdown
{
    private readonly TimeSpan _total;
    private readonly Action _expired;
    private readonly Border? _bar;
    private readonly bool _animate;
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer _timer = new();
    private AnimationClock? _clock;
    private TimeSpan _remaining;
    private DateTime _resumedAt;
    private bool _running;
    private bool _stopped;

    internal ToastCountdown(TimeSpan total, Action expired, Border? bar, bool animate, Func<DateTime>? now = null)
    {
        _total = total;
        _remaining = total;
        _expired = expired;
        _bar = bar;
        _animate = animate && bar != null;
        _now = now ?? (() => DateTime.UtcNow);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_stopped) return;
            Stop();
            _expired();
        };
    }

    /// <summary>What is left of the countdown now.</summary>
    internal TimeSpan Remaining => _running ? Max0(_remaining - (_now() - _resumedAt)) : _remaining;

    internal bool IsRunning => _running;

    internal void Start()
    {
        if (_animate && _bar?.RenderTransform is ScaleTransform scale)
        {
            var anim = new DoubleAnimation(1, 0, _total) { FillBehavior = FillBehavior.HoldEnd };
            Timeline.SetDesiredFrameRate(anim, RankBadge.FrameRate);
            _clock = anim.CreateClock();
            scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _clock);
        }
        Resume();
    }

    internal void Pause()
    {
        if (!_running || _stopped) return;
        _remaining = Remaining;
        _running = false;
        _timer.Stop();
        _clock?.Controller?.Pause();
    }

    internal void Resume()
    {
        if (_running || _stopped) return;
        _running = true;
        _resumedAt = _now();
        _timer.Interval = _remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : _remaining;
        _timer.Start();
        _clock?.Controller?.Resume();
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _running = false;
        _timer.Stop();
        _clock?.Controller?.Stop();
        _clock = null;
    }

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
