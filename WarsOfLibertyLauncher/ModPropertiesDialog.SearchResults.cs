using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The results list under the mod window's search box (design handoff 50b).
///
/// <para><b>What it adds over the filter.</b> The filter alone already lands on the right
/// section — that is what the keywords fixed. The list says WHAT it found and WHY: the action
/// by name, the section and group it lives in, and the keyword that matched, which is the
/// whole story when somebody typed a Spanish word into an English window. Choosing one opens
/// its section, keeps the filter, and rings the target for a few seconds.</para>
///
/// <para><b>Its entries are a short declared table, not a crawl.</b> Only an element that
/// carries <see cref="SectionSearch.KeywordsProperty"/> can be a result, and each needs a
/// title, a glyph and a path the element itself cannot supply. Two today.</para>
/// </summary>
public partial class ModPropertiesDialog
{
    /// <summary>The handoff's cap.</summary>
    internal const int MaxSearchResults = 5;

    /// <summary>One thing the list can offer.</summary>
    internal sealed record SearchTarget(
        FrameworkElement Element,
        Button Section,
        string TitleKey,
        string Glyph,
        string GroupKey,
        FrameworkElement RingTarget,
        double RingCorner,
        bool IsDiagnostics);

    /// <summary>A target that matched, with the keyword that made it match (null for a
    /// multi-word query, which matches as a phrase rather than as one word).</summary>
    internal sealed record SearchHit(SearchTarget Target, string? Keyword);

    private IReadOnlyList<SearchTarget> SearchTargets() => new[]
    {
        new SearchTarget(ShareDiagnosticsBtn, TabLocalFilesBtn, "ModPropShareDiagnostics", "⇪",
            "ModSearchGroupTrouble", DiagStepBlock, 9, IsDiagnostics: true),
        new SearchTarget(ViewLogsBtn, TabLocalFilesBtn, "ModPropViewLogs", "▤",
            "ModSearchGroupTrouble", ViewLogsBtn, 7, IsDiagnostics: false),
    };

    /// <summary>
    /// What the list shows for <paramref name="query"/>, in declared order, at most
    /// <see cref="MaxSearchResults"/>. A target whose section is hidden is never offered — a
    /// search must not send anyone somewhere they cannot get back to (the same rule
    /// <see cref="SearchSections"/> follows).
    /// </summary>
    internal IReadOnlyList<SearchHit> SearchHitsFor(string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return Array.Empty<SearchHit>();

        var hits = new List<SearchHit>();
        foreach (var t in SearchTargets())
        {
            if (t.Section.Visibility != Visibility.Visible) continue;
            var words = SectionSearch.GetKeywords(t.Element);
            if (!SectionSearch.Matches(words, q)) continue;
            hits.Add(new SearchHit(t, SectionSearch.MatchingKeyword(words, q)));
            if (hits.Count == MaxSearchResults) break;
        }
        return hits;
    }

    private Popup? _searchPopup;
    private StackPanel? _searchRows;
    private IReadOnlyList<SearchHit> _searchHits = Array.Empty<SearchHit>();
    private int _searchSelected;

    /// <summary>
    /// Re-reads the hits for the current query, repaints the list and the count beside the
    /// heading. Called by the search box after it has filtered.
    /// </summary>
    private void RefreshSearchResults(string query)
    {
        _searchHits = SearchHitsFor(query);
        _searchSelected = 0;

        if (_searchHits.Count == 0)
        {
            ModSearchCount.Visibility = Visibility.Collapsed;
            CloseSearchResults();
            return;
        }

        ModSearchCount.Text = _searchHits.Count == 1
            ? Strings.Format("ModSearchResultsCountOne", query.Trim())
            : Strings.Format("ModSearchResultsCount", _searchHits.Count, query.Trim());
        ModSearchCount.Visibility = Visibility.Visible;

        EnsureSearchPopup();
        PaintSearchRows();
        // Only on screen: a popup is a window of its own, and a dialog nobody has shown (every
        // test) must not throw one onto the desktop.
        if (IsVisible && ModSearchBox.IsKeyboardFocusWithin) _searchPopup!.IsOpen = true;
    }

    private void CloseSearchResults()
    {
        if (_searchPopup != null) _searchPopup.IsOpen = false;
    }

    /// <summary>
    /// Built once. NOT tracked by <c>ChromePopups</c>: that closes every tracked popup when any
    /// secondary window is activated — this one included — so the list would vanish the
    /// moment the dialog took focus. It is opened and closed here alone.
    /// </summary>
    private void EnsureSearchPopup()
    {
        if (_searchPopup != null) return;

        _searchRows = new StackPanel();
        var hint = new TextBlock
        {
            Text = Strings.Get("ModSearchHint"),
            FontSize = (double)FindResource("SetGroupLabelSize"),
            Foreground = (Brush)FindResource("MpTextMuted"),
            Margin = new Thickness(8, 7, 8, 2),
        };
        var footer = new Border
        {
            BorderBrush = (Brush)FindResource("UiRimSeam"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 0),
            Child = hint,
        };
        var stack = new StackPanel();
        stack.Children.Add(_searchRows);
        stack.Children.Add(footer);

        var radius = new CornerRadius(9);
        var card = new Border
        {
            Width = 330,
            Background = (Brush)FindResource("UiSearchListBg"),
            BorderBrush = (Brush)FindResource("UiSearchListRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = radius,
            Padding = new Thickness(6),
            Child = stack,
        };
        // The shadow sits on a SIBLING underlay, never on an ancestor of the text: an Effect
        // over text costs it its ClearType (the multiplayer rule, launcher-wide).
        var shadow = new Border
        {
            Background = (Brush)FindResource("UiSearchListBg"),
            CornerRadius = radius,
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 10, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        };
        var shell = new Grid { Margin = new Thickness(0, 0, 24, 30) };
        shell.Children.Add(shadow);
        shell.Children.Add(card);

        _searchPopup = new Popup
        {
            PlacementTarget = ModSearchShell,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            AllowsTransparency = true,
            StaysOpen = true,
            Focusable = false,
            Child = shell,
        };

        // A popup computes its place when it opens, so anything that moves the window
        // closes it rather than leaving it floating where the box used to be.
        Deactivated += (_, _) => CloseSearchResults();
        LocationChanged += (_, _) => CloseSearchResults();
        SizeChanged += (_, _) => CloseSearchResults();
        Closed += (_, _) => CloseSearchResults();
    }

    private void PaintSearchRows()
    {
        if (_searchRows == null) return;
        _searchRows.Children.Clear();
        for (var i = 0; i < _searchHits.Count; i++)
            _searchRows.Children.Add(BuildSearchRow(_searchHits[i], i));
    }

    private Border BuildSearchRow(SearchHit hit, int index)
    {
        var t = hit.Target;
        bool selected = index == _searchSelected;
        Brush B(string key) => (Brush)FindResource(key);

        var tile = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = B(t.IsDiagnostics ? "UiDiagSoftBg" : "UiIconTileBg"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = t.Glyph,
                FontSize = (double)FindResource("SetBodySize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = B(t.IsDiagnostics ? "UiDiagText" : "MpTextSecondary"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var title = new TextBlock
        {
            Text = Strings.Get(t.TitleKey),
            FontSize = (double)FindResource("SetBodySize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = B("UiTextHeadline"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var path = new TextBlock
        {
            FontSize = (double)FindResource("SetGroupLabelSize"),
            Foreground = B("MpTextMuted"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        path.Inlines.Add(new Run(
            SentenceCase(LabelOf(t.Section)?.Text) + " › " + Strings.Get(t.GroupKey)));
        if (hit.Keyword is { Length: > 0 } word)
        {
            path.Inlines.Add(new Run(" · "));
            path.Inlines.Add(new Run(word)
            {
                Foreground = B(t.IsDiagnostics ? "UiDiagText" : "MpActionText"),
            });
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(path);

        var enter = new TextBlock
        {
            Text = "↵",
            FontSize = (double)FindResource("SetBodySize"),
            Foreground = B("MpTextMuted"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
            Visibility = selected ? Visibility.Visible : Visibility.Hidden,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(enter, 2);
        grid.Children.Add(tile);
        grid.Children.Add(text);
        grid.Children.Add(enter);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 7, 8, 7),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            // Transparent, not null: a null background is not hit-testable, and the gaps
            // between the children would swallow the click.
            Background = selected ? B(t.IsDiagnostics ? "UiDiagPickBg" : "MpActionSoftBg") : Brushes.Transparent,
            BorderBrush = selected ? B(t.IsDiagnostics ? "UiDiagPickRim" : "MpActionRimSoft") : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 2),
        };
        row.MouseEnter += (_, _) =>
        {
            if (_searchSelected == index) return;
            _searchSelected = index;
            PaintSearchRows();
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenSearchHit(hit);
        };
        return row;
    }

    /// <summary>
    /// "LOCAL FILES" → "Local files": the rail labels are capitals, and a path reads as a
    /// sentence (handoff 50b). Read off the rail label for the same reason the heading is — one
    /// source, already localized.
    /// </summary>
    internal static string SentenceCase(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var lower = label.Trim().ToLower(Strings.Culture);
        return char.ToUpper(lower[0], Strings.Culture) + lower[1..];
    }

    /// <summary>↑↓ move through the list, ↵ opens the chosen entry, Esc closes the list.</summary>
    private void ModSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool open = _searchPopup?.IsOpen == true && _searchHits.Count > 0;
        switch (e.Key)
        {
            case Key.Down when open:
                _searchSelected = (_searchSelected + 1) % _searchHits.Count;
                PaintSearchRows();
                e.Handled = true;
                break;
            case Key.Up when open:
                _searchSelected = (_searchSelected - 1 + _searchHits.Count) % _searchHits.Count;
                PaintSearchRows();
                e.Handled = true;
                break;
            case Key.Enter when open:
                OpenSearchHit(_searchHits[_searchSelected]);
                e.Handled = true;
                break;
            case Key.Escape when open:
                CloseSearchResults();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Focus leaving the box closes the list. Rows are not focusable, so clicking one leaves
    /// the focus where it was and its click still arrives.
    /// </summary>
    private void ModSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CloseSearchResults();

    /// <summary>
    /// Opens a result: its section comes to the front, the filter that found it stays, and the
    /// target is brought into view and ringed for a few seconds.
    /// </summary>
    internal void OpenSearchHit(SearchHit hit)
    {
        CloseSearchResults();
        var t = hit.Target;
        if (t.Section.Visibility == Visibility.Visible
            && !Equals(t.Section.Tag, "active"))
        {
            var section = SearchSections().FirstOrDefault(s => ReferenceEquals(PanelButton(s.Panel), t.Section));
            section.Activate?.Invoke();
        }

        // After layout: the section was collapsed a moment ago, so the target has no place yet.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            t.RingTarget.BringIntoView();
            ShowFoundRing(t.RingTarget, t.RingCorner);
        });
    }

    /// <summary>The rail button that owns a section panel.</summary>
    private Button? PanelButton(Panel panel)
    {
        if (ReferenceEquals(panel, GeneralPanel)) return TabGeneralBtn;
        if (ReferenceEquals(panel, LocalFilesPanel)) return TabLocalFilesBtn;
        if (ReferenceEquals(panel, UserDataPanel)) return TabUserDataBtn;
        if (ReferenceEquals(panel, LanguagePanel)) return TabLanguageBtn;
        if (ReferenceEquals(panel, AddonsPanel)) return TabAddonsBtn;
        if (ReferenceEquals(panel, DecksPanel)) return TabDecksBtn;
        if (ReferenceEquals(panel, StatsPanel)) return TabStatsBtn;
        return null;
    }

    // ------------------------------------------------------------------------
    // The "found it" ring
    // ------------------------------------------------------------------------

    /// <summary>How long the ring stays before it goes, and how long it takes to fade.</summary>
    internal static readonly TimeSpan FoundRingHold = TimeSpan.FromSeconds(2.6);
    internal static readonly TimeSpan FoundRingFade = TimeSpan.FromSeconds(0.4);

    private Adorner? _foundRing;
    private DispatcherTimer? _foundRingTimer;

    /// <summary>
    /// A 4-px ring around the target for about three seconds (handoff 50b). An ADORNER, so it
    /// draws outside the element without moving a pixel of layout. With the system's
    /// animations off it appears and disappears with no transition. No adorner layer (a window
    /// nobody has shown) means no ring, and nothing breaks.
    /// </summary>
    private void ShowFoundRing(FrameworkElement target, double corner)
    {
        RemoveFoundRing();
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer == null) return;

        var ring = new FoundRing(target, corner, (Brush)FindResource("UiDiagRing"));
        layer.Add(ring);
        _foundRing = ring;

        _foundRingTimer = new DispatcherTimer { Interval = FoundRingHold };
        _foundRingTimer.Tick += (_, _) =>
        {
            _foundRingTimer!.Stop();
            if (!SystemParameters.ClientAreaAnimation)
            {
                RemoveFoundRing();
                return;
            }
            var fade = new DoubleAnimation(1, 0, FoundRingFade);
            fade.Completed += (_, _) => { if (ReferenceEquals(_foundRing, ring)) RemoveFoundRing(); };
            ring.BeginAnimation(OpacityProperty, fade);
        };
        _foundRingTimer.Start();
    }

    private void RemoveFoundRing()
    {
        _foundRingTimer?.Stop();
        _foundRingTimer = null;
        if (_foundRing == null) return;
        AdornerLayer.GetAdornerLayer(_foundRing.AdornedElement)?.Remove(_foundRing);
        _foundRing = null;
    }

    /// <summary>The ring itself: a rounded rectangle 4 px wide, drawn just outside the target.</summary>
    private sealed class FoundRing : Adorner
    {
        private const double Width4 = 4;
        private readonly double _corner;
        private readonly Pen _pen;

        public FoundRing(UIElement target, double corner, Brush brush) : base(target)
        {
            _corner = corner;
            _pen = new Pen(brush, Width4);
            _pen.Freeze();
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            var half = Width4 / 2;
            var rect = new Rect(-half, -half, size.Width + Width4, size.Height + Width4);
            dc.DrawRoundedRectangle(null, _pen, rect, _corner + half, _corner + half);
        }
    }
}
