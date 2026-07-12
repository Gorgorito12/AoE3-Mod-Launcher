using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// A small, NON-modal, auto-dismissing "toast" card that slides into a corner of
/// the window and stacks — the launcher's Discord/GameRanger-style in-app
/// notification (distinct from the persistent bell and the OS tray balloon).
///
/// Deliberately different from <see cref="MpAlertOverlay"/> (which is a modal,
/// centred, scrim-backed confirm): a toast has NO scrim, sits bottom-right, times
/// out on its own, and several can be visible at once. It reuses the same card
/// look (MpSurface fill + two-tone rim + drop shadow) so it reads as the same UI
/// family. Optional action buttons (e.g. Join / Ignore) run a callback and close.
///
/// Host: a vertical <see cref="Panel"/> (StackPanel) anchored bottom-right in
/// MainWindow that spans the whole window and is hit-test-transparent except over
/// the cards themselves. All work is best-effort try/caught — a toast must never
/// take down the app.
/// </summary>
public static class AppToast
{
    /// <summary>One action button on a toast.</summary>
    public sealed record ToastAction(string Label, bool IsPrimary, Action OnClick);

    /// <summary>What to show. <paramref name="Icon"/> is a short glyph/emoji.</summary>
    public sealed record ToastOptions(
        string Icon,
        string Title,
        string? Body,
        IReadOnlyList<ToastAction> Actions,
        int AutoDismissMs = 9000);

    /// <summary>Max cards visible at once; the oldest is evicted past this.</summary>
    private const int MaxVisible = 4;

    /// <summary>
    /// Build a toast card and add it to <paramref name="host"/> (newest on top).
    /// Slides + fades in, auto-dismisses after <see cref="ToastOptions.AutoDismissMs"/>,
    /// and closes when an action runs or the ✕ is clicked. Safe to call from the UI thread.
    /// </summary>
    public static void Show(Panel host, ToastOptions opts)
    {
        if (host == null || opts == null) return;
        try
        {
            Brush Res(string key) => (Brush)Application.Current.FindResource(key);
            double F(string key) => (double)Application.Current.FindResource(key);

            // Evict oldest cards beyond the cap (host stacks newest at index 0).
            while (host.Children.Count >= MaxVisible)
                host.Children.RemoveAt(host.Children.Count - 1);

            // Two-tone "punched-out" rim: dark outer band wrapping a bright inner card.
            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 340,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 0,
                    BlurRadius = 18,
                    Opacity = 0.5,
                },
                Opacity = 0,
                RenderTransform = new TranslateTransform(28, 0),
            };
            var card = new Border
            {
                Background = Res("MpSurface"),
                BorderBrush = Res("MpDivider"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 12, 12),
            };
            outer.Child = card;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // icon
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // close
            card.Child = root;

            // Icon disc.
            var icon = new TextBlock
            {
                Text = opts.Icon,
                FontSize = F("FontSizeSubtitle"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(icon, 0);
            root.Children.Add(icon);

            var col = new StackPanel();
            Grid.SetColumn(col, 1);
            col.Children.Add(new TextBlock
            {
                Text = opts.Title,
                FontSize = F("FontSizeBodyStrong"),
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrWhiteSpace(opts.Body))
                col.Children.Add(new TextBlock
                {
                    Text = opts.Body,
                    FontSize = F("FontSizeCaption"),
                    Foreground = Res("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });

            // Close-on-timeout + manual close share one path.
            DispatcherTimer? timer = null;
            void Close()
            {
                try { timer?.Stop(); } catch { }
                // Fade + slide out, then remove.
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (_, _) => { try { host.Children.Remove(outer); } catch { } };
                outer.BeginAnimation(UIElement.OpacityProperty, fade);
                outer.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(28, TimeSpan.FromMilliseconds(160)));
            }

            // Action buttons (below the text), if any.
            if (opts.Actions is { Count: > 0 })
            {
                var btnRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                foreach (var a in opts.Actions)
                {
                    var btn = new Button
                    {
                        Content = a.Label,
                        Style = (Style)Application.Current.FindResource(
                            a.IsPrimary ? "MpPrimaryButton" : "MpSecondaryButton"),
                        MinWidth = 72,
                        Padding = new Thickness(12, 5, 12, 5),
                        Margin = new Thickness(0, 0, 8, 0),
                        FontSize = F("FontSizeCaption"),
                    };
                    var act = a.OnClick;
                    btn.Click += (_, _) =>
                    {
                        Close();
                        try { act?.Invoke(); } catch (Exception ex) { DiagnosticLog.Write($"AppToast action failed: {ex.Message}"); }
                    };
                    btnRow.Children.Add(btn);
                }
                col.Children.Add(btnRow);
            }
            root.Children.Add(col);

            // ✕ close.
            var closeBtn = new Button
            {
                Content = "",   // Segoe MDL2 cancel glyph
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = Res("TextSecondary"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(4),
                Margin = new Thickness(6, -2, -2, 0),
            };
            closeBtn.Click += (_, _) => Close();
            Grid.SetColumn(closeBtn, 2);
            root.Children.Add(closeBtn);

            // Newest on top.
            host.Children.Insert(0, outer);

            // Slide + fade in.
            outer.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            outer.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

            // Auto-dismiss.
            if (opts.AutoDismissMs > 0)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(opts.AutoDismissMs) };
                timer.Tick += (_, _) => Close();
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"AppToast.Show failed: {ex.Message}");
        }
    }
}
