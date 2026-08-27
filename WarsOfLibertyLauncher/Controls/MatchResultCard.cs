using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The end-of-match card (design handoff 1f).
///
/// <para>A static factory rather than a control, because the same card has to render in
/// two hosts: the lobby window, which is where the match ended, and — when the player
/// closed that window mid-match — the multiplayer tab. Building it from one place is what
/// keeps those two from drifting into different cards.</para>
///
/// <para>Everything it shows comes from <see cref="MatchOutcomeView"/>, which is where the
/// three refusals live: a 0.5 is "no result" and never a draw, an unknown rating has no
/// delta rather than a zero one, and an undecided record shows an em dash rather than
/// 0 %. This file only paints them.</para>
/// </summary>
public static class MatchResultCard
{
    /// <summary>What the card's buttons can do, supplied by the caller.</summary>
    /// <param name="OnRematch">
    /// Recreate the room with the same mod and title. Null hides the button — the
    /// rematch has to leave the closed room before it can create one, so a caller that
    /// cannot sequence that must not offer it.
    /// </param>
    /// <param name="OnDismiss">Close the card and go back to the rooms list.</param>
    public sealed record Actions(Action? OnRematch, Action? OnDismiss);

    /// <summary>Build the card for a finished match.</summary>
    public static FrameworkElement Build(MatchOutcomeView model, Actions actions)
    {
        var root = new StackPanel();
        root.Children.Add(BuildHeadline(model));
        root.Children.Add(BuildCells(model));

        var footer = BuildFooter(model, actions);
        if (footer != null) root.Children.Add(footer);

        return root;
    }

    /// <summary>Icon, verdict, subtitle and the rating on the right.</summary>
    private static FrameworkElement BuildHeadline(MatchOutcomeView model)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var (glyph, fgKey, bgKey, titleKey) = model.Verdict switch
        {
            MatchVerdict.Win => ("✓", "MpOk", "MpOkBg", "MpResultWin"),
            MatchVerdict.Loss => ("✕", "MpDestructiveText", "MpEventBg", "MpResultLoss"),
            // Grey, and the word is "no result". The match happened; what is missing is
            // any way to know who won, which is not the same as a tie.
            _ => ("—", "MpTextFaint", "MpPanel", "MpResultNone"),
        };

        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = Brush(bgKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Brush(fgKey),
                FontSize = Size("FontSizeSubtitle"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = Strings.Get(titleKey),
            Foreground = Brush("MpTextHeading"),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = Size("MpResultTitleSize"),
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = Subtitle(model),
            Foreground = Brush("MpTextMuted"),
            FontSize = Size("MpLabelSize"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var rating = BuildRating(model);
        if (rating != null)
        {
            Grid.SetColumn(rating, 2);
            grid.Children.Add(rating);
        }
        return grid;
    }

    /// <summary>
    /// The new rating, its delta, and what it was before — or nothing at all.
    ///
    /// <para>Returns null when the server did not tell us. Showing the rating with no
    /// delta would leave the player wondering what the match did to it, and inventing a
    /// "+0" would answer that wrongly.</para>
    /// </summary>
    private static FrameworkElement? BuildRating(MatchOutcomeView model)
    {
        var delta = model.RatingDelta;
        if (delta == null || model.RatingAfter == null) return null;

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        line.Children.Add(new TextBlock
        {
            Text = ((int)Math.Round(model.RatingAfter.Value)).ToString(),
            Foreground = Brush("MpTextHeading"),
            FontFamily = Mono(),
            FontSize = Size("MpRatingSize"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        line.Children.Add(new TextBlock
        {
            // Non-null: the guard at the top of this method already returned on a null delta.
            Text = RatingDisplay.FormatDelta(delta)!,
            Foreground = Brush(delta.Value >= 0 ? "MpOk" : "MpDestructiveText"),
            FontSize = Size("MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        stack.Children.Add(line);

        stack.Children.Add(new TextBlock
        {
            Text = Strings.Format("MpResultRatingBefore", (int)Math.Round(model.RatingBefore ?? 0)),
            Foreground = Brush("MpTextFaint"),
            FontSize = Size("MpPillSize"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0),
        });
        return stack;
    }

    /// <summary>The three cells: decided record, replay, opponent.</summary>
    private static FrameworkElement BuildCells(MatchOutcomeView model)
    {
        var grid = new Grid { Margin = new Thickness(0, 15, 0, 0) };
        for (var i = 0; i < 5; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(11),
            });
        }

        // DECIDED. The win rate divides by decided games, and shows an em dash when
        // nothing has been decided — never 0 %, which would read as "lost them all".
        var decided = model.WinPercent is int pct
            ? $"{model.Wins}-{model.Losses} · {pct} %"
            : Strings.Get("MpResultUnknownValue");
        grid.Children.Add(Cell(0, "MpResultDecidedHeader", decided, "MpTextPrimary", mono: true));

        // REPLAY. Upload is scaffolded with no live caller, so there is nothing to link
        // to; saying "not uploaded" is the honest version of an empty cell.
        grid.Children.Add(Cell(2, "MpResultReplayHeader",
            Strings.Get("MpResultReplayNone"), "MpTextFaint", mono: false));

        // RIVAL. Only a 1v1 has one; past two players "the opponent" is a fiction.
        var rival = string.IsNullOrEmpty(model.RivalLogin)
            ? Strings.Get("MpResultUnknownValue")
            : model.RivalRating is double r
                ? $"{model.RivalLogin} {(int)Math.Round(r)}"
                : model.RivalLogin!;
        grid.Children.Add(Cell(4, "MpResultRivalHeader", rival, "MpTextPrimary", mono: false));
        return grid;
    }

    private static FrameworkElement Cell(int column, string headerKey, string value, string valueBrush, bool mono)
    {
        var border = new Border
        {
            Background = Brush("MpPanel"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 11, 12, 11),
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Get(headerKey),
            Foreground = Brush("MpTextLabel"),
            FontSize = Size("MpSectionLabelSize"),
            FontWeight = FontWeights.SemiBold,
        });
        var valueText = new TextBlock
        {
            Text = value,
            Foreground = Brush(valueBrush),
            FontSize = Size(mono ? "FontSizeCaption" : "MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 7, 0, 0),
        };
        if (mono) valueText.FontFamily = Mono();
        stack.Children.Add(valueText);
        border.Child = stack;
        Grid.SetColumn(border, column);
        return border;
    }

    /// <summary>
    /// The provisional note and the rematch button, or null when there is neither.
    ///
    /// <para>A match with no result gets its explanation here instead: it is the one thing
    /// the player will want to know, and the headline only has room to say that there
    /// isn't one.</para>
    /// </summary>
    private static FrameworkElement? BuildFooter(MatchOutcomeView model, Actions actions)
    {
        string? note = null;
        if (model.Verdict == MatchVerdict.NoResult)
        {
            note = Strings.Get(MatchOutcomeView.UnratedNoteKey(
                model.UnratedReason, model.LocalFailure));
            // The particulars go after the sentence, not inside it: they are data (profile
            // names), they must not be translated, and without them "none of the recordings
            // are yours" is a dead end rather than something to go and fix.
            if (!string.IsNullOrWhiteSpace(model.LocalFailureDetail))
                note += " " + model.LocalFailureDetail;
        }
        else if (MatchOutcomeView.IsProvisional(model.Rd)) note = Strings.Get("MpResultProvisional");

        if (note == null && actions.OnRematch == null && actions.OnDismiss == null) return null;

        var border = new Border
        {
            BorderBrush = Brush("MpRimSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 11, 12, 11),
            Margin = new Thickness(0, 13, 0, 0),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (note != null)
        {
            var text = new TextBlock
            {
                Text = note,
                Foreground = Brush("MpTextMuted"),
                FontSize = Size("MpLabelSize"),
                LineHeight = 17,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);
        }

        if (actions.OnDismiss != null)
        {
            var back = new Button
            {
                Content = Strings.Get("MpResultBackToRooms"),
                Style = (Style)Application.Current.FindResource("MpGhostButton"),
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            back.Click += (_, _) => actions.OnDismiss();
            Grid.SetColumn(back, 1);
            grid.Children.Add(back);
        }

        if (actions.OnRematch != null)
        {
            var rematch = new Button
            {
                Content = Strings.Get("MpResultRematch"),
                Style = (Style)Application.Current.FindResource("MpPrimaryButton"),
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                FontSize = Size("MpMetaSize"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            rematch.Click += (_, _) => actions.OnRematch();
            Grid.SetColumn(rematch, 2);
            grid.Children.Add(rematch);
        }

        border.Child = grid;
        return border;
    }

    /// <summary>
    /// "{mod} · {map} · {duration} · {N} players", dropping whatever is not known.
    ///
    /// <para>Each segment is optional because each of them genuinely can be missing: the
    /// map comes from the recording, and the player count is 0 on a backend that predates
    /// the field. Joining only what exists beats printing an em dash three times.</para>
    /// </summary>
    private static string Subtitle(MatchOutcomeView model)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(model.ModId))
        {
            var profile = Services.ModRegistry.Find(model.ModId!);
            parts.Add(profile?.DisplayName ?? model.ModId!);
        }
        if (!string.IsNullOrWhiteSpace(model.MapName)) parts.Add(model.MapName!);
        if (model.DurationSeconds > 0)
            parts.Add(Strings.Format("MpResultMinutes", Math.Max(1, model.DurationSeconds / 60)));
        if (model.PlayerCount > 0)
            parts.Add(Strings.Format("MpResultPlayers", model.PlayerCount));
        return string.Join(" · ", parts);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static double Size(string key) => (double)Application.Current.FindResource(key);
    private static FontFamily Mono() => (FontFamily)Application.Current.FindResource("MonoFont");
}
