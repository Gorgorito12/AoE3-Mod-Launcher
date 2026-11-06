using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The wheel of a dialog that is waiting (designs 97, 101). A track and a quarter arc, turning only
/// while somebody can see it: the animation is a clock that is STOPPED when the wheel goes away,
/// because a detached forever animation keeps WPF drawing for nobody (see RankBadge).
/// </summary>
public sealed class WindowSpinner : Grid
{
    private const double Stroke = 2;

    private readonly RotateTransform _turn = new();
    private AnimationClock? _clock;

    public WindowSpinner()
    {
        Width = 14;
        Height = 14;
        VerticalAlignment = VerticalAlignment.Center;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _turn;
        Loaded += (_, _) => { Build(); Sync(); };
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Sync();
    }

    private void Build()
    {
        if (Children.Count > 0) return;
        var track = new Ellipse { StrokeThickness = Stroke };
        track.SetResourceReference(Shape.StrokeProperty, "WindowSpinnerTrack");
        Children.Add(track);

        var radius = (Width - Stroke) / 2;
        var centre = Width / 2;
        var reach = radius * Math.Sqrt(0.5);
        var start = new Point(centre - reach, centre - reach);
        var end = new Point(centre + reach, centre - reach);
        var figure = new PathFigure { StartPoint = start, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        var arc = new Path { StrokeThickness = Stroke, Data = new PathGeometry(new[] { figure }) };
        arc.SetResourceReference(Shape.StrokeProperty, "WindowSpinnerArc");
        Children.Add(arc);
    }

    private void Sync()
    {
        if (IsVisible && IsLoaded) Start();
        else Stop();
    }

    private void Start()
    {
        if (_clock != null || RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation) return;
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(spin, 30);
        _clock = spin.CreateClock();
        _turn.ApplyAnimationClock(RotateTransform.AngleProperty, _clock);
    }

    private void Stop()
    {
        if (_clock == null) return;
        _clock.Controller?.Stop();
        _turn.ApplyAnimationClock(RotateTransform.AngleProperty, null);
        _clock = null;
    }
}
