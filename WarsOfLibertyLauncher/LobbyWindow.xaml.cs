using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Non-modal lobby window. Replaces the in-tab Canvas overlay that
/// used to live inside <see cref="Controls.MultiplayerTab"/> (the
/// <c>RoomPanel</c> Grid + Canvas + floating-card Border).
///
/// Lifecycle:
///   • Created and <see cref="Window.Show()"/>n by MultiplayerTab when
///     the session enters a room (joined or created).
///   • Tracked in a single-instance field on MultiplayerTab; re-entering
///     a room with the window already open just <see cref="Window.Activate"/>s
///     it instead of stacking a duplicate.
///   • Closed (X / Esc / Alt+F4 / external Close) fires
///     <see cref="Window.Closed"/>; MultiplayerTab clears its single-instance
///     field and triggers a leave-room flow on the session if the user
///     dismissed mid-lobby.
///
/// Why a real Window: the previous in-tab popup looked modal because
/// of its floating-card chrome and dropshadow even though it was
/// technically non-modal. A real top-level Window gives the user:
///   • OS-native edge-drag resize (instead of a custom Thumb grip)
///   • Drag-to-move outside the launcher's bounds
///   • Alt-tab visibility (or not — controlled by ShowInTaskbar)
///   • Independent minimise from the main launcher window
/// — which is the menu/properties dialog pattern (see CLAUDE.md
/// under Runtime conventions).
///
/// Click forwarding: the lobby UI logic (rendering, chat send, match
/// phase transitions, etc.) lives in MultiplayerTab — it's tightly
/// coupled to <see cref="MultiplayerSession"/> events, the catalog,
/// telemetry, and the rest of the tab's state. Rather than move all
/// that across, this window exposes a set of <c>Action</c> callbacks
/// that MultiplayerTab populates on construction; the XAML click
/// handlers (<see cref="LeaveRoomButton_Click"/> etc.) are tiny
/// forwarders that invoke those callbacks. MultiplayerTab reads/writes
/// the UI elements directly via the field-modifier-internal x:Name
/// fields auto-generated for the Window (same assembly = accessible).
///
/// The window stores its <see cref="MultiplayerSession"/> reference but
/// deliberately does NOT subscribe to its events — that subscription
/// already exists on MultiplayerTab and continues to drive the UI.
/// Storing the session here is just so future callers / event handlers
/// that need it (chat send composing, etc.) can reach it without
/// passing it through every callback.
/// </summary>
public partial class LobbyWindow : Window
{
    /// <summary>
    /// Session this lobby window is rendering. Held so click handlers
    /// that need session data (e.g. "am I the host?") can reach it
    /// without round-tripping through a callback.
    /// </summary>
#pragma warning disable IDE0052 // Field is intentionally held for future direct callers.
    private readonly MultiplayerSession _session;
#pragma warning restore IDE0052

    // ------------------------------------------------------------------
    // Click forwarder callbacks. MultiplayerTab populates these on
    // construction; the XAML click handlers below invoke whichever is
    // non-null. Defaulted nullable so a window opened without callbacks
    // (e.g. designer preview) doesn't NRE on every click.
    // ------------------------------------------------------------------

    /// <summary>"Leave room" button + title-bar close — same flow.</summary>
    public Action? OnLeaveRoom { get; set; }

    /// <summary>"Mark as ready" / "Ready" toggle button.</summary>
    public Action? OnReady { get; set; }

    /// <summary>"Start game" (host only).</summary>
    public Action? OnStart { get; set; }

    /// <summary>"Cancel game" / "Leave game" while a match is running.</summary>
    public Action? OnInGameCancel { get; set; }

    /// <summary>
    /// "Open the game" — relaunch AoE3 after it closed while the room carried on playing.
    /// Purely local: it sends nothing to the server and does not disturb the other players.
    /// </summary>
    public Action? OnRejoinGame { get; set; }

    /// <summary>Pencil beside the room name — rename the room (host only).</summary>
    public Action? OnRenameRoom { get; set; }

    /// <summary>"Clear chat" — wipes the local chat log only.</summary>
    public Action? OnClearChat { get; set; }

    /// <summary>
    /// "Don't show this again" on the Record Game band. The ONLY way that reminder is ever
    /// silenced — it is never inferred from a match that happened to record, because one
    /// success says nothing about whether the next match's checkbox will be ticked.
    /// </summary>
    /// <summary>Invite someone to this room (the link beside the PLAYERS header).</summary>
    public Action? OnInvitePlayers { get; set; }

    /// <summary>Explain where AoE3 hides the per-match Record Game checkbox.</summary>
    public Action? OnRecordHelp { get; set; }

    /// <summary>Copy the room code (the alone-in-the-room box).</summary>
    public Action? OnCopyRoomCode { get; set; }

    /// <summary>Announce this room in the global chat (the alone-in-the-room box).</summary>
    public Action? OnAnnounceRoom { get; set; }

    /// <summary>"Send" button on the chat input bar.</summary>
    public Action? OnSendChat { get; set; }

    /// <summary>Emoji icon next to the chat input.</summary>
    public Action? OnEmoji { get; set; }

    /// <summary>Chat input TextChanged — drives placeholder visibility.</summary>
    public Action? OnChatTextChanged { get; set; }

    /// <summary>Chat input KeyDown — Enter to send. Forwards the
    /// <see cref="KeyEventArgs"/> so the handler can read Key + check
    /// modifiers.</summary>
    public Action<KeyEventArgs>? OnChatKeyDown { get; set; }

    /// <summary>
    /// Cheap, synchronous "would closing this window cost something the user should be told
    /// about?". Kept separate from <see cref="ConfirmLeave"/> so the ordinary close — the vast
    /// majority — stays fully synchronous instead of taking a dispatcher hop.
    /// </summary>
    public Func<bool>? NeedsLeaveConfirm { get; set; }

    /// <summary>
    /// Ask the question, and answer true when the window may go. Only consulted after
    /// <see cref="NeedsLeaveConfirm"/> said there was something to ask.
    /// </summary>
    public Func<System.Threading.Tasks.Task<bool>>? ConfirmLeave { get; set; }

    public LobbyWindow(MultiplayerSession session)
    {
        InitializeComponent();
        _session = session;

        // Window-size scaling (Controls/UiScale.cs): the lobby content (Row 1,
        // below the fixed title bar) shrinks to fit smaller windows. sizeSource
        // is the window root grid (window-sized, so the LayoutTransform on the
        // content can't feed back into it); the title bar (Row 0) and the
        // MpAlertOverlay host (LobbyRootGrid) stay at base scale. ref 900x600 ≈
        // the default content footprint, so a default-sized window is 1.0.
        UiScale.Attach(LobbyContentRoot, LobbyRootGrid, 900, 600);
    }

    // ------------------------------------------------------------------
    // XAML click handlers. All tiny forwarders to the public callbacks.
    // Same-named as the originals in MultiplayerTab.xaml so the XAML
    // Click="…" wiring in LobbyWindow.xaml resolves cleanly here.
    // ------------------------------------------------------------------

    // Title-bar minimise / maximise-restore / close + the maximize-glyph
    // swap are now owned by the shared controls:TitleBar (see Controls/
    // TitleBar.xaml.cs). Closing still routes through Window.Close(), so the
    // Closed handler's leave-room flow runs identically for ✕ / Esc / Alt+F4.

    // ------------------------------------------------------------------
    // Closing: ask before walking out of a live match
    // ------------------------------------------------------------------

    /// <summary>Set once the user has answered, or when the close is not theirs to approve.</summary>
    private bool _leaveConfirmSuppressed;

    /// <summary>
    /// Close without asking. Every PROGRAMMATIC close goes through here — being kicked, signing
    /// out, the tab tearing the room down — because those are not a decision the player is making
    /// and a confirmation box would be nonsense (worse: on the kick path it would ask them to
    /// approve something that has already happened).
    /// </summary>
    public void SuppressLeaveConfirm() => _leaveConfirmSuppressed = true;

    /// <summary>
    /// The guard has to be here, in <c>OnClosing</c>: the <c>Closed</c> event that MultiplayerTab
    /// hangs the leave-room flow off already runs with the window on its way out, far too late to
    /// change anyone's mind.
    ///
    /// <para>Cancel-then-reclose, because <c>OnClosing</c> is synchronous and the confirmation is
    /// an awaited <c>MpAlertOverlay</c>. The first pass always cancels; the answer arrives later
    /// and calls <see cref="Window.Close"/> again with the flag set. Anything that throws or is
    /// missing lets the close through — being unable to shut a window is worse than any warning
    /// it could have shown.</para>
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_leaveConfirmSuppressed
            && NeedsLeaveConfirm?.Invoke() == true
            && ConfirmLeave != null)
        {
            e.Cancel = true;
            _ = ConfirmThenCloseAsync();
            return;
        }

        base.OnClosing(e);
    }

    private async System.Threading.Tasks.Task ConfirmThenCloseAsync()
    {
        bool proceed;
        try { proceed = await ConfirmLeave!(); }
        catch { proceed = true; }

        if (!proceed) return;

        _leaveConfirmSuppressed = true;
        Close();
    }

    private void LeaveRoomButton_Click(object sender, RoutedEventArgs e) => OnLeaveRoom?.Invoke();
    private void ReadyButton_Click(object sender, RoutedEventArgs e) => OnReady?.Invoke();
    private void StartButton_Click(object sender, RoutedEventArgs e) => OnStart?.Invoke();
    private void InGameCancelButton_Click(object sender, RoutedEventArgs e) => OnInGameCancel?.Invoke();
    private void RejoinGameButton_Click(object sender, RoutedEventArgs e) => OnRejoinGame?.Invoke();
    private void RenameRoomButton_Click(object sender, RoutedEventArgs e) => OnRenameRoom?.Invoke();
    private void ClearChatButton_Click(object sender, RoutedEventArgs e) => OnClearChat?.Invoke();
    private void InvitePlayersButton_Click(object sender, RoutedEventArgs e) => OnInvitePlayers?.Invoke();
    private void PreflightRecordHelp_Click(object sender, RoutedEventArgs e) => OnRecordHelp?.Invoke();
    private void InGameSoloCopyButton_Click(object sender, RoutedEventArgs e) => OnCopyRoomCode?.Invoke();
    private void InGameSoloAnnounceButton_Click(object sender, RoutedEventArgs e) => OnAnnounceRoom?.Invoke();
    private void ChatSendButton_Click(object sender, RoutedEventArgs e) => OnSendChat?.Invoke();
    private void ChatEmojiButton_Click(object sender, RoutedEventArgs e) => OnEmoji?.Invoke();
    private void ChatInputBox_TextChanged(object sender, TextChangedEventArgs e) => OnChatTextChanged?.Invoke();
    private void ChatInputBox_KeyDown(object sender, KeyEventArgs e) => OnChatKeyDown?.Invoke(e);

    /// <summary>
    /// Copy the room code to the clipboard, flashing a ✓ on the button
    /// for a moment as confirmation. Pure UI with no session coupling,
    /// so unlike the other handlers it does the work here directly
    /// instead of forwarding to a MultiplayerTab callback.
    /// </summary>
    private void CopyRoomIdButton_Click(object sender, RoutedEventArgs e)
    {
        var code = RoomIdText.Text;
        if (string.IsNullOrWhiteSpace(code)) return;
        try { Clipboard.SetText(code); }
        catch { return; } // clipboard can be momentarily locked by another app

        CopyRoomIdButton.Content = "✓";
        var revert = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        revert.Tick += (_, _) =>
        {
            CopyRoomIdButton.Content = "\u29C9";
            revert.Stop();
        };
        revert.Start();
    }
}
