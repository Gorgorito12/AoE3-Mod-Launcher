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
    {
        var lang = Strings.Language;
        string Ord(int n) => RankGuideView.Ordinal(n, lang);

        var root = new Grid { Tag = "RankGuide" };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── 1. Who you are ──
        var header = new Grid { Margin = new Thickness(22, 18, 14, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (view.MyAge is { } myAge)
        {
            var mine = RankBadge.Build(myAge,
                view.MyPosition is > 0 ? view.MyPosition.Value.ToString() : null, 36, "guide-me");
            mine.Margin = new Thickness(0, 0, 14, 0);
            header.Children.Add(mine);
        }
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Text(Strings.Get("MpGuideTitle"), "MpTextHeading", "FontSizeTitle", FontWeights.Bold));
        who.Children.Add(Text(YouAre(view, myRating, Ord), "MpTextMuted", "FontSizeBody", FontWeights.Normal,
            margin: new Thickness(0, 2, 0, 0), trim: true, tag: "RankGuideYouAre"));
        Grid.SetColumn(who, 1);
        header.Children.Add(who);
        var x = new Button
        {
            Content = "✕",
            Style = (Style)Application.Current.FindResource("MpIconButton"),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = TooltipHelper.Wrap(Strings.Get("MpGuideClose")),
            Tag = "RankGuideClose",
        };
        x.Click += (_, _) => close();
        Grid.SetColumn(x, 2);
        header.Children.Add(x);
        root.Children.Add(header);

        // ── 2. The next step ──
        var next = NextStepLine(view, Ord);
        if (next != null)
        {
            Grid.SetRow(next, 1);
            root.Children.Add(next);
        }

        // ── 3. The six ages, and 4. how it works — the only part that scrolls ──
        var list = new StackPanel { Margin = new Thickness(22, 6, 22, 8) };
        list.Children.Add(Label(Strings.Get("MpGuideAgesTitle")));
        foreach (var age in view.Ages)
            list.Children.Add(AgeRow(age, view, Ord));
        list.Children.Add(Label(Strings.Get("MpGuideHowTitle"), top: 16));
        foreach (var key in new[] { "MpGuideHow1", "MpGuideHow2", "MpGuideHow3", "MpGuideHow4" })
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
        var footer = new Grid { Margin = new Thickness(22, 10, 22, 16) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (view.LadderSize > 0)
            footer.Children.Add(Text(Strings.Format("MpGuideFooter", view.LadderSize), "MpTextFaint", "FontSizeCaption",
                FontWeights.Normal, trim: true));
        if (openRanking != null)
        {
            var open = new Button
            {
                Content = Strings.Get("MpGuideOpenRanking"),
                Style = (Style)Application.Current.FindResource("MpSecondaryButton"),
                Tag = "RankGuideOpenRanking",
            };
            open.Click += (_, _) => { close(); openRanking(); };
            Grid.SetColumn(open, 1);
            footer.Children.Add(open);
        }
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    private static string YouAre(RankGuideView view, double? rating, Func<int, string> ord)
    {
        if (view.MyAge is not { } age) return Strings.Get("MpGuideYouUnknown");
        var parts = new System.Collections.Generic.List<string>
        {
            Strings.Format("MpGuideYouAre", Strings.Get(RankAges.NameKey(age))),
        };
        if (view.MyPosition is > 0 && view.LadderSize > 0)
            parts.Add(Strings.Format("MpGuidePlaceOf", ord(view.MyPosition.Value), view.LadderSize));
        if (rating is { } r) parts.Add(((int)Math.Round(r)).ToString());
        return string.Join(" · ", parts);
    }

    private static FrameworkElement? NextStepLine(RankGuideView view, Func<int, string> ord)
    {
        var step = view.Next;
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
            var badge = RankBadge.Build(a, NumeralOf(a), 20, "guide-next");
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

    private static FrameworkElement AgeRow(RankGuideAge age, RankGuideView view, Func<int, string> ord)
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = RankBadge.Build(age.Age, NumeralOf(age.Age), 26, "guide-" + age.Age);
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
        if (age.Age == RankAge.Discovery) return Strings.Get("MpGuideRangeDiscovery");
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
