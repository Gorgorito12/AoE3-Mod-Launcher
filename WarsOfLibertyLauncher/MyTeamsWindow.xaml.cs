using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// "My teams": the invitations waiting for me, my teams and their members, and what a captain
/// may do — invite, remove, disband — plus creating a team.
///
/// <para><b>Why it exists.</b> A REGISTERED team tournament is entered with a saved team, and the
/// launcher had the whole team API and no screen for any of it, so nobody could ever have a team
/// to enter with. Invitations persist on the server (inviting somebody who is at work is the
/// normal case), so they are listed here and also arrive as a push.</para>
///
/// <para><b>Every call goes through the tournament server the tab hands in</b>
/// (<see cref="ITournamentApi"/>): in the preview that is the simulator, so an invitation sent from
/// the preview can never reach a real person.</para>
/// </summary>
public partial class MyTeamsWindow : Window
{
    /// <summary>The server's cap on members per team (teams/rest.ts TEAM_MAX_MEMBERS).</summary>
    internal const int TeamMaxMembers = 6;

    private readonly ITournamentApi _api;
    private readonly string? _me;
    private readonly Func<IReadOnlyList<PickCandidate>> _online;
    private readonly Func<string, Task<IReadOnlyList<PickCandidate>>>? _search;

    private MyTeamsResponse? _data;
    private bool _unavailable;
    private string? _error;
    private string? _notice;

    /// <summary>Whether anything changed, so the tab refreshes what depends on teams.</summary>
    internal bool Changed { get; private set; }

    internal MyTeamsWindow(
        ITournamentApi api,
        string? me,
        Func<IReadOnlyList<PickCandidate>> online,
        Func<string, Task<IReadOnlyList<PickCandidate>>>? search)
    {
        InitializeComponent();
        _api = api;
        _me = me;
        _online = online;
        _search = search;

        Title = Strings.Get("MpTeamsTitle");
        TitleBarControl.Title = Strings.Get("MpTeamsTitle");
        NewTeamButton.Content = Strings.Get("MpTeamsNew");
        CloseButton.Content = Strings.Get("MpTeamsClose");
        Render();
    }

    /// <summary>What is on screen, for the tests.</summary>
    internal MyTeamsResponse? Data => _data;

    /// <summary>Read the teams and invitations again and redraw.</summary>
    internal async Task LoadAsync()
    {
        try
        {
            _data = await _api.GetMyTeamsAsync();
            _unavailable = false;
        }
        catch (LobbyApiException ex) when (ex.Status == 404)
        {
            // A server older than teams: say so, never show it as a failure.
            _unavailable = true;
        }
        catch (LobbyApiException ex)
        {
            _error = MultiplayerTab.TournamentErrorText(ex);
        }
        catch
        {
            _error = Strings.Get("MpTeamsLoadFailed");
        }
        Render();
    }

    private async Task RunAsync(Func<Task> action, string? noticeOnSuccess = null)
    {
        _error = null;
        _notice = null;
        try
        {
            await action();
            Changed = true;
            _notice = noticeOnSuccess;
        }
        catch (LobbyApiException ex)
        {
            _error = MultiplayerTab.TournamentErrorText(ex);
        }
        catch
        {
            _error = Strings.Get("MpTournamentActionFailed");
        }
        await LoadAsync();
    }

    // ---------------------------------------------------------------- drawing

    internal void Render()
    {
        ContentPanel.Children.Clear();
        NewTeamButton.Visibility = _unavailable ? Visibility.Collapsed : Visibility.Visible;

        if (_unavailable)
        {
            ContentPanel.Children.Add(Body(Strings.Get("MpTeamsUnavailable"), "MpTextMuted"));
            return;
        }
        if (_error != null) ContentPanel.Children.Add(Banner(_error, "MpCautionText"));
        if (_notice != null) ContentPanel.Children.Add(Banner(_notice, "MpOkText"));

        if (_data == null)
        {
            ContentPanel.Children.Add(Body(Strings.Get("MpTeamsLoading"), "MpTextMuted"));
            return;
        }

        var invites = _data.Invites ?? new List<TeamInvite>();
        if (invites.Count > 0)
        {
            ContentPanel.Children.Add(Heading(Strings.Get("MpTeamsInvitesHeading")));
            foreach (var invite in invites) ContentPanel.Children.Add(BuildInviteRow(invite));
        }

        ContentPanel.Children.Add(Heading(Strings.Get("MpTeamsHeading")));
        var teams = (_data.Teams ?? new List<TeamSummary>()).Where(t => t.Disbanded != true).ToList();
        if (teams.Count == 0)
        {
            ContentPanel.Children.Add(Body(Strings.Get("MpTeamsEmpty"), "MpTextMuted"));
        }
        foreach (var team in teams) ContentPanel.Children.Add(BuildTeamCard(team));
    }

    private UIElement BuildInviteRow(TeamInvite invite)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = "TeamInvite" };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = Body(Strings.Format("MpTeamsInviteFrom", TeamLabel(invite.TeamName, invite.TeamTag)), "MpTextBody");
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);

        var accept = SmallButton(Strings.Get("MpTeamsAccept"), "MpPrimaryButton",
            () => RunAsync(() => _api.AcceptTeamInviteAsync(invite.Id), Strings.Get("MpTeamsAccepted")));
        Grid.SetColumn(accept, 1);
        grid.Children.Add(accept);

        var decline = SmallButton(Strings.Get("MpTeamsDecline"), "MpGhostButton",
            () => RunAsync(() => _api.DeclineTeamInviteAsync(invite.Id)));
        Grid.SetColumn(decline, 2);
        grid.Children.Add(decline);
        return grid;
    }

    private UIElement BuildTeamCard(TeamSummary team)
    {
        bool captain = !string.IsNullOrEmpty(_me) && string.Equals(team.OwnerUserId, _me, StringComparison.Ordinal);
        var members = team.Members ?? new List<TeamMember>();

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = TeamLabel(team.Name, team.Tag),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "MpTextHeading");
        name.SetResourceReference(TextBlock.FontSizeProperty, "MpLabelSize");
        head.Children.Add(name);
        var count = Body(Strings.Format("MpTeamsMemberCount", members.Count, TeamMaxMembers), "MpTextGhost");
        Grid.SetColumn(count, 1);
        head.Children.Add(count);
        stack.Children.Add(head);

        foreach (var m in members) stack.Children.Add(BuildMemberRow(team, m, captain));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (captain && members.Count < TeamMaxMembers)
        {
            actions.Children.Add(SmallButton(Strings.Get("MpTeamsInvite"), "MpGhostButton",
                () => InviteAsync(team)));
        }
        if (captain)
        {
            actions.Children.Add(SmallButton(Strings.Get("MpTeamsDisband"), "MpGhostDangerButton",
                () => DisbandAsync(team)));
        }
        if (actions.Children.Count > 0) stack.Children.Add(actions);

        var card = new Border
        {
            Child = stack,
            Tag = "TeamCard",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12, 10, 12, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
        };
        card.SetResourceReference(Border.BackgroundProperty, "MpPanel");
        card.SetResourceReference(Border.BorderBrushProperty, "MpRimSoft");
        return card;
    }

    private UIElement BuildMemberRow(TeamSummary team, TeamMember m, bool viewerIsCaptain)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool isCaptain = string.Equals(m.UserId, team.OwnerUserId, StringComparison.Ordinal);
        bool isMe = !string.IsNullOrEmpty(_me) && string.Equals(m.UserId, _me, StringComparison.Ordinal);
        var text = (m.DisplayName ?? m.DiscordUsername ?? m.UserId)
                   + (isCaptain ? " · " + Strings.Get("MpTeamsCaptain") : "")
                   + (isMe ? " · " + Strings.Get("MpTeamsYou") : "");
        var label = Body(text, "MpTextBody");
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);

        // The captain removes anybody but himself (he disbands instead); anybody else may leave.
        Button? action = null;
        if (viewerIsCaptain && !isCaptain)
        {
            action = SmallButton(Strings.Get("MpTeamsRemove"), "MpGhostButton",
                () => RunAsync(() => _api.RemoveTeamMemberAsync(team.Id, m.UserId)));
        }
        else if (!viewerIsCaptain && isMe)
        {
            action = SmallButton(Strings.Get("MpTeamsLeave"), "MpGhostButton",
                () => RunAsync(() => _api.RemoveTeamMemberAsync(team.Id, m.UserId)));
        }
        if (action != null)
        {
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }
        return grid;
    }

    // ---------------------------------------------------------------- actions

    private async Task InviteAsync(TeamSummary team)
    {
        var inTeam = new HashSet<string>((team.Members ?? new()).Select(m => m.UserId), StringComparer.Ordinal);
        var candidates = _online().Where(c => !inTeam.Contains(c.Id) && c.Id != _me).ToList();
        var dlg = new PlayerPickerDialog(
            Strings.Get("MpTeamsInviteTitle"),
            Strings.Format("MpTeamsInviteIntro", team.Name),
            Strings.Get("MpPickPlayers"),
            candidates,
            required: 1,
            okLabel: Strings.Get("MpTeamsInviteOk"),
            search: _search == null
                ? null
                : async q => (await _search(q)).Where(c => !inTeam.Contains(c.Id) && c.Id != _me).ToList());
        try { dlg.Owner = this; } catch { /* not shown yet in a test */ }
        if (dlg.ShowDialog() != true || dlg.SelectedIds.Count != 1) return;
        var target = dlg.SelectedIds[0];
        await RunAsync(() => _api.InviteToTeamAsync(team.Id, target), Strings.Get("MpTeamsInviteSent"));
    }

    private async Task DisbandAsync(TeamSummary team)
    {
        if (!ThemedConfirmDialog.Ask(this,
                Strings.Get("MpTeamsDisbandTitle"),
                Strings.Format("MpTeamsDisbandBody", team.Name),
                Strings.Get("MpTeamsDisband"),
                Strings.Get("BtnCancel"),
                ConfirmTone.Warning))
        {
            return;
        }
        await RunAsync(() => _api.DisbandTeamAsync(team.Id));
    }

    private async void NewTeamButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CreateTeamDialog();
        try { dlg.Owner = this; } catch { /* off-tree */ }
        if (dlg.ShowDialog() != true) return;
        var name = dlg.EnteredName;
        var tag = dlg.EnteredTag;
        await RunAsync(() => _api.CreateTeamAsync(new { name, tag = string.IsNullOrEmpty(tag) ? null : tag }),
            Strings.Get("MpTeamsCreated"));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- little builders

    private static string TeamLabel(string? name, string? tag)
        => string.IsNullOrWhiteSpace(tag) ? name ?? "" : $"{name} [{tag}]";

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 6, 0, 8), FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MpTextLabel");
        t.SetResourceReference(TextBlock.FontSizeProperty, "MpSectionLabelSize");
        return t;
    }

    private static TextBlock Body(string text, string brush)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontSizeProperty, "MpPillSize");
        return t;
    }

    private static Border Banner(string text, string brush)
    {
        var body = Body(text, brush);
        var b = new Border
        {
            Child = body,
            Tag = "TeamsBanner",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
        };
        b.SetResourceReference(Border.BackgroundProperty, "MpField");
        return b;
    }

    private static Button SmallButton(string label, string style, Func<Task> action)
    {
        var b = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.SetResourceReference(Control.FontSizeProperty, "MpPillSize");
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try { await action(); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }
}
