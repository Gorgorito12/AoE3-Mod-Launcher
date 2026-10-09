using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The rank guide's card (docs/design_insignias_pantallas_guia, 46a), shown through
/// <see cref="MpAlertOverlay.ShowContent"/>. Top to bottom: who you are, the next step, the six
/// ages with who holds each, how the table works in four sentences, and a way to the full table.
///
/// <para>Everything it states comes from <see cref="RankGuideView"/>, which cuts the ages with
/// the same rule as every badge — so the guide and the badges cannot disagree. The badges are
/// the SAME control as everywhere else, never a copy, and each age row wears the same banner as
/// the ranking rows.</para>
///
/// <para>Only the list of ages scrolls: the header and the next step stay put, because they are
/// what the player opened the guide to read.</para>
/// </summary>
/// <summary>One tab of the guide: what it shows, the rating its header prints, and where its
/// "Open ranking" goes (null = no button — the lobby window has no ranking to open).</summary>
internal sealed record RankGuideTab(RankGuideView View, double? Rating, Action? OpenRanking);

internal static class RankGuideCard
{
    /// <summary>The numeral each age's badge carries in the list: the handoff's I-V, and a star
    /// for the one age that is a place rather than a range.</summary>
    internal static string NumeralOf(RankAge age) => age switch
    {
        RankAge.Sovereign => "★",
        RankAge.Imperial => "V",
        RankAge.Industrial => "IV",
        RankAge.Fortress => "III",
        RankAge.Colonial => "II",
        _ => "I",
    };

    public static FrameworkElement Build(RankGuideView view, double? myRating, Action close, Action? openRanking)
        => Build(new RankGuideTab(view, myRating, openRanking), team: null, BadgeKind.Solo, close);

    /// <summary>
    /// The guide with its 1v1 / Teams selector (docs/design_guia_rangos_equipos, 53a). With
    /// <paramref name="team"/> null there is no selector and the card is exactly the 1v1 guide
    /// it always was — a server with no team ladder (53b rule 3). The tab shown first is
    /// <paramref name="initial"/>, i.e. the kind of badge that was clicked; switching tabs
    /// rebuilds the card in place and is never saved (rule 1).
    /// </summary>
    /// <param name="entryBar">The ladder's entry bar as the server states it, quoted by the Teams
    /// notice for a viewer with no decided team match. Null = the server did not say.</param>
    public static FrameworkElement Build(RankGuideTab solo, RankGuideTab? team, BadgeKind initial, Action close,
        int? entryBar = null)
    {
        var root = new Grid { Tag = "RankGuide" };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shown = initial == BadgeKind.Team && team != null ? BadgeKind.Team : BadgeKind.Solo;
        void Render()
        {
            root.Children.Clear();
            var tab = shown == BadgeKind.Team ? team! : solo;
            Fill(root, tab, shown, team != null, entryBar, close, kind =>
            {
                if (kind == shown) return;
                shown = kind;
                Render();
            });
        }
        Render();
        return root;
    }

    private static void Fill(Grid root, RankGuideTab tab, BadgeKind ladder, bool withSelector, int? entryBar,
        Action close, Action<BadgeKind> select)
    {
        var view = tab.View;
        var lang = Strings.Language;
        string Ord(int n) => RankGuideView.Ordinal(n, lang);
        var team = ladder == BadgeKind.Team;

        // ── 1. Who you are ──
        var header = new Grid { Margin = new Thickness(22, 18, 14, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (view.MyAge is { } myAge)
        {
            var mine = Badge(ladder, myAge,
                view.MyPosition is > 0 ? view.MyPosition.Value.ToString() : null, HeaderBadge(ladder), "guide-me");
            mine.Margin = new Thickness(0, 0, 14, 0);
            header.Children.Add(mine);
        }
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Text(Strings.Get("MpGuideTitle"), "MpTextHeading", "FontSizeTitle", FontWeights.Bold));
        // With the selector beside it the line has ~100 px less, and the team Discovery sentence
        // ("You are Discovery · play a competitive team match to join") is the one thing in the
        // header a player has to read whole — so it wraps there rather than ending in "…".
        who.Children.Add(Text(YouAre(view, tab.Rating, Ord), "MpTextMuted", "FontSizeBody", FontWeights.Normal,
            margin: new Thickness(0, 2, 0, 0), trim: !withSelector, wrap: withSelector, tag: "RankGuideYouAre"));
        Grid.SetColumn(who, 1);
        header.Children.Add(who);
        if (withSelector)
        {
            var selector = Selector(ladder, select);
            Grid.SetColumn(selector, 2);
            header.Children.Add(selector);
        }
        var x = new Button
        {
            Content = "✕",
            Style = (Style)Application.Current.FindResource("MpIconButton"),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = TooltipHelper.Wrap(Strings.Get("MpGuideClose")),
            Tag = "RankGuideClose",
        };
        x.Click += (_, _) => close();
        Grid.SetColumn(x, 3);
        header.Children.Add(x);
        root.Children.Add(header);

        // ── 2. The next step ──
        var next = NextStepLine(view, Ord, entryBar);
        if (next != null)
        {
            Grid.SetRow(next, 1);
            root.Children.Add(next);
        }

        // ── 3. The six ages, and 4. how it works — the only part that scrolls ──
        var list = new StackPanel { Margin = new Thickness(22, 6, 22, 8) };
        list.Children.Add(Label(Strings.Get(team ? "MpGuideAgesTitleTeam" : "MpGuideAgesTitle")));
        var nameColumn = NameColumnWidth(view, Ord);
        foreach (var age in view.Ages)
            list.Children.Add(AgeRow(age, view, Ord, nameColumn));
        list.Children.Add(Label(Strings.Get(team ? "MpGuideHowTitleTeam" : "MpGuideHowTitle"), top: 16));
        var sentences = team
            ? new[] { "MpGuideHow1Team", "MpGuideHow2", "MpGuideHow3Team", "MpGuideHow4Team" }
            : new[] { "MpGuideHow1", "MpGuideHow2", "MpGuideHow3", "MpGuideHow4" };
        foreach (var key in sentences)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.Children.Add(Text("•", "MpTextFaint", "FontSizeBody", FontWeights.Normal, margin: new Thickness(0, 0, 8, 0)));
            var sentence = Text(Strings.Get(key), "MpTextBody", "FontSizeBody", FontWeights.Normal, wrap: true);
            Grid.SetColumn(sentence, 1);
            line.Children.Add(sentence);
            list.Children.Add(line);
        }
        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Tag = "RankGuideScroll",
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // ── 5. Footer ──
        // Without a selector the footer is today's, word for word; with one, it names the ladder
        // it counts, since the two tabs count different tables.
        var footer = new Grid { Margin = new Thickness(22, 10, 22, 16) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (view.LadderSize > 0)
        {
            var footerKey = !withSelector ? "MpGuideFooter" : team ? "MpGuideFooterTeam" : "MpGuideFooterSolo";
            footer.Children.Add(Text(Strings.Format(footerKey, view.LadderSize), "MpTextFaint", "FontSizeCaption",
                FontWeights.Normal, trim: true, tag: "RankGuideFooter"));
        }
        if (tab.OpenRanking is { } openRanking)
        {
            var open = new Button
            {
                Content = Strings.Get(team ? "MpGuideOpenTeamRanking" : "MpGuideOpenRanking"),
                Style = (Style)Application.Current.FindResource("MpSecondaryButton"),
                Tag = "RankGuideOpenRanking",
            };
            open.Click += (_, _) => { close(); openRanking(); };
            Grid.SetColumn(open, 1);
            footer.Children.Add(open);
        }
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
    }

    /// <summary>
    /// The 1v1 / Teams selector: the prototype's dark tray with two segments, the shown one lit.
    /// Each segment is NAMED (<c>RankGuideTab1v1</c> / <c>RankGuideTabTeam</c>) rather than
    /// tagged, because <c>Tag</c> is what <c>MpGuideSegment</c>'s active trigger reads.
    /// </summary>
    private static FrameworkElement Selector(BadgeKind shown, Action<BadgeKind> select)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (kind, key, name) in new[]
                 {
                     (BadgeKind.Solo, "MpBadgeMode1v1", "RankGuideTab1v1"),
                     (BadgeKind.Team, "MpBadgeModeTeams", "RankGuideTabTeam"),
                 })
        {
            var segment = new Button
            {
                Content = Strings.Get(key),
                Name = name,
                Style = (Style)Application.Current.FindResource("MpGuideSegment"),
                Tag = kind == shown ? "active" : null,
                Margin = new Thickness(kind == BadgeKind.Solo ? 0 : 3, 0, 0, 0),
            };
            segment.Click += (_, _) => select(kind);
            row.Children.Add(segment);
        }
        return new Border
        {
            Child = row,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Background = Res("MpGuideSegmentTray"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 2, 10, 0),
            Tag = "RankGuideSelector",
        };
    }

    /// <summary>The header badge's FRONT width. A team badge adds its back shield to the left, so
    /// its front is a little smaller to keep the footprint the prototype draws (44 px).</summary>
    private static double HeaderBadge(BadgeKind ladder) => ladder == BadgeKind.Team ? 34 : 36;

    /// <summary>One badge of the guide: a single shield on the 1v1 tab, the double one on Teams —
    /// every badge of the Teams tab, header, notice and rows alike (53a).</summary>
    private static FrameworkElement Badge(BadgeKind ladder, RankAge age, string? numeral, double width, string seed)
        => ladder == BadgeKind.Team
            ? RankBadge.BuildTeam(age, numeral, width, seed)
            : RankBadge.Build(age, numeral, width, seed);

    private static string YouAre(RankGuideView view, double? rating, Func<int, string> ord)
    {
        if (view.MyAge is not { } age) return Strings.Get("MpGuideYouUnknown");
        var parts = new System.Collections.Generic.List<string>
        {
            Strings.Format("MpGuideYouAre", Strings.Get(RankAges.NameKey(age))),
        };
        // No decided team match: say what gets you onto the team table, and print no rating —
        // the 1500 the server would hand back is a placeholder, not a result (53b rule 2).
        if (view.Ladder == BadgeKind.Team && age == RankAge.Discovery)
        {
            parts.Add(Strings.Get("MpGuideTeamPlayToJoin"));
            return string.Join(" · ", parts);
        }
        if (view.MyPosition is > 0 && view.LadderSize > 0)
            parts.Add(Strings.Format("MpGuidePlaceOf", ord(view.MyPosition.Value), view.LadderSize));
        if (rating is { } r) parts.Add(((int)Math.Round(r)).ToString());
        return string.Join(" · ", parts);
    }

    private static FrameworkElement? NextStepLine(RankGuideView view, Func<int, string> ord, int? entryBar)
    {
        var step = view.Next;
        var team = view.Ladder == BadgeKind.Team;
        string text;
        switch (step.Kind)
        {
            case RankGuideStepKind.Pass when step.NextAge is { } nextAge:
                var ageName = Strings.Get(RankAges.NameKey(nextAge));
                text = step.PlacesToPass == 1
                    ? Strings.Format("MpGuideNextPassOne", ageName)
                    : Strings.Format("MpGuideNextPassMany", step.PlacesToPass, ageName);
                if (!string.IsNullOrEmpty(step.TargetName))
                    text += " " + Strings.Format("MpGuideNextWho", step.TargetName, ord(step.TargetPosition));
                break;
            case RankGuideStepKind.Defend:
                text = Strings.Get("MpGuideNextDefend");
                break;
            case RankGuideStepKind.PlayFirst when team:
                // The entry bar is the SERVER's (min_decided), quoted as it said it — the same bar
                // that admits a player to the 1v1 table. Unknown: the sentence without a number.
                text = entryBar switch
                {
                    1 => Strings.Get("MpGuideNextTeamFirstOne"),
                    > 1 => Strings.Format("MpGuideNextTeamFirstMany", entryBar.Value),
                    _ => Strings.Get("MpGuideNextTeamFirst"),
                };
                break;
            case RankGuideStepKind.PlayFirst:
                text = Strings.Get("MpGuideNextPlayFirst");
                break;
            default:
                return null;
        }

        var line = new Border
        {
            Background = Res("MpSurfaceAlt"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(22, 0, 22, 6),
            Tag = "RankGuideNext",
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badgeAge = step.Kind == RankGuideStepKind.Defend ? RankAge.Sovereign : step.NextAge;
        if (badgeAge is { } a)
        {
            var badge = Badge(view.Ladder, a, NumeralOf(a), team ? 19 : 20, "guide-next");
            badge.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(badge);
        }
        var sentence = Text(text, "MpTextBody", "FontSizeBody", FontWeights.Normal, wrap: true);
        sentence.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sentence, 1);
        grid.Children.Add(sentence);
        line.Child = grid;
        return line;
    }

    /// <summary>
    /// The age-name column's width: 176, the width the 1v1 guide was measured at ("no decided
    /// matches yet" fits; 150 cut it mid-word) — and wider only when one of this tab's own lines
    /// would not fit, which is the Teams tab's longer Discovery line in Spanish. Shared by every
    /// row of the tab, since each row is its own Grid and a per-row width would break the
    /// holders column's alignment.
    /// </summary>
    private static double NameColumnWidth(RankGuideView view, Func<int, string> ord)
    {
        const double minimum = 176;
        const double stackMargins = 6 + 8;
        var family = Application.Current.TryFindResource("BodyFont") as FontFamily ?? new FontFamily("Segoe UI");
        double widest = 0;
        foreach (var age in view.Ages)
        {
            widest = Math.Max(widest, Measure(Strings.Get(RankAges.NameKey(age.Age)), family, "FontSizeBody", FontWeights.SemiBold));
            widest = Math.Max(widest, Measure(RangeText(age, view, ord), family, "FontSizeCaption", FontWeights.Normal));
        }
        return Math.Max(minimum, Math.Ceiling(widest) + stackMargins);
    }

    /// <summary>
    /// Measured the way the column will DRAW it: in Display mode — the launcher's, applied to every
    /// window — at the main window's DPI. It used Ideal mode at 1.0, a few pixels narrow of what
    /// Display draws, so the widest Spanish name could end up wider than its own column. The card
    /// is built before it is in any tree, so the main window stands in for its host; the +14 of
    /// margins in <see cref="NameColumnWidth"/> stays as slack.
    /// </summary>
    private static double Measure(string text, FontFamily family, string sizeKey, FontWeight weight)
    {
        var dpi = 1.0;
        try
        {
            if (Application.Current?.MainWindow is { } main) dpi = VisualTreeHelper.GetDpi(main).PixelsPerDip;
        }
        catch { /* not on the main window's thread: 1.0, as before */ }
        return new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), Size(sizeKey), Brushes.Black,
            null, TextFormattingMode.Display, dpi).WidthIncludingTrailingWhitespace;
    }

    private static FrameworkElement AgeRow(RankGuideAge age, RankGuideView view, Func<int, string> ord, double nameColumn)
    {
        // Layers, like the ranking rows: the tint and the banner are rounded Borders with no
        // child, so they clip nothing and the Sovereign's halo keeps its full reach.
        var layers = new Grid { MinHeight = 46, Margin = new Thickness(0, 0, 0, 5), Tag = age.Age };
        if (age.IsMine)
        {
            layers.Children.Add(new Border
            {
                Background = Res("MpActivityOwnRow"),
                BorderBrush = Res("MpAction"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                IsHitTestVisible = false,
                Tag = "RankGuideMine",
            });
        }
        layers.Children.Add(RankBadge.BuildRowBanner(age.Age));

        var grid = new Grid { Margin = new Thickness(8, 6, 10, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        // Wide enough for "no decided matches yet" / "sin partidas decididas" at caption size:
        // 150 cut the Discovery line mid-word, measured on screen.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 25 for the double shield: its back shield adds 7/24, and 32 is what the prototype's
        // row draws and what the 38-px column holds.
        var badge = Badge(view.Ladder, age.Age, NumeralOf(age.Age), view.Ladder == BadgeKind.Team ? 25 : 26,
            "guide-" + age.Age);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(badge);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        name.Children.Add(Text(Strings.Get(RankAges.NameKey(age.Age)), "MpTextHeading", "FontSizeBody", FontWeights.SemiBold, trim: true));
        name.Children.Add(Text(RangeText(age, view, ord), "MpTextFaint", "FontSizeCaption", FontWeights.Normal, trim: true));
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var holders = Text(HoldersText(age), "MpTextBody", "FontSizeCaption", FontWeights.Normal, trim: true);
        holders.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(holders, 2);
        grid.Children.Add(holders);

        var tail = age.IsMine && view.MyPosition is > 0
            ? Text(Strings.Format("MpGuideYouAt", ord(view.MyPosition.Value)), "MpActionText", "FontSizeCaption", FontWeights.SemiBold)
            : age.Age == RankAge.Discovery || age.Count == 0
                ? null
                : Text(age.Count == 1 ? Strings.Get("MpGuidePlayersOne") : Strings.Format("MpGuidePlayersMany", age.Count),
                    "MpTextMuted", "FontSizeCaption", FontWeights.Normal);
        if (tail != null)
        {
            tail.VerticalAlignment = VerticalAlignment.Center;
            tail.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(tail, 3);
            grid.Children.Add(tail);
        }

        layers.Children.Add(grid);
        return layers;
    }

    private static string RangeText(RankGuideAge age, RankGuideView view, Func<int, string> ord)
    {
        if (age.Age == RankAge.Discovery)
            return Strings.Get(view.Ladder == BadgeKind.Team ? "MpGuideRangeDiscoveryTeam" : "MpGuideRangeDiscovery");
        if (age.To == 0 && view.LadderSize > 0) return Strings.Get("MpGuideRangeNone");
        if (age.To == 0 || (age.Age == RankAge.Colonial && age.To >= view.LadderSize))
            return Strings.Format("MpGuideRangeAndBelow", ord(age.From));
        return age.From == age.To
            ? Strings.Format("MpGuideRangeOne", ord(age.From))
            : Strings.Format("MpGuideRange", ord(age.From), ord(age.To));
    }

    /// <summary>The first two names, then "+N" for the rest of the age — the count column says
    /// the total, this says who.</summary>
    private static string HoldersText(RankGuideAge age)
    {
        if (age.Age == RankAge.Discovery) return Strings.Get("MpGuideDiscoveryHolders");
        if (age.Holders.Count == 0) return "—";
        var shown = string.Join(", ", age.Holders.Take(2));
        var more = Math.Max(age.Count, age.Holders.Count) - Math.Min(2, age.Holders.Count);
        return more > 0 ? $"{shown} +{more}" : shown;
    }

    private static TextBlock Label(string text, double top = 0) => new()
    {
        Text = text,
        Foreground = Res("MpTextLabel"),
        FontSize = Size("MpSectionLabelSize"),
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, top, 0, 8),
    };

    private static TextBlock Text(string text, string brush, string size, FontWeight weight,
        Thickness? margin = null, bool trim = false, bool wrap = false, object? tag = null) => new()
    {
        Text = text,
        Foreground = Res(brush),
        FontSize = Size(size),
        FontWeight = weight,
        Margin = margin ?? new Thickness(0),
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Tag = tag,
    };

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static double Size(string key) => (double)Application.Current.FindResource(key);
}
