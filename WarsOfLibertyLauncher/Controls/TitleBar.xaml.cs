using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The launcher's ONE custom window title bar — dropped into the top row of
/// every Window so there is a single implementation of the chrome bar (no
/// per-window copy). Per-window behaviour is configured purely through
/// DependencyProperties (<see cref="Title"/>, <see cref="TitleIcon"/>,
/// <see cref="ShowMinimize"/>, <see cref="ShowMaximize"/>,
/// <see cref="ShowClose"/>) plus the host Window's own
/// <c>ResizeMode</c>/<c>SizeToContent</c> — never via the window name. Extra
/// bar content (a brand button, a version badge, a subtitle) is the control's
/// normal <see cref="ContentControl.Content"/>.
///
/// It is a templated <see cref="ContentControl"/> (template lives in
/// Styles/Chrome.xaml) rather than a UserControl ON PURPOSE: a UserControl
/// owns a XAML namescope, so naming elements inside content passed from a
/// consumer Window throws MC3093 ("name already registered in another scope").
/// A ContentControl hosts named content exactly like a Button does.
///
/// Window movement, double-click-to-maximize, restore-on-drag, minimum-size
/// clamping, DPI and multi-monitor come NATIVELY from WindowChrome (applied
/// centrally in <c>App.OnAnyWindowLoaded</c> with CaptionHeight = the
/// TitleBarHeight token, so the whole bar is the caption region). This control
/// only owns the buttons, the maximize/restore glyph swap, and showing/hiding
/// controls.
/// </summary>
public class TitleBar : ContentControl
{
    private Window? _window;
    private Image? _icon;
    private TextBlock? _title;
    private Button? _minButton;
    private Button? _maxButton;
    private Button? _closeButton;

    public TitleBar()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar),
            new PropertyMetadata(string.Empty, OnChromeChanged));

    /// <summary>Title text (gold, DisplayFont). Empty = hidden.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleIconProperty =
        DependencyProperty.Register(nameof(TitleIcon), typeof(ImageSource), typeof(TitleBar),
            new PropertyMetadata(null, OnChromeChanged));

    /// <summary>Optional small icon shown left of the title. Null = hidden.</summary>
    public ImageSource? TitleIcon
    {
        get => (ImageSource?)GetValue(TitleIconProperty);
        set => SetValue(TitleIconProperty, value);
    }

    public static readonly DependencyProperty ShowMinimizeProperty =
        DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, OnChromeChanged));

    public bool ShowMinimize
    {
        get => (bool)GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(false, OnChromeChanged));

    /// <summary>Request a maximize/restore button. Auto-suppressed when the
    /// host window can't actually maximize (NoResize / CanMinimize, or
    /// SizeToContent), so a misconfiguration can't show a broken button.</summary>
    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public static readonly DependencyProperty ShowCloseProperty =
        DependencyProperty.Register(nameof(ShowClose), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnChromeChanged));

    public bool ShowClose
    {
        get => (bool)GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    // --- Geometry (purely visual; no RefreshChrome callback) ---------------
    // Defaults match the compact secondary tokens; the implicit Style in
    // Chrome.xaml feeds the real values from the TitleBar* tokens, and the main
    // launcher header overrides these locally back to the classic 46/10/— so the
    // slim secondary bar never shrinks it. Height (the bar height) reuses the
    // built-in FrameworkElement.Height — no extra DP needed.

    public static readonly DependencyProperty ButtonWidthProperty =
        DependencyProperty.Register(nameof(ButtonWidth), typeof(double), typeof(TitleBar),
            new PropertyMetadata(40.0));

    /// <summary>Width of each min/max/close caption button.</summary>
    public double ButtonWidth
    {
        get => (double)GetValue(ButtonWidthProperty);
        set => SetValue(ButtonWidthProperty, value);
    }

    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(TitleBar),
            new PropertyMetadata(8.0));

    /// <summary>Segoe MDL2 glyph size for the caption buttons.</summary>
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public static readonly DependencyProperty TitleSizeProperty =
        DependencyProperty.Register(nameof(TitleSize), typeof(double), typeof(TitleBar),
            new PropertyMetadata(16.0));

    /// <summary>Font size of the window title text (PART_Title).</summary>
    public double TitleSize
    {
        get => (double)GetValue(TitleSizeProperty);
        set => SetValue(TitleSizeProperty, value);
    }

    private static void OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TitleBar)d).RefreshChrome();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_minButton != null) _minButton.Click -= MinButton_Click;
        if (_maxButton != null) _maxButton.Click -= MaxButton_Click;
        if (_closeButton != null) _closeButton.Click -= CloseButton_Click;

        _icon = GetTemplateChild("PART_Icon") as Image;
        _title = GetTemplateChild("PART_Title") as TextBlock;
        _minButton = GetTemplateChild("PART_Min") as Button;
        _maxButton = GetTemplateChild("PART_Max") as Button;
        _closeButton = GetTemplateChild("PART_Close") as Button;

        if (_minButton != null) _minButton.Click += MinButton_Click;
        if (_maxButton != null) _maxButton.Click += MaxButton_Click;
        if (_closeButton != null) _closeButton.Click += CloseButton_Click;

        RefreshChrome();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window != null)
        {
            _window.StateChanged += OnWindowStateChanged;
            // Mirror the bar title into the OS title (taskbar / Alt-Tab) when
            // the window didn't set one itself.
            if (string.IsNullOrEmpty(_window.Title) && !string.IsNullOrEmpty(Title))
                _window.Title = Title;
        }
        Strings.LanguageChanged += RefreshChrome;
        RefreshChrome();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window != null)
            _window.StateChanged -= OnWindowStateChanged;
        Strings.LanguageChanged -= RefreshChrome;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => RefreshChrome();

    /// <summary>
    /// Repaint title/icon, button visibility and the maximize/restore glyph
    /// from the current DP values + the host window's state. Idempotent; a
    /// no-op until the template has been applied.
    /// </summary>
    private void RefreshChrome()
    {
        if (_title != null)
        {
            _title.Text = Title ?? string.Empty;
            _title.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_icon != null)
        {
            _icon.Source = TitleIcon;
            _icon.Visibility = TitleIcon == null ? Visibility.Collapsed : Visibility.Visible;
        }

        bool maximized = _window?.WindowState == WindowState.Maximized;
        bool canMaximize = _window != null
            && (_window.ResizeMode == ResizeMode.CanResize || _window.ResizeMode == ResizeMode.CanResizeWithGrip)
            && _window.SizeToContent == SizeToContent.Manual;

        if (_minButton != null)
            _minButton.Visibility = ShowMinimize ? Visibility.Visible : Visibility.Collapsed;
        if (_maxButton != null)
        {
            _maxButton.Visibility = ShowMaximize && canMaximize ? Visibility.Visible : Visibility.Collapsed;
            // Maximize glyph (E922) <-> Restore glyph (E923). Char-from-hex
            // keeps the source pure ASCII.
            _maxButton.Content = ((char)(maximized ? 0xE923 : 0xE922)).ToString();
            _maxButton.ToolTip = Strings.Get(maximized ? "TitleBarRestore" : "TitleBarMaximize");
        }
        if (_closeButton != null)
        {
            _closeButton.Visibility = ShowClose ? Visibility.Visible : Visibility.Collapsed;
            _closeButton.ToolTip = Strings.Get("TitleBarClose");
        }
        if (_minButton != null)
            _minButton.ToolTip = Strings.Get("TitleBarMinimize");
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_window != null)
            SystemCommands.MinimizeWindow(_window);
    }

    private void MaxButton_Click(object sender, RoutedEventArgs e)
    {
        if (_window == null)
            return;
        if (_window.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(_window);
        else
            SystemCommands.MaximizeWindow(_window);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_window != null)
            SystemCommands.CloseWindow(_window);
    }
}
