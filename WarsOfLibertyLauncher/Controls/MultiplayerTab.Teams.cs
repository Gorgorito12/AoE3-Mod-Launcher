using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// TEAM tournaments, from the player's side: entering a 2v2 or 3v3 in each of its three modes,
/// the organiser forming the teams of a draft, and "My teams".
///
/// <para><b>Why this file exists.</b> The Enter button posted an empty body for every format,
/// with a comment promising a team picker that did not exist — so the server refused every
/// registered and every ad-hoc entry, a draft could never be turned into teams, and the launcher
/// had the whole team API and no screen for any of it. No team tournament could be played from
/// the launcher at all.</para>
///
/// <para><b>Every call goes through <see cref="TournamentApi"/></b>, the teams' included, so the
/// preview answers them from its simulator and never reaches the real server — the rule
/// <c>TournamentPreviewTests.NoTournamentCallGoesAroundTheSwappableServer</c> enforces.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Team invitations waiting for me, for the "My teams" button.</summary>
    private int _teamInviteCount;

    /// <summary>The server answered 404 to a search: it predates it, so stop offering the box.</summary>
    private bool _playerSearchUnavailable;

    // ---------------------------------------------------------------- entering

    /// <summary>
    /// Enter the tournament with the body its mode needs: none for a 1v1 or a draft (the server
    /// registers the caller alone), a saved team for a registered one, a line-up for an ad-hoc
    /// one. Nothing is sent when the player backs out of a question.
    /// </summary>
    private async Task EnterTournamentAsync(TournamentDetail t)
    {
        var me = TournamentViewerId(t);
        object? body = t.TeamSource switch
        {
            _ when string.Equals(t.Format, "1v1", StringComparison.Ordinal) => null,
            "registered" => await ChooseRegisteredEntryAsync(t, me),
            "adhoc" => await ChooseAdhocEntryAsync(t, me),
            _ => null,
        };
        bool asked = t.TeamSource is "registered" or "adhoc"
                     && !string.Equals(t.Format, "1v1", StringComparison.Ordinal);
        if (asked && body == null) return;
        await TournamentApi.EnterTournamentAsync(t.Id, body);
    }

    /// <summary>
    /// A registered team: one of MY teams that has enough players, and — when it has more than
    /// the format fields and the server can take the choice — who plays. Null when there is no
    /// such team or the player backed out.
    /// </summary>
    private async Task<object?> ChooseRegisteredEntryAsync(TournamentDetail t, string? me)
    {
        int size = TournamentPermissions.RosterSizeOf(t);
        var mine = await TournamentApi.GetMyTeamsAsync();
        var eligible = (mine.Teams ?? new List<TeamSummary>())
            .Where(x => x.Disbanded != true
                        && string.Equals(x.OwnerUserId, me, StringComparison.Ordinal)
                        && (x.Members?.Count ?? 0) >= size)
            .ToList();

        var owner = OwnerWindow();
        if (eligible.Count == 0)
        {
            if (ThemedConfirmDialog.Ask(owner,
                    Strings.Get("MpTournamentNeedTeamTitle"),
                    Strings.Format("MpTournamentNeedTeamBody", size),
                    Strings.Get("MpTournamentOpenMyTeams"),
                    Strings.Get("BtnCancel")))
            {
                await OpenMyTeamsAsync();
            }
            return null;
        }

        var team = eligible[0];
        if (eligible.Count > 1)
        {
            var pickTeam = new PlayerPickerDialog(
                Strings.Get("MpTournamentPickTeamTitle"),
                Strings.Format("MpTournamentPickTeamIntro", size),
                Strings.Get("MpTournamentPickTeamList"),
                eligible.Select(x => new PickCandidate(
                    x.Id, TeamLabel(x.Name, x.Tag),
                    string.Join(", ", (x.Members ?? new()).Select(m => m.DisplayName ?? m.UserId)))).ToList(),
                required: 1,
                okLabel: Strings.Get("MpTournamentPickTeamOk"));
            if (!Show(pickTeam) || pickTeam.SelectedIds.Count != 1) return null;
            team = eligible.First(x => x.Id == pickTeam.SelectedIds[0]);
        }

        List<string>? lineup = null;
        var members = team.Members ?? new List<TeamMember>();
        // The choice only when the server would honour it: an older one ignores `member_ids`
        // and takes the first players, and asking would then be a lie.
        if (members.Count > size && t.RosterSize != null)
        {
            var pickPlayers = new PlayerPickerDialog(
                Strings.Get("MpTournamentLineupTitle"),
                Strings.Format("MpTournamentLineupIntro", size, team.Name),
                Strings.Get("MpPickPlayers"),
                members.Select(m => new PickCandidate(
                    m.UserId, m.DisplayName ?? m.UserId,
                    Locked: string.Equals(m.UserId, me, StringComparison.Ordinal))).ToList(),
                required: size,
                okLabel: Strings.Get("MpTournamentEnter"));
            if (!Show(pickPlayers)) return null;
            lineup = pickPlayers.SelectedIds.ToList();
        }

        return new { team_id = team.Id, member_ids = lineup };
    }

    /// <summary>
    /// An ad-hoc line-up: the captain (fixed) and the players he names, plus the team's name.
    /// Players already in this tournament are listed but cannot be chosen, with the reason.
    /// </summary>
    private async Task<object?> ChooseAdhocEntryAsync(TournamentDetail t, string? me)
    {
        int size = TournamentPermissions.RosterSizeOf(t);
        var taken = new HashSet<string>(
            (t.Entrants ?? new List<TournamentEntrant>())
                .Where(e => e.Status is "pending" or "confirmed" or "waitlist")
                .SelectMany(e => e.MemberIds ?? new List<string>()),
            StringComparer.Ordinal);

        var candidates = new List<PickCandidate>();
        void Add(string id, string label)
        {
            if (string.IsNullOrEmpty(id) || candidates.Any(c => c.Id == id)) return;
            bool isMe = string.Equals(id, me, StringComparison.Ordinal);
            candidates.Add(new PickCandidate(id, label,
                Locked: isMe,
                Disabled: !isMe && taken.Contains(id),
                DisabledReason: Strings.Get("MpTournamentAlreadyIn")));
        }

        if (!string.IsNullOrEmpty(me)) Add(me!, MyDisplayName(me!));
        // The people I already play with first, then whoever is online.
        try
        {
            var mine = await TournamentApi.GetMyTeamsAsync();
            foreach (var team in mine.Teams ?? new List<TeamSummary>())
                foreach (var m in team.Members ?? new List<TeamMember>())
                    Add(m.UserId, m.DisplayName ?? m.UserId);
        }
        catch
        {
            // Teams are a convenience here; the online list and the search still work.
        }
        foreach (var c in OnlinePlayerCandidates()) Add(c.Id, c.Label);

        var search = PlayerSearch();
        var dlg = new PlayerPickerDialog(
            Strings.Get("MpTournamentAdhocTitle"),
            Strings.Format("MpTournamentAdhocIntro", size),
            Strings.Get("MpPickPlayers"),
            candidates,
            required: size,
            okLabel: Strings.Get("MpTournamentEnter"),
            nameLabel: Strings.Get("MpTournamentAdhocName"),
            search: search == null
                ? null
                : async q => (await search(q)).Select(c => c with
                {
                    Disabled = taken.Contains(c.Id),
                    DisabledReason = Strings.Get("MpTournamentAlreadyIn"),
                }).ToList());
        if (!Show(dlg)) return null;
        return new { member_ids = dlg.SelectedIds.ToList(), display_name = dlg.EnteredName };
    }

    // ---------------------------------------------------------------- draft

    /// <summary>The solo entrants of a draft that are still waiting for a team.</summary>
    internal static List<TournamentEntrant> DraftPlayersWithoutTeam(TournamentDetail t)
        => (t.Entrants ?? new List<TournamentEntrant>())
            .Where(e => string.Equals(e.Kind, "solo", StringComparison.Ordinal)
                        && e.Status is "pending" or "confirmed" or "waitlist")
            .ToList();

    /// <summary>The tag of the draft bar, so the tests find it.</summary>
    internal const string DraftTeamsBarTag = "TournamentDraftTeamsBar";

    /// <summary>
    /// "N players without a team" and the organiser's button to form one, above the entrant
    /// table of a draft tournament.
    /// </summary>
    private UIElement BuildDraftTeamsBar(TournamentDetail t, int withoutTeam)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var text = new System.Windows.Controls.TextBlock
        {
            Text = Strings.Format("MpTournamentWithoutTeam", withoutTeam, TournamentPermissions.RosterSizeOf(t)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MpCautionText");
        text.SetResourceReference(System.Windows.Controls.TextBlock.FontSizeProperty, "MpLabelSize");
        grid.Children.Add(text);

        var button = new System.Windows.Controls.Button { Content = Strings.Get("MpTournamentFormTeam") };
        button.SetResourceReference(FrameworkElement.StyleProperty, "MpPrimaryButton");
        // Enabled only when there are enough players for one team.
        button.IsEnabled = withoutTeam >= TournamentPermissions.RosterSizeOf(t);
        button.Click += (_, _) => { _ = RunTournamentActionAsync(() => FormDraftTeamAsync(t)); };
        System.Windows.Controls.Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        var bar = new System.Windows.Controls.Border
        {
            Child = grid,
            Tag = DraftTeamsBarTag,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusRow"),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        bar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "MpField");
        return bar;
    }

    /// <summary>The organiser folds exactly one side's worth of solo players into a team.</summary>
    private async Task FormDraftTeamAsync(TournamentDetail t)
    {
        int size = TournamentPermissions.RosterSizeOf(t);
        var solos = DraftPlayersWithoutTeam(t);
        var dlg = new PlayerPickerDialog(
            Strings.Get("MpTournamentFormTeamTitle"),
            Strings.Format("MpTournamentFormTeamIntro", size),
            Strings.Get("MpPickPlayers"),
            solos.Select(e => new PickCandidate(e.Id, e.DisplayName ?? e.Id)).ToList(),
            required: size,
            okLabel: Strings.Get("MpTournamentFormTeamOk"),
            nameLabel: Strings.Get("MpTournamentFormTeamName"),
            nameRequired: false);
        if (!Show(dlg)) return;
        var name = string.IsNullOrWhiteSpace(dlg.EnteredName) ? null : dlg.EnteredName;
        await TournamentApi.MergeTournamentEntrantsAsync(t.Id, dlg.SelectedIds.ToList(), name);
    }

    // ---------------------------------------------------------------- my teams

    private async void TournamentsMyTeams_Click(object sender, RoutedEventArgs e) => await OpenMyTeamsAsync();

    /// <summary>Open "My teams" on the tournament server in use.</summary>
    internal async Task OpenMyTeamsAsync()
    {
        if (TournamentApiOrNull is not { } api) return;
        var window = new MyTeamsWindow(api, TournamentViewerId(null), OnlinePlayerCandidates, PlayerSearch());
        try { window.Owner = OwnerWindow(); } catch { /* off-tree */ }
        _ = window.LoadAsync();
        window.ShowDialog();
        await RefreshTeamInvitesAsync();
        if (window.Changed) await RefreshTournamentsAsync(force: true);
    }

    /// <summary>How many team invitations are waiting, for the button's label. Silent on failure:
    /// a server older than teams simply has none.</summary>
    internal async Task RefreshTeamInvitesAsync()
    {
        if (TournamentApiOrNull is not { } api) return;
        if (!_demoTournaments && _session?.CurrentUser == null)
        {
            _teamInviteCount = 0;
            UpdateMyTeamsButton();
            return;
        }
        try
        {
            var mine = await api.GetMyTeamsAsync();
            if (!ReferenceEquals(api, TournamentApiOrNull)) return;
            _teamInviteCount = mine.Invites?.Count ?? 0;
        }
        catch
        {
            _teamInviteCount = 0;
        }
        UpdateMyTeamsButton();
    }

    private void UpdateMyTeamsButton()
    {
        if (TournamentsMyTeamsButton == null) return;
        TournamentsMyTeamsButton.Content = _teamInviteCount > 0
            ? Strings.Format("MpTeamsButtonInvites", _teamInviteCount)
            : Strings.Get("MpTeamsButton");
    }

    /// <summary>
    /// Somebody invited me to a team (<c>team_invite</c> on the global socket). Only a doorbell:
    /// the invitation itself is a row, waiting in "My teams" whether or not this arrives.
    /// </summary>
    private void HandleTeamInviteFrame(string json)
    {
        TeamInviteNotice? n;
        try { n = JsonSerializer.Deserialize<TeamInviteNotice>(json); }
        catch { return; }
        if (n == null) return;

        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "\U0001F465",
            Strings.Get("MpTeamsInviteToastTitle"),
            Strings.Format("MpTeamsInviteToastBody", n.TeamName ?? ""),
            new[]
            {
                new AppToast.ToastAction(Strings.Get("MpTeamsInviteToastOpen"), IsPrimary: true,
                    OnClick: () => { _ = OpenMyTeamsAsync(); }),
            },
            PreferDesktop: true));
        _ = RefreshTeamInvitesAsync();
    }

    // ---------------------------------------------------------------- players to choose from

    /// <summary>Everybody online right now, as picker candidates.</summary>
    internal IReadOnlyList<PickCandidate> OnlinePlayerCandidates()
        => _globalOnlineUsers
            .Where(u => !string.IsNullOrEmpty(u.UserId))
            .Select(u => new PickCandidate(u.UserId, string.IsNullOrEmpty(u.Login) ? u.UserId : u.Login))
            .ToList();

    /// <summary>
    /// The search the pickers offer, or null when the server cannot search (it answered 404
    /// once) — a box that never finds anybody reads as broken.
    /// </summary>
    private Func<string, Task<IReadOnlyList<PickCandidate>>>? PlayerSearch()
    {
        if (_playerSearchUnavailable || TournamentApiOrNull is not { } api) return null;
        return async q =>
        {
            try
            {
                var found = await api.SearchPlayersAsync(q);
                return (found.Users ?? new List<PlayerSearchResult>())
                    .Select(u => new PickCandidate(u.UserId, u.DisplayName ?? u.UserId))
                    .ToList();
            }
            catch (LobbyApiException ex) when (ex.Status == 404)
            {
                _playerSearchUnavailable = true;
                return Array.Empty<PickCandidate>();
            }
        };
    }

    private string MyDisplayName(string me)
    {
        var online = _globalOnlineUsers.FirstOrDefault(u => string.Equals(u.UserId, me, StringComparison.Ordinal));
        if (online != null && !string.IsNullOrEmpty(online.Login)) return online.Login;
        var user = _session?.CurrentUser;
        if (user != null && string.Equals(user.Id, me, StringComparison.Ordinal))
            return string.IsNullOrEmpty(user.DisplayName) ? me : user.DisplayName!;
        return Strings.Get("MpTeamsYou");
    }

    private static string TeamLabel(string? name, string? tag)
        => string.IsNullOrWhiteSpace(tag) ? name ?? "" : $"{name} [{tag}]";

    private Window? OwnerWindow()
    {
        try { return Window.GetWindow(this); } catch { return null; }
    }

    private bool Show(Window dialog)
    {
        try { dialog.Owner = OwnerWindow(); } catch { /* off-tree */ }
        return dialog.ShowDialog() == true;
    }
}
