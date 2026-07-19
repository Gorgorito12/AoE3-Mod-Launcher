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

    /// <summary>Pencil beside the room name — rename the room (host only).</summary>
    public Action? OnRenameRoom { get; set; }

    /// <summary>"Clear chat" — wipes the local chat log only.</summary>
    public Action? OnClearChat { get; set; }

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

    private void LeaveRoomButton_Click(object sender, RoutedEventArgs e) => OnLeaveRoom?.Invoke();
    private void ReadyButton_Click(object sender, RoutedEventArgs e) => OnReady?.Invoke();
    private void StartButton_Click(object sender, RoutedEventArgs e) => OnStart?.Invoke();
    private void InGameCancelButton_Click(object sender, RoutedEventArgs e) => OnInGameCancel?.Invoke();
    private void RenameRoomButton_Click(object sender, RoutedEventArgs e) => OnRenameRoom?.Invoke();
    private void ClearChatButton_Click(object sender, RoutedEventArgs e) => OnClearChat?.Invoke();
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
            CopyRoomIdButton.Content = "📋";
            revert.Stop();
        };
        revert.Start();
    }
}
