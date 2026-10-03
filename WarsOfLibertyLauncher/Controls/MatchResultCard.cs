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

    /// <summary>The <c>Tag</c> of the 55j card, so the tests find it.</summary>
    internal const string CardTag = "MatchResultCase";

    /// <summary>
    /// Build the card for a finished match: the 55j card (verdict, delta and the case under it),
    /// the line with the mod, the map and the civilizations, the three cells, and the buttons.
    ///
    /// <para>55j draws only the card. The cells and the buttons are design 1f's and stay: the
    /// REPLAY cell is the only place that points at the recording's file, and "back to rooms" is
    /// the way out of the result phase.</para>
    /// </summary>
    public static FrameworkElement Build(MatchOutcomeView model, Actions actions)
    {
        var root = new StackPanel();
        root.Children.Add(BuildCaseCard(model));

        var details = Details(model);
        if (details.Length > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = details,
                Foreground = Brush("MpTextMuted"),
                FontSize = Size("MpMetaSize"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 10, 0, 0),
            });
        }
        root.Children.Add(BuildCells(model));

        var footer = BuildFooter(model, actions);
        if (footer != null) root.Children.Add(footer);

        return root;
    }

    /// <summary>
    /// The 55j card: a 4-px stripe in the verdict's colour, the verdict in serif with "vs Pedro ·
    /// 1v1" under it, the delta with "1598 → 1612" on the right, and under a hairline what this
    /// case has to say. A match that did not count is dimmer, with a grey stripe and "—"; the one
    /// that finished the placement sits on the profile header's gradient.
    /// </summary>
    internal static FrameworkElement BuildCaseCard(MatchOutcomeView model)
    {
        var dim = model.LooksUnrated;
        var placed = model.Case == ResultCase.PlacementDone;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = Strings.Get(model.Verdict switch
            {
                MatchVerdict.Win => "MpResultWin",
                MatchVerdict.Loss => "MpResultLoss",
                _ => "MpResultNone",
            }),
            Foreground = Brush("UiTextHeadline"),
            FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"),
            FontSize = Size("MpProfileNameSize"),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (model.Case is ResultCase.Unrated or ResultCase.KeptNoMove)
        {
            titleRow.Children.Add(new Border
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(4),
                Background = Brush("MpInactiveTagBg"),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "UnratedTag",
                Child = new TextBlock
                {
                    Text = Strings.Get("MpHistUnrated"),
                    FontSize = Size("MpSectionLabelSize"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("MpRankMutedText"),
                },
            });
        }
        left.Children.Add(titleRow);
        var vs = Versus(model);
        if (vs != null)
        {
            left.Children.Add(new TextBlock
            {
                Text = vs,
                Foreground = Brush("MpTextMuted"),
                FontSize = Size("MpMetaSize"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
        head.Children.Add(left);

        var right = BuildDelta(model, dim);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Child = head,
            Padding = new Thickness(12, 16, 16, 14),
        });
        if (BuildCaseSection(model) is { } section)
        {
            stack.Children.Add(new Border
            {
                Child = section,
                Padding = new Thickness(12, 11, 16, 13),
                BorderBrush = Brush(placed ? "MpRimSoft" : "MpRimHair"),
                BorderThickness = new Thickness(0, 1, 0, 0),
            });
        }

        // The stripe is the card's own left border: 4 px, so the content sits 4 px further in —
        // the same as CSS's inset shadow, which paints over the padding instead.
        return new Border
        {
            Tag = CardTag,
            Child = new Border
            {
                Child = stack,
                BorderBrush = Brush(dim ? "MpResultUnratedStripe"
                    : model.Verdict == MatchVerdict.Loss ? "MpDestructive" : "MpOk"),
                BorderThickness = new Thickness(4, 0, 0, 0),
            },
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(placed ? "MpResultPlacementBg" : dim ? "MpPanelDim" : "MpPanel"),
            BorderBrush = Brush(placed ? "MpRimStrong" : "MpRimMedium"),
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>"+14" (and "40 %" when the anti-farm discounted it) over "1598 → 1612"; "—" when
    /// nothing moved or nobody said.</summary>
    private static FrameworkElement BuildDelta(MatchOutcomeView model, bool dim)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var delta = dim ? null : model.RatingDelta;
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        top.Children.Add(new TextBlock
        {
            Text = RatingDisplay.FormatDelta(delta) ?? Strings.Get("MpDash"),
            FontFamily = Mono(),
            FontSize = Size("MpRatingSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(delta == null ? "MpTextMuted" : delta >= 0 ? "MpOkText" : "MpDestructiveText"),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        if (delta != null && model.FarmDiscounted && model.EloFactor is double factor)
        {
            top.Children.Add(new TextBlock
            {
                Text = Strings.Format("MpPercentValue", AntiFarmView.Percent(factor)),
                Margin = new Thickness(6, 0, 0, 0),
                FontFamily = Mono(),
                FontSize = Size("MpMetaSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("MpCaution"),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }
        stack.Children.Add(top);
        if (delta != null && model.RatingBefore is double before && model.RatingAfter is double after)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{Math.Round(before):0} \u2192 {Math.Round(after):0}",
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0),
                FontFamily = Mono(),
                FontSize = Size("MpRankSmallSize"),
                Foreground = Brush("MpTextFade"),
            });
        }
        return stack;
    }

    /// <summary>What the case says under the headline, or null when it has nothing to say.</summary>
    private static FrameworkElement? BuildCaseSection(MatchOutcomeView model)
    {
        switch (model.Case)
        {
            case ResultCase.Streak:
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(MultiplayerTab.BuildStreakPill(model.StreakCurrent!.Value));
                row.Children.Add(new TextBlock
                {
                    Text = Strings.Format("MpResultStreak", model.StreakCurrent!.Value),
                    Margin = new Thickness(10, 0, 0, 0),
                    FontSize = Size("MpMetaSize"),
                    Foreground = Brush("MpTextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return row;
            }
            case ResultCase.AntiFarm:
            {
                var deltaText = RatingDisplay.FormatDelta(model.RatingDelta) ?? "";
                var factor = model.EloFactor ?? 1;
                var streak = model.FarmStreak ?? 0;
                var text = model.Verdict == MatchVerdict.Win
                    ? AntiFarmView.WinText(deltaText, factor, streak)
                    : AntiFarmView.LossText(deltaText, factor, model.FarmRivalNames ?? model.RivalLogin ?? "", streak);
                // "+5 (40 %):" in bold monospace, the explanation after it as written.
                var colon = text.IndexOf(':');
                var tb = Note();
                if (colon > 0)
                {
                    tb.Inlines.Add(new System.Windows.Documents.Run(text[..(colon + 1)])
                    {
                        FontFamily = Mono(),
                        FontWeight = FontWeights.Bold,
                        Foreground = Brush("MpCautionTextAlt"),
                    });
                    tb.Inlines.Add(new System.Windows.Documents.Run(text[(colon + 1)..]));
                }
                else tb.Text = text;
                return tb;
            }
            case ResultCase.Unrated:
            case ResultCase.KeptNoMove:
            case ResultCase.NoResult:
            {
                var tb = Note();
                tb.Text = UnratedText(model);
                return tb;
            }
            case ResultCase.Placing:
            {
                var tb = Note();
                tb.Text = Strings.Format("MpResultPlacementLeft", model.PlacementPlayed!.Value, model.PlacementRequired!.Value);
                return tb;
            }
            case ResultCase.PlacementDone:
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (model.EnteredRank is int rank && rank > 0)
                {
                    var team = string.Equals(model.RatingMode, "team", StringComparison.Ordinal);
                    var shown = new ShownBadge(team ? BadgeKind.Team : BadgeKind.Solo,
                        RankAges.For(rank, model.LadderSize), rank, null, 0);
                    var badge = RankBadge.BuildFor(shown, 30, "result-placement");
                    badge.VerticalAlignment = VerticalAlignment.Center;
                    badge.Margin = new Thickness(0, 0, 12, 0);
                    grid.Children.Add(badge);
                }
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = Strings.Get("MpResultPlacementDone"),
                    FontSize = Size("MpResultPlacementTitleSize"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("ChromeTextBright"),
                });
                if (model.EnteredRank is int place && place > 0)
                {
                    text.Children.Add(new TextBlock
                    {
                        Text = Strings.Format("MpResultPlacementRank", place),
                        Margin = new Thickness(0, 2, 0, 0),
                        FontSize = Size("MpMetaSize"),
                        Foreground = Brush("MpTextSecondary"),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                return grid;
            }
            default:
                return null;
        }

        static TextBlock Note() => new()
        {
            FontSize = Size("MpMetaSize"),
            LineHeight = 18,
            Foreground = Brush("MpTextSecondary"),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>Why the match did not count, in the words that fit the cause.</summary>
    internal static string UnratedText(MatchOutcomeView model)
    {
        if (model.UnratedReason == "teams_mismatch"
            && model.IngameTeamNames is { Count: 2 } sides && sides[0].Count > 0 && sides[1].Count > 0)
        {
            return Strings.Format("MpResultTeamsMismatch", NameList.Join(sides[0]), NameList.Join(sides[1]));
        }
        if (model.UnratedReason == "teams_mismatch") return Strings.Get("MpResultTeamsMismatchShort");
        if (model.UnratedReason == "new_account_short") return Strings.Get("MpResultNewAccount");

        var note = Strings.Get(MatchOutcomeView.UnratedNoteKey(model.UnratedReason, model.LocalFailure));
        // The particulars go after the sentence, not inside it: they are data (profile names),
        // they must not be translated, and without them "none of the recordings are yours" is a
        // dead end rather than something to go and fix.
        if (model.Verdict == MatchVerdict.NoResult && !string.IsNullOrWhiteSpace(model.LocalFailureDetail))
            note += " " + model.LocalFailureDetail;
        return note;
    }

    /// <summary>"vs Pedro · 1v1", or "Ana and Luis vs Pedro and Sara · 2v2"; null when there is
    /// nobody to name.</summary>
    internal static string? Versus(MatchOutcomeView model)
    {
        var format = model.FormatLabelKey is { } key ? Strings.Get(key) : null;
        if (!string.IsNullOrWhiteSpace(model.RivalLogin))
            return format == null
                ? Strings.Format("MpResultVsAlone", model.RivalLogin!)
                : Strings.Format("MpResultVs", model.RivalLogin!, format);
        if (model.OwnSide is { Count: > 0 } own && model.OtherSide is { Count: > 0 } other)
        {
            var sides = Strings.Format("MpResultTeamsVs", NameList.Join(own), NameList.Join(other));
            return format == null ? sides : sides + " \u00B7 " + format;
        }
        return null;
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
        // The notes that used to sit here (no result, kept result, placement) are in the card
        // now (design 55j), each in the case it belongs to.
        string? note = null;

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
    /// "{mod} · {map} · {civilizations} · {duration}", dropping whatever is not known — the line
    /// under the 55j card. It was the headline's subtitle until 55j put "vs Pedro · 1v1" there.
    ///
    /// <para>Each segment is optional because each of them genuinely can be missing: the
    /// map comes from the recording, and the player count is 0 on a backend that predates
    /// the field. Joining only what exists beats printing an em dash three times.</para>
    /// </summary>
    private static string Details(MatchOutcomeView model)
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
        return string.Join(" · ", parts);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static double Size(string key) => (double)Application.Current.FindResource(key);
    private static FontFamily Mono() => (FontFamily)Application.Current.FindResource("MonoFont");
}
