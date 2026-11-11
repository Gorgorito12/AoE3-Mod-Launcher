using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>
    /// A card of ANY content over <paramref name="host"/>, with the same scrim, sibling shadow
    /// and two-tone rim as the alerts — for the rank guide, which is a panel rather than a
    /// question. <paramref name="build"/> receives the close action so the card can wire its own
    /// ✕ and its buttons to it. It also closes on Esc and on a click outside the card. Returns
    /// the close action; calling it twice is harmless.
    ///
    /// <para>The card is capped at <paramref name="maxWidth"/> and at the host's height, so a
    /// tall card on a short window scrolls inside itself (the content decides what scrolls)
    /// instead of running off the bottom.</para>
    /// </summary>
    public static Action ShowContent(Grid host, Func<Action, FrameworkElement> build, double maxWidth = 620)
    {
        Brush Res(string key) => (Brush)Application.Current.FindResource(key);
        void Span(UIElement e)
        {
            if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(e, host.RowDefinitions.Count);
            if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(e, host.ColumnDefinitions.Count);
        }

        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x05, 0x07, 0x0A)) };
        Span(scrim);
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = maxWidth,
            Margin = new Thickness(24),
            Focusable = true,
            Tag = "MpContentOverlay",
        };
        Span(outer);
        // Same reason as the alerts: the shadow is a SIBLING, or it would take ClearType off
        // every glyph in the card.
        var shadow = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            IsHitTestVisible = false,
            // Cached: a shadow with nothing in it, so the blur is worked out once rather than on
            // every frame drawn under the card (a slow PC paid it on every one).
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, ShadowDepth = 0, BlurRadius = 24, Opacity = 0.55,
            },
        };
        shadow.SetBinding(FrameworkElement.WidthProperty,
            new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)) { Source = outer });
        shadow.SetBinding(FrameworkElement.HeightProperty,
            new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight)) { Source = outer });
        Span(shadow);

        var closed = false;
        System.Windows.Input.KeyEventHandler? onKey = null;
        void Close()
        {
            if (closed) return;
            closed = true;
            host.PreviewKeyDown -= onKey;
            host.Children.Remove(scrim);
            host.Children.Remove(shadow);
            host.Children.Remove(outer);
        }
        onKey = (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            Close();
        };

        var card = new Border
        {
            Background = Res("GlassMenu"),
            BorderBrush = Res("MpDivider"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = build(Close),
        };
        outer.Child = card;
        // Never taller than the host allows: the content's own ScrollViewer takes the rest.
        outer.SetBinding(FrameworkElement.MaxHeightProperty,
            new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight))
            {
                Source = host,
                Converter = new SubtractConverter(48),
            });

        scrim.MouseLeftButtonDown += (_, _) => Close();
        host.PreviewKeyDown += onKey;
        host.Children.Add(scrim);
        host.Children.Add(shadow);
        host.Children.Add(outer);
        // Keyboard focus INTO the card, or Esc goes wherever focus happened to be.
        outer.Loaded += (_, _) => outer.Focus();
        return Close;
    }

    /// <summary>value − amount, floored at 0; for "as tall as the host, less the margins".</summary>
    private sealed class SubtractConverter(double amount) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is double d && d > amount ? d - amount : double.PositiveInfinity;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

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
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x05, 0x0A, 0x12)),
        };
        if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(scrim, host.RowDefinitions.Count);
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(scrim, host.ColumnDefinitions.Count);

        // ---- Card (103m): the small dialog's measures and tone, without its bar. ----
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x21, 0x3A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 20, 22, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 440,
            MaxWidth = 440,
            Margin = new Thickness(24),
        };
        if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(card, host.RowDefinitions.Count);
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(card, host.ColumnDefinitions.Count);

        // The shadow lives on a SIBLING underneath, never on the card — whose children are the
        // title and the body text. An Effect on an ancestor sends the whole subtree through a
        // composition pass and ClearType is disabled for every glyph inside it: the dialog's text
        // renders softer than the text behind it. Sized off the card, which is content-sized.
        var shadow = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 20,
                Direction = 270,
                BlurRadius = 50,
                Opacity = 0.5,
            },
        };
        shadow.SetBinding(FrameworkElement.WidthProperty,
            new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)) { Source = card });
        shadow.SetBinding(FrameworkElement.HeightProperty,
            new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight)) { Source = card });
        if (host.RowDefinitions.Count > 0) Grid.SetRowSpan(shadow, host.RowDefinitions.Count);
        if (host.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(shadow, host.ColumnDefinitions.Count);

        var stack = new StackPanel();
        card.Child = stack;

        // The tile beside the words, like the small dialog.
        var tone = danger ? "Destructive" : "Info";
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = new TextBlock
        {
            Text = danger ? "!" : "i",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res($"DialogTone{tone}Glyph"),
        };
        header.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Background = Res($"DialogTone{tone}Fill"),
            BorderBrush = Res($"DialogTone{tone}Ring"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
            Child = glyph,
        });

        var words = new StackPanel();
        Grid.SetColumn(words, 1);
        var headline = new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("DialogHeadline") };
        headline.LineHeight = (double)Application.Current.FindResource("DialogSubheadSize") * 1.3;
        words.Children.Add(headline);
        var lead = new TextBlock
        {
            Text = body,
            Style = (Style)Application.Current.FindResource("WindowBody"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        lead.LineHeight = (double)Application.Current.FindResource("DialogBodySize") * 1.55;
        words.Children.Add(lead);
        header.Children.Add(words);
        stack.Children.Add(header);

        // Button row, right-aligned. Cancel (neutral) then primary, so the
        // primary sits at the far right where the eye lands last.
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        KeyEventHandler? onKey = null;
        void Close(bool result)
        {
            host.PreviewKeyDown -= onKey;
            host.Children.Remove(scrim);
            host.Children.Remove(shadow);   // it is a sibling now, so it needs its own removal
            host.Children.Remove(card);
            tcs.TrySetResult(result);
        }

        // Escape is Cancel, and acknowledges a notice: the card has no bar, so no ✕.
        onKey = (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(!isConfirm);
        };
        host.PreviewKeyDown += onKey;

        // Cancel button only exists for confirms; a notice is OK-only.
        if (isConfirm && cancelLabel != null)
        {
            var cancelBtn = new Button
            {
                Content = cancelLabel,
                Style = (Style)Application.Current.FindResource("WindowSecondaryButton"),
                MinWidth = 112,
                Margin = new Thickness(0, 0, 8, 0),
            };
            cancelBtn.Click += (_, _) => Close(false);
            System.Windows.Automation.AutomationProperties.SetAutomationId(cancelBtn, "MpAlertCancel");
            buttonRow.Children.Add(cancelBtn);
        }

        var primaryBtn = new Button
        {
            Content = primaryLabel,
            Style = (Style)Application.Current.FindResource(danger ? "WindowDangerButton" : "WindowPrimaryButton"),
            MinWidth = 112,
            // Enter must not carry out what cannot be undone.
            IsDefault = !danger,
        };
        // A notice's single button resolves true (acknowledged); a confirm's
        // primary also resolves true (the "do it" answer).
        primaryBtn.Click += (_, _) => Close(true);
        // The confirm's answer often carries the same words as the button that asked (Cancel the
        // tournament / Cancel the tournament), so a screen reader or the testbed tells them apart here.
        System.Windows.Automation.AutomationProperties.SetAutomationId(primaryBtn, "MpAlertPrimary");
        buttonRow.Children.Add(primaryBtn);

        stack.Children.Add(buttonRow);

        // Scrim click cancels a confirm (but never a notice — must press OK).
        if (isConfirm)
            scrim.MouseLeftButtonDown += (_, _) => Close(false);

        host.Children.Add(scrim);
        host.Children.Add(shadow);   // under the card, so its Effect never covers text
        host.Children.Add(card);

        // Focus the primary so Enter confirms / acknowledges immediately. A destructive card
        // keeps the keyboard on its neutral button.
        var firstFocus = danger && buttonRow.Children.Count > 1 ? (Button)buttonRow.Children[0] : primaryBtn;
        firstFocus.Loaded += (_, _) => firstFocus.Focus();

        return tcs.Task;
    }
}
