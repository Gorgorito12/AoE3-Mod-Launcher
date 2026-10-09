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
    /// <param name="KeepOpen">
    /// Run the action and leave the card where it is. For an action that is not the card's
    /// answer — copying a list next to a Repair button: closing the card on Copy took the
    /// Repair button with it, and that was the one worth pressing next.
    /// </param>
    /// <param name="DoneLabel">What the button says after it has been pressed, so an action that
    /// happens off screen (the clipboard) still shows that it happened.</param>
    public sealed record ToastAction(
        string Label, bool IsPrimary, Action OnClick, bool KeepOpen = false, string? DoneLabel = null);

    /// <summary>What to show. <paramref name="Icon"/> is a short glyph/emoji.</summary>
    /// <param name="PreferDesktop">
    /// Put this card on the DESKTOP even when the user is looking at the launcher.
    ///
    /// <para>For the notifications that expire and carry a button worth pressing — a room
    /// invite — rather than the ambient ones. An invite drawn inside the window is easy to
    /// miss from another tab or another monitor, and by the time it is noticed the room
    /// may be gone. It does NOT add a second surface: the card still appears exactly once,
    /// just always in the same place.</para>
    ///
    /// <para>It does not override the game-is-running suppression: nothing floats over
    /// AoE3, which can be knocked out of full-screen by a topmost window.</para>
    /// </param>
    /// <param name="Tone">
    /// Draw the card the way design handoff 63 draws its replay notices: a 24-px coloured
    /// circle (a tick in green, "i" in blue, "!" in grey) instead of <paramref name="Icon"/>,
    /// 360 px wide, on <c>MpPanel</c>. Null - every other toast - keeps the launcher's toast
    /// exactly as it was.
    /// </param>
    /// <param name="Subtitle">One muted line under the title (the match a notice is about),
    /// trimmed rather than wrapped. Drawn only with a <paramref name="Tone"/>.</param>
    public sealed record ToastOptions(
        string Icon,
        string Title,
        string? Body,
        IReadOnlyList<ToastAction> Actions,
        int AutoDismissMs = 9000,
        bool PreferDesktop = false,
        ToastTone? Tone = null,
        string? Subtitle = null);

    /// <summary>The coloured circle of a <see cref="ToastOptions.Tone"/> card.</summary>
    public enum ToastTone { Ok, Info, Error }

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

            if (opts.Tone is ToastTone tone)
            {
                ShowToned(host, opts, tone);
                return;
            }

            // Three layers, and the split exists for the text's sake. `outer` is the
            // two-tone "punched-out" rim and it holds the card, which holds the title and
            // body TextBlocks — so an Effect on it would push every glyph of every toast
            // through a composition pass and disable ClearType, permanently. The shadow
            // therefore sits on `halo`, a sibling UNDER it, fully covered by `outer` (same
            // size, same radius) so only the blur spills. Same pattern as the multiplayer
            // cards, MpAlertOverlay and the PLAY button.
            //
            // `root` is what gets added, animated and removed: the fade has to move the
            // shadow with the card, and it lands back on 1.0 so it leaves no lasting layer.
            var shell = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 340,
                Opacity = 0,
                RenderTransform = new TranslateTransform(28, 0),
            };
            var halo = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(9),
                IsHitTestVisible = false,
                // Cached: an empty shadow, so the toast slides in without re-running its blur.
                CacheMode = new BitmapCache(),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 0,
                    BlurRadius = 18,
                    Opacity = 0.5,
                },
            };
            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(1),
            };
            shell.Children.Add(halo);
            shell.Children.Add(outer);
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
                fade.Completed += (_, _) => { try { host.Children.Remove(shell); } catch { } };
                shell.BeginAnimation(UIElement.OpacityProperty, fade);
                shell.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
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
                    var keepOpen = a.KeepOpen;
                    var doneLabel = a.DoneLabel;
                    btn.Click += (_, _) =>
                    {
                        if (!keepOpen) Close();
                        try { act?.Invoke(); } catch (Exception ex) { DiagnosticLog.Write($"AppToast action failed: {ex.Message}"); }
                        if (keepOpen && !string.IsNullOrEmpty(doneLabel)) btn.Content = doneLabel;
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
            host.Children.Insert(0, shell);

            // Slide + fade in.
            shell.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            shell.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
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

    /// <summary>The <c>Tag</c> of a toned card's close button, for the tests.</summary>
    internal const string ToastCloseTag = "ToastClose";

    /// <summary>
    /// The handoff-63 card: 360 wide, padding 14 / 14 / 14 / 16, radius 8, <c>MpPanel</c>, a
    /// 1-px rim at .22 and a 0 12 32 shadow at .45 - on a SIBLING underlay, never on an
    /// ancestor of the text. Columns: icon 24, 12, text, 12, close 16.
    /// </summary>
    internal static Grid BuildToned(ToastOptions opts, ToastTone tone, Action close)
    {
        Brush Res(string key) => (Brush)Application.Current.FindResource(key);
        double F(string key) => (double)Application.Current.FindResource(key);

        var shell = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 360,
        };
        shell.Children.Add(new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            CacheMode = new BitmapCache(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 12,
                Direction = 270,
                BlurRadius = 32,
                Opacity = 0.45,
            },
        });
        var card = new Border
        {
            Background = Res("MpPanel"),
            BorderBrush = Res("MpToastRim"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 14, 14),
        };
        shell.Children.Add(card);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        card.Child = root;

        var (glyph, glyphBrush, discBrush) = tone switch
        {
            ToastTone.Ok => ("\u2713", "MpOk", "MpToastOkBg"),
            ToastTone.Info => ("i", "MpActionText", "MpToastInfoBg"),
            _ => ("!", "UiTextStrong", "MpToastErrorBg"),
        };
        root.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = Res(discBrush),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontWeight = FontWeights.Bold,
                FontSize = F("MpBodySize"),
                Foreground = Res(glyphBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        var col = new StackPanel();
        Grid.SetColumn(col, 2);
        col.Children.Add(new TextBlock
        {
            Text = opts.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = F("MpToastTitleSize"),
            Foreground = Res("MpTextHeading"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(opts.Subtitle))
            col.Children.Add(new TextBlock
            {
                Text = opts.Subtitle,
                FontSize = F("MpMetaSize"),
                Foreground = Res("MpTextFaint"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
            });
        if (!string.IsNullOrWhiteSpace(opts.Body))
            col.Children.Add(new TextBlock
            {
                Text = opts.Body,
                FontSize = F("MpBodySize"),
                Foreground = Res("MpToastBody"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        root.Children.Add(col);

        if (opts.Actions is { Count: > 0 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var a in opts.Actions)
            {
                var btn = new Button
                {
                    Content = a.Label,
                    Style = (Style)Application.Current.FindResource(a.IsPrimary ? "MpPrimaryButton" : "MpSecondaryButton"),
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = F("MpMetaSize"),
                    FontWeight = FontWeights.SemiBold,
                };
                var act = a.OnClick;
                var keepOpen = a.KeepOpen;
                var doneLabel = a.DoneLabel;
                btn.Click += (_, _) =>
                {
                    if (!keepOpen) close();
                    try { act?.Invoke(); } catch (Exception ex) { DiagnosticLog.Write($"AppToast action failed: {ex.Message}"); }
                    if (keepOpen && !string.IsNullOrEmpty(doneLabel)) btn.Content = doneLabel;
                };
                row.Children.Add(btn);
            }
            col.Children.Add(row);
        }

        var closeBtn = new Button
        {
            Content = "\u2715",
            FontSize = F("MpBodySize"),
            Foreground = Res("MpTextFaint"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0),
            Width = 16,
            Height = 16,
            Tag = ToastCloseTag,
        };
        closeBtn.Click += (_, _) => close();
        Grid.SetColumn(closeBtn, 4);
        root.Children.Add(closeBtn);
        return shell;
    }

    private static void ShowToned(Panel host, ToastOptions opts, ToastTone tone)
    {
        DispatcherTimer? timer = null;
        Grid? shell = null;
        void Close()
        {
            if (shell == null) return;
            try { timer?.Stop(); } catch { }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            var closing = shell;
            fade.Completed += (_, _) => { try { host.Children.Remove(closing); } catch { } };
            closing.BeginAnimation(UIElement.OpacityProperty, fade);
            closing.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(28, TimeSpan.FromMilliseconds(160)));
        }

        shell = BuildToned(opts, tone, Close);
        shell.Opacity = 0;
        shell.RenderTransform = new TranslateTransform(28, 0);
        host.Children.Insert(0, shell);
        shell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        shell.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

        if (opts.AutoDismissMs > 0)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(opts.AutoDismissMs) };
            timer.Tick += (_, _) => Close();
            timer.Start();
        }
    }
}
