using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The window's LIVE mode (2026-11-18): a real room, read through <c>GET /lobbies/:id/live</c> and
/// drawn from a <see cref="MatchWatchModel"/>. It asks again every
/// <see cref="MatchWatchView.PollInterval"/> while the room may still change, and ONLY while the
/// window is visible and not minimised; a result or a gone room never asks again. The state chip
/// uses <c>StateColors</c> (waiting green, playing blue, result grey) — a state is never the mod's
/// colour; the mod's colour stays on the chrome.
/// </summary>
public partial class MatchWatchWindow
{
    private readonly Func<Task<MatchWatchModel>>? _refresh;
    private DispatcherTimer? _poll;
    private MatchWatchState _liveState = MatchWatchState.Loading;
    private bool _asking;

    /// <summary>A real room. <paramref name="refresh"/> asks the server and builds the model.</summary>
    /// <param name="tournament">The bracket match's tournament, when it is one (null: a room).</param>
    /// <param name="match">That match, read for its two entrants.</param>
    /// <param name="decide">The bracket's own "Decide this match" (winner id, name), for an organiser
    /// who may decide it; null for everybody else. Offered while the match is undecided.</param>
    internal MatchWatchWindow(MatchWatchModel initial, Func<Task<MatchWatchModel>> refresh,
        Models.Multiplayer.TournamentDetail? tournament = null,
        Models.Multiplayer.TournamentMatch? match = null,
        Action<string, string>? decide = null)
    {
        _tournament = tournament ?? new Models.Multiplayer.TournamentDetail();
        _match = match ?? new Models.Multiplayer.TournamentMatch();
        _sample = TournamentDemoData.WatchSample();
        _refresh = refresh;
        _decide = match != null && !string.IsNullOrEmpty(match.Entrant1Id) && !string.IsNullOrEmpty(match.Entrant2Id)
            ? decide : null;

        InitializeComponent();
        if (ModDialogChrome.Current != null) ModDialogChrome.Attach(this, TitleBarControl);
        else ModDialogChrome.AttachGlobal(this);

        Title = Strings.Get("MpWatchLiveTitle");
        TitleBarControl.Title = Strings.Get("MpWatchLiveTitle");
        RosterLabel.Text = Strings.Get("MpWatchRoster").ToUpper(Strings.Culture);
        ChatLabel.Text = Strings.Get("MpWatchChat").ToUpper(Strings.Culture);
        ReadOnlyText.Text = Strings.Get("MpWatchReadOnly");
        ReadOnlyChip.ToolTip = TooltipHelper.Wrap(Strings.Get("MpWatchReadOnlyTip"));
        ResultLabel.Text = Strings.Get("MpWatchResult");
        CloseBtn.Content = Strings.Get("DlgClose");
        DecideBtn.Content = Strings.Get("MpTournamentAward");
        DecideBtn.ToolTip = TooltipHelper.Wrap(Strings.Get("MpWatchDecideTip"));
        DecideBtn.Visibility = Visibility.Collapsed;
        ModVersionText.Visibility = Visibility.Collapsed;

        RenderModel(initial);

        Loaded += (_, _) => StartPolling();
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _ = AskAsync(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) _ = AskAsync(); };
        Closed += (_, _) =>
        {
            _poll?.Stop();
            _poll = null;
            StopPulse();
        };
    }

    /// <summary>The lobby this window shows, so a second click focuses it instead of opening another.</summary>
    internal string? LobbyId { get; init; }

    private void StartPolling()
    {
        _poll = new DispatcherTimer { Interval = MatchWatchView.PollInterval };
        _poll.Tick += (_, _) => _ = AskAsync();
        _poll.Start();
        _ = AskAsync();
    }

    /// <summary>"Decide this match" shows for somebody who may decide it, until a result or a gone
    /// room is on screen (then the bracket already says how it ended).</summary>
    internal static bool DecideOffered(bool mayDecide, MatchWatchState state)
        => mayDecide && state is not (MatchWatchState.Result or MatchWatchState.Gone);

    /// <summary>Whether a tick may ask the server now. Pure enough to pin.</summary>
    internal static bool MayAsk(MatchWatchState state, bool visible, WindowState windowState)
        => MatchWatchView.ShouldPoll(state) && visible && windowState != WindowState.Minimized;

    private async Task AskAsync()
    {
        if (_refresh == null || _asking || !MayAsk(_liveState, IsVisible, WindowState)) return;
        _asking = true;
        try
        {
            var model = await _refresh();
            if (_poll != null || IsLoaded) RenderModel(model);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MatchWatchWindow: live read failed: {ex.Message}");
        }
        finally
        {
            _asking = false;
        }
        if (!MatchWatchView.ShouldPoll(_liveState)) _poll?.Stop();
    }

    internal void RenderModel(MatchWatchModel model)
    {
        _liveState = model.State;
        MatchLabel.Text = model.Header;
        RoomTitleText.Text = model.Title;
        RoomTitleText.LineHeight = RoomTitleText.FontSize * 1.2;
        FormatText.Text = model.FormatText;
        ModText.Text = model.ModName;
        StateText.Text = model.StateText;
        PreviewNote.Text = model.Foot;
        RosterNote.Visibility = Visibility.Collapsed;

        StopPulse();
        var (mark, ink) = model.State switch
        {
            MatchWatchState.Waiting => (StateColors.Green, StateColors.GreenText),
            MatchWatchState.Playing => (StateColors.Blue, StateColors.BlueText),
            _ => (StateColors.Neutral, StateColors.NeutralText),
        };
        StateDot.Fill = new System.Windows.Media.SolidColorBrush(mark);
        StateText.Foreground = new System.Windows.Media.SolidColorBrush(ink);
        if (model.State == MatchWatchState.Playing) StartPulse();

        RosterPanel.Children.Clear();
        foreach (var side in model.Sides)
        {
            if (side.Heading != null)
            {
                var heading = new TextBlock
                {
                    Text = side.Heading,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 12, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                heading.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
                heading.SetResourceReference(TextBlock.ForegroundProperty, "UiBodyText");
                RosterPanel.Children.Add(heading);
            }
            foreach (var p in side.People) RosterPanel.Children.Add(Row(null, p.Name, p.Sub, p.Missing));
        }
        RosterCount.Text = model.Total > 0 ? $"{model.InRoom} / {model.Total}" : "";

        ChatPanel.Children.Clear();
        if (model.Chat == null)
        {
            var closed = new TextBlock { Text = Strings.Get("MpWatchChatClosed"), TextWrapping = TextWrapping.Wrap };
            closed.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            closed.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
            ChatPanel.Children.Add(closed);
        }
        else
        {
            foreach (var line in model.Chat)
            {
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
                var who = new TextBlock { Text = line.Time.Length > 0 ? $"{line.Author}  {line.Time}" : line.Author, FontWeight = FontWeights.SemiBold };
                who.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
                who.SetResourceReference(TextBlock.ForegroundProperty, "UiBodyText");
                var body = new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                body.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
                body.SetResourceReference(TextBlock.ForegroundProperty, "WindowSoftText");
                block.Children.Add(who);
                block.Children.Add(body);
                ChatPanel.Children.Add(block);
            }
        }

        // An organiser's way to settle it, while there is nothing settled yet.
        DecideBtn.Visibility = DecideOffered(_decide != null, model.State) ? Visibility.Visible : Visibility.Collapsed;

        if (model.Result is { } result)
        {
            ResultWhen.Text = result.When;
            ResultText.Text = result.Headline;
            ResultScore.Text = result.Score;
            ResultSub.Text = result.Sub;
            ResultBox.Visibility = Visibility.Visible;
        }
        else
        {
            ResultBox.Visibility = Visibility.Collapsed;
        }
    }
}
