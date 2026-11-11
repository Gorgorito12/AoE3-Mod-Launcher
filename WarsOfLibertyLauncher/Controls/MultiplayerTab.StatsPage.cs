using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The Statistics page, design 98: glass cards with the label inside them, the balance and the
/// matchups side by side when there is room, and the colour of the mod being looked at.
///
/// <para>Only this page wears the mod's colour. Its accent keys are overridden in
/// <c>StatsView.Resources</c> rather than in the application's, so the tab bar above it stays the
/// launcher's blue and the win and loss colours stay what they mean everywhere.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>A civilization with this many matches or fewer is folded (design 98).</summary>
    internal const int CivTailMaxPlayed = 3;

    /// <summary>What the page has decided about its width.</summary>
    private StatsPageLayout _statsLayout = StatsPageLayout.For(0);

    /// <summary>Whether the fold under the balance is open.</summary>
    private bool _statsCivsExpanded;

    /// <summary>The community payload failed with nothing on screen to keep.</summary>
    private bool _statsLoadFailed;

    private Color? _statsAccent;

    private bool StatsLoading
        => !_statsLoadFailed && _statsCommunity == null && _civStats == null;

    // ---------------------------------------------------------------- width and colour

    private void StatsView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var next = StatsPageLayout.For(StatsView.ActualWidth);
        bool rebuild = next.Gutter != _statsLayout.Gutter
                       || next.RowHeight != _statsLayout.RowHeight
                       || next.SideBySide != _statsLayout.SideBySide;

        ApplyStatsLayout();

        // Nothing in the rebuild can move StatsView's own size: the page scrolls inside it.
        if (rebuild && _activeSubtab == Subtab.Stats) RenderStatsTab();
    }

    private void ApplyStatsLayout()
    {
        _statsLayout = StatsPageLayout.For(StatsView.ActualWidth);

        StatsRightColumn.Width = _statsLayout.RightColumn;
        StatsColumnsGap.Width = new GridLength(_statsLayout.Gutter);
        StatsColumns.Margin = new Thickness(0, _statsLayout.Gutter, 0, 0);

        StatsTitleText.SetResourceReference(TextBlock.FontSizeProperty,
            _statsLayout.Wide ? "StatsTitleSize" : "StatsTitleSizeCompact");
    }

    private void ApplyStatsAccent()
    {
        var profile = ModRegistry.Find(StatsModId());
        var accent = ModTheme.AccentOf(profile?.AccentColor, _config?.UseModColor ?? true);
        if (_statsAccent == accent) return;
        _statsAccent = accent;

        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            StatsView.Resources[key] = brush;
        }

        Set("MpAction", accent);
        Set("MpActionText", ModAccent.TextOn(accent));
        Set("StatsAccentFill", ModAccent.SelectionFill(accent));
        Set("StatsAccentBar", Color.FromArgb(0xD9, accent.R, accent.G, accent.B));
    }

    // ---------------------------------------------------------------- the page

    /// <summary>The line under the title.</summary>
    private void RenderStatsCounts()
    {
        if (_statsLoadFailed)
        {
            StatsCountsText.Text = "—";
            return;
        }
        if (StatsLoading)
        {
            StatsCountsText.Text = Strings.Get("MpStatsLoadingShort");
            return;
        }

        var totals = _statsCommunity?.Totals;
        var parts = new List<string>
        {
            // The RATED figure when the server sends one. The total carries no rated predicate,
            // and printing it under the word "rated" is a mistake this page has made once.
            totals?.Rated is int rated
                ? Strings.Format("MpStatsHeadRated", rated, totals.WindowDays)
                : Strings.Format("MpStatsHeadMatches", totals?.Matches ?? 0, totals?.WindowDays ?? 0),
        };

        if (totals is { Players: > 0, PlayersWindowDays: > 0 })
            parts.Add(Strings.Format("MpStatsHeadPlayers", totals.Players, totals.PlayersWindowDays));

        parts.Add(ResolveModDisplayName(StatsModId()));
        parts.Add(Strings.Get(StatsTeamMode() ? "MpStatsModeTeam" : "MpStatsModeSolo"));

        StatsCountsText.Text = string.Join(" · ", parts);
    }

    /// <summary>Fill the page.</summary>
    private void RenderStatsColumns()
    {
        StatsLeftColumn.Children.Clear();
        StatsRightColumn.Children.Clear();

        StatsErrorBox.Visibility = _statsLoadFailed ? Visibility.Visible : Visibility.Collapsed;
        StatsColumns.Visibility = _statsLoadFailed ? Visibility.Collapsed : Visibility.Visible;
        if (_statsLoadFailed)
        {
            StatsErrorTitle.Text = Strings.Get("MpStatsErrorTitle");
            StatsErrorBody.Text = Strings.Get("MpStatsErrorBody");
            StatsRetryButton.Content = Strings.Get("MpStatsRetry");
            return;
        }

        double gap = _statsLayout.Gutter;

        var pairs = new StackPanel();
        AddSpaced(pairs, BuildPairCard(_matchups?.Matchups, StatsTeamMode()
            ? "MpStatsRivalsHint" : "MpStatsMatchupsHint"), gap);
        // Only in team mode: nobody has an ally in a 1v1.
        AddSpaced(pairs, BuildAlliesCard(), gap);

        var top = new Grid();
        var balance = BuildCivTableCard();
        if (pairs.Children.Count > 0 && _statsLayout.SideBySide)
        {
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            // Each card its own height: the prototype aligns them to the start.
            balance.VerticalAlignment = VerticalAlignment.Top;
            top.Children.Add(WithColumn(balance, 0));
            top.Children.Add(WithColumn(pairs, 2));
        }
        else
        {
            var stacked = new StackPanel();
            AddSpaced(stacked, balance, gap);
            AddSpaced(stacked, pairs, gap);
            top.Children.Add(stacked);
        }

        AddSpaced(StatsLeftColumn, top, gap);
        AddSpaced(StatsLeftColumn, BuildDeckCard(), gap);

        AddSpaced(StatsRightColumn, BuildActivityCard(), gap);
        AddSpaced(StatsRightColumn, BuildMapCard(), gap);
    }

    // ---------------------------------------------------------------- answers

    /// <summary>
    /// How long the page waits for the next answer before drawing, and the longest it ever waits.
    /// Opening Statistics sends six requests, and each answer used to rebuild the whole page as it
    /// landed: five or six full builds in the first second, measured (SETTLE). Answers that land
    /// close together now make ONE build, and an answer identical to the one on screen none.
    /// </summary>
    internal const int StatsRenderQuietMs = 90, StatsRenderMaxWaitMs = 350;

    private System.Windows.Threading.DispatcherTimer? _statsRenderTimer;
    private DateTime _statsRenderFirst, _statsRenderLast;

    /// <summary>A server answer changed what the page may show: draw it soon, once, and only if
    /// something it draws moved (<see cref="RenderStatsTabIfChanged"/>).</summary>
    private void QueueStatsRender()
    {
        if (_activeSubtab != Subtab.Stats) return;
        var now = DateTime.UtcNow;
        _statsRenderLast = now;
        if (_statsRenderTimer is { IsEnabled: true }) return;
        _statsRenderFirst = now;
        if (_statsRenderTimer == null)
        {
            _statsRenderTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(30),
            };
            _statsRenderTimer.Tick += (_, _) =>
            {
                var t = DateTime.UtcNow;
                if ((t - _statsRenderLast).TotalMilliseconds < StatsRenderQuietMs
                    && (t - _statsRenderFirst).TotalMilliseconds < StatsRenderMaxWaitMs) return;
                _statsRenderTimer!.Stop();
                if (_activeSubtab == Subtab.Stats) RenderStatsTabIfChanged();
            };
        }
        _statsRenderTimer.Start();
    }

    /// <summary>Adds a card with the gap above it, or nothing when the card has nothing.</summary>
    private static void AddSpaced(Panel host, UIElement? card, double gap)
    {
        if (card == null || card is Panel { Children.Count: 0 }) return;
        if (card is FrameworkElement element && host.Children.Count > 0)
            element.Margin = new Thickness(0, gap, 0, 0);
        host.Children.Add(card);
    }

    private void StatsRetryButton_Click(object sender, RoutedEventArgs e)
    {
        _statsLoadFailed = false;
        if (_demoStats)
        {
            ApplyDemoStats();
            RenderStatsTab();
            return;
        }

        InvalidateStatsForModChange();
        RenderStatsTab();
        RefreshStatsForMod();
    }

    // ---------------------------------------------------------------- shared pieces

    /// <summary>One glass card. The label lives inside it, which is what makes a page of
    /// tables read as a page of things rather than as headings with boxes under them.</summary>
    private static Border StatsPanel(UIElement content, double bottomPadding = 14)
    {
        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, bottomPadding),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = content,
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        card.SetResourceReference(Border.BorderBrushProperty, "StatsCardRim");
        return card;
    }

    /// <summary>A card's label on the left and what it covers on the right.</summary>
    private static Grid StatsHead(string title, string? scope, double bottom)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, bottom) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = StatsText(title.ToUpper(Strings.Culture), "StatsLabelSize", "StatsLabel",
                              FontWeights.Bold);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(label);

        if (!string.IsNullOrEmpty(scope))
        {
            var extra = StatsText(scope, "StatsNoteSize", "StatsNote", FontWeights.Normal);
            extra.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(WithColumn(extra, 1));
        }

        return row;
    }

    private static TextBlock StatsText(string text, string size, string ink, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.FontSizeProperty, size);
        block.SetResourceReference(TextBlock.ForegroundProperty, ink);
        return block;
    }

    private static TextBlock StatsMono(string text, string size, string ink, FontWeight weight)
    {
        var block = StatsText(text, size, ink, weight);
        block.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        return block;
    }

    private static TextBlock StatsProse(string text, string size = "StatsProseSize",
                                        string ink = "StatsProse")
    {
        var block = StatsText(text, size, ink, FontWeights.Normal);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    /// <summary>The note a table ends on.</summary>
    private static TextBlock StatsTableNote(string text)
    {
        var note = StatsProse(text, "StatsNoteSize", "StatsNote");
        note.Margin = new Thickness(0, 8, 0, 2);
        return note;
    }

    /// <summary>A sentence on the left and a link hard right: every fold on this page.</summary>
    private static Grid StatsFoldRow(string text, string? link, Action? onClick)
    {
        var row = new Grid { MinHeight = 40, Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var words = StatsProse(text);
        words.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(words);

        if (link != null && onClick != null)
        {
            var button = new Button
            {
                Content = link,
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.SetResourceReference(FrameworkElement.StyleProperty, "MpLinkButton");
            button.SetResourceReference(Control.FontSizeProperty, "StatsProseSize");
            button.Click += (_, _) => onClick();
            row.Children.Add(WithColumn(button, 1));
        }

        return row;
    }

    /// <summary>A placeholder bar, for the page before anything has arrived (98e).</summary>
    private static Border StatsSkeleton(double width, double height, byte alpha)
        => new()
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(Math.Min(height / 2, 7)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)),
        };

    private static UIElement StatsSkeletonRows(int columns)
    {
        var stack = new StackPanel();
        foreach (double width in new double[] { 150, 116, 132, 96, 168 })
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(StatsSkeleton(width, 11, 0x17));
            for (int i = 1; i < columns; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
                var cell = StatsSkeleton(60, 11, 0x0F);
                cell.HorizontalAlignment = HorizontalAlignment.Right;
                row.Children.Add(WithColumn(cell, i));
            }
            stack.Children.Add(row);
        }
        return stack;
    }

    // ---------------------------------------------------------------- balance

    /// <summary>The civilization rows the server sent, with the name a player may read.</summary>
    private List<(string Civ, string Label, int Played, int Wins, int Losses)> CivRows()
        => (_civStats?.Civs ?? new List<Models.Multiplayer.CivStatEntry>())
            .Select(c => (c.Civ ?? "", StatsCivLabel(c.Civ), c.Played, c.Wins, c.Losses))
            .ToList();

    private FrameworkElement BuildCivTableCard()
    {
        var stack = new StackPanel();
        var rows = CivRows();

        if (StatsLoading)
        {
            stack.Children.Add(StatsHead(Strings.Get("MpCivsTitle"), null, 10));
            stack.Children.Add(StatsSkeletonRows(4));
            return StatsPanel(stack, 10);
        }

        if (rows.Count == 0)
        {
            stack.Children.Add(StatsHead(Strings.Get("MpCivsTitle"), null, 10));
            stack.Children.Add(BuildCivsEmpty());
            return StatsPanel(stack, 10);
        }

        int total = _civStats?.RatedMatches ?? 0;
        int with = _civStats?.RatedMatchesWithCiv ?? 0;
        stack.Children.Add(StatsHead(
            Strings.Get("MpCivsTitle"),
            total > 0 ? Strings.Format("MpStatsCivsScope", with, total) : null,
            10));
        stack.Children.Add(BuildCivHeader());

        var ordered = rows
            .OrderByDescending(r => r.Played)
            .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // If nothing clears the bar, the most-played handful anyway: an empty table under a
        // heading that says there is data reads as a fault rather than as a shortage.
        var kept = ordered.Where(r => r.Played > CivTailMaxPlayed).ToList();
        if (kept.Count == 0) kept = ordered.Take(6).ToList();
        int folded = ordered.Count - kept.Count;

        foreach (var row in _statsCivsExpanded ? ordered : kept)
            stack.Children.Add(BuildCivRow(row.Label, row.Played, row.Wins, row.Losses,
                                           AvgSecondsFor(row.Civ), _statsLayout.RowHeight,
                                           DeckCardNames.Peek(StatsModId())?.CivIconOf(row.Civ)));

        if (folded > 0)
        {
            stack.Children.Add(StatsFoldRow(
                Strings.Format(
                    _statsCivsExpanded
                        ? (folded == 1 ? "MpStatsTailCivsOpenOne" : "MpStatsTailCivsOpen")
                        : (folded == 1 ? "MpStatsTailCivsOne" : "MpStatsTailCivs"),
                    folded, CivTailMaxPlayed),
                Strings.Get(_statsCivsExpanded ? "MpStatsSeeLess" : "MpStatsSeeAllCivs"),
                () =>
                {
                    _statsCivsExpanded = !_statsCivsExpanded;
                    RenderStatsTab();
                }));
        }

        stack.Children.Add(StatsTableNote(Strings.Format("MpStatsCivsRules",
            CivStatsView.MinDecidedForPercent)));
        return StatsPanel(stack, 10);
    }

    /// <summary>Nothing to rank yet, and why.</summary>
    private UIElement BuildCivsEmpty()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());

        var mark = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = "i",
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xAC, 0xEE)),
            },
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0x55, 0xAC, 0xEE)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x55, 0xAC, 0xEE)),
        };
        head.Children.Add(mark);

        var words = new StackPanel();
        words.Children.Add(StatsText(Strings.Get("MpStatsCivsNoData"), "StatsSubtitleSize",
                                     "StatsStrong", FontWeights.SemiBold));
        var count = StatsProse(Strings.Format("MpStatsCivsScopeEmpty",
            _civStats?.RatedMatchesWithCiv ?? 0,
            _civStats?.RatedMatches ?? _civStats?.RatedMatchesWithCiv ?? 0));
        count.Margin = new Thickness(0, 3, 0, 0);
        words.Children.Add(count);
        head.Children.Add(WithColumn(words, 1));
        stack.Children.Add(head);

        var why = StatsProse(Strings.Format("MpStatsCivsEmptyBody",
                                            ResolveModDisplayName(StatsModId())),
                             "StatsBodySize", "StatsSoft");
        why.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(why);
        return stack;
    }

    /// <summary>The server's mean match length for a civilization, when it sent one.</summary>
    private int? AvgSecondsFor(string civ)
        => _civStats?.Civs?.FirstOrDefault(
            c => string.Equals(c.Civ, civ, StringComparison.Ordinal))?.AvgSeconds;

    /// <summary>Wins over decided, green on red. Empty and grey when there is no sample.</summary>
    private static Border BuildWinBar(int wins, int decided, bool enough)
    {
        var track = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        track.SetResourceReference(Border.BackgroundProperty,
            enough ? "StatsBarLoss" : "StatsTrack");

        if (!enough || decided <= 0) return track;

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0, wins), GridUnitType.Star),
        });
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0, decided - wins), GridUnitType.Star),
        });

        var win = new Border { Height = 6 };
        win.SetResourceReference(Border.BackgroundProperty, "MpOk");
        host.Children.Add(win);

        track.Child = host;
        return track;
    }

    private static Grid BuildCivTableGrid(IReadOnlyList<CivColumnSpec> specs, string[] titles)
    {
        var grid = BuildCivGrid(specs);
        grid.Height = 30;

        for (int i = 0; i < specs.Count; i++)
        {
            var title = StatsText(titles[i], "StatsHeaderSize", "StatsLabel", FontWeights.Bold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.HorizontalAlignment = specs[i].RightAligned
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
            title.Margin = new Thickness(0, 0, CivTrailingGap(i, specs.Count), 0);
            grid.Children.Add(WithColumn(title, i));
        }

        return grid;
    }

    private static FrameworkElement StatsRule(UIElement child, string brush = "StatsRowRule")
    {
        var row = new Border { Child = child, BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, brush);
        return row;
    }

    private static FrameworkElement BuildCivHeader()
    {
        var specs = CivTableLayout.All;
        return StatsRule(
            BuildCivTableGrid(specs, specs
                .Select(s => Strings.Get(CivTableLayout.HeaderKey(s.Column))).ToArray()),
            "StatsHeadRule");
    }

    private static FrameworkElement BuildMatchupHeader(string pairColumnKey)
    {
        var specs = CivTableLayout.Matchups;
        return StatsRule(
            BuildCivTableGrid(specs, specs
                .Select(s => Strings.Get(s.Column == CivColumn.Civ
                    ? pairColumnKey
                    : CivTableLayout.MatchupHeaderKey(s.Column))).ToArray()),
            "StatsHeadRule");
    }

    /// <summary>"34–27", the wins green and the losses red.</summary>
    private static TextBlock BuildRecordCell(int wins, int losses, int column, int columns)
    {
        var block = StatsMono("", "StatsBodySize", "StatsSoft", FontWeights.Medium);
        block.Text = null;

        var won = new Run(wins.ToString());
        won.SetResourceReference(TextElement.ForegroundProperty, "StatsWin");
        block.Inlines.Add(won);
        block.Inlines.Add(new Run("–"));
        var lost = new Run(losses.ToString());
        lost.SetResourceReference(TextElement.ForegroundProperty, "StatsLoss");
        block.Inlines.Add(lost);

        block.HorizontalAlignment = HorizontalAlignment.Right;
        block.Margin = new Thickness(0, 0, CivTrailingGap(column, columns), 0);
        return block;
    }

    private static TextBlock RightCell(TextBlock block, int column, int columns)
    {
        block.HorizontalAlignment = HorizontalAlignment.Right;
        block.Margin = new Thickness(0, 0, CivTrailingGap(column, columns), 0);
        return block;
    }

    private static string NameSizeKey(double rowHeight)
        => rowHeight >= 44 ? "StatsRowNameSize" : "StatsRowNameSizeCompact";

    /// <summary>One civilization.</summary>
    /// <remarks><c>internal static</c> so <c>DialogXamlTests</c> can build the real row - nothing
    /// else constructs it and no compile step checks a resource looked up by name.</remarks>
    /// <param name="civ">The civilization AS THE PLAYER SAW IT, never the server's internal
    /// string, which for a reskin names one nobody played.</param>
    /// <summary>The flag beside a civilization's name.</summary>
    internal const double CivFlagSize = 20;

    /// <summary>A game texture at the size it is shown. HighQuality because they are 128x128
    /// images coming down to under thirty pixels, which alias into mush otherwise.</summary>
    internal static Image ArtImage(ImageSource source, double size)
    {
        var image = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    internal static FrameworkElement BuildCivRow(
        string civ, int played, int wins, int losses, int? avgSeconds, double rowHeight = 40,
        ImageSource? flag = null)
    {
        int columns = CivTableLayout.All.Count;
        var grid = BuildCivGrid();
        grid.MinHeight = rowHeight;

        var nameCell = new Grid { Margin = new Thickness(0, 0, CivTrailingGap(0, columns), 0) };
        var name = StatsText(civ, NameSizeKey(rowHeight), "StatsStrong", FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        if (flag != null)
        {
            nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameCell.ColumnDefinitions.Add(new ColumnDefinition());
            var picture = ArtImage(flag, CivFlagSize);
            picture.Margin = new Thickness(0, 0, 8, 0);
            nameCell.Children.Add(picture);
            Grid.SetColumn(name, 1);
        }
        nameCell.Children.Add(name);
        grid.Children.Add(WithColumn(nameCell, 0));

        grid.Children.Add(WithColumn(RightCell(
            StatsMono(played.ToString(), "StatsBodySize", "StatsFigure", FontWeights.SemiBold),
            1, columns), 1));
        grid.Children.Add(WithColumn(BuildRecordCell(wins, losses, 2, columns), 2));

        // The SAME rule the Profile uses, so the two can never disagree about when there is
        // enough behind a civilization to state a rate.
        var pct = CivStatsView.WinPercent(new CivStatRow(civ, played, wins, losses));

        // The bar's cell and not the bar carries the balloon: six pixels is not something
        // anybody can aim at, and a tooltip fires only over what is hit.
        int decided = wins + losses;
        var barCell = new Grid
        {
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, CivTrailingGap(3, columns), 0),
            ToolTip = TooltipHelper.Wrap(pct != null
                ? Strings.Format("MpStatsBarTip", wins, decided)
                : Strings.Format("MpStatsBarTipNoSample", decided,
                                 CivStatsView.MinDecidedForPercent)),
        };
        barCell.Children.Add(BuildWinBar(wins, decided, pct != null));
        grid.Children.Add(WithColumn(barCell, 3));

        // Nothing is drawn where there is no percentage: not an em dash, and never a 0.
        var percent = RightCell(StatsMono(
            pct == null ? "" : pct.Value + " %",
            "StatsBodySize",
            pct == null ? "StatsLabel" : RankingTableLayout.PercentBrushKey(pct.Value),
            FontWeights.SemiBold), 4, columns);
        if (pct == null)
        {
            percent.ToolTip = TooltipHelper.Wrap(Strings.Format("MpStatsPctTip",
                CivStatsView.MinDecidedForPercent));
        }
        grid.Children.Add(WithColumn(percent, 4));

        grid.Children.Add(WithColumn(RightCell(StatsMono(
            avgSeconds is > 0
                ? Strings.Format("MpResultMinutes", Math.Max(1, avgSeconds.Value / 60))
                : "",
            "StatsProseSize", "StatsProse", FontWeights.Normal), 5, columns), 5));

        return StatsRule(grid);
    }

    // ---------------------------------------------------------------- matchups and allies

    /// <summary>What a pairs table is showing: one civilization's pairs, or all of them.</summary>
    private sealed class PairTableState
    {
        public string? Civ;
        public bool Expanded;
    }

    private readonly PairTableState _rivalsTable = new();
    private readonly PairTableState _alliesTable = new();

    /// <summary>A new mod or mode is a different set of civilizations: a kept choice would mean nothing.</summary>
    private void ResetPairTables()
    {
        _rivalsTable.Civ = null;
        _rivalsTable.Expanded = false;
        _alliesTable.Civ = null;
        _alliesTable.Expanded = false;
    }

    private UIElement? BuildAlliesCard()
        => _matchups?.Allies is { Count: > 0 } rows
            ? BuildPairPanel(rows, "MpStatsAlliesTitle", "MpStatsAlliesScope",
                             "MpStatsAlliesHint", "+", "MpStatsAlliesColPair", _alliesTable, sameSide: true)
            : null;

    private UIElement? BuildPairCard(List<Models.Multiplayer.MatchupEntry>? rows, string hintKey)
    {
        if (StatsLoading)
        {
            var stack = new StackPanel();
            stack.Children.Add(StatsHead(Strings.Get("MpStatsMatchupsTitle"), null, 10));
            stack.Children.Add(StatsSkeletonRows(3));
            return StatsPanel(stack, 10);
        }

        return rows is { Count: > 0 }
            ? BuildPairPanel(rows, "MpStatsMatchupsTitle", "MpStatsMatchupsScope", hintKey,
                             Strings.Get("MpMatchupVs"), "MpMatchupColPair", _rivalsTable, sameSide: false)
            : null;
    }

    /// <summary>
    /// A pairs table: the <see cref="MatchupListView.TopCount"/> most played, a civilization
    /// picker, and the rest on request in a box that scrolls on its own, so the card is never
    /// taller than <see cref="MatchupListView.BoxRows"/> rows. It used to draw every pair the
    /// server sent (up to 600, ~25,000 px), which made the page endless and cost most of the
    /// second a switch to the subtab took. The picker and the fold redraw this card only.
    /// </summary>
    private Border BuildPairPanel(
        List<Models.Multiplayer.MatchupEntry> rows, string titleKey, string scopeKey,
        string hintKey, string separator, string columnKey, PairTableState state, bool sameSide)
    {
        var lines = MatchupListView.Merge(rows, sameSide);
        var names = DeckCardNames.Peek(StatsModId());
        // A civilization the data no longer mentions (another mode, a refresh) is forgotten.
        if (state.Civ != null && !MatchupListView.Civs(lines).Contains(state.Civ, StringComparer.Ordinal))
            state.Civ = null;

        var card = StatsPanel(new StackPanel(), 10);
        void Fill()
        {
            var stack = new StackPanel();
            stack.Children.Add(StatsHead(Strings.Get(titleKey), Strings.Get(scopeKey), 10));

            // The picker only when there is more than the top to choose from.
            if (lines.Count > MatchupListView.TopCount)
                stack.Children.Add(BuildPairCivPicker(lines, state, Fill));

            stack.Children.Add(BuildMatchupHeader(columnKey));

            var shown = state.Civ != null
                ? MatchupListView.For(lines, state.Civ, sameSide)
                : state.Expanded ? lines : MatchupListView.Top(lines);
            var list = new StackPanel();
            foreach (var line in shown)
            {
                list.Children.Add(BuildMatchupRow(
                    line.ToEntry(), StatsCivLabel(line.CivA), StatsCivLabel(line.CivB), separator,
                    _statsLayout.RowHeight, names?.CivIconOf(line.CivA), names?.CivIconOf(line.CivB)));
            }

            if (shown.Count > MatchupListView.BoxRows)
            {
                var box = new ScrollViewer
                {
                    Content = list,
                    // A row is its height plus the 1-px rule under it.
                    MaxHeight = MatchupListView.BoxRows * (_statsLayout.RowHeight + 1),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                };
                AutomationProperties.SetAutomationId(box, "StatsPairsBox");
                ChainWheelAtTheEnds(box);
                stack.Children.Add(box);
            }
            else stack.Children.Add(list);

            if (state.Civ == null && lines.Count > MatchupListView.TopCount)
            {
                stack.Children.Add(StatsFoldRow(
                    state.Expanded
                        ? Strings.Format("MpStatsPairsAllShown", lines.Count)
                        : Strings.Format("MpStatsPairsTopShown", MatchupListView.TopCount, lines.Count),
                    state.Expanded ? Strings.Get("MpStatsSeeLess") : Strings.Format("MpStatsPairsSeeAll", lines.Count),
                    () =>
                    {
                        state.Expanded = !state.Expanded;
                        Fill();
                    }));
            }

            stack.Children.Add(StatsTableNote(Strings.Get(hintKey)));
            card.Child = stack;
        }

        Fill();
        return card;
    }

    /// <summary>"All civilizations" or one of them, by the name the player reads.</summary>
    private FrameworkElement BuildPairCivPicker(IReadOnlyList<MatchupLine> lines, PairTableState state, Action refill)
    {
        var combo = new ComboBox
        {
            MinWidth = 220,
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        if (TryFindResource("SetCompactCombo") is Style style) combo.Style = style;
        AutomationProperties.SetAutomationId(combo, "StatsPairsCiv");
        AutomationProperties.SetName(combo, Strings.Get("MpStatsPairsAllCivs"));

        combo.Items.Add(new ComboBoxItem { Content = Strings.Get("MpStatsPairsAllCivs"), Tag = null });
        foreach (var civ in MatchupListView.Civs(lines)
                     .Select(c => (Civ: c, Label: StatsCivLabel(c)))
                     .OrderBy(c => c.Label, StringComparer.Create(Strings.Culture, ignoreCase: true)))
        {
            combo.Items.Add(new ComboBoxItem { Content = civ.Label, Tag = civ.Civ });
        }
        combo.SelectedIndex = 0;
        for (int i = 1; i < combo.Items.Count; i++)
            if (combo.Items[i] is ComboBoxItem { Tag: string tag } && tag == state.Civ) combo.SelectedIndex = i;

        combo.SelectionChanged += (_, _) =>
        {
            state.Civ = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            refill();
        };
        return combo;
    }

    /// <summary>
    /// A box inside the scrolling page swallows the wheel at its ends, which strands the reader
    /// inside it. At the top or the bottom the turn goes to the page instead.
    /// </summary>
    private static void ChainWheelAtTheEnds(ScrollViewer box)
    {
        box.PreviewMouseWheel += (_, e) =>
        {
            bool atTop = box.VerticalOffset <= 0 && e.Delta > 0;
            bool atBottom = box.VerticalOffset >= box.ScrollableHeight && e.Delta < 0;
            if (!atTop && !atBottom) return;
            e.Handled = true;
            if (box.Parent is UIElement parent)
            {
                parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = box,
                });
            }
        };
    }

    /// <summary>One pairing.</summary>
    /// <remarks><c>internal static</c> so <c>DialogXamlTests</c> can build the real row.</remarks>
    internal static FrameworkElement BuildMatchupRow(
        Models.Multiplayer.MatchupEntry row,
        string? labelA = null,
        string? labelB = null,
        string? separator = null,
        double rowHeight = 40,
        ImageSource? flagA = null,
        ImageSource? flagB = null)
    {
        var specs = CivTableLayout.Matchups;
        var grid = BuildCivGrid(specs);
        grid.MinHeight = rowHeight;

        // The record belongs to the FIRST of the two, so the pair has to read in that order:
        // "A vs B" with B's record is the same numbers meaning the opposite.
        var nameA = string.IsNullOrWhiteSpace(labelA) ? row.CivA : labelA!;
        var nameB = string.IsNullOrWhiteSpace(labelB) ? row.CivB : labelB!;

        // One TextBlock, because the cell has to trim: a horizontal StackPanel measures its
        // children at infinite width and an ellipsis never fires.
        var pair = StatsText("", NameSizeKey(rowHeight), "StatsStrong", FontWeights.SemiBold);
        pair.Text = null;
        void Flag(ImageSource? flag)
        {
            if (flag == null) return;
            var picture = ArtImage(flag, CivFlagSize - 2);
            picture.Margin = new Thickness(0, 0, 6, 0);
            pair.Inlines.Add(new InlineUIContainer(picture) { BaselineAlignment = BaselineAlignment.Center });
        }
        Flag(flagA);
        pair.Inlines.Add(new Run(nameA));
        var between = new Run($" {separator ?? Strings.Get("MpMatchupVs")} ")
        {
            FontWeight = FontWeights.Normal,
        };
        between.SetResourceReference(TextElement.ForegroundProperty, "StatsNote");
        pair.Inlines.Add(between);
        Flag(flagB);
        pair.Inlines.Add(new Run(nameB));
        pair.TextTrimming = TextTrimming.CharacterEllipsis;
        // Hidden past the ellipsis: WPF trims runs but goes on drawing what sits between them.
        pair.SizeChanged += (_, _) => InlineFlagFit.Apply(pair);
        pair.Loaded += (_, _) => InlineFlagFit.Apply(pair);
        pair.Margin = new Thickness(0, 0, CivTrailingGap(0, specs.Count), 0);
        grid.Children.Add(WithColumn(pair, 0));

        grid.Children.Add(WithColumn(RightCell(
            StatsMono(row.Played.ToString(), "StatsBodySize", "StatsFigure", FontWeights.SemiBold),
            1, specs.Count), 1));
        grid.Children.Add(WithColumn(BuildRecordCell(row.WinsA, row.LossesA, 2, specs.Count), 2));

        // The same bar as the table beside it, through the same method. Below it nothing is
        // drawn: not an em dash, never a 0.
        var pct = CivStatsView.WinPercent(new CivStatRow(row.CivA, row.Played, row.WinsA, row.LossesA));
        grid.Children.Add(WithColumn(RightCell(StatsMono(
            pct == null ? "" : pct.Value + " %",
            "StatsBodySize",
            pct == null ? "StatsLabel" : RankingTableLayout.PercentBrushKey(pct.Value),
            FontWeights.SemiBold), 3, specs.Count), 3));

        return StatsRule(grid);
    }

    // ---------------------------------------------------------------- activity

    private const int ActivityDays = 30;

    private UIElement BuildActivityCard()
    {
        var stack = new StackPanel();
        var totals = _statsCommunity?.Totals;
        int windowDays = totals?.WindowDays is > 0 ? totals.WindowDays : ActivityDays;

        stack.Children.Add(StatsHead(Strings.Get("MpStatsActivityTitle"),
                                     Strings.Format("MpStatsWindowDays", windowDays), 12));

        if (StatsLoading)
        {
            stack.Children.Add(StatsSkeleton(180, 26, 0x14));
            var chart = StatsSkeleton(double.NaN, 44, 0x0D);
            chart.HorizontalAlignment = HorizontalAlignment.Stretch;
            chart.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(chart);
            return StatsPanel(stack);
        }

        if (totals == null) return StatsPanel(stack);

        var figure = new StackPanel { Orientation = Orientation.Horizontal };
        var count = StatsText(totals.Matches.ToString("N0", Strings.Culture),
                              _statsLayout.Wide ? "StatsFigureSize" : "StatsFigureSizeCompact",
                              "StatsStrong", FontWeights.SemiBold);
        figure.Children.Add(count);
        var unit = StatsText(Strings.Get("MpStatsActivityMatches"), "StatsBodySize", "StatsProse",
                             FontWeights.Normal);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(8, 0, 0, 5);
        figure.Children.Add(unit);
        stack.Children.Add(figure);

        var days = DayBars(totals.MatchesPerDay, windowDays);
        if (days != null)
        {
            days.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(days);
        }

        var facts = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (totals.Rated is int rated)
        {
            facts.Children.Add(ActivityFact(Strings.Get("MpStatsActivityRated"), rated));

            // In the words of the server when the launcher knows no better: it is an identifier
            // somebody can grep for, and a sentence invented for it would say less.
            if (!string.IsNullOrWhiteSpace(totals.UnratedTopReason)
                && totals.UnratedTopReasonMatches is int n && n > 0)
            {
                facts.Children.Add(ActivityFact(UnratedReasonText(totals.UnratedTopReason!), n));
            }
        }
        if (totals is { Players: > 0, PlayersWindowDays: > 0 })
        {
            facts.Children.Add(ActivityFact(
                Strings.Format("MpStatsActivePlayers", totals.PlayersWindowDays), totals.Players));
        }
        if (facts.Children.Count > 0) stack.Children.Add(facts);

        var peak = BuildPeakHours(windowDays);
        if (peak != null) stack.Children.Add(peak);

        return StatsPanel(stack);
    }

    private static string UnratedReasonText(string reason)
        => reason == "no_decided_result"
            ? Strings.Get("MpStatsActivityNoResult")
            : Strings.Format("MpStatsActivityUnrated", reason);

    private static Grid ActivityFact(string label, int value)
    {
        var row = new Grid { MinHeight = 22, Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var words = StatsProse(label);
        words.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(words);
        row.Children.Add(WithColumn(
            StatsMono(value.ToString("N0", Strings.Culture), "StatsBodySize", "StatsFigure",
                      FontWeights.SemiBold), 1));
        return row;
    }

    /// <summary>
    /// A row of bars of one height, bottom aligned, each a share of the tallest. Days and hours
    /// are the same drawing with different colours.
    /// </summary>
    private static Grid BarChart(IReadOnlyList<int> values, double height, Func<int, int, string> ink)
    {
        var chart = new Grid { Height = height, Background = Brushes.Transparent };
        int max = Math.Max(1, values.Max());

        for (int i = 0; i < values.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border
            {
                Margin = new Thickness(i == 0 ? 0 : 1, 0, i == values.Count - 1 ? 0 : 1, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                // At least a sliver: a day nobody played is part of the axis, not a hole in it.
                Height = Math.Max(2, Math.Round(values[i] * height / max)),
            };
            bar.SetResourceReference(Border.BackgroundProperty, ink(i, values[i]));
            chart.Children.Add(WithColumn(bar, i));
        }

        return chart;
    }

    /// <summary>The matches of each day of the window, oldest first. The server leaves out a
    /// day with nothing in it, so the days are walked rather than the rows.</summary>
    private static Grid? DayBars(List<Models.Multiplayer.DayCount>? perDay, int windowDays)
    {
        if (perDay == null || perDay.Count == 0) return null;

        var byDay = perDay.GroupBy(d => d.Day, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Matches), StringComparer.Ordinal);
        var today = DateTime.UtcNow.Date;
        var values = new int[windowDays];
        for (int i = 0; i < windowDays; i++)
        {
            string key = today.AddDays(i - windowDays + 1).ToString("yyyy-MM-dd");
            values[i] = byDay.TryGetValue(key, out var n) ? n : 0;
        }

        var chart = BarChart(values, 44, (_, v) => v == 0 ? "StatsEmptyBar" : "StatsAccentBar");
        chart.ToolTip = TooltipHelper.Wrap(Strings.Format("MpStatsDaysTip", windowDays));
        return chart;
    }

    /// <summary>
    /// When the rooms open, in the viewer's own hours. Under the sample the hours are not drawn
    /// and the card says why: the tallest of twenty-four bars over a dozen rooms is noise.
    /// </summary>
    private UIElement? BuildPeakHours(int windowDays)
    {
        var activity = _statsCommunity?.Activity;
        if (activity?.Hours == null || activity.Hours.Count == 0) return null;

        var utc = new int[24];
        foreach (var hour in activity.Hours)
            if (hour.Hour is >= 0 and < 24) utc[hour.Hour] += hour.Count;

        var local = CommunityStatsView.ToLocalHours(
            utc, TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
        int rooms = local.Sum();
        int? start = CommunityStatsView.PeakWindow(local, rooms);

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 10) };
        rule.SetResourceReference(Border.BackgroundProperty, "StatsRowRule");
        stack.Children.Add(rule);

        if (start == null)
        {
            stack.Children.Add(StatsProse(
                Strings.Format("MpStatsPeakNoSample", CommunityStatsView.MinSampleRooms, windowDays),
                "StatsNoteSize", "StatsNote"));
            return stack;
        }

        int first = start.Value;
        bool InPeak(int hour)
        {
            for (int i = 0; i < CommunityStatsView.PeakWindowHours; i++)
                if ((first + i) % 24 == hour) return true;
            return false;
        }

        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = StatsText(Strings.Get("MpStatsPeakTitle").ToUpper(Strings.Culture),
                              "StatsHeaderSize", "StatsNote", FontWeights.Bold);
        label.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(label);
        head.Children.Add(WithColumn(StatsMono(
            $"{first:00}:00–{(first + CommunityStatsView.PeakWindowHours) % 24:00}:00",
            "StatsBodySize", "StatsStrong", FontWeights.SemiBold), 1));
        stack.Children.Add(head);

        var chart = BarChart(local, 30, (hour, value) => value == 0
            ? "StatsEmptyBar"
            : InPeak(hour) ? "MpAction" : "StatsDimBar");
        chart.ToolTip = TooltipHelper.Wrap(Strings.Get("MpStatsHoursTip"));
        stack.Children.Add(chart);

        var axis = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        string[] marks = { "0 h", "6 h", "12 h", "18 h", "24 h" };
        for (int i = 0; i < marks.Length; i++)
        {
            axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (i < marks.Length - 1) axis.ColumnDefinitions.Add(new ColumnDefinition());
            axis.Children.Add(WithColumn(
                StatsMono(marks[i], "StatsHeaderSize", "StatsLabel", FontWeights.Normal), i * 2));
        }
        stack.Children.Add(axis);

        var caption = StatsProse(Strings.Format("MpStatsPeakCaption", rooms, windowDays),
                                 "StatsNoteSize", "StatsNote");
        caption.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(caption);
        return stack;
    }

    // ---------------------------------------------------------------- maps

    /// <summary>The map rows the server sent for this mod.</summary>
    private List<(string Map, int Matches)> MapRows()
        => (_statsCommunity?.Totals?.TopMaps ?? new List<Models.Multiplayer.MapCount>())
            .Select(m => (m.Map ?? "", m.Matches))
            .ToList();

    private UIElement BuildMapCard()
    {
        var all = MapRows();
        int named = all.Sum(m => m.Matches);

        var stack = new StackPanel();
        stack.Children.Add(StatsHead(
            Strings.Get("MpStatsMapsTitle"),
            all.Count > 0 ? Strings.Format("MpStatsMapsOver", named) : null,
            6));

        if (StatsLoading)
        {
            stack.Children.Add(StatsSkeletonRows(2));
            return StatsPanel(stack, 10);
        }

        if (all.Count == 0)
        {
            var nothing = StatsProse(Strings.Get("MpStatsMapsEmpty"));
            nothing.Margin = new Thickness(0, 4, 0, 6);
            stack.Children.Add(nothing);
            return StatsPanel(stack, 10);
        }

        // Folded only when there are at least two: a fold that hides one row hides nothing.
        var once = all.Where(m => m.Matches < MapRowMinMatches).ToList();
        bool foldable = once.Count >= 2;
        var shown = foldable && !_statsMapsExpanded
            ? all.Where(m => m.Matches >= MapRowMinMatches).ToList()
            : all;

        int max = all.Max(m => m.Matches);
        for (int i = 0; i < shown.Count; i++)
            stack.Children.Add(BuildMapRow(i + 1, shown[i].Map, shown[i].Matches, max));

        if (foldable)
        {
            stack.Children.Add(StatsFoldRow(
                Strings.Format(_statsMapsExpanded ? "MpStatsTailMapsOpen" : "MpStatsTailMaps",
                               once.Count),
                Strings.Get(_statsMapsExpanded ? "MpStatsSeeLess" : "MpStatsSeeAll"),
                () =>
                {
                    _statsMapsExpanded = !_statsMapsExpanded;
                    RenderStatsTab();
                }));
        }

        int unnamed = (_statsCommunity?.Totals?.Matches ?? 0) - named;
        if (unnamed > 0)
        {
            stack.Children.Add(StatsTableNote(Strings.Format(
                unnamed == 1 ? "MpStatsNoMapOne" : "MpStatsNoMapMany", unnamed)));
        }

        return StatsPanel(stack, 10);
    }

    /// <summary>A rank, the map with its pack as a tag, a proportional bar and the count.</summary>
    internal UIElement BuildMapRow(int rank, string rawName, int count, int max)
    {
        var (name, pack) = LocalMatchView.MapLabel(rawName);

        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        grid.Children.Add(StatsMono(rank.ToString(), "StatsNoteSize",
            rank == 1 ? "MpActionText" : "StatsNote", FontWeights.SemiBold));

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mapName = StatsText(name, "StatsBodySize", "StatsStrong", FontWeights.SemiBold);
        mapName.TextTrimming = TextTrimming.CharacterEllipsis;
        // An Auto column measures at infinity and an ellipsis never fires, so the ceiling goes on
        // the text: the card's width less its padding, the rank, the count and the pack's tag.
        mapName.MaxWidth = Math.Max(60, _statsLayout.RightColumn - 34 - 78 - (pack != null ? 58 : 0));
        title.Children.Add(mapName);

        if (pack != null)
        {
            var tag = new Border
            {
                Height = 16,
                Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(7, 0, 0, 0),
                Child = StatsText(pack, "StatsTagSize", "StatsProse", FontWeights.Bold),
                ToolTip = TooltipHelper.Wrap(Strings.Get("MpStatsMapPackTip")),
            };
            tag.SetResourceReference(Border.BackgroundProperty, "StatsTagFill");
            title.Children.Add(WithColumn(tag, 1));
        }
        middle.Children.Add(title);
        middle.Children.Add(BuildProportionBar(count, max, rank == 1));
        grid.Children.Add(WithColumn(middle, 1));

        grid.Children.Add(WithColumn(RightCell(
            StatsMono(count.ToString("N0", Strings.Culture), "StatsBodySize", "StatsFigure",
                      FontWeights.SemiBold), 0, 1), 2));

        return StatsRule(grid);
    }

    private static UIElement BuildProportionBar(int value, int max, bool leader)
    {
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "StatsTrack");

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(1, value), GridUnitType.Star),
        });
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0, max - value), GridUnitType.Star),
        });

        var fill = new Border { Opacity = leader ? 1 : .72 };
        fill.SetResourceReference(Border.BackgroundProperty, "MpAction");
        host.Children.Add(fill);

        track.Child = host;
        return track;
    }

    // ---------------------------------------------------------------- decks

    /// <summary>
    /// What people BRING with one civilization. "Bring", never "play": a deck holds 25 cards and
    /// a match may use five, and no recording carries the card that was played.
    /// </summary>
    private UIElement? BuildDeckCard()
    {
        var stack = new StackPanel();
        string title = Strings.Get("MpStatsCommunityDecksTitle");

        if (StatsLoading)
        {
            stack.Children.Add(StatsHead(title, null, 12));
            var pills = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (double width in new double[] { 96, 120, 84, 70 })
            {
                var pill = StatsSkeleton(width, 32, 0x12);
                pill.Margin = new Thickness(0, 0, 7, 0);
                pills.Children.Add(pill);
            }
            stack.Children.Add(pills);
            return StatsPanel(stack);
        }

        // Nothing arrived at all: a backend without the route, which is not "nobody shared one".
        if (_deckStats == null) return null;

        var rows = _deckStats.Cards;
        var names = rows == null ? null : DeckCardNames.Peek(StatsModId());
        var vocabulary = names ?? DeckCardNames.Vocabulary.None;
        var groups = rows == null
            ? Array.Empty<DeckCivGroup>()
            : DeckStatsView.Group(rows, vocabulary.NameOf, vocabulary.CivOf, _deckTailsOpen);
        var group = DeckStatsView.PickDefault(groups, _deckCivSelected, PreferredDeckCiv());

        if (group == null)
        {
            stack.Children.Add(StatsHead(title, null, 12));
            stack.Children.Add(StatsProse(
                Strings.Format("MpStatsDecksEmptyBody", ResolveModDisplayName(StatsModId())),
                "StatsBodySize", "StatsSoft"));
            return StatsPanel(stack);
        }

        _ = EnsureDeckNamesAsync(rows!);

        int people = _deckStats.Contributors;
        stack.Children.Add(StatsHead(
            title,
            people > 0
                ? Strings.Format(people == 1 ? "MpStatsDecksScopeOne" : "MpStatsDecksScope", people)
                : null,
            12));
        stack.Children.Add(BuildDeckCivPills(groups, group, vocabulary));

        bool sampled = group.Decks >= DeckStatsView.MinDecksForPercent;
        if (!sampled && group.Bands.Count == 0)
        {
            var under = StatsProse(
                Strings.Format(group.Decks == 1 ? "MpStatsDecksUnderOne" : "MpStatsDecksUnderMany",
                               group.Decks, group.CivLabel, DeckStatsView.MinDecksForPercent)
                + " "
                + Strings.Format(group.DistinctCards == 1
                                     ? "MpStatsDecksDistinctOne"
                                     : "MpStatsDecksDistinctMany",
                                 group.DistinctCards),
                "StatsBodySize", "StatsSoft");
            under.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(under);
        }
        else
        {
            foreach (var band in group.Bands)
                stack.Children.Add(StatsBandRow(
                    BuildDeckBand(band, vocabulary, group.CivLabel, group.Decks, group.Civ,
                                  consensus: false, first: false)));
            stack.Children.Add(StatsBandRow(BuildDeckTailRow(group)));
        }

        var note = StatsProse(Strings.Get("MpStatsDecksNote"), "StatsNoteSize", "StatsNote");
        note.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(note);

        // Said whenever ANY card is still an identifier, not only when every one of them is.
        int raw = groups
            .SelectMany(g => g.Shown.Concat(g.Tail))
            .Count(r => names == null || string.Equals(r.Label, r.Card, StringComparison.Ordinal));
        if (raw > 0)
        {
            var unresolved = StatsProse(
                Strings.Get(names == null || !names.Resolved
                    ? "MpStatsDecksNotResolved"
                    : "MpStatsDecksPartlyResolved"),
                "StatsNoteSize", "StatsNote");
            unresolved.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(unresolved);
        }

        return StatsPanel(stack);
    }

    /// <summary>A rule above, and the band's breath under it.</summary>
    private static Border StatsBandRow(UIElement content)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = content,
        };
        row.SetResourceReference(Border.BorderBrushProperty, "StatsBandRule");
        return row;
    }

    /// <summary>The civilization strip: the open one lit, the rest one click away.</summary>
    internal FrameworkElement BuildDeckCivPills(
        IReadOnlyList<DeckCivGroup> groups,
        DeckCivGroup selected,
        DeckCardNames.Vocabulary vocabulary)
    {
        var (shown, hidden) = DeckStatsView.CivPills(
            groups,
            selected.Civ,
            _deckCivsExpanded ? 0 : DeckStatsView.MaxCivPills);

        var row = new WrapPanel { Margin = new Thickness(0, 0, -7, -7) };

        foreach (var group in shown)
        {
            row.Children.Add(BuildDeckCivPill(
                group, string.Equals(group.Civ, selected.Civ, StringComparison.Ordinal)));
        }

        if (hidden > 0)
        {
            var more = new Button { Content = Strings.Format("MpStatsDecksMorePills", hidden) };
            more.SetResourceReference(FrameworkElement.StyleProperty, "StatsCivChip");
            more.Click += (_, _) => { _deckCivsExpanded = true; RenderStatsTab(); };
            row.Children.Add(more);
        }

        return row;
    }

    private FrameworkElement BuildDeckCivPill(DeckCivGroup group, bool open)
    {
        var inner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (DeckCardNames.Peek(StatsModId())?.CivIconOf(group.Civ) is { } civFlag)
        {
            var picture = ArtImage(civFlag, 18);
            picture.Margin = new Thickness(0, 0, 7, 0);
            inner.Children.Add(picture);
        }

        // No Foreground of its own: the style's hover and active triggers reach the text only
        // through the ContentPresenter, and a local value here would beat every one of them.
        inner.Children.Add(new TextBlock
        {
            Text = group.CivLabel,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var decks = StatsMono(group.Decks.ToString(), "StatsDeckCountSize",
                              open ? "StatsChipCountOn" : "StatsNote", FontWeights.Medium);
        decks.Margin = new Thickness(8, 0, 0, 0);
        inner.Children.Add(decks);

        var pill = new Button
        {
            Content = inner,
            // "active" is what the trigger reads; the internal name rides on the automation name
            // for the tests, which have nothing else to identify a pill by.
            Tag = open ? "active" : null,
        };
        pill.SetResourceReference(FrameworkElement.StyleProperty, "StatsCivChip");
        System.Windows.Automation.AutomationProperties.SetName(pill, group.Civ);

        var civ = group.Civ;
        pill.Click += (_, _) =>
        {
            if (string.Equals(_deckCivSelected, civ, StringComparison.Ordinal)) return;
            _deckCivSelected = civ;
            RenderStatsTab();
        };
        return pill;
    }

    /// <summary>
    /// One band: "83 % en 5 de los 6 mazos", and the cards under it.
    ///
    /// <para>Null below the sample minimum, and then the phrase states the count alone: "en 2 de
    /// los 3" says everything a percentage would and claims nothing a sample of three cannot
    /// support.</para>
    /// </summary>
    /// <remarks><c>internal static</c> so the tests can build the real band.</remarks>
    internal static FrameworkElement BuildDeckBand(
        DeckBand band,
        DeckCardNames.Vocabulary vocabulary,
        string civLabel,
        int decks,
        string? civ,
        bool consensus,
        bool first)
    {
        var panel = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());

        if (band.Percent != null)
        {
            var percent = StatsMono(Strings.Format("MpStatsDecksBandPercent", band.Percent.Value),
                                    "StatsBandSize", "StatsStrong", FontWeights.SemiBold);
            percent.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(WithColumn(percent, 0));
        }

        head.Children.Add(WithColumn(StatsText(
            band.Players >= decks
                ? Strings.Format("MpStatsDecksBandEvery", decks)
                : Strings.Format("MpStatsDecksBandOf", band.Players, decks),
            "StatsProseSize", "StatsProse", FontWeights.Normal), 1));
        panel.Children.Add(head);

        // Each card carries its gap right and below; the panel's negative margin takes it back,
        // so the gap is BETWEEN cards and the band ends flush with its heading.
        var cards = new WrapPanel { Margin = new Thickness(0, 0, -6, -6) };
        foreach (var card in band.Cards)
            cards.Children.Add(BuildDeckTile(card, vocabulary, consensus, civ));
        panel.Children.Add(cards);

        return panel;
    }

    /// <summary>
    /// One card: its name, with the shipment number in a tag when it has one. What the card does
    /// goes in the balloon, and a card with nothing to say shows only its name there.
    /// </summary>
    /// <remarks>The number comes from the mod's own <c>homecity&lt;civ&gt;.xml</c>, and the
    /// civilization is part of the key: the same card ships a different count for different
    /// civilizations.</remarks>
    /// <summary>The card's picture in a community deck; the tile grows to hold it.</summary>
    internal const double DeckTileArtSize = 26;

    internal static FrameworkElement BuildDeckTile(
        DeckCardRow row,
        DeckCardNames.Vocabulary vocabulary,
        bool consensus,
        string? civ = null)
    {
        var lines = vocabulary.DescriptionLinesOf(row.Card);
        var tip = lines.Count == 0
            ? row.Label
            : row.Label + "\n" + string.Join("\n", lines);

        var shipment = vocabulary.ShipmentOf(civ, row.Card).Count;
        bool hasNumber = shipment is > 0;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var art = vocabulary.IconOf(row.Card);
        if (art != null)
        {
            var picture = ArtImage(art, DeckTileArtSize);
            picture.Margin = new Thickness(0, 0, 7, 0);
            content.Children.Add(picture);
        }
        if (hasNumber)
        {
            var number = new Border
            {
                MinWidth = 18,
                Height = 18,
                Padding = new Thickness(4, 0, 4, 0),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = shipment.ToString(),
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ((TextBlock)number.Child).SetResourceReference(TextBlock.FontSizeProperty, "StatsLabelSize");
            number.SetResourceReference(Border.BackgroundProperty, "StatsAccentFill");
            content.Children.Add(number);
        }
        content.Children.Add(StatsText(row.Label, "StatsProseSize", "StatsFigure", FontWeights.Medium));

        var card = new Border
        {
            Height = art != null ? 34 : 28,
            Padding = new Thickness(art != null ? 4 : hasNumber ? 5 : 10, 0, 10, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Child = content,
            // Read by the tests: once a name can be cut, the identifier is what is left.
            Tag = row.Card,
            ToolTip = TooltipHelper.Wrap(tip),
        };
        card.SetResourceReference(Border.BackgroundProperty, "StatsChipFill");
        card.SetResourceReference(Border.BorderBrushProperty, "StatsChipRim");
        ToolTipService.SetInitialShowDelay(card, 0);
        ToolTipService.SetShowDuration(card, 30_000);
        return card;
    }

    /// <summary>The fold under the bands, and the sentence for when there is nothing in it.</summary>
    private FrameworkElement BuildDeckTailRow(DeckCivGroup group)
    {
        bool open = _deckTailsOpen.Contains(group.Civ);
        int folded = open
            ? group.Bands.Where(b => b.Players <= DeckStatsView.TailMaxPlayers)
                .Sum(b => b.Cards.Count)
            : group.Tail.Count;

        string text = folded == 0
            ? Strings.Format("MpStatsTailDecksNone", DeckStatsView.TailMaxPlayers + 1)
            : open
                ? Strings.Format("MpStatsTailDecksOpen", folded)
                : Strings.Format(folded == 1 ? "MpStatsTailDecksOne" : "MpStatsTailDecks", folded);

        var row = StatsFoldRow(
            text,
            folded == 0 ? null : Strings.Get(open ? "MpStatsSeeLess" : "MpStatsSeeAllCivs"),
            () =>
            {
                if (open) _deckTailsOpen.Remove(group.Civ);
                else _deckTailsOpen.Add(group.Civ);
                RenderStatsTab();
            });
        row.MinHeight = 0;
        row.Margin = new Thickness(0);
        return row;
    }
}
