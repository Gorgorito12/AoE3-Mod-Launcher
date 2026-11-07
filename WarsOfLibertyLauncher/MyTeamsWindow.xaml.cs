using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// "My teams" (design 83): invitations and teams in a list, the chosen one beside it, and creating
/// and inviting in the same window.
///
/// <para>Invitations persist on the server (inviting somebody who is at work is the normal case), so
/// they wait here as well as arriving as a push.</para>
///
/// <para><b>Every call goes through the tournament server the tab hands in</b>
/// (<see cref="ITournamentApi"/>): in the preview that is the simulator, so an invitation sent from
/// the preview can never reach a real person.</para>
///
/// <para><b>Nothing here needs a datum the server does not send.</b> Renaming a team and handing the
/// captaincy over have no route, so neither is offered; sent invitations appear only when the server
/// lists them.</para>
/// </summary>
public partial class MyTeamsWindow : Window
{
    private const int MinNameLength = 2;

    private enum LoadState { Loading, Ready, Unavailable, Failed, Offline, SignedOut }

    private const string NewSelection = "new";

    private static readonly string[] Crests = { "#3F7A6A", "#6A4F8A", "#8A5A3F", "#4F6A8A", "#3F5F8A", "#7A5A6A" };

    private readonly ITournamentApi _api;
    private readonly string? _me;
    private readonly Func<IReadOnlyList<PickCandidate>> _online;
    private readonly Func<string, Task<IReadOnlyList<PickCandidate>>>? _search;
    private readonly Func<string, bool> _isOnline;
    private readonly Action? _signIn;
    private readonly Dictionary<string, TeamSummary> _inviteTeams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inviteTeamsAsked = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<PickCandidate> _picked = new();

    private MyTeamsResponse? _data;
    private LoadState _state = LoadState.Loading;
    private string? _selection;
    private string? _note;
    private bool _noteIsError;
    private IReadOnlyList<PickCandidate> _found = Array.Empty<PickCandidate>();
    private bool _sending;

    /// <summary>Whether anything changed, so the tab refreshes what depends on teams.</summary>
    internal bool Changed { get; private set; }

    /// <summary>What is on screen, for the tests.</summary>
    internal MyTeamsResponse? Data => _data;

    internal MyTeamsWindow(
        ITournamentApi api,
        string? me,
        Func<IReadOnlyList<PickCandidate>> online,
        Func<string, Task<IReadOnlyList<PickCandidate>>>? search,
        Func<string, bool>? isOnline = null,
        Action? signIn = null,
        string? selectInvite = null)
    {
        InitializeComponent();
        _api = api;
        _me = me;
        _online = online;
        _search = search;
        _isOnline = isOnline ?? (_ => false);
        _signIn = signIn;
        if (selectInvite != null) _selection = "inv:" + selectInvite;

        var work = SystemParameters.WorkArea;
        if (work.Height > 200) MaxHeight = work.Height;
        ApplyStrings();
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RunSearchAsync();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || SheetLayer.Visibility != Visibility.Visible) return;
            CloseSheet();
            e.Handled = true;
        };
        Closed += (_, _) => _searchTimer.Stop();
        Render();
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("MpTeamsTitle");
        TitleBarControl.Title = Title;
        NewTeamButton.Content = Strings.Get("MpTeamsNew");
        DeclineButton.Content = Strings.Get("MtDecline");
        AcceptButton.Content = Strings.Get("MtAccept");
        NameLabel.Text = Strings.Get("MtName");
        NamePlaceholder.Text = Strings.Get("MtNamePlaceholder");
        TagLabel.Text = Strings.Get("MtTag");
        FormHint.Text = Strings.Format("MtFormHint", MyTeamsView.MaxMembers);
        NewCancelButton.Content = Strings.Get("MtCancel");
        CreateButton.Content = Strings.Get("MtCreate");
        SheetBody.Text = Strings.Get("MtSheetBody");
        SheetLabel.Text = Strings.Get("MtPlayers");
        SearchPlaceholder.Text = Strings.Get("MtSearch");
        SheetCancel.Content = Strings.Get("MtCancel");
        ModDialogChrome.SetTightLine(1, NameLabel, NameCount, TagLabel, SheetLabel, SheetFree, SheetCount);
        ModDialogChrome.SetTightLine(1.5, FormHint, SheetBody, CenterBody);
        ModDialogChrome.SetTightLine(1.35, CenterTitle, SheetTitle);
        ModDialogChrome.SetTightLine(1.3, HeadSub);
    }

    // ------------------------------------------------------------------- loading

    /// <summary>Read the teams and invitations again and redraw.</summary>
    internal async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_me))
        {
            _state = LoadState.SignedOut;
            Render();
            return;
        }
        try
        {
            _data = await _api.GetMyTeamsAsync();
            _state = LoadState.Ready;
            foreach (var invite in _data.Invites ?? new List<TeamInvite>()) _ = LoadInviteTeamAsync(invite);
        }
        catch (LobbyApiException ex) when (ex.Status == 404)
        {
            // A server older than teams: say so, never show it as a failure.
            _state = LoadState.Unavailable;
        }
        catch (LobbyApiException)
        {
            _state = LoadState.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _state = LoadState.Offline;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MyTeams: load failed: {ex.Message}");
            _state = LoadState.Failed;
        }
        Render();
    }

    private async Task LoadInviteTeamAsync(TeamInvite invite)
    {
        if (string.IsNullOrEmpty(invite.TeamId) || !_inviteTeamsAsked.Add(invite.TeamId)) return;
        try
        {
            _inviteTeams[invite.TeamId] = await _api.GetTeamAsync(invite.TeamId);
        }
        catch (Exception ex)
        {
            // The invitation still reads without the roster; nothing to tell anybody about.
            DiagnosticLog.Write($"MyTeams: reading the invited team failed: {ex.Message}");
            return;
        }
        Render();
    }

    private async Task RunAsync(Func<Task> action, string? noticeOnSuccess, string? selectAfter = null)
    {
        try
        {
            await action();
            Changed = true;
            _note = noticeOnSuccess;
            _noteIsError = false;
            if (selectAfter != null) _selection = selectAfter == "" ? null : selectAfter;
        }
        catch (LobbyApiException ex)
        {
            _note = MultiplayerTab.TournamentErrorText(ex);
            _noteIsError = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _note = Strings.Get("MtOfflineTitle");
            _noteIsError = true;
        }
        await LoadAsync();
    }

    // ------------------------------------------------------------------- what is chosen

    private List<TeamSummary> Teams => (_data?.Teams ?? new List<TeamSummary>()).Where(t => t.Disbanded != true).ToList();

    private List<TeamInvite> Invites => _data?.Invites ?? new List<TeamInvite>();

    private TeamSummary? SelectedTeam => Teams.FirstOrDefault(t => t.Id == _selection);

    private TeamInvite? SelectedInvite => Invites.FirstOrDefault(i => "inv:" + i.Id == _selection);

    private void SettleSelection()
    {
        if (_selection == NewSelection || SelectedTeam != null || SelectedInvite != null) return;
        _selection = Teams.FirstOrDefault()?.Id ?? (Invites.Count > 0 ? "inv:" + Invites[0].Id : null);
    }

    private void Select(string? selection)
    {
        _selection = selection;
        _note = null;
        CloseSheet();
        Render();
    }

    private bool IsCaptainOf(TeamSummary team) => !string.IsNullOrEmpty(_me) && team.OwnerUserId == _me;

    // ------------------------------------------------------------------- drawing

    internal void Render()
    {
        var ready = _state == LoadState.Ready;
        if (ready) SettleSelection();
        var team = ready ? SelectedTeam : null;
        var invite = ready ? SelectedInvite : null;
        var isNew = ready && _selection == NewSelection;

        RenderRail(ready);
        var hasRail = RailRows.Children.Count > 0;
        RailCard.Visibility = hasRail ? Visibility.Visible : Visibility.Collapsed;
        RailColumn.Width = new GridLength(hasRail ? 270 : 0);
        RailGap.Width = new GridLength(hasRail ? 16 : 0);

        HeadPanel.Visibility = team != null || invite != null || isNew ? Visibility.Visible : Visibility.Collapsed;
        TeamView.Visibility = team != null || invite != null ? Visibility.Visible : Visibility.Collapsed;
        NewView.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        CenterCard.Visibility = team == null && invite == null && !isNew ? Visibility.Visible : Visibility.Collapsed;

        var showNote = _note != null && ready;
        NoteBox.Visibility = showNote ? Visibility.Visible : Visibility.Collapsed;
        if (showNote) PaintNote();

        if (team != null) RenderTeam(team);
        else if (invite != null) RenderInvite(invite);
        else if (isNew) RefreshForm();
        else RenderCenter();
    }

    private void PaintNote()
    {
        var tint = _noteIsError ? Color.FromRgb(0xF4, 0x90, 0x0C) : Color.FromRgb(0x4F, 0xD6, 0x8A);
        NoteBox.Background = new SolidColorBrush(Color.FromArgb(0x1A, tint.R, tint.G, tint.B));
        NoteBox.BorderBrush = new SolidColorBrush(Color.FromArgb(_noteIsError ? (byte)0x73 : (byte)0x66, tint.R, tint.G, tint.B));
        NoteGlyph.Text = _noteIsError ? "!" : "✓";
        NoteGlyph.Foreground = new SolidColorBrush(tint);
        NoteText.Text = _note;
    }

    private void RenderRail(bool ready)
    {
        RailRows.Children.Clear();
        NewTeamButton.Background = _selection == NewSelection ? Hex("#233A5C") : (Brush)FindResource("WindowSecondary");
        if (!ready) return;
        var invites = Invites;
        var teams = Teams;
        if (invites.Count > 0)
        {
            RailRows.Children.Add(RailHead("MtGroupInvites", invites.Count));
            foreach (var invite in invites)
                RailRows.Children.Add(RailItem("inv:" + invite.Id, invite.TeamName ?? "", InvitedByLine(invite), null, true));
        }
        if (teams.Count > 0)
        {
            RailRows.Children.Add(RailHead("MtGroupTeams", teams.Count));
            for (var i = 0; i < teams.Count; i++)
            {
                var team = teams[i];
                var count = team.Members?.Count ?? 0;
                var line = Strings.Format("MtRailLine", Strings.Get(IsCaptainOf(team) ? "MtRoleCaptain" : "MtRoleMember"),
                    count, MyTeamsView.MaxMembers, MyTeamsView.Fit(count));
                RailRows.Children.Add(RailItem(team.Id, team.Name, line, Crests[i % Crests.Length], false));
            }
        }
        // With nothing at all the whole list goes: the card in the middle says what to do.
        if (invites.Count + teams.Count == 0 && _selection != NewSelection) return;
        if (RailRows.Children.Count == 0) RailRows.Children.Add(new Border { Height = 1 });
    }

    private string InvitedByLine(TeamInvite invite)
    {
        var inviter = InviterName(invite);
        return inviter == null ? Strings.Get("MtInvited") : Strings.Format("MtInvitedBy", inviter);
    }

    /// <summary>The server sends who invited as an id: the name comes with the team's roster.</summary>
    private string? InviterName(TeamInvite invite)
    {
        if (string.IsNullOrEmpty(invite.TeamId) || !_inviteTeams.TryGetValue(invite.TeamId, out var team)) return null;
        var member = team.Members?.FirstOrDefault(m => m.UserId == invite.InvitedBy);
        return member == null ? null : MyTeamsView.NameOf(member);
    }

    private FrameworkElement RailHead(string key, int count)
    {
        var head = new TextBlock { Margin = new Thickness(10, 10, 10, 8), FontWeight = FontWeights.SemiBold, Foreground = Hex("#A8BCD2") };
        head.Inlines.Add(new System.Windows.Documents.Run(Strings.Get(key) + "  "));
        head.Inlines.Add(new System.Windows.Documents.Run(count.ToString()) { FontFamily = new FontFamily("Consolas, Courier New, monospace"), Foreground = Hex("#8C9CB1") });
        head.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
        ModDialogChrome.SetTightLine(12.0 / 11, head);
        return head;
    }

    private Button RailItem(string selection, string name, string line, string? crest, bool invite)
    {
        var on = _selection == selection;
        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(invite ? 1 : 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0x82, 0xAF, 0xFF)),
            Background = invite ? Brushes.Transparent : Hex(crest!),
            Child = new TextBlock { Text = EnrollView.CrestOf(name), FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = invite ? Hex("#A8BCD2") : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, Foreground = on ? Brushes.White : Hex("#DCE7F5"), TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.FontSizeProperty, "SetRowTitleSize");
        var sub = new TextBlock { Text = line, Margin = new Thickness(0, 3, 0, 0), Foreground = Hex("#A8BCD2"), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(title);
        words.Children.Add(sub);
        Grid.SetColumn(words, 1);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(tile);
        grid.Children.Add(words);
        if (invite)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(10, 0, 0, 0), Fill = Hex("#55ACEE"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dot, 2);
            grid.Children.Add(dot);
        }
        var row = new Button { Content = grid, Height = 52, Margin = new Thickness(0, 0, 0, 2), Background = on ? Hex("#233A5C") : Brushes.Transparent, BorderBrush = Brushes.Transparent, Padding = new Thickness(9, 0, 9, 0), Tag = "TeamRailItem" };
        row.SetResourceReference(StyleProperty, "GlassChoiceRow");
        System.Windows.Automation.AutomationProperties.SetName(row, name);
        row.Click += (_, _) => Select(selection);
        return row;
    }

    private void PaintHead(string name, string? tag, string sub, string crest, string fill, bool hollow, IReadOnlyList<TeamFormatChip> chips)
    {
        HeadCrest.Background = hollow ? Brushes.Transparent : Hex(fill);
        HeadCrest.BorderBrush = hollow ? new SolidColorBrush(Color.FromArgb(0x73, 0x82, 0xAF, 0xFF)) : Brushes.Transparent;
        HeadCrestText.Text = crest;
        HeadCrestText.Foreground = hollow ? Hex("#A8BCD2") : Brushes.White;
        HeadName.Text = name;
        HeadTag.Visibility = string.IsNullOrWhiteSpace(tag) ? Visibility.Collapsed : Visibility.Visible;
        HeadTagText.Text = tag;
        HeadSub.Text = sub;
        HeadChips.Children.Clear();
        foreach (var chip in chips) HeadChips.Children.Add(BuildChip(chip));
    }

    private static FrameworkElement BuildChip(TeamFormatChip chip)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Hex(chip.Ok ? "#4FD68A" : "#F4900C"), VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = chip.Text, Margin = new Thickness(6, 0, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Hex(chip.Ok ? "#DCE7F5" : "#F5BD76"), VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
        line.Children.Add(text);
        return new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            Child = line,
            ToolTip = TooltipHelper.Wrap(chip.Tip),
        };
    }

    private string CrestColor(TeamSummary team)
    {
        var index = Teams.FindIndex(t => t.Id == team.Id);
        return Crests[Math.Max(0, index) % Crests.Length];
    }

    private void RenderTeam(TeamSummary team)
    {
        var captain = IsCaptainOf(team);
        var roster = MyTeamsView.Roster(team, _me, _isOnline);
        var captainName = roster.FirstOrDefault(m => m.UserId == team.OwnerUserId) is { } lead ? MyTeamsView.NameOf(lead) : "?";
        PaintHead(team.Name, team.Tag, captain ? Strings.Get("MtYouAreCaptain") : Strings.Format("MtCaptainIs", captainName),
            EnrollView.CrestOf(team.Name), CrestColor(team), false, MyTeamsView.Chips(roster.Count));

        TeamBody.Children.Clear();
        TeamBody.Children.Add(PlayersHead(roster.Count, captain, team));
        TeamBody.Children.Add(MembersCard(team, roster, captain));

        var sent = captain ? (_data?.SentInvites ?? new List<TeamSentInvite>()).Where(s => s.TeamId == team.Id).ToList() : new List<TeamSentInvite>();
        if (sent.Count > 0)
        {
            TeamBody.Children.Add(SentHead());
            TeamBody.Children.Add(SentCard(sent));
        }
        TeamBody.Children.Add(Hint(captain ? Strings.Format("MtHintCaptain", MyTeamsView.MaxMembers) : Strings.Format("MtHintMember", captainName)));

        DangerButton.Visibility = Visibility.Visible;
        DangerButton.Content = Strings.Get(captain ? "MtDisband" : "MtLeave");
        InviteBlock.Text = "";
        InviteBlock.Margin = new Thickness(8, 0, 8, 0);
        DeclineButton.Visibility = AcceptButton.Visibility = Visibility.Collapsed;
    }

    private void RenderInvite(TeamInvite invite)
    {
        _inviteTeams.TryGetValue(invite.TeamId ?? "", out var team);
        var roster = team == null ? new List<TeamMember>() : MyTeamsView.Roster(team, _me, _isOnline).ToList();
        var ago = MyTeamsView.Ago(invite.CreatedAt, DateTimeOffset.UtcNow);
        var sub = InvitedByLine(invite) + (ago == null ? "" : " · " + ago);
        PaintHead(invite.TeamName ?? "", invite.TeamTag, sub, EnrollView.CrestOf(invite.TeamName), "#4F6A8A", false,
            team == null ? Array.Empty<TeamFormatChip>() : MyTeamsView.Chips(roster.Count));

        TeamBody.Children.Clear();
        var full = roster.Count >= MyTeamsView.MaxMembers;
        if (team != null)
        {
            TeamBody.Children.Add(PlayersHead(roster.Count, false, team));
            TeamBody.Children.Add(MembersCard(team, roster, false));
        }
        TeamBody.Children.Add(Hint(Strings.Get(full ? "MtHintInviteFull" : "MtHintInvite")));

        DangerButton.Visibility = Visibility.Collapsed;
        InviteBlock.Text = full ? Strings.Format("MtInviteFull", invite.TeamName ?? "", MyTeamsView.MaxMembers) : "";
        InviteBlock.Margin = new Thickness(0, 0, 8, 0);
        DeclineButton.Visibility = AcceptButton.Visibility = Visibility.Visible;
        AcceptButton.IsEnabled = !full;
    }

    private FrameworkElement PlayersHead(int count, bool captain, TeamSummary team)
    {
        var grid = new Grid { MinHeight = 32, Margin = new Thickness(4, 0, 4, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Hex("#A8BCD2") };
        label.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("MtPlayers") + "  "));
        label.Inlines.Add(new System.Windows.Documents.Run(Strings.Format("MtOf", count, MyTeamsView.MaxMembers)) { FontFamily = new FontFamily("Consolas, Courier New, monospace"), Foreground = Hex("#8C9CB1") });
        label.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
        grid.Children.Add(label);
        if (captain && count >= MyTeamsView.MaxMembers)
        {
            var full = new TextBlock { Text = Strings.Get("MtTeamFull"), Foreground = Hex("#8C9CB1"), VerticalAlignment = VerticalAlignment.Center };
            full.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            Grid.SetColumn(full, 2);
            grid.Children.Add(full);
        }
        else if (captain)
        {
            var invite = new Button { Name = "InviteButton", Content = Strings.Get("MtInvite"), MinWidth = 112, Padding = new Thickness(16, 0, 16, 0) };
            invite.SetResourceReference(StyleProperty, "WindowPrimaryButton");
            invite.SetResourceReference(Control.FontSizeProperty, "SetRowDescSize");
            invite.Click += (_, _) => OpenSheet(team);
            Grid.SetColumn(invite, 2);
            grid.Children.Add(invite);
        }
        return grid;
    }

    private static Border CardOf(UIElement child) => new()
    {
        Child = child,
        Tag = "TeamCard",
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.FindResource("GlassPanel"),
        BorderBrush = (Brush)Application.Current.FindResource("WindowCardRim"),
    };

    private FrameworkElement MembersCard(TeamSummary team, IReadOnlyList<TeamMember> roster, bool captain)
    {
        var rows = new StackPanel();
        for (var i = 0; i < roster.Count; i++) rows.Children.Add(MemberRow(team, roster[i], captain, i == roster.Count - 1));
        return CardOf(rows);
    }

    private FrameworkElement MemberRow(TeamSummary team, TeamMember member, bool viewerCaptain, bool last)
    {
        var name = MyTeamsView.NameOf(member);
        var isMe = member.UserId == _me;
        var isCaptain = member.UserId == team.OwnerUserId;
        var online = isMe || _isOnline(member.UserId);

        var title = new TextBlock { Text = name, MaxWidth = 200, FontWeight = FontWeights.SemiBold, Foreground = Hex("#F0F5FB"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontSizeProperty, "SetRowTitleSize");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0) };
        line.Children.Add(title);
        if (isMe)
        {
            var you = new TextBlock { Text = Strings.Get("MtYou"), Margin = new Thickness(8, 0, 0, 0), Foreground = Hex("#8C9CB1"), VerticalAlignment = VerticalAlignment.Center };
            you.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
            line.Children.Add(you);
        }
        if (isCaptain)
        {
            var text = new TextBlock { Text = Strings.Get("MtRoleCaptain"), FontWeight = FontWeights.SemiBold, Foreground = Hex("#DCE7F5"), VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
            line.Children.Add(new Border
            {
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(8, 0, 0, 0),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                Child = text,
            });
        }
        Grid.SetColumn(line, 1);

        var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        state.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Hex(online ? "#4FD68A" : "#61779A"), VerticalAlignment = VerticalAlignment.Center });
        var word = new TextBlock { Text = Strings.Get(online ? "MtOnline" : "MtOffline"), Margin = new Thickness(7, 0, 0, 0), Foreground = Hex(online ? "#8FE0B0" : "#8C9CB1"), VerticalAlignment = VerticalAlignment.Center };
        word.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        state.Children.Add(word);
        Grid.SetColumn(state, 2);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = MultiplayerTab.BuildAvatarDisc(name, member.AvatarUrl, 30);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(avatar);
        grid.Children.Add(line);
        grid.Children.Add(state);

        var row = new Border
        {
            Height = 48,
            Padding = new Thickness(14, 0, 10, 0),
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            Child = grid,
        };

        if (viewerCaptain)
        {
            var remove = new Button { Content = Strings.Get("MtRemove"), Height = 30, MinWidth = 84, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), Foreground = Hex("#F0A6A6"), Opacity = 0, IsHitTestVisible = false };
            remove.SetResourceReference(StyleProperty, "WindowSecondaryButton");
            remove.SetResourceReference(Control.FontSizeProperty, "SetControlSize");
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);
            if (!isMe)
            {
                remove.Click += async (_, _) => await RemoveAsync(team, member);
                row.MouseEnter += (_, _) =>
                {
                    row.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                    remove.Opacity = 1;
                    remove.IsHitTestVisible = true;
                };
                row.MouseLeave += (_, _) =>
                {
                    row.Background = Brushes.Transparent;
                    remove.Opacity = 0;
                    remove.IsHitTestVisible = false;
                };
                // The keyboard reaches it too: a button only the mouse can find is half a button.
                remove.GotKeyboardFocus += (_, _) => remove.Opacity = 1;
                remove.LostKeyboardFocus += (_, _) => { if (!row.IsMouseOver) remove.Opacity = 0; };
            }
            else
            {
                remove.Visibility = Visibility.Hidden;
            }
        }
        return row;
    }

    private static FrameworkElement SentHead()
    {
        var line = new TextBlock { MinHeight = 32, Margin = new Thickness(4, 0, 4, 8), Padding = new Thickness(0, 9, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Hex("#A8BCD2") };
        line.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("MtSent") + "   "));
        line.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("MtSentHint")) { FontWeight = FontWeights.Normal, Foreground = Hex("#8C9CB1") });
        line.SetResourceReference(TextBlock.FontSizeProperty, "SetCaptionSize");
        return line;
    }

    private FrameworkElement SentCard(IReadOnlyList<TeamSentInvite> sent)
    {
        var rows = new StackPanel();
        for (var i = 0; i < sent.Count; i++)
        {
            var invite = sent[i];
            var name = invite.InvitedName ?? invite.InvitedUserId ?? "?";
            var disc = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x82, 0xAF, 0xFF)),
                Child = new TextBlock { Text = name[..1].ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Hex("#A8BCD2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var title = new TextBlock { Text = name, Margin = new Thickness(12, 0, 12, 0), FontWeight = FontWeights.SemiBold, Foreground = Hex("#C3D2E5"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
            Grid.SetColumn(title, 1);
            var ago = MyTeamsView.Ago(invite.CreatedAt, DateTimeOffset.UtcNow);
            var when = new TextBlock { Text = ago == null ? Strings.Get("MtSentNow") : Strings.Format("MtSentWhen", ago), Foreground = Hex("#8C9CB1"), VerticalAlignment = VerticalAlignment.Center };
            when.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            Grid.SetColumn(when, 2);
            var cancel = new Button { Name = "CancelInviteButton", Content = Strings.Get("MtCancelInvite"), Height = 28, MinWidth = 0, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
            cancel.SetResourceReference(StyleProperty, "WindowSecondaryButton");
            cancel.SetResourceReference(Control.FontSizeProperty, "SetControlSize");
            cancel.Click += async (_, _) => await RunAsync(() => _api.DeclineTeamInviteAsync(invite.Id), Strings.Format("MtDoneCancelled", name));
            Grid.SetColumn(cancel, 3);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(disc);
            grid.Children.Add(title);
            grid.Children.Add(when);
            grid.Children.Add(cancel);
            rows.Children.Add(new Border
            {
                Height = 44,
                Padding = new Thickness(14, 0, 10, 0),
                BorderThickness = new Thickness(0, 0, 0, i < sent.Count - 1 ? 1 : 0),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                Child = grid,
            });
        }
        return CardOf(rows);
    }

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, Margin = new Thickness(4, 2, 4, 0), TextWrapping = TextWrapping.Wrap, Foreground = Hex("#A8BCD2") };
        hint.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        ModDialogChrome.SetTightLine(1.5, hint);
        return hint;
    }

    private void RenderCenter()
    {
        var (title, body, button, primary) = _state switch
        {
            LoadState.Loading => (Strings.Get("MpTeamsLoading"), "", null, false),
            LoadState.Unavailable => (Strings.Get("MpTeamsUnavailable"), "", null, false),
            LoadState.SignedOut => (Strings.Get("MtSignInTitle"), Strings.Get("MtSignInBody"), Strings.Get("MtSignIn"), true),
            LoadState.Offline => (Strings.Get("MtOfflineTitle"), Strings.Get("MtOfflineBody"), Strings.Get("MtRetry"), false),
            LoadState.Failed => (Strings.Get("MtFailedTitle"), Strings.Get("MtFailedBody"), Strings.Get("MtRetry"), false),
            _ => (Strings.Get("MtEmptyTitle"), Strings.Get("MtEmptyBody"), Strings.Get("MtCreate"), true),
        };
        CenterTitle.Text = title;
        CenterBody.Text = body;
        CenterBody.Visibility = body.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CenterButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        CenterButton.Content = button;
        CenterButton.SetResourceReference(StyleProperty, primary ? "WindowPrimaryButton" : "WindowSecondaryButton");
        CenterButton.SetResourceReference(Control.FontSizeProperty, "SetRowDescSize");
    }

    private async void CenterButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case LoadState.SignedOut:
                Close();
                _signIn?.Invoke();
                break;
            case LoadState.Ready:
                Select(NewSelection);
                break;
            default:
                _state = LoadState.Loading;
                Render();
                await LoadAsync();
                break;
        }
    }

    // ------------------------------------------------------------------- a new team

    private void NewTeamButton_Click(object sender, RoutedEventArgs e)
    {
        NameEntry.Text = "";
        TagEntry.Text = "";
        Select(NewSelection);
        NameEntry.Focus();
    }

    private void RefreshForm()
    {
        var name = NameEntry.Text.Trim();
        var tag = TagEntry.Text.Trim();
        var has = name.Length > 0;
        PaintHead(has ? name : Strings.Get("MtNewTeam"), tag, Strings.Get(has ? "MtNewTeam" : "MtNewHint"),
            has ? EnrollView.CrestOf(name) : "+", "#3F5F8A", !has, Array.Empty<TeamFormatChip>());
        NameCount.Text = $"{NameEntry.Text.Length}/{NameEntry.MaxLength}";
        NamePlaceholder.Visibility = NameEntry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TagPlaceholder.Visibility = TagEntry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var block = MyTeamsView.NameBlock(name, MinNameLength);
        NewBlock.Text = block ?? "";
        CreateButton.IsEnabled = block == null && !_sending;
        CreateButton.ToolTip = block == null ? null : TooltipHelper.Wrap(block);
    }

    private void Form_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized && CreateButton != null && _selection == NewSelection) RefreshForm();
    }

    private void NewCancelButton_Click(object sender, RoutedEventArgs e) => Select(null);

    private async void CreateButton_Click(object sender, RoutedEventArgs e) => await CreateAsync();

    internal async Task CreateAsync()
    {
        var name = NameEntry.Text.Trim();
        var tag = TagEntry.Text.Trim();
        if (_sending || MyTeamsView.NameBlock(name, MinNameLength) != null) return;
        _sending = true;
        RefreshForm();
        try
        {
            var created = await _api.CreateTeamAsync(new { name, tag = string.IsNullOrEmpty(tag) ? null : tag });
            Changed = true;
            _note = Strings.Get("MtDoneCreated");
            _noteIsError = false;
            _selection = created.Id;
        }
        catch (LobbyApiException ex)
        {
            // The form stays with what was typed: a name in use is corrected, not retyped.
            _note = MultiplayerTab.TournamentErrorText(ex);
            _noteIsError = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _note = Strings.Get("MtOfflineTitle");
            _noteIsError = true;
        }
        finally
        {
            _sending = false;
        }
        await LoadAsync();
    }

    // ------------------------------------------------------------------- asking first

    private async Task RemoveAsync(TeamSummary team, TeamMember member)
    {
        var name = MyTeamsView.NameOf(member);
        if (!await MpAlertOverlay.ConfirmAsync(Root, Strings.Format("MtRemoveTitle", name, team.Name), Strings.Get("MtRemoveBody"),
                Strings.Get("MtRemoveYes"), Strings.Get("MtCancel"), danger: true)) return;
        await RunAsync(() => _api.RemoveTeamMemberAsync(team.Id, member.UserId), Strings.Format("MtDoneRemoved", name));
    }

    private async void DangerButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTeam is not { } team) return;
        if (IsCaptainOf(team))
        {
            if (!await MpAlertOverlay.ConfirmAsync(Root, Strings.Format("MtDisbandTitle", team.Name), Strings.Format("MtDisbandBody", team.Name),
                    Strings.Get("MtDisbandYes"), Strings.Get("MtCancel"), danger: true)) return;
            await RunAsync(() => _api.DisbandTeamAsync(team.Id), Strings.Format("MtDoneDisbanded", team.Name), "");
            return;
        }
        var captain = team.Members?.FirstOrDefault(m => m.UserId == team.OwnerUserId);
        if (!await MpAlertOverlay.ConfirmAsync(Root, Strings.Format("MtLeaveTitle", team.Name),
                Strings.Format("MtLeaveBody", captain == null ? "?" : MyTeamsView.NameOf(captain)),
                Strings.Get("MtLeaveYes"), Strings.Get("MtCancel"), danger: true)) return;
        await RunAsync(() => _api.RemoveTeamMemberAsync(team.Id, _me!), Strings.Format("MtDoneLeft", team.Name), "");
    }

    private async void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInvite is not { } invite) return;
        await RunAsync(() => _api.AcceptTeamInviteAsync(invite.Id), Strings.Get("MtDoneJoined"), invite.TeamId);
    }

    private async void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInvite is not { } invite) return;
        await RunAsync(() => _api.DeclineTeamInviteAsync(invite.Id), Strings.Format("MtDoneDeclined", invite.TeamName ?? ""), "");
    }

    private void NoteClose_Click(object sender, RoutedEventArgs e)
    {
        _note = null;
        Render();
    }

    // ------------------------------------------------------------------- inviting several

    private int FreePlaces(TeamSummary team) => MyTeamsView.MaxMembers - (team.Members?.Count ?? 0);

    private void OpenSheet(TeamSummary team)
    {
        _picked.Clear();
        _found = Array.Empty<PickCandidate>();
        SearchEntry.Text = "";
        SheetTitle.Text = Strings.Format("MtSheetTitle", team.Name);
        var free = FreePlaces(team);
        SheetFree.Text = free == 1 ? Strings.Get("MtFreeOne") : Strings.Format("MtFree", free);
        SheetLayer.Visibility = Visibility.Visible;
        PaintSheet(team);
        SearchEntry.Focus();
    }

    private void CloseSheet()
    {
        if (SheetLayer.Visibility != Visibility.Visible) return;
        SheetLayer.Visibility = Visibility.Collapsed;
        _searchTimer.Stop();
    }

    private void PaintSheet(TeamSummary team)
    {
        var free = FreePlaces(team);
        var count = _picked.Count;
        SheetCount.Text = Strings.Format("MtOf", count, free);
        SheetCount.Foreground = Hex(count > 0 ? "#8FE0B0" : "#8C9CB1");
        SheetChips.Children.Clear();
        SheetChips.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var player in _picked.ToList()) SheetChips.Children.Add(PickedChip(player, team));

        var query = SearchEntry.Text.Trim();
        var inTeam = new HashSet<string>((team.Members ?? new()).Select(m => m.UserId), StringComparer.Ordinal) { _me ?? "" };
        var pending = new HashSet<string>((_data?.SentInvites ?? new()).Where(s => s.TeamId == team.Id).Select(s => s.InvitedUserId ?? ""), StringComparer.Ordinal);
        var online = _online().Where(c => !inTeam.Contains(c.Id) && (query.Length == 0 || c.Label.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        var ids = new HashSet<string>(online.Select(c => c.Id), StringComparer.Ordinal);
        var others = query.Length == 0 ? new List<PickCandidate>() : _found.Where(c => !inTeam.Contains(c.Id) && !ids.Contains(c.Id)).ToList();

        SheetList.Children.Clear();
        void Group(string key, IReadOnlyList<PickCandidate> list, bool live)
        {
            if (list.Count == 0) return;
            var head = new TextBlock { Text = Strings.Get(key), FontWeight = FontWeights.Bold, Margin = new Thickness(4, 8, 4, 3), Foreground = Hex("#8C9CB1") };
            head.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
            SheetList.Children.Add(head);
            foreach (var player in list) SheetList.Children.Add(SheetRow(team, player, live, pending.Contains(player.Id)));
        }
        Group("MtGroupOnline", online, true);
        Group("MtGroupOther", others, false);
        var note = query.Length == 0 ? Strings.Get("MtSearchHint")
            : online.Count + others.Count == 0 ? Strings.Format("MtNoPlayer", query) : null;
        if (note != null)
        {
            var line = new TextBlock { Text = note, Margin = new Thickness(4, 8, 4, 2), TextWrapping = TextWrapping.Wrap, Foreground = Hex("#8C9CB1") };
            line.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            SheetList.Children.Add(line);
        }

        SheetBlock.Text = count == 0 ? Strings.Get("MtBlockPick") : "";
        SheetOk.IsEnabled = count > 0 && !_sending;
        SheetOk.Content = _sending ? Strings.Get("MtInviting")
            : count == 0 ? Strings.Get("MtInviteNone")
            : count == 1 ? Strings.Format("MtInviteOne", _picked[0].Label)
            : Strings.Format("MtInviteMany", count);
    }

    private FrameworkElement PickedChip(PickCandidate player, TeamSummary team)
    {
        var disc = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Hex("#3F6A8A"),
            Child = new TextBlock { Text = player.Label[..1].ToUpperInvariant(), FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = player.Label, Margin = new Thickness(7, 0, 7, 0), FontWeight = FontWeights.SemiBold, Foreground = Hex("#F0F5FB"), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(disc);
        line.Children.Add(label);
        line.Children.Add(new TextBlock { Text = "✕", FontSize = 11, Foreground = Hex("#8C9CB1"), VerticalAlignment = VerticalAlignment.Center });
        var chip = new Border
        {
            Height = 28,
            Padding = new Thickness(4, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = Hex("#233A5C"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x82, 0xAF, 0xFF)),
            Child = line,
            Cursor = Cursors.Hand,
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            _picked.Remove(player);
            PaintSheet(team);
        };
        return chip;
    }

    private Button SheetRow(TeamSummary team, PickCandidate player, bool online, bool pending)
    {
        var on = _picked.Any(p => p.Id == player.Id);
        var full = !on && _picked.Count >= FreePlaces(team);
        var tick = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = on ? Hex("#2F7FE0") : Brushes.Transparent,
            BorderBrush = on ? Hex("#2F7FE0") : new SolidColorBrush(Color.FromArgb(0x73, 0x82, 0xAF, 0xFF)),
            Child = on ? new TextBlock { Text = "✓", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        var avatar = MultiplayerTab.BuildAvatarDisc(player.Label, null, 26);
        avatar.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(avatar, 1);
        var name = new TextBlock { Text = player.Label, FontWeight = FontWeights.SemiBold, Foreground = Hex("#F0F5FB"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        Grid.SetColumn(name, 2);
        var state = new TextBlock
        {
            Text = Strings.Get(pending ? "MtAlreadyInvited" : online ? "MtOnline" : "MtOffline"),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Hex(pending ? "#8C9CB1" : online ? "#8FE0B0" : "#A8BCD2"),
        };
        state.SetResourceReference(TextBlock.FontSizeProperty, "SetDescSize");
        Grid.SetColumn(state, 3);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(tick);
        grid.Children.Add(avatar);
        grid.Children.Add(name);
        grid.Children.Add(state);
        var row = new Button
        {
            Content = grid,
            Height = 40,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(9, 0, 9, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsHitTestVisible = !pending && !full,
            Opacity = pending ? .55 : 1,
            Background = on ? Hex("#233A5C") : Hex("#1A2C48"),
            BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x8C, 0x82, 0xAF, 0xFF) : Color.FromArgb(0x38, 0x82, 0xAF, 0xFF)),
        };
        row.SetResourceReference(StyleProperty, "GlassChoiceRow");
        System.Windows.Automation.AutomationProperties.SetName(row, player.Label);
        row.Click += (_, _) =>
        {
            if (on) _picked.RemoveAll(p => p.Id == player.Id);
            else _picked.Add(player);
            PaintSheet(team);
        };
        return row;
    }

    private void SearchEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || SheetOk == null) return;
        SearchPlaceholder.Visibility = SearchEntry.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (SheetLayer.Visibility != Visibility.Visible || SelectedTeam is not { } team) return;
        _found = Array.Empty<PickCandidate>();
        PaintSheet(team);
        _searchTimer.Stop();
        if (_search != null && SearchEntry.Text.Trim().Length >= 2) _searchTimer.Start();
    }

    private async Task RunSearchAsync()
    {
        var query = SearchEntry.Text.Trim();
        try
        {
            var found = await _search!(query);
            if (query != SearchEntry.Text.Trim() || SelectedTeam is not { } team) return;
            _found = found;
            PaintSheet(team);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MyTeams: player search failed: {ex.Message}");
        }
    }

    private void SheetCancel_Click(object sender, RoutedEventArgs e) => CloseSheet();

    private async void SheetOk_Click(object sender, RoutedEventArgs e) => await SendInvitesAsync();

    /// <summary>One request per player, as the server takes them. The first refusal stops the rest
    /// and is named, so a player told "sent" really was invited.</summary>
    internal async Task SendInvitesAsync()
    {
        if (_sending || _picked.Count == 0 || SelectedTeam is not { } team) return;
        _sending = true;
        PaintSheet(team);
        var sent = new List<string>();
        string? failure = null;
        foreach (var player in _picked)
        {
            try
            {
                await _api.InviteToTeamAsync(team.Id, player.Id);
                sent.Add(player.Label);
            }
            catch (LobbyApiException ex)
            {
                failure = Strings.Format("MtInviteFailed", player.Label, MultiplayerTab.TournamentErrorText(ex));
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failure = Strings.Format("MtInviteFailed", player.Label, Strings.Get("MtOfflineTitle"));
                break;
            }
        }
        _sending = false;
        if (sent.Count > 0) Changed = true;
        _note = failure ?? Strings.Format(sent.Count == 1 ? "MtDoneInvitedOne" : "MtDoneInvitedMany", EnrollView.JoinNames(sent));
        _noteIsError = failure != null;
        CloseSheet();
        await LoadAsync();
    }

    private static SolidColorBrush Hex(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    // ------------------------------------------------------------------- previews

    /// <summary>A prototype state, for the harness: nothing here reaches a server.</summary>
    internal void ShowPreview(MyTeamsResponse? data, IReadOnlyDictionary<string, TeamSummary> inviteTeams, string? selection, string state, string? note)
    {
        _data = data;
        foreach (var pair in inviteTeams) _inviteTeams[pair.Key] = pair.Value;
        _selection = selection;
        _state = state switch
        {
            "failed" => LoadState.Failed,
            "offline" => LoadState.Offline,
            "signedout" => LoadState.SignedOut,
            _ => LoadState.Ready,
        };
        _note = note;
        _noteIsError = note != null;
        Render();
        if (state == "invite" && SelectedTeam is { } team) OpenSheet(team);
    }
}
