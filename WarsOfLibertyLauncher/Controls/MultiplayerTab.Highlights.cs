using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The month's highlights under the rooms list (design 55l): who climbed the most, who played
/// the most, and the best streak of the month, from <c>monthly_highlights</c> in
/// <c>/stats/community</c>. The server picks the players; <see cref="HighlightsView"/> picks
/// which ladder a cell names and whether the card is drawn; this draws it.
///
/// <para><b>Its place in the column.</b> Its own row between the list and the community panel,
/// and it gives way before the rooms do: <see cref="RoomsActivityLayout.HighlightsFit"/> shows
/// it only when the list still keeps its header and two rows beside it and the panel's folded
/// strip. Whether it is drawn is recorded on <c>HighlightsHost.Tag</c>, as the panel's is on
/// <c>ActivityStrip.Tag</c>, and <see cref="ApplyActivityLayout"/> decides the rest.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Last month is on screen ("Ver septiembre" pressed).</summary>
    private bool _highlightsShowPrevious;

    /// <summary>Below this width the three cells stack (55l: "por debajo de 600 px").</summary>
    internal const double HighlightsStackBelow = 600;

    /// <summary>The <c>Tag</c> of each highlight cell, and of the card, for the tests.</summary>
    internal const string HighlightsCellTag = "HighlightsCell";

    internal const string HighlightsCardTag = "HighlightsCard";

    /// <summary>The cells' grid, re-laid when the card crosses <see cref="HighlightsStackBelow"/>.</summary>
    private Grid? _highlightCells;

    private bool _highlightsSizeHooked;

    /// <summary>Paints the card from <c>_communityStats</c>. Called with the activity strip.</summary>
    private void RenderHighlights()
    {
        if (HighlightsHost == null) return;
        if (!_highlightsSizeHooked)
        {
            _highlightsSizeHooked = true;
            HighlightsHost.SizeChanged += (_, e) =>
            {
                if (!e.WidthChanged || _highlightCells == null) return;
                LayOutHighlightCells(_highlightCells, HighlightsHost.ActualWidth < HighlightsStackBelow);
                // Stacked, the card is three cells tall: the column's split has to be asked again.
                QueueActivityLayout();
            };
        }

        var all = _communityStats?.MonthlyHighlights;
        var current = all?.Current;
        var previous = all?.Previous;
        var showingPrevious = _highlightsShowPrevious && HighlightsView.HasCells(previous);
        var shown = showingPrevious ? previous : current;
        var state = HighlightsView.StateOf(shown, isCurrent: !showingPrevious, DateTime.UtcNow);

        _highlightCells = null;
        if (state == HighlightsCardState.Hidden || shown == null)
        {
            HighlightsHost.Child = null;
            HighlightsHost.Tag = false;
            return;
        }

        // The link goes to the other month: last month from this one when it has cells, this
        // month back from last.
        MonthHighlights? other = showingPrevious ? current : HighlightsView.HasCells(previous) ? previous : null;
        HighlightsHost.Child = BuildHighlightsCard(shown, state, other, showingPrevious);
        HighlightsHost.Tag = true;
    }

    private Border BuildHighlightsCard(
        MonthHighlights month, HighlightsCardState state, MonthHighlights? other, bool showingPrevious)
    {
        var stack = new StackPanel();

        // ---- header: "DESTACADOS DE OCTUBRE · hasta hoy" … "Ver septiembre" ---------------
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var monthName = HighlightsView.MonthName(month.Month, Strings.Culture) ?? month.Month;
        title.Inlines.Add(new System.Windows.Documents.Run(Strings.Format("MpHlTitle", monthName.ToUpper(Strings.Culture)))
        {
            Foreground = (Brush)Application.Current.FindResource("MpTextLabel"),
            FontSize = (double)Application.Current.FindResource("MpHlTitleSize"),
            FontWeight = FontWeights.SemiBold,
        });
        // "so far" only while the month is still running and has its cells; the empty state is
        // the month starting, which says it already.
        if (month.SoFar && state == HighlightsCardState.Cells)
        {
            title.Inlines.Add(new System.Windows.Documents.Run("   " + Strings.Get("MpHlSub"))
            {
                Foreground = (Brush)Application.Current.FindResource("MpTextDim"),
                FontSize = (double)Application.Current.FindResource("MpHlMetaSize"),
            });
        }
        Grid.SetColumn(title, 0);
        head.Children.Add(title);

        if (other != null)
        {
            var otherName = HighlightsView.MonthName(other.Month, Strings.Culture) ?? other.Month;
            var link = new Button
            {
                Content = Strings.Format(showingPrevious ? "MpHlSeeCurrent" : "MpHlSeePrev", otherName),
                Style = (Style)Application.Current.FindResource("MpLinkButton"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            link.Click += (_, _) =>
            {
                _highlightsShowPrevious = !showingPrevious;
                RenderHighlights();
                QueueActivityLayout();
            };
            Grid.SetColumn(link, 1);
            head.Children.Add(link);
        }
        stack.Children.Add(head);

        if (state == HighlightsCardState.JustStarted)
        {
            stack.Children.Add(new TextBlock
            {
                Text = Strings.Format("MpHlEmpty", HighlightsView.MinMonthMatches),
                Foreground = (Brush)Application.Current.FindResource("MpRankMutedText"),
                FontSize = (double)Application.Current.FindResource("MpBodySize"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
        else
        {
            var (climber, climbMode) = HighlightsView.TopClimb(month);
            var (streaker, streakMode) = HighlightsView.BestStreak(month);
            var most = month.MostMatches;

            var cells = new Grid { Margin = new Thickness(0, 11, 0, 0) };
            cells.Children.Add(BuildHighlightCell(
                "MpHlTopGain", climber,
                climber?.Points is int points ? RatingDisplay.FormatDelta(points) : null, "MpOkText",
                climber != null
                    ? Strings.Format("MpHlTopGainSub", ModeName(climbMode), climber.Matches ?? 0)
                    : null));
            cells.Children.Add(BuildHighlightCell(
                "MpHlMostGames", most,
                most?.Matches?.ToString(Strings.Culture), "MpHlMostFigure",
                most != null ? Strings.Get(most.Matches == 1 ? "MpHlMostGamesSubOne" : "MpHlMostGamesSub") : null));
            cells.Children.Add(BuildHighlightCell(
                "MpHlBestStreak", streaker,
                streaker?.Wins is int wins ? "\U0001F525" + wins : null, "MpStreakText",
                streaker != null
                    ? Strings.Format(streaker.Wins == 1 ? "MpHlBestStreakSubOne" : "MpHlBestStreakSub", ModeName(streakMode))
                    : null));
            LayOutHighlightCells(cells, HighlightsHost.ActualWidth > 0 && HighlightsHost.ActualWidth < HighlightsStackBelow);
            _highlightCells = cells;
            stack.Children.Add(cells);
        }

        return new Border
        {
            Child = stack,
            Tag = HighlightsCardTag,
            Padding = new Thickness(14, 13, 14, 13),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.FindResource("MpPanel"),
            BorderBrush = (Brush)Application.Current.FindResource("MpRimFaint"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>"1v1" or "Equipos" for a ladder.</summary>
    private static string ModeName(string mode)
        => Strings.Get(mode == "team" ? "MpModeTeams" : "MpModeOneVsOne");

    /// <summary>
    /// One cell: the label, then avatar · name · figure, then a line of context. A cell with
    /// nobody to name still takes its place — three cells always, so the card's shape does not
    /// depend on the month — and says "nobody yet".
    /// </summary>
    private static Border BuildHighlightCell(
        string labelKey, HighlightPlayer? player, string? figure, string figureBrushKey, string? context)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get(labelKey),
            Foreground = (Brush)Application.Current.FindResource("MpTextMuted"),
            FontSize = (double)Application.Current.FindResource("MpSectionLabelSize"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (player != null)
        {
            // Avatar · name · figure: a Grid, so the name TRIMS (55l draws a fifty-character one
            // ending in an ellipsis) and the figure keeps its place on the right.
            var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = string.IsNullOrWhiteSpace(player.DisplayName) ? "?" : player.DisplayName;
            var avatar = BuildAvatarDisc(name, player.AvatarUrl, 26);
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            var nameText = new TextBlock
            {
                Text = name,
                Margin = new Thickness(8, 0, 8, 0),
                Foreground = (Brush)Application.Current.FindResource("MpTextHeading"),
                FontSize = (double)Application.Current.FindResource("MpHlNameSize"),
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            if (figure != null)
            {
                var figureText = new TextBlock
                {
                    Text = figure,
                    FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                    FontSize = (double)Application.Current.FindResource("MpHlFigureSize"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource(figureBrushKey),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(figureText, 2);
                row.Children.Add(figureText);
            }
            stack.Children.Add(row);
        }

        stack.Children.Add(new TextBlock
        {
            Text = context ?? Strings.Get("MpHlNobodyYet"),
            Foreground = (Brush)Application.Current.FindResource("MpTextDim"),
            FontSize = (double)Application.Current.FindResource("MpHlMetaSize"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, player != null ? 6 : 9, 0, 0),
        });

        return new Border
        {
            Child = stack,
            Tag = HighlightsCellTag,
            Padding = new Thickness(12, 11, 12, 11),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("MpHlCellBg"),
        };
    }

    /// <summary>Three equal columns with 8 px between them, or — narrower than
    /// <see cref="HighlightsStackBelow"/> — one under the other.</summary>
    private static void LayOutHighlightCells(Grid grid, bool stacked)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        var cells = grid.Children.OfType<UIElement>().ToList();
        for (var i = 0; i < cells.Count; i++)
        {
            if (stacked)
            {
                if (i > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(cells[i], grid.RowDefinitions.Count - 1);
                Grid.SetColumn(cells[i], 0);
            }
            else
            {
                if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(cells[i], grid.ColumnDefinitions.Count - 1);
                Grid.SetRow(cells[i], 0);
            }
        }
    }
}
