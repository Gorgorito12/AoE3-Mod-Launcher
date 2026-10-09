using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>What a recording button is showing (design handoff 63, «Estados del botón»).</summary>
public enum ReplayButtonState
{
    /// <summary>Ready to download: a blue disc with an arrow.</summary>
    Idle,

    /// <summary>A ring that fills with the download; no second click.</summary>
    Downloading,

    /// <summary>Already on disk: a green folder; a click shows the file.</summary>
    Done,

    /// <summary>The last attempt failed: a circular arrow; a click retries.</summary>
    Error,

    /// <summary>The year is over: the word "expired" and nothing to click.</summary>
    Expired,
}

/// <summary>
/// The 20 × 20 recording button in a match row's right column (design handoff 63). Built in
/// code like the rest of the row; every colour is a resource and every value is the handoff's.
///
/// <para><b>A chromeless Button, not a Border with a mouse handler</b>, for the reason
/// <c>DeckTiles</c> gives: inside a ScrollViewer a <c>MouseLeftButtonUp</c> on a Border can be
/// swallowed, and a button that does nothing when pressed is the one failure nobody reports
/// precisely. The template is a bare ContentPresenter, one per THREAD (a sealed template belongs
/// to the thread that first applied it), and the empty <see cref="Style"/> keeps the app-wide
/// implicit Button style from being applied — and sealed on whichever thread drew first.</para>
///
/// <para>The spinner is a forever animation, so it is started through a clock and STOPPED
/// through that clock (<c>Controller.Stop()</c>), never merely detached — the rule the rank
/// badges learned the hard way. It is capped at 30 fps and replaced by a still arc when effects
/// are reduced or Windows animations are off.</para>
/// </summary>
internal sealed class ReplayDownloadButton : Button
{
    public const double Size = 20;

    /// <summary>The <c>Tag</c> the tests find the button by.</summary>
    internal const string ButtonTag = "ReplayDownloadButton";

    [ThreadStatic] private static ControlTemplate? s_template;

    private readonly Grid _root = new() { Width = Size, Height = Size };
    private readonly Border _disc = new() { Width = Size, Height = Size, CornerRadius = new CornerRadius(Size / 2) };
    private readonly Path _icon = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.None,
        SnapsToDevicePixels = false,
    };
    // Full size: an Ellipse draws its stroke INSIDE its bounds, so 20 with a 3-px stroke is the
    // ring from radius 7 to 10 — the 14-px centre the handoff draws, and the arc's own radius.
    private readonly Ellipse _track = new() { Width = Size, Height = Size, StrokeThickness = 3 };
    private readonly Path _arc = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat };
    private readonly RotateTransform _spin = new(0, Size / 2, Size / 2);
    private readonly TextBlock _expired = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    private readonly Func<ReplayButtonState, (string Title, string? Body)> _tip;
    private AnimationClock? _spinClock;
    private ReplayButtonState _state = ReplayButtonState.Idle;
    private double? _progress;

    /// <param name="matchId">The match whose recording this is.</param>
    /// <param name="expiredLabel">"expired" / "caducada", in the launcher's language.</param>
    /// <param name="tip">The tooltip's title and text for a state; null text for a title alone.</param>
    public ReplayDownloadButton(string matchId, string expiredLabel,
        Func<ReplayButtonState, (string Title, string? Body)> tip)
    {
        MatchId = matchId;
        _tip = tip;
        Tag = ButtonTag;
        Style = new Style(typeof(Button));
        Template = s_template ??= BuildTemplate();
        Focusable = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        MinHeight = Size;

        _expired.Text = expiredLabel;
        _expired.SetResourceReference(TextBlock.FontSizeProperty, "MpPillSize");
        _expired.SetResourceReference(TextBlock.ForegroundProperty, "MpTextFaint");

        _arc.RenderTransform = _spin;
        _root.Children.Add(_disc);
        _root.Children.Add(_track);
        _root.Children.Add(_arc);
        _root.Children.Add(_icon);
        Content = _root;

        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetShowDuration(this, 20000);
        ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Bottom);

        MouseEnter += (_, _) => Paint();
        MouseLeave += (_, _) => Paint();
        Unloaded += (_, _) => StopSpin();
        Loaded += (_, _) => Paint();
        Paint();
    }

    /// <summary>The match whose recording this button downloads.</summary>
    public string MatchId { get; }

    public ReplayButtonState State => _state;

    /// <summary>How much of the download is in, 0-1, or null when the size is not known.</summary>
    public double? Progress => _progress;

    /// <summary>Move to a state; <paramref name="progress"/> matters only while downloading.</summary>
    public void SetState(ReplayButtonState state, double? progress = null)
    {
        var changedState = state != _state;
        _state = state;
        _progress = progress is double p ? Math.Clamp(p, 0, 1) : null;
        if (changedState) Paint();
        else if (state == ReplayButtonState.Downloading) PaintRing();
    }

    private void Paint()
    {
        Cursor = _state is ReplayButtonState.Downloading or ReplayButtonState.Expired ? Cursors.Arrow : Cursors.Hand;

        if (_state == ReplayButtonState.Expired)
        {
            StopSpin();
            Content = _expired;
            ToolTip = null;
            return;
        }
        if (!ReferenceEquals(Content, _root)) Content = _root;

        var hover = IsMouseOver && _state == ReplayButtonState.Idle;
        _disc.BorderThickness = new Thickness(0);
        _disc.Background = Brushes.Transparent;
        _disc.BorderBrush = null;
        _track.Visibility = Visibility.Collapsed;
        _arc.Visibility = Visibility.Collapsed;
        _icon.Visibility = Visibility.Visible;

        switch (_state)
        {
            case ReplayButtonState.Idle:
                _disc.Background = Res(hover ? "MpAction" : "MpReplayButtonBg");
                SetIcon(ArrowGeometry, 10, 11, hover ? Brushes.White : Res("MpActionText"), 1.6);
                StopSpin();
                break;
            case ReplayButtonState.Downloading:
                _icon.Visibility = Visibility.Collapsed;
                _track.Visibility = Visibility.Visible;
                _arc.Visibility = Visibility.Visible;
                _track.Stroke = Res("MpReplayRingTrack");
                _arc.Stroke = Res("MpAction");
                PaintRing();
                break;
            case ReplayButtonState.Done:
                _disc.BorderThickness = new Thickness(1);
                _disc.BorderBrush = Res("MpReplayDoneRim");
                SetIcon(FolderGeometry, 11, 9, Res("MpOk"), 1.5);
                StopSpin();
                break;
            case ReplayButtonState.Error:
                _disc.BorderThickness = new Thickness(1);
                _disc.BorderBrush = Res("MpReplayErrorRim");
                SetIcon(RetryGeometry, 10, 10, Res("UiTextStrong"), 1.5);
                StopSpin();
                break;
        }

        var (title, body) = _tip(_state);
        ToolTip = BuildTip(title, body);
    }

    /// <summary>The ring: a filled arc when the size is known, a quarter that turns when it is not.</summary>
    private void PaintRing()
    {
        if (_state != ReplayButtonState.Downloading) return;
        if (_progress is double p)
        {
            StopSpin();
            _arc.Data = ArcGeometry(p);
        }
        else
        {
            _arc.Data = ArcGeometry(0.25);
            StartSpin();
        }
    }

    private void StartSpin()
    {
        if (_spinClock != null) return;
        if (RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation) return;
        var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(turn, 30);
        _spinClock = turn.CreateClock();
        _spin.ApplyAnimationClock(RotateTransform.AngleProperty, _spinClock);
    }

    private void StopSpin()
    {
        if (_spinClock == null) return;
        _spinClock.Controller?.Stop();
        _spin.ApplyAnimationClock(RotateTransform.AngleProperty, null);
        _spinClock = null;
        _spin.Angle = 0;
    }

    /// <summary>An icon in its SVG view box, centred in the disc as the handoff draws it.</summary>
    private void SetIcon(Geometry geometry, double width, double height, Brush stroke, double thickness)
    {
        _icon.Data = geometry;
        _icon.Width = width;
        _icon.Height = height;
        _icon.Stroke = stroke;
        _icon.StrokeThickness = thickness;
        _icon.Fill = null;
    }

    private static FrameworkElement BuildTip(string title, string? body)
    {
        var stack = new StackPanel { MaxWidth = 228 };
        var head = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        head.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
        head.SetResourceReference(TextBlock.ForegroundProperty, "MpTextHeading");
        head.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont");
        stack.Children.Add(head);
        if (!string.IsNullOrWhiteSpace(body))
        {
            var text = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            text.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
            text.SetResourceReference(TextBlock.ForegroundProperty, "MpReplayTipBody");
            text.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont");
            stack.Children.Add(text);
        }
        var tip = new ToolTip { Content = stack };
        if (Application.Current?.TryFindResource("MpRichToolTip") is Style style) tip.Style = style;
        return tip;
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static ControlTemplate BuildTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    /// <summary>The ring's arc from twelve o'clock, clockwise, <paramref name="fraction"/> of the way round.</summary>
    internal static Geometry ArcGeometry(double fraction)
    {
        const double c = Size / 2, r = (Size - 3) / 2;
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        if (fraction <= 0) return Geometry.Empty;
        var angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    // The handoff's SVG paths, in WPF's own syntax (explicit L after each M).
    private static readonly Geometry ArrowGeometry = Frozen("M5,1 L5,7.2 M2.2,4.6 L5,7.4 L7.8,4.6 M1.5,10 L8.5,10");
    private static readonly Geometry FolderGeometry = Frozen("M1,1.5 L4,1.5 L5,2.7 L10,2.7 L10,8 L1,8 Z");
    private static readonly Geometry RetryGeometry = Frozen("M8.6,5 A3.6,3.6 0 1 1 7.5,2.4 M7.9,0.8 L7.9,2.9 L5.8,2.9");

    private static Geometry Frozen(string data)
    {
        var g = Geometry.Parse(data);
        if (g.CanFreeze) g.Freeze();
        return g;
    }
}
