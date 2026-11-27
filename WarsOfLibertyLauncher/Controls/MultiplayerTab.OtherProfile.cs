using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Another player's profile (2026-11-18). The page is the viewer's own page drawn for a SUBJECT:
/// his standing and history are swapped into the fields the page reads for one synchronous render
/// and put back in a <c>finally</c> — the seam the sample profile already uses
/// (<see cref="TryRenderPreviewProfile"/>). So the viewer's own cached standing, which the account
/// chip and the roster read, is never touched (pinned by <c>OtherProfileTests</c>).
///
/// <para>What another player's page leaves out: Decks and Games (local files, which exist only for
/// the viewer), the badge selector and the refund notice. What it adds: "You vs them". A rating
/// he keeps private, or one still being placed, is never drawn — the server withholds it, and the
/// page would otherwise print the missing number as 0.</para>
/// </summary>
public partial class MultiplayerTab
{
    private readonly ProfileCache _profileCache = new();

    /// <summary>The player the profile window is showing; null = the viewer himself.</summary>
    private LobbyUserSummary? _profileOther;

    private bool _renderingOtherProfile;

    /// <summary>Whether the window shows somebody else (tests).</summary>
    internal bool ProfileShowsOther => _profileOther != null;

    /// <summary>
    /// Opens a player's profile: the viewer's own when it is him (or with no id), anybody else's
    /// otherwise. The window is re-pointed when it is already open, on its Overview.
    /// </summary>
    internal void OpenProfileOf(string? userId, string? displayName, string? login = null, string? avatarUrl = null)
    {
        if (_eloPreview)
        {
            ShowPreviewProfile(refund: false);
            return;
        }
        var self = _session?.CurrentUser?.Id;
        var decision = ProfileLinkRule.Decide(userId, self);
        if (decision == ProfileLinkRule.Target.None) return;

        _profileOther = decision == ProfileLinkRule.Target.Own ? null : new LobbyUserSummary
        {
            Id = userId!,
            DisplayName = displayName ?? "",
            DiscordUsername = login ?? "",
            AvatarUrl = avatarUrl,
        };
        _profileSection = ProfileSection.Overview;
        _h2hShowsTeam = null;
        _h2hExpanded = false;
        DiagnosticLog.Write(_profileOther == null
            ? "Profile: own profile opened."
            : $"Profile: opened another player's profile ({userId}).");

        if (_profileWindow != null || ProfileBodyOverride != null)
        {
            _profileWindow?.SetSubject(_profileOther == null ? null : SubjectName(_profileOther));
            RenderProfileTab();
            _profileWindow?.Activate();
            return;
        }
        OpenProfileWindowCore();
    }

    private static string SubjectName(LobbyUserSummary u)
        => string.IsNullOrEmpty(u.DisplayName) ? u.DiscordUsername : u.DisplayName;

    /// <summary>
    /// Makes a name, a photo or a whole plate open that player's profile: a hand cursor, a
    /// tooltip when the element has none, and the click — never over a click it already has
    /// (those are Buttons, which this is not attached to).
    /// </summary>
    internal void AttachProfileLink(FrameworkElement element, string? userId, string? displayName,
        string? login = null, string? avatarUrl = null)
    {
        if (ProfileLinkRule.Decide(userId, _session?.CurrentUser?.Id) == ProfileLinkRule.Target.None) return;
        if (element is System.Windows.Controls.Primitives.ButtonBase) return;
        // Signed out (and every sample screen): there is no session to ask the server with.
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn) return;
        element.Cursor = Cursors.Hand;
        if (element.ToolTip == null)
            element.ToolTip = TooltipHelper.Wrap(Strings.Format("MpProfileOpenTip", displayName ?? login ?? ""));
        element.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenProfileOf(userId, displayName, login, avatarUrl);
        };
    }

    /// <summary>
    /// Makes each player's NAME in a match row open his profile. The row is built by a static
    /// method (the tests build it with no tab), so each name's <see cref="System.Windows.Documents.Run"/>
    /// carries its <see cref="MatchParticipantLine"/> in <c>Tag</c> and this walks the row once.
    /// No tooltip: a cut line reveals itself on hover (RevealText), and a Run's tooltip would cover
    /// that; the underline under the pointer is the sign.
    /// </summary>
    internal T LinkPlayerNames<T>(T row) where T : UIElement
    {
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn) return row;
        var self = _session.CurrentUser?.Id;
        foreach (var run in NameRunsIn(row))
        {
            if (run.Tag is not MatchParticipantLine p) continue;
            if (ProfileLinkRule.Decide(p.UserId, self) == ProfileLinkRule.Target.None) continue;
            run.Cursor = Cursors.Hand;
            run.MouseEnter += (_, _) => run.TextDecorations = TextDecorations.Underline;
            run.MouseLeave += (_, _) => run.TextDecorations = null;
            run.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenProfileOf(p.UserId, p.Name, null, p.AvatarUrl);
            };
        }
        return row;
    }

    internal static IEnumerable<System.Windows.Documents.Run> NameRunsIn(DependencyObject root)
    {
        if (root is TextBlock text)
        {
            foreach (var run in text.Inlines.OfType<System.Windows.Documents.Run>())
                if (run.Tag is MatchParticipantLine) yield return run;
            yield break;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var run in NameRunsIn(child))
                yield return run;
    }

    /// <summary>A player of a History card: his profile, except the one whose page this is.</summary>
    private void LinkHistoryPlayer(FrameworkElement element, MatchParticipantLine player)
    {
        var subject = _profileOther?.Id ?? _session?.CurrentUser?.Id;
        if (string.Equals(player.UserId, subject, StringComparison.Ordinal)) return;
        AttachProfileLink(element, player.UserId, player.Name, null, player.AvatarUrl);
    }

    /// <summary>The page drawn for another player, or false when the window shows the viewer.</summary>
    private bool TryRenderOtherProfile()
    {
        if (_renderingOtherProfile || _renderingPreviewProfile || _profileOther is not { } other) return false;

        var now = DateTime.UtcNow;
        var entry = _profileCache.Get(other.Id, now);
        bool minimized = _profileWindow?.WindowState == WindowState.Minimized;
        if (!minimized && ProfileCache.NeedsFetch(entry, now)) _ = LoadOtherProfileAsync(other);

        if (_profileSection is ProfileSection.Decks or ProfileSection.Games) _profileSection = ProfileSection.Overview;

        var (standing, history, historyError, refreshing, failed) =
            (_cachedStanding, _historyRows, _historyError, _isRefreshingHistory, _standingFailed);
        _renderingOtherProfile = true;
        _cachedStanding = entry.Standing;
        _standingFailed = entry.StandingFailed && entry.Standing == null;
        _historyRows = entry.History;
        _historyError = entry.History == null ? entry.HistoryError : null;
        _isRefreshingHistory = entry.Loading && entry.History == null;
        try
        {
            RenderProfileTab();
        }
        finally
        {
            _cachedStanding = standing;
            _historyRows = history;
            _historyError = historyError;
            _isRefreshingHistory = refreshing;
            _standingFailed = failed;
            _renderingOtherProfile = false;
        }
        return true;
    }

    private async Task LoadOtherProfileAsync(LobbyUserSummary who)
    {
        var session = _session;
        if (session == null || ConnectivityState.IsOffline) return;
        var entry = _profileCache.Get(who.Id, DateTime.UtcNow);
        entry.Loading = true;
        entry.AskedUtc = DateTime.UtcNow;

        var standingTask = session.Api.GetEloAsync(who.Id);
        var historyTask = session.Api.GetHistoryAsync(who.Id);
        try
        {
            entry.Standing = await standingTask;
            entry.StandingFailed = false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Profile: standing of {who.Id} not read: {ex.Message}");
            entry.StandingFailed = true;
        }
        try
        {
            entry.History = (await historyTask).Matches;
            entry.HistoryError = null;
            // An entry point that knew only the id (a link from an older payload): the history
            // names him, so the card and the title say his name rather than an id.
            if (string.IsNullOrWhiteSpace(who.DisplayName) || who.DisplayName == who.Id)
            {
                var named = entry.History?.SelectMany(m => m.Participants ?? new List<MatchHistoryParticipant>())
                    .FirstOrDefault(p => p.UserId == who.Id && !string.IsNullOrWhiteSpace(p.DisplayName));
                if (named != null)
                {
                    who.DisplayName = named.DisplayName;
                    if (string.IsNullOrWhiteSpace(who.DiscordUsername)) who.DiscordUsername = named.DiscordUsername;
                    who.AvatarUrl ??= named.AvatarUrl;
                    if (ReferenceEquals(_profileOther, who)) _profileWindow?.SetSubject(SubjectName(who));
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Profile: history of {who.Id} not read: {ex.Message}");
            entry.HistoryError = ex.Message;
        }
        entry.Loading = false;

        if (_profileOther?.Id == who.Id && (_profileWindow != null || ProfileBodyOverride != null))
            RenderProfileTab();
    }

    /// <summary>A match naming these players was stored: an open profile of one of them asks again.</summary>
    private void OtherProfilesChanged(IEnumerable<string>? userIds)
    {
        var ids = userIds?.ToList();
        _profileCache.MarkStale(ids);
        foreach (var id in ids ?? new List<string>()) _achv.MarkStale(id);
        if (_profileOther is { } other && ids != null && ids.Contains(other.Id)
            && _profileWindow != null && _profileWindow.WindowState != WindowState.Minimized)
        {
            RenderProfileTab();
        }
    }

    /// <summary>Whether the subject's number on this ladder must not be drawn: his own choice, or
    /// still being placed. Only ever for somebody else.</summary>
    private bool SubjectRatingHidden(LadderStanding? ladder)
        => _renderingOtherProfile
           && (_cachedStanding?.RatingPrivate == true || ladder?.InPlacement == true);

    /// <summary>"‹ My profile", at the top of the card while it shows somebody else.</summary>
    private UIElement BuildBackToOwnProfile()
    {
        var back = new Button
        {
            Content = Strings.Get("MpProfileBackToMine"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(14, 12, 14, 4),
        };
        back.SetResourceReference(FrameworkElement.StyleProperty, "MpLinkButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(back, "ProfileBackToMine");
        back.Click += (_, _) => OpenProfileOf(_session?.CurrentUser?.Id, null);
        return back;
    }

    /// <summary>"You vs them": the record between the viewer and the subject, and the last three
    /// meetings. Says "never played" only when the history page was complete.</summary>
    private UIElement? BuildVersusCard()
    {
        if (!_renderingOtherProfile || _profileOther is not { } other) return null;
        var summary = VersusView.For(_historyRows, _session?.CurrentUser?.Id, other.Id);
        if (summary == null) return null;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Strings.Format("MpProfileVersusTitle", SubjectName(other)),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("UiTextStrong"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        ((TextBlock)stack.Children[0]).SetResourceReference(TextBlock.FontSizeProperty, "MpHistoryTitleSize");

        string line = summary.NeverPlayed ? Strings.Get("MpProfileVersusNever")
            : summary.Played == 0 ? Strings.Get("MpProfileVersusNoneRecent")
            : Strings.Format(summary.Played == 1 ? "MpProfileVersusRecordOne" : "MpProfileVersusRecord",
                summary.Wins, summary.Losses, summary.Played);
        var record = StatsProse(line, "StatsProseSize", "StatsProse");
        record.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(record);

        foreach (var meeting in summary.Recent)
        {
            var when = MatchHistoryView.ParseLocal(meeting.Match.EndedAt) is { } at
                ? at.ToString("d MMM", Strings.Culture) : "";
            var map = string.IsNullOrEmpty(meeting.Match.MapName) ? "" : meeting.Match.MapName;
            var verdict = meeting.ViewerWon switch
            {
                true => Strings.Get("MpProfileVersusYouWon"),
                false => Strings.Get("MpProfileVersusYouLost"),
                _ => Strings.Get("MpProfileVersusNoWinner"),
            };
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new TextBlock
            {
                Text = string.IsNullOrEmpty(map) ? when : $"{when} · {map}",
                Foreground = (Brush)Application.Current.FindResource("MpTextSecondary"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 10, 0),
            };
            left.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            var right = new TextBlock
            {
                Text = verdict,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource(meeting.ViewerWon switch
                {
                    true => "RmTextGreen",
                    false => "MpDanger",
                    _ => "MpTextMuted",
                }),
            };
            right.SetResourceReference(TextBlock.FontSizeProperty, "MpMetaSize");
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            stack.Children.Add(row);
        }

        var card = new Border
        {
            Child = stack,
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14),
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        card.SetResourceReference(Border.BorderBrushProperty, "MpRimSoft");
        return card;
    }

    /// <summary>"Keeps the rating private" — instead of a number nobody may see.</summary>
    private UIElement BuildPrivateRatingNotice()
    {
        var text = StatsProse(Strings.Format("MpProfilePrivateNotice", _profileOther is { } o ? SubjectName(o) : ""),
            "StatsProseSize", "StatsProse");
        text.TextWrapping = TextWrapping.Wrap;
        var card = new Border
        {
            Child = text,
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14),
        };
        card.SetResourceReference(Border.BackgroundProperty, "GlassPanel");
        card.SetResourceReference(Border.BorderBrushProperty, "MpRimSoft");
        return card;
    }
}
