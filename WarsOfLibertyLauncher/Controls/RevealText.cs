using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Hover a piece of text that ends in an ellipsis and the rest of it appears, IN PLACE —
/// same font, same colour, starting on the same pixel, so it reads as the sentence
/// continuing rather than as a label about it.
///
/// <para><b>Why this exists.</b> 65 places in this launcher trim text with
/// <c>CharacterEllipsis</c> and, before this, not one of them offered any way to read what
/// was cut. The report that prompted it is the community strip's own totals line, which ends
/// "…Mapa más jugado: ESOC Fertile…" — the map is the last thing on the line precisely so it
/// is the first thing to be lost, and there was nowhere else in the launcher to find it.</para>
///
/// <para><b>It reaches all 65 without touching any of them.</b> An implicit
/// <c>TextBlock</c> style (<c>Styles/Text.xaml</c>) sets <see cref="EnabledProperty"/> from a
/// trigger on <c>TextTrimming</c>, so nothing opts in by hand and a block that does not trim
/// pays nothing. It covers the 38 built in CODE as well as the 27 written in XAML: a style
/// trigger is evaluated against the property's CURRENT value whatever set it, so
/// <c>new TextBlock { TextTrimming = CharacterEllipsis }</c> arms it exactly like an
/// attribute would.</para>
///
/// <para><b>The one style that needs saying so is <c>SidebarNavLabel</c></b>
/// (<c>Styles/Buttons.xaml</c>) — the only NAMED TextBlock style in the repo that sets
/// <c>TextTrimming</c>, and a named style does not inherit the implicit one. It carries a
/// <c>BasedOn="{StaticResource {x:Type TextBlock}}"</c> for this. Any future named style that
/// trims needs the same.</para>
///
/// <para><b>THE TOOLTIP IS ARMED WHEN THE BLOCK IS LAID OUT, NOT WHEN THE POINTER ARRIVES —
/// and the first version of this got that backwards and did nothing at all.</b> It computed on
/// hover to avoid measuring labels nobody points at, which sounds thrifty and cannot work:
/// WPF's tooltip service inspects an element from a CLASS handler when the mouse ENTERS it,
/// class handlers run before instance handlers, and this withdrew its tooltip again on the way
/// out — so at the one instant the service ever looked there was nothing to find, every time,
/// for ever. Nothing threw and nothing looked wrong. The proof it had to be this way was
/// already in the repo: the rooms table's room name has carried a plain tooltip assigned at
/// CONSTRUCTION for months and has always worked.</para>
///
/// <para>So <c>SizeChanged</c> and <c>Loaded</c> arm it — the pointer finds a tooltip already
/// in place and the ordinary path takes over — and <c>MouseEnter</c> re-evaluates, which is
/// what catches a block whose TEXT changed without its width changing (a room's age ticking in
/// a fixed column). That refresh runs after the service looked, but by then layout has put a
/// tooltip there, and the service re-reads the property when its delay expires. The measuring
/// is one <c>FormattedText</c> and nothing is allocated until something actually overflows.</para>
///
/// <para><b>Sitting UNDER the tooltip service is the whole reason it feels native:</b> closing
/// on exit, never stealing focus and never covering the pointer are all inherited rather than
/// reimplemented. What is overridden is the look, the placement, and — alone among the
/// launcher's tooltips — the DELAY, which is set to zero; see <c>Evaluate</c>.</para>
///
/// <para><b>An honest limitation, measured in this repo:</b> a transparent popup loses
/// ClearType, so the revealed text is antialiased slightly differently from the original it
/// is aligned with. Every tooltip in the launcher already pays this; it is just more visible
/// here, where the two are edge to edge. It is also why <c>RevealTooltip</c> has NO drop
/// shadow, unlike the general tooltip style — an <c>Effect</c> on an ancestor of text
/// disables ClearType a second time, and this is the one surface where that is the point.
/// </para>
/// </summary>
public static class RevealText
{
    /// <summary>Width the revealed text wraps at. Long enough for any label in the launcher
    /// to land on one line; short enough that a pathological string does not become a band
    /// across the window.</summary>
    public const double MaxRevealWidth = 560;

    /// <summary>Inner padding of the reveal, and therefore also its negative offset — the
    /// text has to start on the same pixel as the text underneath it.</summary>
    public const double PadX = 8;
    public const double PadY = 4;

    /// <summary>Slack, in DIPs, before text counts as cut. Layout rounding puts a string that
    /// fits exactly a fraction of a pixel over its own cell often enough to matter, and a
    /// reveal that shows the same words back is worse than none.</summary>
    public const double OverflowSlack = 0.5;

    // ------------------------------------------------------------------ the switch

    /// <summary>
    /// Armed by the implicit TextBlock style whenever the block trims. Public because a Setter
    /// needs it to be; nothing sets it by hand.
    /// </summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(RevealText),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value)
        => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element)
        => (bool)element.GetValue(EnabledProperty);

    /// <summary>
    /// Marks a tooltip as OURS, so a re-evaluation replaces it and never touches one somebody
    /// else put there. It is not a cache: the whole point is that this is decided again
    /// whenever the block is laid out or pointed at.
    /// </summary>
    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State", typeof(HoverState), typeof(RevealText),
            new PropertyMetadata(HoverState.Unknown));

    private enum HoverState { Unknown, Revealed }

    /// <summary>
    /// What the armed tooltip was built FROM — the text and the width it was measured against.
    /// <see cref="OnMouseEnter"/> compares against it so an unchanged block is left completely
    /// alone; see that method for why touching it is not free.
    /// </summary>
    private static readonly DependencyProperty SignatureProperty =
        DependencyProperty.RegisterAttached(
            "Signature", typeof(string), typeof(RevealText), new PropertyMetadata(null));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        tb.SizeChanged -= OnSizeChanged;
        tb.Loaded -= OnLoaded;
        tb.MouseEnter -= OnMouseEnter;

        if (e.NewValue is true)
        {
            tb.SizeChanged += OnSizeChanged;
            tb.Loaded += OnLoaded;
            tb.MouseEnter += OnMouseEnter;
            Evaluate(tb);
        }
        else
        {
            Withdraw(tb);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock tb) Evaluate(tb);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb) Evaluate(tb);
    }

    /// <summary>
    /// A refresh, and ONLY when something actually changed — an unchanged block is not touched.
    ///
    /// <para><b>This is not an optimisation, it is half the fix for a real defect.</b> By the
    /// time this runs the tooltip service has already inspected the element (its class handler
    /// beats every instance handler) and scheduled the show; clearing the ToolTip property
    /// cancels that, and re-assigning it does not reschedule, because the timer only starts
    /// when the mouse ENTERS. So the reveal needed a second inspection to appear — another
    /// full delay on top of the first, which is what "it takes a few seconds" was.</para>
    ///
    /// <para>The reason to be here at all survives untouched: a block whose TEXT changed
    /// without its width changing (a room's age ticking in a fixed column) has a stale
    /// signature and is rebuilt.</para>
    /// </summary>
    private static void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        if ((HoverState)tb.GetValue(StateProperty) == HoverState.Revealed
            && string.Equals((string?)tb.GetValue(SignatureProperty), SignatureOf(tb), StringComparison.Ordinal))
            return;

        Evaluate(tb);
    }

    /// <summary>
    /// What the reveal depends on: the words, and the room they have. Cheap enough to run on
    /// every mouse-enter, which is the point — the alternative is rebuilding blindly.
    /// </summary>
    private static string SignatureOf(TextBlock tb)
        => $"{tb.ActualWidth - tb.Padding.Left - tb.Padding.Right:0.##}␟{PlainTextOf(tb)}";

    /// <summary>
    /// Decide, and put the answer on the element. Withdrawing FIRST is what lets this run any
    /// number of times: it drops our own tooltip so the "somebody else already owns this
    /// element" rule inside <see cref="BuildRevealFor"/> keeps meaning that, instead of us
    /// standing in our own way from the second call onwards.
    /// </summary>
    private static void Evaluate(TextBlock tb)
    {
        Withdraw(tb);

        var reveal = BuildRevealFor(tb);
        if (reveal == null) return;

        tb.SetValue(StateProperty, HoverState.Revealed);
        tb.SetValue(SignatureProperty, SignatureOf(tb));

        // NO DELAY. It goes on the TextBlock and not on the ToolTip because the service reads
        // it from the OWNER; set on the balloon it would do nothing at all.
        //
        // Per element rather than in the app-wide ToolTip style, deliberately: every other
        // tooltip in the launcher EXPLAINS something — a button, a setting, a url — and wants
        // its pause, or a balloon leaps out at every control the pointer brushes past. This one
        // explains nothing. It is the same text already on screen, finished, so a pause only
        // reads as the thing being slow. (The repo already overrides this per element in three
        // places: the gear menu, the offline chip and the notification bell.)
        ToolTipService.SetInitialShowDelay(tb, 0);

        tb.ToolTip = reveal;
    }

    /// <summary>
    /// Take our OWN tooltip back off, and never anybody else's, which is what the marker is
    /// for. Text that has since changed, or now fits, has to stop answering; and a stale one
    /// would go on shadowing an ancestor's, the exact harm <see cref="AlreadyExplained"/>
    /// exists to avoid.
    /// </summary>
    private static void Withdraw(TextBlock tb)
    {
        if ((HoverState)tb.GetValue(StateProperty) == HoverState.Revealed)
        {
            tb.ClearValue(FrameworkElement.ToolTipProperty);
            // The delay goes back with it. It was ours to impose only while our own tooltip was
            // the one being shown; a later tooltip on this element, from anywhere, gets the
            // launcher's ordinary pause.
            tb.ClearValue(ToolTipService.InitialShowDelayProperty);
        }
        tb.ClearValue(StateProperty);
        tb.ClearValue(SignatureProperty);
    }

    // ------------------------------------------------------------------ the decision

    /// <summary>
    /// Everything that has to be true before a word of this is worth drawing, in the order it
    /// gets cheaper to be wrong about. Returns null — no reveal — far more often than not, and
    /// the refusals ARE the feature.
    /// </summary>
    /// <remarks>Public so a test can build a real reveal against a real element, which is the
    /// only way to catch a resource that does not resolve or an offset that drifts: this is a
    /// surface assembled entirely in code, and nothing else in the app would notice.</remarks>
    public static ToolTip? BuildRevealFor(TextBlock tb)
    {
        if (!ShapeAllows(tb.TextTrimming, tb.TextWrapping)) return null;
        if (tb.ToolTip != null) return null;
        if (AlreadyExplained(tb)) return null;

        var available = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (available <= 0) return null;

        // PlainTextOf, never tb.Text — see its own remarks for why asking the obvious property
        // would refuse exactly the line this feature was reported for.
        if (string.IsNullOrWhiteSpace(PlainTextOf(tb))) return null;

        // Measured before anything is cloned. This runs on every layout of every trimmed block
        // in the window, so nothing is allocated until something actually overflows.
        if (!Overflows(MeasureContentWidth(tb), available)) return null;

        var content = CloneText(tb);
        if (content == null) return null;

        return Compose(tb, content);
    }

    /// <summary>
    /// A block only qualifies when it trims AND stays on one line.
    ///
    /// <para>A WRAPPING block that is cut is cut VERTICALLY — it ran out of height, not
    /// width — and no width measurement can see that, so those are out of scope and say so
    /// rather than guessing. Every one of the 65 trimming sites in the launcher is NoWrap
    /// today; this is the guard for the day one is not.</para>
    /// </summary>
    public static bool ShapeAllows(TextTrimming trimming, TextWrapping wrapping)
        => trimming != TextTrimming.None && wrapping == TextWrapping.NoWrap;

    /// <summary>Is the text actually cut? Measured, with <see cref="OverflowSlack"/> of
    /// tolerance for layout rounding.</summary>
    public static bool Overflows(double contentWidth, double availableWidth)
        => contentWidth > availableWidth + OverflowSlack;

    /// <summary>
    /// Does anything from here up already explain this element? If so it wins, and we add
    /// nothing.
    ///
    /// <para>This is the clause that keeps the feature from costing information. Real cases:
    /// the rooms table's PLAYERS cell carries its tooltip on the StackPanel while the trimmed
    /// text is its child; the end-of-match stat cards carry theirs on the Border; every gear
    /// <c>MenuItem</c> carries a two-line explanation. A tooltip on the child would cover all
    /// of those — trading a sentence somebody wrote for a repeat of what is already on
    /// screen.</para>
    /// </summary>
    public static bool AlreadyExplained(DependencyObject? element)
    {
        for (var d = Parent(element); d != null; d = Parent(d))
        {
            if (d is FrameworkElement fe && fe.ToolTip != null) return true;
            if (d is FrameworkContentElement fce && fce.ToolTip != null) return true;
        }
        return false;
    }

    /// <summary>Visual parent first, logical as the fallback — a TextBlock inside a
    /// <c>MenuItem</c>'s header, or one not yet in a visual tree, only has the latter.</summary>
    private static DependencyObject? Parent(DependencyObject? d)
    {
        if (d == null) return null;
        if (d is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var visual = VisualTreeHelper.GetParent(d);
            if (visual != null) return visual;
        }
        return LogicalTreeHelper.GetParent(d);
    }

    /// <summary>
    /// The words a block actually shows, wherever they came from.
    ///
    /// <para><b><c>TextBlock.Text</c> IS NOT THAT, and believing it was cost a round here.</b>
    /// The getter reports only content assigned THROUGH that property; a block whose content
    /// was built by adding <c>Run</c>s — which is how <c>BuildEmphasisRuns</c> writes the
    /// community strip's totals line, i.e. the exact line this feature was reported for —
    /// answers the empty string. An emptiness guard written on <c>tb.Text</c> therefore
    /// refuses precisely the text that needed revealing, and looks correct doing it.</para>
    /// </summary>
    public static string PlainTextOf(TextBlock tb)
    {
        if (tb.Inlines.Count == 0) return tb.Text ?? "";

        var sb = new System.Text.StringBuilder();
        foreach (var inline in tb.Inlines)
            if (inline is Run run) sb.Append(run.Text);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ measuring

    /// <summary>
    /// How wide the text WANTS to be, run by run.
    ///
    /// <para><b>Never <c>DesiredSize</c>.</b> <c>Measure</c> clamps what it reports to the
    /// constraint it was handed, so a block that overflows reports that it fits — the trap
    /// this repo has already been caught by twice, both times as a silent overlap that a green
    /// test could not see. <see cref="FormattedText"/> answers the question that was actually
    /// asked.</para>
    ///
    /// <para>Summed per RUN rather than measured once over <c>tb.Text</c>, because the line
    /// that prompted the whole feature is built by <c>BuildEmphasisRuns</c> — the figures in
    /// it are SemiBold and a different colour, and measuring them in the block's own weight
    /// would under-read the width and decline to reveal exactly the text that needed it.</para>
    /// </summary>
    private static double MeasureContentWidth(TextBlock tb)
    {
        var dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
        if (tb.Inlines.Count == 0) return MeasureOne(tb.Text, tb.FontFamily, tb.FontStyle,
                                                    tb.FontWeight, tb.FontStretch, tb.FontSize,
                                                    tb.FlowDirection, dpi);

        var total = 0.0;
        foreach (var inline in tb.Inlines)
        {
            if (inline is not Run run) continue;
            total += MeasureOne(run.Text, run.FontFamily, run.FontStyle, run.FontWeight,
                                run.FontStretch, run.FontSize, tb.FlowDirection, dpi);
        }
        return total;
    }

    private static double MeasureOne(string? text, FontFamily family, FontStyle style,
                                     FontWeight weight, FontStretch stretch, double size,
                                     FlowDirection flow, double dpi)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return new FormattedText(
            text, CultureInfo.CurrentUICulture, flow,
            new Typeface(family, style, weight, stretch),
            size, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
    }

    // ------------------------------------------------------------------ the reveal

    /// <summary>
    /// A copy of the text as it is drawn, wrapped instead of trimmed. Refuses anything that is
    /// not plain runs — a Hyperlink or an embedded control is not text we can restate, and
    /// half-copying one would produce something that looks like the original and is not.
    /// </summary>
    private static TextBlock? CloneText(TextBlock source)
    {
        var copy = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = MaxRevealWidth,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            FontStretch = source.FontStretch,
            Foreground = source.Foreground,
            FlowDirection = source.FlowDirection,
            LineHeight = source.LineHeight,
            LineStackingStrategy = source.LineStackingStrategy,
        };

        if (source.Inlines.Count == 0)
        {
            copy.Text = source.Text;
            return string.IsNullOrWhiteSpace(copy.Text) ? null : copy;
        }

        var anything = false;
        foreach (var inline in source.Inlines)
        {
            if (inline is not Run run) return null;
            anything |= !string.IsNullOrWhiteSpace(run.Text);
            copy.Inlines.Add(new Run(run.Text)
            {
                FontFamily = run.FontFamily,
                FontSize = run.FontSize,
                FontWeight = run.FontWeight,
                FontStyle = run.FontStyle,
                FontStretch = run.FontStretch,
                Foreground = run.Foreground,
            });
        }
        return anything ? copy : null;
    }

    /// <summary>
    /// Place the copy exactly over the original.
    ///
    /// <para>The tooltip is positioned relative to the TextBlock itself, offset back by its
    /// own border and padding plus whatever inset the TextBlock gives its text, so the first
    /// glyph of the reveal lands on the first glyph of what it is replacing. THAT is what
    /// makes it read as the same sentence continuing instead of as a note about it — get the
    /// offset wrong and it is just a tooltip in an odd place.</para>
    ///
    /// <para>The backdrop is taken from the first ancestor that paints one rather than from a
    /// fixed token: the same trimmed label sits on <c>MpPanel</c> in multiplayer and on
    /// <c>BgPanel</c> in the library, and one colour cannot belong to both.</para>
    /// </summary>
    private static ToolTip Compose(TextBlock anchor, TextBlock content)
    {
        var tip = new ToolTip
        {
            Content = content,
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            HorizontalOffset = -(PadX + 1 - anchor.Padding.Left),
            VerticalOffset = -(PadY + 1 - anchor.Padding.Top),
            Background = ResolveBackdrop(anchor),
            BorderBrush = Brush(anchor, "BorderSecondary", "#3C4650"),
            Padding = new Thickness(PadX, PadY, PadX, PadY),
            HasDropShadow = false,
        };

        if (anchor.TryFindResource("RevealTooltip") is Style style) tip.Style = style;
        return tip;
    }

    /// <summary>
    /// The nearest ancestor that actually paints something. A transparent or zero-alpha fill
    /// is not a backdrop — reading through to whatever is behind it is the failure this is
    /// looking for, since the revealed text has the ORIGINAL text underneath it.
    /// </summary>
    private static Brush ResolveBackdrop(DependencyObject start)
    {
        for (var d = Parent(start); d != null; d = Parent(d))
        {
            var brush = d switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Control control => control.Background,
                _ => null,
            };
            if (Paints(brush)) return brush!;
        }
        return Brush(start as FrameworkElement, "BgPanel", "#15191C");
    }

    private static bool Paints(Brush? brush)
    {
        if (brush == null || brush.Opacity <= 0) return false;
        return brush is not SolidColorBrush solid || solid.Color.A > 0;
    }

    private static Brush Brush(FrameworkElement? scope, string key, string fallback)
    {
        if (scope?.TryFindResource(key) is Brush found) return found;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
        brush.Freeze();
        return brush;
    }
}
