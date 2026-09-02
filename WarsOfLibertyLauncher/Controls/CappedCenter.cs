using System;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Centres a width-capped content column inside whatever space it is given, by growing
/// its left and right margin instead of moving it.
///
/// <para><b>Why this needs code at all.</b> The settings surfaces cap their content
/// column so a description cannot become a 200-character line on a wide monitor — the
/// handoff calls an unbounded column "the defect that repeats most". The cap was written
/// as a star column with a <c>MaxWidth</c>, which stops growing at the cap and leaves the
/// surplus UNALLOCATED at the right edge: maximised on a 2560 panel that surplus was half
/// the window, and the whole page hugged the left.</para>
///
/// <para>Centring it is not expressible in XAML, and the three attempts are worth writing
/// down so nobody spends an afternoon rediscovering them:</para>
/// <list type="bullet">
///   <item><c>MaxWidth</c> + <c>HorizontalAlignment="Center"</c> on the panel makes it
///   shrink-wrap its content, so the cards come out at their natural width instead of the
///   column width — the exact failure the comment on that column already records for
///   <c>Left</c>.</item>
///   <item><c>* | *(MaxWidth) | *</c> gives each star a third of the width, so on a narrow
///   window the content takes a third of the room instead of all of it.</item>
///   <item><c>Auto | *(MaxWidth) | Auto</c> with empty side columns measures those at 0 and
///   centres nothing. That is today's behaviour.</item>
/// </list>
///
/// <para>So the margin moves and the columns inside are left completely alone, which is
/// what keeps the cards stretching to the column width.</para>
/// </summary>
public static class CappedCenter
{
    /// <summary>
    /// The width the content is capped at. Setting it arms the behaviour; the element is
    /// centred within its parent whenever the parent is wider than this.
    /// </summary>
    public static readonly DependencyProperty MaxContentWidthProperty =
        DependencyProperty.RegisterAttached(
            "MaxContentWidth", typeof(double), typeof(CappedCenter),
            new PropertyMetadata(0d, OnMaxContentWidthChanged));

    public static void SetMaxContentWidth(DependencyObject o, double value)
        => o.SetValue(MaxContentWidthProperty, value);

    public static double GetMaxContentWidth(DependencyObject o)
        => (double)o.GetValue(MaxContentWidthProperty);

    /// <summary>
    /// The element's own declared margin, captured once when the behaviour is armed.
    ///
    /// <para>Load-bearing: the centring is ADDED to it rather than replacing it. These
    /// grids carry the surface's content padding in their margin, and overwriting it would
    /// silently delete that padding the moment the window got wide enough to centre.</para>
    /// </summary>
    private static readonly DependencyProperty BaseMarginProperty =
        DependencyProperty.RegisterAttached(
            "BaseMargin", typeof(Thickness), typeof(CappedCenter),
            new PropertyMetadata(default(Thickness)));

    /// <summary>
    /// How much air goes on EACH side. Pure, so the rule can be argued with in a test
    /// rather than on a screenshot.
    ///
    /// <para>Zero when there is nothing to spare, which is the case that matters most: at
    /// the default window size the cap does not bind, so an armed element lays out exactly
    /// as it did before this existed.</para>
    /// </summary>
    public static double SideMargin(double available, double cap)
    {
        if (cap <= 0 || double.IsNaN(available) || double.IsInfinity(available)) return 0;
        return Math.Max(0, (available - cap) / 2);
    }

    private static void OnMaxContentWidthChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement el) return;

        el.SetValue(BaseMarginProperty, el.Margin);
        el.Loaded -= OnLoaded;
        el.Loaded += OnLoaded;
        if (el.IsLoaded) Arm(el);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el) Arm(el);
    }

    /// <summary>
    /// Subscribes to whatever tells us the available width changed. The element's OWN size
    /// is the result of this behaviour, so listening to it would feed back on itself.
    /// </summary>
    private static void Arm(FrameworkElement el)
    {
        if (el.Parent is not FrameworkElement parent) return;

        parent.SizeChanged -= OnAvailableWidthChanged;
        parent.SizeChanged += OnAvailableWidthChanged;

        // A ScrollViewer additionally changes the space it offers when its scrollbar
        // appears or disappears, which is not a size change of the ScrollViewer itself.
        if (parent is ScrollViewer sv)
        {
            sv.ScrollChanged -= OnScrollChanged;
            sv.ScrollChanged += OnScrollChanged;
        }

        Apply(el);
    }

    private static void OnAvailableWidthChanged(object sender, SizeChangedEventArgs e)
        => ApplyToChildOf(sender);

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        => ApplyToChildOf(sender);

    private static void ApplyToChildOf(object sender)
    {
        if (sender is ContentControl { Content: FrameworkElement content }) Apply(content);
        else if (sender is Decorator { Child: FrameworkElement child }) Apply(child);
    }

    private static void Apply(FrameworkElement el)
    {
        var cap = GetMaxContentWidth(el);
        if (cap <= 0) return;

        var b = (Thickness)el.GetValue(BaseMarginProperty);

        // The base margin is the surface's content padding and sits OUTSIDE the cap, so it
        // has to come off the available width before the split. Measured against the raw
        // viewport instead, the padding is charged twice — once as itself and once as part
        // of what the cap is being compared to — and the column settles at cap minus the
        // padding: 820 where 860 was asked for.
        var side = SideMargin(AvailableWidthFor(el) - b.Left - b.Right, cap);

        var wanted = new Thickness(b.Left + side, b.Top, b.Right + side, b.Bottom);
        if (el.Margin != wanted) el.Margin = wanted;
    }

    /// <summary>
    /// The space the element actually gets. For a <see cref="ScrollViewer"/> that is the
    /// VIEWPORT, not the control: its <c>ActualWidth</c> includes the vertical scrollbar,
    /// and centring against it would push the content off-centre by half a scrollbar the
    /// moment the page got long enough to scroll.
    /// </summary>
    private static double AvailableWidthFor(FrameworkElement el) => el.Parent switch
    {
        ScrollViewer { ViewportWidth: > 0 } sv => sv.ViewportWidth,
        FrameworkElement parent => parent.ActualWidth,
        _ => 0,
    };
}
