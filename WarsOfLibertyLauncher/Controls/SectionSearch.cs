using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The search that filters a settings surface down to the rows matching what you typed.
/// Shared by <c>LauncherSettingsDialog</c> and <c>ModPropertiesDialog</c>.
///
/// <para>It lived privately inside the launcher window until the mod window needed one too.
/// Copying it would have been six small methods, which is exactly the duplication this repo
/// keeps paying for — and it would have shipped a real bug, see <see cref="Restore"/>.</para>
///
/// <para>A ROW is identified by the reference of its Style: one of the four app-level
/// <c>SetRow</c> / <c>SetRowLast</c> / <c>SetActionRow</c> / <c>SetActionRowLast</c> resources
/// and nothing else. That is why a section built at RUNTIME with hand-written styles — the mod
/// window's language packs, addons, AI games and decks — contributes no rows and is searched
/// only through whatever static text sits around it. Leaving them out is a decision rather than
/// an oversight: those cards are content, not settings.</para>
/// </summary>
internal static class SectionSearch
{
    /// <summary>
    /// One searchable section: the panel holding its groups, and how to bring it to the front.
    ///
    /// <para><see cref="Activate"/> is a callback rather than an id because the two windows key
    /// their sections differently — the launcher by a string, the mod window by the rail Button —
    /// and the mod window's STATS entry additionally has to kick off a lazy load. Neither of
    /// those is this class's business.</para>
    /// </summary>
    internal readonly record struct Section(Panel Panel, Action Activate);

    /// <summary>
    /// Filters every section and returns the first one with a hit, or null when nothing matched
    /// anywhere. The caller owns the empty state: this class touches no UI it was not handed.
    /// </summary>
    internal static Section? Apply(string query, IEnumerable<Section> sections)
    {
        query = (query ?? "").Trim();

        Section? first = null;
        foreach (var section in sections)
        {
            var any = FilterPanel(section.Panel, query);
            if (any && first is null) first = section;
        }

        // Only when it is not already on screen. A panel's own visibility is the one "is this
        // section active" signal both windows share — and asking costs nothing, where calling
        // Activate on every keystroke would re-run whatever the caller hung off it (the mod
        // window's STATS entry starts an async load).
        if (first is { } hit && hit.Panel.Visibility != Visibility.Visible) hit.Activate();

        return first;
    }

    /// <summary>
    /// Puts every section back the way it was before the search started.
    ///
    /// <para><b>Back the way it WAS, not "everything visible" — and that distinction is the whole
    /// reason this is not a copy of the original.</b> The launcher's version set every direct
    /// child to Visible and got away with it because <c>ShowSection</c> ran immediately after and
    /// re-decided the handful of elements that are collapsed on purpose. The mod window's
    /// <c>SetActiveTab</c> re-decides nothing, so the same code there would have revealed six
    /// elements hidden deliberately — the "no backups yet" line beside an actual list of backups,
    /// and three more empty-state hints. Remembering beats repairing.</para>
    /// </summary>
    internal static void Restore(IEnumerable<Section> sections)
    {
        foreach (var section in sections)
        {
            foreach (var row in RowsIn(section.Panel)) Forget(row);
            foreach (var child in section.Panel.Children.OfType<UIElement>()) Forget(child);
        }
    }

    /// <summary>
    /// Whether <paramref name="query"/> occurs in <paramref name="haystack"/>, ignoring case AND
    /// diacritics.
    ///
    /// <para>The diacritics half is a fix, not a flourish. The original compared with
    /// <c>StringComparison.CurrentCultureIgnoreCase</c>, which is case-insensitive and
    /// accent-SENSITIVE — so in a Spanish-first UI typing <c>actualizacion</c> found nothing at
    /// all, and the search read as broken rather than as empty.</para>
    ///
    /// <para>Pure, so it is the part of this file a test can reach without a window.</para>
    /// </summary>
    internal static bool Matches(string? haystack, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(haystack)) return false;

        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            haystack, query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>
    /// Filters one section. Returns whether anything in it survived.
    ///
    /// <para>Each DIRECT CHILD of the panel is one group. A child holding rows keeps the rows
    /// that match and collapses when none do — a card that lost every row has to take its label
    /// with it, or the search leaves a heading over nothing. A child holding NO rows is matched
    /// on its own flattened text instead, which is what keeps a group label, a lone paragraph or
    /// a notice searchable.</para>
    /// </summary>
    internal static bool FilterPanel(Panel panel, string query)
    {
        CacheRowStyles();
        var any = false;

        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            var rows = RowsIn(child).ToList();

            if (rows.Count == 0)
            {
                var ownHit = Matches(TextOf(child), query);
                Show(child, ownHit);
                any |= ownHit;
                continue;
            }

            var anyHere = false;
            foreach (var row in rows)
            {
                var hit = Matches(TextOf(row), query);
                Show(row, hit);
                anyHere |= hit;
            }

            Show(child, anyHere);
            any |= anyHere;
        }

        return any;
    }

    private static void Show(UIElement el, bool visible)
    {
        Remember(el);
        el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The visibility an element had before the first keystroke of the current search.
    ///
    /// <para>An attached property rather than a dictionary so it lives and dies with the element
    /// itself — two windows share this class, and a static map keyed by element would outlive a
    /// closed dialog.</para>
    /// </summary>
    private static readonly DependencyProperty PreSearchVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "PreSearchVisibility", typeof(object), typeof(SectionSearch),
            new PropertyMetadata(null));

    private static void Remember(UIElement el)
    {
        // Only the FIRST time: later keystrokes must not record what the previous filter did.
        if (el.GetValue(PreSearchVisibilityProperty) is null)
            el.SetValue(PreSearchVisibilityProperty, el.Visibility);
    }

    private static void Forget(UIElement el)
    {
        if (el.GetValue(PreSearchVisibilityProperty) is Visibility was) el.Visibility = was;
        el.ClearValue(PreSearchVisibilityProperty);
    }

    private static Style? s_rowStyle;
    private static Style? s_rowLastStyle;
    private static Style? s_actionRowStyle;
    private static Style? s_actionRowLastStyle;

    private static void CacheRowStyles()
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        s_rowStyle ??= res["SetRow"] as Style;
        s_rowLastStyle ??= res["SetRowLast"] as Style;
        s_actionRowStyle ??= res["SetActionRow"] as Style;
        s_actionRowLastStyle ??= res["SetActionRowLast"] as Style;
    }

    /// <summary>Every settings row under <paramref name="root"/>, by Style reference.</summary>
    internal static IEnumerable<Border> RowsIn(DependencyObject root)
    {
        foreach (var b in Descendants(root).OfType<Border>())
        {
            if (b.Style is null) continue;
            if (ReferenceEquals(b.Style, s_rowStyle) || ReferenceEquals(b.Style, s_rowLastStyle)
                || ReferenceEquals(b.Style, s_actionRowStyle)
                || ReferenceEquals(b.Style, s_actionRowLastStyle))
                yield return b;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>
    /// All the text an element shows, flattened, so the query can match any of it.
    ///
    /// <para>The LOGICAL tree, and TextBlocks only. A Button whose Content is a TextBlock is
    /// therefore read; one with a plain string Content is not, and neither is a ComboBox's items —
    /// their text only exists inside a template, in the visual tree.</para>
    /// </summary>
    private static string TextOf(DependencyObject root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tb in Descendants(root).OfType<TextBlock>())
        {
            var t = RevealText.PlainTextOf(tb);
            if (!string.IsNullOrWhiteSpace(t)) sb.Append(t).Append(' ');
        }
        return sb.ToString();
    }
}
