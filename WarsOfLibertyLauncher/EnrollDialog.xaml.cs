using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>What the enrol window asks of the launcher around it.</summary>
internal sealed class EnrollActions
{
    public required Func<object?, Task<TournamentEntryResponse>> Enter { get; init; }
    public required Func<string, Task> Withdraw { get; init; }

    /// <summary>The tournament as it stands now, to name who took a player meanwhile.</summary>
    public required Func<Task<TournamentDetail>> Reload { get; init; }

    public required Func<LobbyApiException, string> ErrorText { get; init; }
    public Func<string, Task<IReadOnlyList<EnrollPlayer>>>? Search { get; init; }
    public Action? OpenMyTeams { get; init; }
    public Action? OpenModInLibrary { get; init; }
}

/// <summary>
/// Entering a tournament (design 82): one window of the tournament's mod that takes the shape of
/// how its teams are formed, stays open to say what happened, and doubles as "my entry" for
/// somebody already in. The primary says what it does; what stops it is written beside it.
/// </summary>
public partial class EnrollDialog : Window
{
    private readonly TournamentDetail _tournament;
    private readonly string? _me;
    private readonly EnrollActions _actions;
    private readonly IReadOnlyList<EnrollTeam> _teams;
    private readonly IReadOnlyList<EnrollPlayer> _recent;
    private readonly IReadOnlyList<EnrollPlayer> _online;
    private readonly HashSet<string> _taken;
    private readonly int _size;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly List<string> _mates = new();

    private EnrollMode _mode;
    private Color _accent = Color.FromRgb(0x2F, 0x7F, 0xE0);
    private TeamSummary? _team;
    private List<string> _lineup = new();
    private IReadOnlyList<EnrollPlayer> _found = Array.Empty<EnrollPlayer>();
    private bool _sending;
    private bool _finished;
    private bool _retry;

    /// <summary>Something changed on the server, so the page behind has to ask again.</summary>
    public bool Changed { get; private set; }

    internal EnrollMode Mode => _mode;

    internal EnrollDialog(
        TournamentDetail tournament,
        string? me,
        ModProfile? mod,
        bool modInstalled,
        IReadOnlyList<TeamSummary>? teams,
        IReadOnlyList<EnrollPlayer> recent,
        IReadOnlyList<EnrollPlayer> online,
        EnrollActions actions)
    {
        InitializeComponent();
        _tournament = tournament;
        _me = me;
        _actions = actions;
        _recent = recent;
        _online = online;
        _size = TournamentPermissions.RosterSizeOf(tournament);
        _taken = EnrollView.TakenPlayers(tournament);
        _teams = EnrollView.TeamsOf(tournament, me, teams);
        _mode = EnrollView.ModeOf(tournament, me, teams);
        foreach (var p in recent.Concat(online)) _names[p.Id] = p.Name;
        FitToScreen();
        ApplyStrings();

        if (mod != null)
        {
            _accent = ModDialogChrome.AttachPicked(this, TitleBarControl, Root, mod, Width, topGlow: true);
            OkButton.Foreground = new SolidColorBrush(ModAccent.OnAccent(_accent));
            var pending = _mode is EnrollMode.Solo or EnrollMode.Saved or EnrollMode.Build;
            ModNote.Visibility = pending && !modInstalled ? Visibility.Visible : Visibility.Collapsed;
            ModNoteText.Text = Strings.Format("EnModMissing", mod.DisplayName);
            ModInstallButton.Visibility = actions.OpenModInLibrary != null ? Visibility.Visible : Visibility.Collapsed;
        }

        NameText.Text = tournament.Name;
        MetaText.Text = EnrollView.Meta(tournament);
        BuildFacts();
        if (_mode == EnrollMode.Saved) BuildTeams();
        if (_mode == EnrollMode.Build) BuildChooser();
        if (_mode == EnrollMode.NoTeams) BuildNoTeam();

        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RunSearchAsync();
        };
        Refresh();

        FrameworkElement[] cards = { FactsCard, TeamsCard, LineupCard, BuildCard, NoTeamCard, DoneCard };
        void Frost() => ModDialogChrome.FrostCards(Root, cards.Where(c => c.Visibility == Visibility.Visible).ToArray());
        foreach (var card in cards) card.SizeChanged += (_, _) => Frost();
        Scroller.ScrollChanged += (_, _) => Frost();
        Closed += (_, _) => _searchTimer.Stop();
    }

    private void FitToScreen()
    {
        var work = SystemParameters.WorkArea;
        if (work.Height > 200) MaxHeight = work.Height - 40;
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("EnTitle");
        TitleBarControl.Title = Title;
        TeamsLabel.Text = Strings.Get("EnPickTeam");
        TeamsHint.Text = Strings.Get("EnPickTeamHint");
        FrozenNote.Text = Strings.Get("EnFrozenNote");
        LineupLabel.Text = Strings.Get("EnPickLineup");
        TeamNameLabel.Text = Strings.Get("EnTeamName");
        TeamNamePlaceholder.Text = Strings.Get("EnTeamNameHint");
        MatesLabel.Text = Strings.Get("EnMates");
        MatesHint.Text = Strings.Get("EnMatesHint");
        SearchPlaceholder.Text = Strings.Get("EnSearchPlayer");
        ModInstallButton.Content = Strings.Get("EnInstall");
        ApprovalNoteText.Text = Strings.Get("EnApprovalNote");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
        LeaveButton.Content = Strings.Get(_mode == EnrollMode.TeamCaptain ? "EnLeaveTeam" : "EnLeave");
        ModDialogChrome.SetTightLine(1.2, NameText);
        ModDialogChrome.SetTightLine(1, TeamsLabel, TeamsHint, LineupLabel, LineupHint, LineupCount, TeamNameLabel, MatesLabel, MatesHint, MatesCount);
        ModDialogChrome.SetTightLine(1.45, FrozenNote);
    }

    // ------------------------------------------------------------------- the shapes

    private void BuildFacts()
    {
        var facts = EnrollView.Facts(_tournament, _me, _mode);
        FactsCard.Visibility = facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < facts.Count; i++)
        {
            var label = new TextBlock { Text = Strings.Get(facts[i].LabelKey), Margin = new Thickness(0, 3, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Solid(0xA8, 0xBC, 0xD2), TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
            var text = new TextBlock { Text = facts[i].Text, TextWrapping = TextWrapping.Wrap, Foreground = Solid(0xF0, 0xF5, 0xFB) };
            text.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
            ModDialogChrome.SetTightLine(1.45, text);
            Grid.SetColumn(text, 1);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(162) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(label);
            row.Children.Add(text);
            FactRows.Children.Add(new Border
            {
                Padding = new Thickness(16, 12, 16, i < facts.Count - 1 ? 11 : 12),
                BorderThickness = new Thickness(0, 0, 0, i < facts.Count - 1 ? 1 : 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                Child = row,
            });
        }
    }

    private void BuildNoTeam()
    {
        NoTeamCard.Visibility = Visibility.Visible;
        NoTeamTitle.Text = Strings.Format("EnNoTeamTitle", _size);
        var mineShort = _teams.FirstOrDefault(t => string.Equals(t.Team.OwnerUserId, _me, StringComparison.Ordinal));
        NoTeamBody.Text = mineShort == null
            ? Strings.Get("EnNoTeamBody")
            : Strings.Format("EnNoTeamShort", mineShort.Team.Name, _size - (mineShort.Team.Members?.Count ?? 0));
    }

    private void BuildTeams()
    {
        TeamsCard.Visibility = Visibility.Visible;
        _team = _teams.First(t => t.CanEnter).Team;
        _lineup = (_team.Members ?? new()).Select(m => m.UserId).Take(_size).ToList();
        EnsureMeInLineup();
        PaintTeams();
    }

    private void EnsureMeInLineup()
    {
        if (_me == null || _lineup.Contains(_me)) return;
        _lineup.Insert(0, _me);
        if (_lineup.Count > _size) _lineup.RemoveAt(_lineup.Count - 1);
    }

    private static readonly (byte R, byte G, byte B)[] Crests = { (0x3F, 0x7A, 0x6A), (0x6A, 0x4F, 0x8A), (0x8A, 0x5A, 0x3F) };

    private void PaintTeams()
    {
        TeamRows.Children.Clear();
        for (var i = 0; i < _teams.Count; i++)
        {
            var row = _teams[i];
            var on = ReferenceEquals(row.Team, _team);
            var tone = Crests[i % Crests.Length];
            var crest = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = on ? new SolidColorBrush(_accent) : Solid(tone.R, tone.G, tone.B),
                Child = new TextBlock { Text = EnrollView.CrestOf(row.Team.Name), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var name = new TextBlock { Text = row.Team.Name, FontWeight = FontWeights.SemiBold, Foreground = Solid(0xF0, 0xF5, 0xFB), TextTrimming = TextTrimming.CharacterEllipsis };
            ModDialogChrome.SetTightLine(1.35, name);
            name.SetResourceReference(TextBlock.FontSizeProperty, "SetRowTitleSize");
            var line = new TextBlock { Text = row.Line, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = row.CanEnter ? Solid(0xA8, 0xBC, 0xD2) : Solid(0xF5, 0xBD, 0x76) };
            line.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            ModDialogChrome.SetTightLine(1.4, line);
            var words = new StackPanel { Margin = new Thickness(11, 0, 8, 0) };
            words.Children.Add(name);
            words.Children.Add(line);
            Grid.SetColumn(words, 1);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(crest);
            grid.Children.Add(words);
            if (on)
            {
                var tick = new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                tick.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
                Grid.SetColumn(tick, 2);
                grid.Children.Add(tick);
            }
            var button = new Button { Content = grid, Margin = new Thickness(0, 9, 0, 0), IsHitTestVisible = row.CanEnter, Opacity = row.CanEnter ? 1 : .6, Cursor = row.CanEnter ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow };
            button.SetResourceReference(StyleProperty, "GlassChoiceRow");
            PaintChoice(button, on);
            var team = row.Team;
            if (row.CanEnter)
            {
                button.Click += (_, _) =>
                {
                    _team = team;
                    _lineup = (team.Members ?? new()).Select(m => m.UserId).Take(_size).ToList();
                    EnsureMeInLineup();
                    PaintTeams();
                    Refresh();
                };
            }
            TeamRows.Children.Add(button);
        }
        PaintLineup();
    }

    /// <summary>The choice of who plays, only when the server would honour it: an older one ignores
    /// <c>member_ids</c> and takes the first players, and asking would then be a lie.</summary>
    private bool NeedsLineup => _team != null && (_team.Members?.Count ?? 0) > _size && _tournament.RosterSize != null;

    private void PaintLineup()
    {
        LineupCard.Visibility = NeedsLineup ? Visibility.Visible : Visibility.Collapsed;
        LineupRows.Children.Clear();
        if (!NeedsLineup) return;
        LineupHint.Text = Strings.Format("EnLineupHint", _team!.Members!.Count, _size);
        foreach (var member in _team.Members)
        {
            var id = member.UserId;
            var isMe = id == _me;
            var on = _lineup.Contains(id);
            var name = member.DisplayName ?? id;
            var row = PlayerButton(isMe ? Strings.Format("EnYouMark", name) : name, name, null, on, false, 24);
            row.Height = 40;
            row.Margin = new Thickness(3, 3, 3, 3);
            // The captain plays: the server refuses a line-up that leaves them out.
            row.IsHitTestVisible = !isMe;
            row.Click += (_, _) =>
            {
                if (on) _lineup.Remove(id);
                else _lineup.Add(id);
                PaintLineup();
                Refresh();
            };
            LineupRows.Children.Add(row);
        }
    }

    private void BuildChooser()
    {
        BuildCard.Visibility = Visibility.Visible;
        PaintChooser();
    }

    private void PaintChooser()
    {
        ChosenChips.Children.Clear();
        var me = _me == null ? "" : NameOf(_me);
        ChosenChips.Children.Add(Chip(me, Strings.Format("EnYouCaptain", me), true, null));
        foreach (var id in _mates.ToList())
        {
            ChosenChips.Children.Add(Chip(NameOf(id), NameOf(id), false, () =>
            {
                _mates.Remove(id);
                PaintChooser();
                Refresh();
            }));
        }

        PlayerRows.Children.Clear();
        var query = SearchEntry.Text;
        var groups = EnrollView.Groups(_recent, _online, _found, query, _me);
        foreach (var group in groups)
        {
            var head = new TextBlock { Text = Strings.Get(group.TitleKey), FontWeight = FontWeights.Bold, Margin = new Thickness(4, 8, 4, 3), Foreground = Solid(0x8C, 0x9C, 0xB1) };
            head.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
            PlayerRows.Children.Add(head);
            foreach (var player in group.Players) PlayerRows.Children.Add(BuildPlayerRow(player));
        }
        if (groups.Count == 0 && query.Trim().Length > 0)
        {
            var none = new TextBlock { Text = Strings.Format("EnNoPlayer", query.Trim()), Margin = new Thickness(4, 8, 4, 8), TextWrapping = TextWrapping.Wrap, Foreground = Solid(0xA8, 0xBC, 0xD2) };
            none.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
            PlayerRows.Children.Add(none);
        }
    }

    private Button BuildPlayerRow(EnrollPlayer player)
    {
        var taken = _taken.Contains(player.Id);
        var on = _mates.Contains(player.Id);
        var full = !on && _mates.Count >= _size - 1;
        var state = taken ? Strings.Get("EnTaken") : Strings.Get(player.Online ? "EnOnline" : "EnOffline");
        if (!taken && player.PlayedAgo != null) state = Strings.Format("EnPlayedAgo", state, player.PlayedAgo);
        var row = PlayerButton(player.Name, player.Name, state, on, taken, 26);
        row.Height = 40;
        row.Margin = new Thickness(0, 0, 0, 4);
        row.IsHitTestVisible = !taken && !full;
        row.Opacity = taken ? .55 : 1;
        row.Click += (_, _) =>
        {
            _names[player.Id] = player.Name;
            if (on) _mates.Remove(player.Id);
            else _mates.Add(player.Id);
            PaintChooser();
            Refresh();
        };
        return row;
    }

    private Button PlayerButton(string label, string initial, string? state, bool on, bool taken, double disc)
    {
        var tick = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = on ? new SolidColorBrush(_accent) : Brushes.Transparent,
            BorderBrush = on ? new SolidColorBrush(_accent) : new SolidColorBrush(Color.FromArgb(0x73, 0x82, 0xAF, 0xFF)),
            Child = on ? new TextBlock { Text = "✓", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(ModAccent.OnAccent(_accent)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        var avatar = new Border
        {
            Width = disc,
            Height = disc,
            Margin = new Thickness(9, 0, 9, 0),
            CornerRadius = new CornerRadius(disc / 2),
            Background = Solid(0x3F, 0x6A, 0x8A),
            Child = new TextBlock { Text = initial.Length > 0 ? initial[..1].ToUpperInvariant() : "?", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = Solid(0xF0, 0xF5, 0xFB), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        Grid.SetColumn(avatar, 1);
        Grid.SetColumn(name, 2);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(tick);
        grid.Children.Add(avatar);
        grid.Children.Add(name);
        if (state != null)
        {
            var text = new TextBlock { Text = state, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = taken ? Solid(0x8C, 0x9C, 0xB1) : state.StartsWith(Strings.Get("EnOnline"), StringComparison.Ordinal) ? Solid(0x8F, 0xE0, 0xB0) : Solid(0xA8, 0xBC, 0xD2) };
            text.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            Grid.SetColumn(text, 3);
            grid.Children.Add(text);
        }
        var button = new Button { Content = grid, Padding = new Thickness(9, 0, 9, 0), VerticalContentAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "GlassChoiceRow");
        PaintChoice(button, on);
        return button;
    }

    private void PaintChoice(Button row, bool on)
    {
        row.Background = new SolidColorBrush(on ? ModAccent.SelectionFill(_accent) : Color.FromRgb(0x1A, 0x2C, 0x48));
        row.BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0xE6, _accent.R, _accent.G, _accent.B) : Color.FromArgb(0x38, 0x82, 0xAF, 0xFF));
    }

    private FrameworkElement Chip(string initial, string text, bool captain, Action? drop)
    {
        var disc = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = captain ? new SolidColorBrush(_accent) : Solid(0x3F, 0x6A, 0x8A),
            Child = new TextBlock { Text = initial.Length > 0 ? initial[..1].ToUpperInvariant() : "?", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = captain ? new SolidColorBrush(ModAccent.OnAccent(_accent)) : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = captain ? Brushes.White : Solid(0xF0, 0xF5, 0xFB), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(disc);
        line.Children.Add(label);
        var chip = new Border
        {
            Height = 28,
            Padding = new Thickness(4, 0, drop == null ? 10 : 8, 0),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = captain ? new SolidColorBrush(ModAccent.SelectionFill(_accent)) : Solid(0x1A, 0x2C, 0x48),
            BorderBrush = captain ? new SolidColorBrush(Color.FromArgb(0xA6, _accent.R, _accent.G, _accent.B)) : new SolidColorBrush(Color.FromArgb(0x4D, 0x82, 0xAF, 0xFF)),
            Child = line,
        };
        if (drop != null)
        {
            line.Children.Add(new TextBlock { Text = "✕", Margin = new Thickness(7, 0, 0, 0), Foreground = Solid(0x8C, 0x9C, 0xB1), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            chip.Cursor = System.Windows.Input.Cursors.Hand;
            chip.MouseLeftButtonUp += (_, _) => drop();
        }
        return chip;
    }

    private string NameOf(string id) => _names.TryGetValue(id, out var name) ? name : id;

    /// <summary>A name the lists do not carry: the player's own.</summary>
    internal void KnowName(string id, string name)
    {
        _names[id] = name;
        if (_mode == EnrollMode.Build) PaintChooser();
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    // ------------------------------------------------------------------- the search

    private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || OkButton == null) return;
        SearchPlaceholder.Visibility = SearchEntry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _found = Array.Empty<EnrollPlayer>();
        PaintChooser();
        _searchTimer.Stop();
        if (_actions.Search != null && SearchEntry.Text.Trim().Length >= 2) _searchTimer.Start();
    }

    private async Task RunSearchAsync()
    {
        var query = SearchEntry.Text.Trim();
        try
        {
            var found = await _actions.Search!(query);
            if (query != SearchEntry.Text.Trim()) return;
            _found = found;
            PaintChooser();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Enroll: player search failed: {ex.Message}");
        }
    }

    private void TeamNameEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || OkButton == null) return;
        TeamNamePlaceholder.Visibility = TeamNameEntry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    // ------------------------------------------------------------------- the footer

    /// <summary>A prototype state, for the harness.</summary>
    internal void PreviewBuild(string team, string? mate)
    {
        TeamNameEntry.Text = team;
        if (mate != null)
        {
            _names[mate] = "Cuzco";
            _mates.Add(mate);
        }
        PaintChooser();
        Refresh();
    }

    private string? Block() => _finished ? null : _mode switch
    {
        EnrollMode.Saved when NeedsLineup => EnrollView.LineupBlock(_lineup.Count, _size),
        EnrollMode.Build => EnrollView.BuildBlock(_mates.Count, _size, TeamNameEntry.Text),
        _ => null,
    };

    private string OkText()
    {
        if (_sending) return Strings.Get("EnSending");
        if (_finished) return Strings.Get("EnDone");
        if (_retry) return Strings.Get("EnRetry");
        var approval = _tournament.EntryMode == "approval";
        return _mode switch
        {
            EnrollMode.Solo => Strings.Get(approval ? "EnOkSend" : "EnOkSolo"),
            EnrollMode.Saved => approval ? Strings.Get("EnOkSend") : Strings.Format("EnOkTeam", _team?.Name ?? ""),
            EnrollMode.Build => Strings.Get(approval ? "EnOkSend" : "EnOkBuild"),
            EnrollMode.NoTeams => Strings.Get("EnOpenTeams"),
            _ => Strings.Get("EnClose"),
        };
    }

    private void Refresh()
    {
        var pending = _mode is EnrollMode.Solo or EnrollMode.Saved or EnrollMode.Build;
        var mine = _mode is EnrollMode.Enrolled or EnrollMode.TeamCaptain or EnrollMode.TeamMember;
        var block = Block();
        OkButton.Content = OkText();
        OkButton.IsEnabled = block == null && !_sending;
        BlockText.Text = block ?? "";
        BlockText.Visibility = block == null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.ToolTip = block == null ? null : TooltipHelper.Wrap(block);

        LeaveButton.Visibility = !_finished && _mode is EnrollMode.Enrolled or EnrollMode.TeamCaptain && !_finished
            ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = _finished || mine ? Visibility.Collapsed : Visibility.Visible;
        ApprovalNote.Visibility = pending && !_finished && _tournament.EntryMode == "approval" && ErrorNote.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        LineupCount.Text = Strings.Format("EnOfN", _lineup.Count, _size);
        LineupCount.Foreground = _lineup.Count == _size ? Solid(0x8F, 0xE0, 0xB0) : Solid(0xF5, 0xBD, 0x76);
        MatesCount.Text = Strings.Format("EnOfN", _mates.Count + 1, _size);
        MatesCount.Foreground = _mates.Count == _size - 1 ? Solid(0x8F, 0xE0, 0xB0) : Solid(0xF5, 0xBD, 0x76);

        if (mine && !_finished)
        {
            OkButton.SetResourceReference(StyleProperty, "WindowSecondaryButton");
            OkButton.ClearValue(ForegroundProperty);
        }
    }

    private async void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_finished || _mode is EnrollMode.Enrolled or EnrollMode.TeamCaptain or EnrollMode.TeamMember)
        {
            Close();
            return;
        }
        if (_mode == EnrollMode.NoTeams)
        {
            Close();
            _actions.OpenMyTeams?.Invoke();
            return;
        }
        await SubmitAsync();
    }

    private object? BuildBody() => _mode switch
    {
        EnrollMode.Saved => new { team_id = _team!.Id, member_ids = NeedsLineup ? _lineup.ToList() : null },
        EnrollMode.Build => new
        {
            member_ids = (_me == null ? _mates : _mates.Prepend(_me)).ToList(),
            display_name = TeamNameEntry.Text.Trim(),
        },
        _ => null,
    };

    /// <summary>Sends the entry and says what came of it. Stays open either way.</summary>
    internal async Task SubmitAsync()
    {
        if (_sending || Block() != null) return;
        _sending = true;
        ErrorNote.Visibility = Visibility.Collapsed;
        Refresh();
        try
        {
            var result = await _actions.Enter(BuildBody());
            Changed = true;
            ShowDone(result.Status);
        }
        catch (LobbyApiException ex) when (ex.Code == "already_entered" && _mode == EnrollMode.Build)
        {
            await ShowTakenAsync(ex);
        }
        catch (LobbyApiException ex)
        {
            ShowError(Strings.Get("EnErrSent"), _actions.ErrorText(ex));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _retry = true;
            ShowError(Strings.Get("EnErrConnTitle"), Strings.Get("EnErrConnBody"));
        }
        finally
        {
            _sending = false;
            Refresh();
        }
    }

    /// <summary>A teammate went into the tournament meanwhile: say who, mark them, keep the rest.</summary>
    private async Task ShowTakenAsync(LobbyApiException ex)
    {
        IReadOnlyList<string> gone = Array.Empty<string>();
        try
        {
            var fresh = await _actions.Reload();
            foreach (var id in EnrollView.TakenPlayers(fresh)) _taken.Add(id);
            gone = EnrollView.NowTaken(fresh, _mates);
        }
        catch (Exception reload)
        {
            DiagnosticLog.Write($"Enroll: reloading after a refusal failed: {reload.Message}");
        }
        foreach (var id in gone) _mates.Remove(id);
        PaintChooser();
        ShowError(Strings.Get("EnErrSent"), gone.Count == 0
            ? _actions.ErrorText(ex)
            : Strings.Format("EnErrTaken", string.Join(", ", gone.Select(NameOf))));
    }

    internal void ShowError(string title, string body)
    {
        ErrorTitle.Text = title;
        ErrorBody.Text = body;
        ErrorNote.Visibility = Visibility.Visible;
        Refresh();
    }

    internal void ShowDone(string? status)
    {
        _finished = true;
        var pending = status == "pending";
        var waiting = status == "waitlist";
        FactsCard.Visibility = TeamsCard.Visibility = LineupCard.Visibility = BuildCard.Visibility = ModNote.Visibility = Visibility.Collapsed;
        ErrorNote.Visibility = Visibility.Collapsed;
        DoneCard.Visibility = Visibility.Visible;
        var tint = pending || waiting ? Color.FromRgb(0x55, 0xAC, 0xEE) : Color.FromRgb(0x4F, 0xD6, 0x8A);
        DoneDisc.Background = new SolidColorBrush(Color.FromArgb(0x24, tint.R, tint.G, tint.B));
        DoneDisc.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, tint.R, tint.G, tint.B));
        DoneGlyph.Text = pending || waiting ? "i" : "✓";
        DoneGlyph.Foreground = new SolidColorBrush(tint);
        DoneTitle.Text = Strings.Get(pending ? "EnSentTitle" : waiting ? "EnWaitTitle" : "EnDoneTitle");
        DoneBody.Text = Strings.Get(pending ? "EnSentBody" : waiting ? "EnWaitBody" : "EnDoneBody");

        var team = _mode == EnrollMode.Saved ? _team?.Name : _mode == EnrollMode.Build ? TeamNameEntry.Text.Trim() : null;
        if (team != null)
        {
            string Name(string id) => _mode == EnrollMode.Saved
                ? _team!.Members?.FirstOrDefault(m => m.UserId == id)?.DisplayName ?? id
                : NameOf(id);
            var names = new List<string>();
            if (_me != null) names.Add(Strings.Format("EnCaptainMark", Name(_me)));
            names.AddRange((_mode == EnrollMode.Saved ? _lineup.Where(id => id != _me) : _mates).Select(Name));
            DoneTeam.Text = Strings.Format("EnDoneTeam", team, EnrollView.JoinNames(names));
            DoneTeam.Visibility = Visibility.Visible;
        }
        OkButton.IsEnabled = true;
        Refresh();
    }

    private async void LeaveButton_Click(object sender, RoutedEventArgs e) => await LeaveAsync();

    internal async Task LeaveAsync()
    {
        if (TournamentPermissions.MyEntrant(_tournament, _me) is not { } mine) return;
        var team = _mode == EnrollMode.TeamCaptain;
        var yes = await MpAlertOverlay.ConfirmAsync(
            Root,
            team ? Strings.Format("EnLeaveTeamTitle", mine.DisplayName ?? "", _tournament.Name) : Strings.Format("EnLeaveTitle", _tournament.Name),
            Strings.Get(team ? "EnLeaveTeamBody" : "EnLeaveBody"),
            Strings.Get(team ? "EnLeaveTeamYes" : "EnLeaveYes"),
            Strings.Get("MpCreateDialogCancel"),
            danger: true);
        if (!yes) return;
        try
        {
            await _actions.Withdraw(mine.Id);
            Changed = true;
            Close();
        }
        catch (LobbyApiException ex)
        {
            ShowError(Strings.Get("EnErrSent"), _actions.ErrorText(ex));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ShowError(Strings.Get("EnErrConnTitle"), Strings.Get("EnErrConnBody"));
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ModInstallButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _actions.OpenModInLibrary?.Invoke();
    }
}
