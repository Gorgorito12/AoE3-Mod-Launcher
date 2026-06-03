using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// In-window themed alert/confirm card — the dark "dorado imperial" /
/// multiplayer-blue replacement for the OS <see cref="MessageBox"/> in the
/// multiplayer surfaces. Instead of a separate top-level Window it injects a
/// full-bleed scrim + a centred card as the LAST child of a host
/// <see cref="Grid"/>, so it floats over whatever that grid already shows
/// (the lobby body, or the Multiplayer tab) and tracks its size/position for
/// free. Returns a <see cref="Task{Boolean}"/> that completes when the user
/// picks an option — <c>true</c> = confirm/primary, <c>false</c> =
/// cancel/dismiss (also what Esc, the ✕, or a scrim click yield).
///
/// Why a helper over the grid rather than a Window: the maintainer's brief is
/// "tarjeta dentro del lobby" — the confirm should read as part of the lobby,
/// not a separate OS dialog. Reusable across both the <c>LobbyWindow</c> (the
/// cancel-the-game confirm) and the <c>MultiplayerTab</c> (join/create error
/// notices), so the look is identical everywhere.
///
/// NOT usable from a synchronous, blocking context (e.g. Window.OnClosing
/// that does task.Wait()) — the await needs the UI thread to keep pumping.
/// The launcher-close confirm stays on MessageBox for exactly that reason.
/// </summary>
internal static class MpAlertOverlay
{
    /// <summary>
    /// Show a two-button confirm (primary danger + neutral cancel) over
    /// <paramref name="host"/>. Resolves true if the user clicks the
    /// primary button, false on cancel / Esc / scrim / ✕.
    /// </summary>
    /// <param name="host">Grid to overlay. The card is added as its last child and removed on dismiss.</param>
    /// <param name="title">Bold header line.</param>
    /// <param name="body">Description paragraph under the header.</param>
    /// <param name="primaryLabel">Caption for the confirm button (e.g. "Yes, cancel").</param>
    /// <param name="cancelLabel">Caption for the dismiss button (e.g. "No").</param>
    /// <param name="danger">When true the primary button uses the red MpDangerButton style; otherwise MpPrimaryButton (blue).</param>
    public static Task<bool> ConfirmAsync(
        Grid host,
        string title,
        string body,
        string primaryLabel,
        string cancelLabel,
        bool danger = true)
        => ShowAsync(host, title, body, primaryLabel, cancelLabel, danger, isConfirm: true);

    /// <summary>
    /// Show a single-button notice (just "OK") over <paramref name="host"/>.
    /// The returned Task always resolves true once dismissed; callers that
    /// only inform can ignore the result. Used for the join/create error
    /// notices that were plain MessageBox.Show(... OK ...) calls.
    /// </summary>
    public static Task<bool> NoticeAsync(
        Grid host,
        string title,
        string body,
        string okLabel)
        => ShowAsync(host, title, body, okLabel, cancelLabel: null, danger: false, isConfirm: false);

    private static Task<bool> ShowAsync(
        Grid host,
        string title,
        string body,
        string primaryLabel,
        string? cancelLabel,
        bool danger,
        bool isConfirm)
    {
        var tcs = new TaskCompletionSource<bool>();

        Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        // ---- Scrim: dim everything behind the card. Clicking it = cancel
        //      (only for confirms; a notice must be acknowledged via OK so
        //      the user actually reads it). Spans every row/column of the
        //      host grid so partial-grid hosts (e.g. a 2-row tab) are
        //      fully covered. ----
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x05, 0x07, 0x0A)),
        };
        if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(scrim, host.RowDefinitions.Count);
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(scrim, host.ColumnDefinitions.Count);

        // ---- Card: MpSurface fill, two-tone rim (bright inner over a dark
        //      outer band, the launcher's standard "punched-out" popup look),
        //      drop shadow on the outer band. ----
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
            Margin = new Thickness(24),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 0,
                BlurRadius = 24,
                Opacity = 0.55,
            },
        };
        if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(outer, host.RowDefinitions.Count);
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(outer, host.ColumnDefinitions.Count);

        var card = new Border
        {
            Background = Res("MpSurface"),
            BorderBrush = Res("MpDivider"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 20, 24, 18),
        };
        outer.Child = card;

        var stack = new StackPanel();
        card.Child = stack;

        // Header row: ⚠ glyph + title.
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = danger ? "⚠" : "ℹ",
            FontSize = 20,
            Foreground = danger ? Res("DangerAlertFg") : Res("MpBlue"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = (double)Application.Current.FindResource("FontSizeTitle"),
            FontWeight = FontWeights.Bold,
            Foreground = Res("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(headerRow);

        stack.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            Foreground = Res("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
        });

        // Button row, right-aligned. Cancel (neutral) then primary, so the
        // primary sits at the far right where the eye lands last.
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        void Close(bool result)
        {
            host.Children.Remove(scrim);
            host.Children.Remove(outer);
            tcs.TrySetResult(result);
        }

        // Cancel button only exists for confirms; a notice is OK-only.
        if (isConfirm && cancelLabel != null)
        {
            var cancelBtn = new Button
            {
                Content = cancelLabel,
                Style = (Style)Application.Current.FindResource("MpSecondaryButton"),
                MinWidth = 96,
                Padding = new Thickness(18, 8, 18, 8),
                Margin = new Thickness(0, 0, 10, 0),
            };
            cancelBtn.Click += (_, _) => Close(false);
            buttonRow.Children.Add(cancelBtn);
        }

        var primaryBtn = new Button
        {
            Content = primaryLabel,
            Style = (Style)Application.Current.FindResource(danger ? "MpDangerButton" : "MpPrimaryButton"),
            MinWidth = 120,
            Padding = new Thickness(18, 8, 18, 8),
            IsDefault = true,
        };
        // A notice's single button resolves true (acknowledged); a confirm's
        // primary also resolves true (the "do it" answer).
        primaryBtn.Click += (_, _) => Close(true);
        buttonRow.Children.Add(primaryBtn);

        stack.Children.Add(buttonRow);

        // Scrim click cancels a confirm (but never a notice — must press OK).
        if (isConfirm)
            scrim.MouseLeftButtonDown += (_, _) => Close(false);

        host.Children.Add(scrim);
        host.Children.Add(outer);

        // Focus the primary so Enter confirms / acknowledges immediately.
        primaryBtn.Loaded += (_, _) => primaryBtn.Focus();

        return tcs.Task;
    }
}
