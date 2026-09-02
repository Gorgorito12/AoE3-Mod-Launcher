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
/// <para>A static factory rather than a control, so the card can be built and thrown away
/// with the room it belongs to.</para>
///
/// <para><b>There is exactly ONE host: the lobby window.</b> This used to claim a second one in
/// the multiplayer tab, "for when the player closed that window mid-match" — and no such host
/// was ever built. Every path into the card goes through <c>_lobbyWindow?.MatchResultHost</c>,
/// so with the window gone the result was computed and silently discarded. That case is now
/// answered by <c>AnnounceResultWithoutAWindow</c> — a desktop toast and a bell entry — rather
/// than by a container that does not exist. Don't reinstate the claim without the host.</para>
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

        // REPLAY. It used to read "not uploaded" and mean it: uploading is scaffolded with
        // no live caller, so there was nothing to link to. There is now — not the upload, the
        // FILE, which the launcher had known all along and never told anyone.
        //
        // The cell shows the name and reveals it SELECTED in Explorer, and the difference is
        // the whole point: AoE3 names every recording "Record Game N" and renumbers after each
        // match, so ten files share one naming scheme and the newest is always number 1.
        // Printing the name is what the room chat already did, and it is wrong by the next
        // match; pointing at the file is not.
        var replayPath = model.RecordingPath;
        var hasReplay = !string.IsNullOrWhiteSpace(replayPath);
        grid.Children.Add(Cell(2, "MpResultReplayHeader",
            hasReplay
                ? System.IO.Path.GetFileName(replayPath!)
                : Strings.Get("MpResultReplayNone"),
            // Only read when the cell is a LABEL. A clickable one takes its colour from
            // MpLinkButton, which is the only way its disabled and hover states can ever work.
            "MpTextFaint",
            mono: false,
            // The full path, because the folder is not always where the player expects: a
            // launcher started as another Windows account writes under THAT account's
            // Documents, and then no amount of browsing their own finds it.
            tooltip: hasReplay
                ? Strings.Get("MpResultReplayReveal") + Environment.NewLine + replayPath
                : null,
            onClick: hasReplay ? () => Services.FileReveal.Reveal(replayPath) : null));

        // RIVAL. Only a 1v1 has one; past two players "the opponent" is a fiction.
        var rival = string.IsNullOrEmpty(model.RivalLogin)
            ? Strings.Get("MpResultUnknownValue")
            : model.RivalRating is double r
                ? $"{model.RivalLogin} {(int)Math.Round(r)}"
                : model.RivalLogin!;
        grid.Children.Add(Cell(4, "MpResultRivalHeader", rival, "MpTextPrimary", mono: false));
        return grid;
    }

    /// <param name="onClick">
    /// Makes the cell's VALUE a button rather than a label. Null leaves the cell exactly as it
    /// was — which is what every cell but one still passes, so a match with no recording renders
    /// byte for byte what it did before.
    /// </param>
    private static FrameworkElement Cell(
        int column, string headerKey, string value, string valueBrush, bool mono,
        string? tooltip = null, Action? onClick = null)
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
            FontSize = Size(mono ? "FontSizeCaption" : "MpBodySize"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 7, 0, 0),
        };
        if (mono) valueText.FontFamily = Mono();

        if (onClick == null)
        {
            valueText.Foreground = Brush(valueBrush);
            stack.Children.Add(valueText);
        }
        else
        {
            // NOTHING here sets a Foreground — not on the button, not on the TextBlock inside
            // it. MpLinkButton declares its own, and its ContentPresenter propagates it down to
            // the content text; a local value on either would beat the style's triggers and
            // leave the disabled state painted the ordinary colour. That is the precedence trap
            // documented in CLAUDE.md, which has already produced a launcher-wide class of dead
            // hovers once.
            var button = new Button
            {
                Style = (Style)Application.Current.FindResource("MpLinkButton"),
                Content = valueText,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => onClick();
            stack.Children.Add(button);
        }

        if (!string.IsNullOrEmpty(tooltip)) border.ToolTip = TooltipHelper.Wrap(tooltip!);
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
                // Raised with MpLabelSize (11.5 -> 13): a line box shorter than the font
                // needs clips descenders rather than tightening the leading.
                LineHeight = 19,
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
        // The matchup, and only when it is one: with the opponent's civilization unresolved this
        // reads as a bare civ name among the map and the minutes, which says nothing about who
        // played it. Same "join only what exists" rule as everything else on this line.
        if (!string.IsNullOrWhiteSpace(model.MyCiv))
        {
            parts.Add(string.IsNullOrWhiteSpace(model.RivalCiv)
                ? model.MyCiv!
                : Strings.Format("MpResultCivMatchup", model.MyCiv!, model.RivalCiv!));
        }
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
