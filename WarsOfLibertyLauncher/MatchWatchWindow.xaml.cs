using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// A match of a tournament, as the person RUNNING it sees it (design 99).
///
/// <para><b>Deliberately not <see cref="LobbyWindow"/>.</b> That window is the room you are IN, and
/// two thirds of it exists because you are about to play. Somebody supervising is not a member.
/// This shows the four things they came for: which bracket slot this is, who is in it, what is being
/// said, and the result when it lands.</para>
///
/// <para><b>Preview surface, and it says so on itself.</b> The server refuses this three times over:
/// a lobby in <c>in_game</c> rejects every join before it looks at seats or roles, a lobby bound to a
/// bracket slot admits only that slot's entrants with no owner exemption, and tournament rooms are
/// created with zero spectator slots. So it is filled from <see cref="TournamentDemoData"/>, reached
/// only from the fabricated tournaments, and its chat is read-only until those doors open.</para>
///
/// <para><b>The roster is read off the BRACKET, not off a second fixture.</b> Who is meant to be in
/// this room is exactly the entrants the bracket froze, which is the one thing an organiser opens
/// this to check.</para>
/// </summary>
public partial class MatchWatchWindow : Window
{
    private static readonly string[] AvatarColors =
        { "#9A6A2E", "#7A5BB5", "#2F7FE0", "#2E8A6E", "#3B6FB0", "#8A3B5C" };

    private readonly TournamentDetail _tournament;
    private readonly TournamentMatch _match;
    private readonly TournamentDemoData.MatchWatchSample _sample;
    private readonly Action<string, string>? _decide;
    private AnimationClock? _pulse;

    /// <param name="decide">The bracket's own "Decide this match", given the winner's id and name;
    /// null when this viewer cannot decide it.</param>
    internal MatchWatchWindow(
        TournamentDetail tournament,
        TournamentMatch match,
        TournamentDemoData.MatchWatchSample sample,
        Action<string, string>? decide = null)
    {
        _tournament = tournament;
        _match = match;
        _sample = sample;
        _decide = decide;

        InitializeComponent();
        if (ModDialogChrome.Current != null) ModDialogChrome.Attach(this, TitleBarControl);
        else ModDialogChrome.AttachGlobal(this);

        ApplyStrings();
        Render();
        Closed += (_, _) => StopPulse();
    }

    private string RoundName()
    {
        // Lowercased, as MyPlayableRound does with the same keys: those strings are BRACKET COLUMN
        // headers and are written in caps for that job.
        var round = Strings.Format(BracketLayout.RoundLabelKey(_match.Round, _tournament.RoundsTotal), _match.Round)
            .ToLower(Strings.Culture);
        return round.Length == 0 ? round : char.ToUpper(round[0], Strings.Culture) + round[1..];
    }

    private TournamentEntrant? Entrant(string? id)
        => _tournament.Entrants?.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    private string NameOf(string? entrantId) => Entrant(entrantId)?.DisplayName ?? "—";

    private void ApplyStrings()
    {
        Title = Strings.Get("MpWatchWindowTitle");
        TitleBarControl.Title = Strings.Get("MpWatchWindowTitle");
        RosterLabel.Text = Strings.Get("MpWatchRoster").ToUpper(Strings.Culture);
        ChatLabel.Text = Strings.Get("MpWatchChat").ToUpper(Strings.Culture);
        ReadOnlyText.Text = Strings.Get("MpWatchReadOnly");
        ReadOnlyChip.ToolTip = TooltipHelper.Wrap(Strings.Get("MpWatchReadOnlyTip"));
        ResultLabel.Text = Strings.Get("MpWatchResult");
        DecideBtn.Content = Strings.Get("MpTournamentAward");
        DecideBtn.ToolTip = TooltipHelper.Wrap(Strings.Get("MpWatchDecideTip"));
        CloseBtn.Content = Strings.Get("DlgClose");
    }

    private void Render()
    {
        var a = NameOf(_match.Entrant1Id);
        var b = NameOf(_match.Entrant2Id);
        var heading = $"{RoundName()} · {a} vs {b}";
        MatchLabel.Text = $"{_tournament.Name} · {heading}".ToUpper(Strings.Culture);
        RoomTitleText.Text = heading;
        RoomTitleText.LineHeight = RoomTitleText.FontSize * 1.2;
        FormatText.Text = Strings.Format("MpWatchCompetitive", _tournament.Format ?? "1v1");
        ModText.Text = _sample.ModName;
        ModVersionText.Text = _sample.ModVersion;

        RenderState(b);
        RenderRoster();
        RenderChat();
        RenderResult(a);

        // A decided match has nothing left to decide.
        DecideBtn.Visibility = _decide != null && _sample.Stage != TournamentDemoData.MatchWatchStage.Finished
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderState(string missing)
    {
        switch (_sample.Stage)
        {
            case TournamentDemoData.MatchWatchStage.Waiting:
                StateText.Text = Strings.Format("MpWatchWaitingFor", missing);
                StateText.SetResourceReference(TextBlock.ForegroundProperty, "ModAccentText");
                StateDot.SetResourceReference(Shape.FillProperty, "ModAccentText");
                PreviewNote.Text = Strings.Format("MpWatchFootWaiting", missing);
                RosterNote.Text = Strings.Get("MpWatchRosterNote");
                RosterNote.Visibility = Visibility.Visible;
                StartPulse();
                break;
            case TournamentDemoData.MatchWatchStage.Finished:
                StateText.Text = Strings.Format("MpWatchFinishedAgo", _sample.StartedMinutesAgo);
                StateText.SetResourceReference(TextBlock.ForegroundProperty, "WindowMuted");
                StateDot.SetResourceReference(Shape.FillProperty, "UiTextFaint");
                PreviewNote.Text = Strings.Format("MpWatchFootDone", NameOf(_match.Entrant1Id));
                break;
            default:
                StateText.Text = Strings.Format("MpWatchInGameFor", _sample.StartedMinutesAgo);
                StateText.SetResourceReference(TextBlock.ForegroundProperty, "WindowCopied");
                StateDot.SetResourceReference(Shape.FillProperty, "WindowSafeTick");
                PreviewNote.Text = Strings.Get("MpWatchFootPlaying");
                break;
        }
    }

    private void StartPulse()
    {
        if (RankBadge.ReducedEffects || !SystemParameters.ClientAreaAnimation) return;
        var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Timeline.SetDesiredFrameRate(pulse, 30);
        _pulse = pulse.CreateClock();
        StateDot.ApplyAnimationClock(OpacityProperty, _pulse);
    }

    private void StopPulse()
    {
        if (_pulse == null) return;
        _pulse.Controller?.Stop();
        StateDot.ApplyAnimationClock(OpacityProperty, null);
        _pulse = null;
    }

    /// <summary>The sides the bracket froze, in bracket order: seed and name for one person each,
    /// grouped by side with the team's seed for teams.</summary>
    private void RenderRoster()
    {
        RosterPanel.Children.Clear();
        var sides = new[] { Entrant(_match.Entrant1Id), Entrant(_match.Entrant2Id) };
        var waiting = _sample.Stage == TournamentDemoData.MatchWatchStage.Waiting;
        var teams = sides.Any(s => s?.Members?.Count > 1);
        var total = 0;
        var inRoom = 0;

        for (var i = 0; i < sides.Length; i++)
        {
            var side = sides[i];
            if (side == null) continue;
            if (teams)
            {
                RosterPanel.Children.Add(SideHeading(side));
                foreach (var member in side.Members ?? new List<TournamentEntrantMember>())
                {
                    var captain = string.Equals(member.UserId, side.CaptainUserId, StringComparison.Ordinal);
                    RosterPanel.Children.Add(Row(null, member.DisplayName ?? "—",
                        captain ? Strings.Get("MpWatchCaptain") : "", missing: false));
                    total++;
                    inRoom++;
                }
                continue;
            }

            var missing = waiting && i == 1;
            RosterPanel.Children.Add(Row(side.Seed?.ToString() ?? "—", side.DisplayName ?? "—",
                missing ? Strings.Get("MpWatchNotIn") : _sample.SideLines.ElementAtOrDefault(i) ?? "", missing));
            total++;
            if (!missing) inRoom++;
        }
        RosterCount.Text = $"{inRoom} / {total}";
        if (teams) ChatScroll.Height = 232;
    }

    private static UIElement SideHeading(TournamentEntrant side)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = side.DisplayName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
        name.SetResourceReference(TextBlock.ForegroundProperty, "UiBodyText");
        row.Children.Add(name);

        var seed = new TextBlock
        {
            Text = Strings.Format("MpWatchSeed", side.Seed?.ToString() ?? "—"),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        seed.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
        seed.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        Grid.SetColumn(seed, 1);
        row.Children.Add(seed);
        return row;
    }

    private static UIElement Row(string? seed, string name, string sub, bool missing)
    {
        var grid = new Grid();
        if (seed != null) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var column = 0;

        if (seed != null)
        {
            var number = new TextBlock
            {
                Text = seed,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontWeight = FontWeights.Medium,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
            };
            number.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
            number.SetResourceReference(TextBlock.ForegroundProperty, "UiTextFaint");
            grid.Children.Add(number);
            column = 1;
        }

        var initial = new TextBlock
        {
            Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var avatar = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = initial,
        };
        if (missing)
        {
            avatar.BorderThickness = new Thickness(1.5);
            avatar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xA8, 0xBC, 0xD2));
        }
        else
        {
            var color = AvatarColors[Math.Abs(StableHash(name)) % AvatarColors.Length];
            avatar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        Grid.SetColumn(avatar, column);
        grid.Children.Add(avatar);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        title.SetResourceReference(TextBlock.ForegroundProperty, missing ? "WindowMuted" : "WindowStrongText");
        text.Children.Add(title);
        if (sub.Length > 0)
        {
            var line = new TextBlock { Text = sub, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            line.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
            line.SetResourceReference(TextBlock.ForegroundProperty, missing ? "WindowNoticeTitle" : "WindowFineText");
            text.Children.Add(line);
        }
        Grid.SetColumn(text, column + 1);
        grid.Children.Add(text);

        var rule = new Border { Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        rule.SetResourceReference(Border.BorderBrushProperty, "WindowFooterRule");
        return rule;
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text) hash = unchecked(hash * 31 + c);
        return hash == int.MinValue ? 0 : hash;
    }

    private void RenderChat()
    {
        ChatPanel.Children.Clear();
        foreach (var line in _sample.Chat)
        {
            if (line.IsSystem)
            {
                var note = new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9) };
                note.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
                note.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
                ChatPanel.Children.Add(note);
                continue;
            }

            var heading = new TextBlock();
            var who = new Run(line.Author) { FontWeight = FontWeights.SemiBold };
            who.SetResourceReference(TextElement.FontSizeProperty, "SetRowDescSize");
            who.SetResourceReference(TextElement.ForegroundProperty, "UiBodyText");
            heading.Inlines.Add(who);
            if (line.Time.Length > 0)
            {
                var at = new Run("  " + line.Time) { FontFamily = new FontFamily("Consolas, Courier New, monospace") };
                at.SetResourceReference(TextElement.FontSizeProperty, "SetGroupLabelSize");
                at.SetResourceReference(TextElement.ForegroundProperty, "UiTextFaint");
                heading.Inlines.Add(at);
            }
            var body = new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            body.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
            body.SetResourceReference(TextBlock.ForegroundProperty, "WindowSoftText");

            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            block.Children.Add(heading);
            block.Children.Add(body);
            ChatPanel.Children.Add(block);
        }
    }

    private void RenderResult(string winner)
    {
        if (_sample.Stage != TournamentDemoData.MatchWatchStage.Finished)
        {
            ResultBox.Visibility = Visibility.Collapsed;
            return;
        }
        ResultWhen.Text = _sample.ResultAt;
        ResultText.Text = Strings.Format("MpWatchWon", winner);
        ResultScore.Text = "1–0";
        ResultSub.Text = Strings.Format("MpWatchResultSub", winner);
        ResultBox.Visibility = Visibility.Visible;
    }

    /// <summary>The same two choices the bracket's button offers, and its confirmation after them.</summary>
    private void DecideBtn_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = DecideBtn };
        if (TryFindResource("LibraryMenu") is Style style) menu.Style = style;
        foreach (var id in new[] { _match.Entrant1Id, _match.Entrant2Id })
        {
            if (string.IsNullOrEmpty(id)) continue;
            var name = NameOf(id);
            var item = new MenuItem { Header = Strings.Format("MpTournamentAwardTo", name) };
            if (TryFindResource("LibraryMenuItem") is Style itemStyle) item.Style = itemStyle;
            var winner = id;
            item.Click += (_, _) =>
            {
                Close();
                _decide?.Invoke(winner, name);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
