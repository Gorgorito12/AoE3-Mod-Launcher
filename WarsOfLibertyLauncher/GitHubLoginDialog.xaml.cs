using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal dialog that drives the sign-in flow against the lobby backend (design 97). Currently
/// backed by Discord OAuth, exposed as a device-flow-shaped API; the class name is kept for
/// git-blame continuity.
///
/// Flow inside the dialog:
///   1. On Loaded → POST /auth/login/device, get verification_uri (and for legacy flows a
///      user_code).
///   2. Show the URL and "Open browser". For Discord, user_code is empty, so the "type this code"
///      panel stays hidden.
///   3. Poll /auth/login/poll until the backend returns a JWT, the player cancels, or the link
///      expires.
///   4. On success → DialogResult = true; the caller reads <see cref="CompletedSession"/>.
/// A failure stays in the window with its reason, and the primary button runs the flow again.
/// </summary>
public partial class GitHubLoginDialog : Window
{
    private const double BodyLineHeight = 1.55;
    private static readonly TimeSpan CopiedFor = TimeSpan.FromMilliseconds(1400);

    private readonly MultiplayerSession _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly string? _previewVariant;

    private DeviceFlowStart? _start;
    private bool _primaryRetries;
    private DispatcherTimer? _copiedTimer;

    public DeviceFlowComplete? CompletedSession { get; private set; }

    /// <param name="session">The session that signs in.</param>
    /// <param name="previewVariant"><c>--preview-dialog=signin:…</c>: paint that state and ask no server.</param>
    public GitHubLoginDialog(MultiplayerSession session, string? previewVariant = null)
    {
        InitializeComponent();
        ModDialogChrome.AttachGlobal(this);
        _session = session;
        _previewVariant = previewVariant;

        Title = Strings.Get("MpSignInDialogTitle");
        TitleBarControl.Title = Strings.Get("MpSignInDialogTitle");
        UriLabelText.Text = Strings.Get("MpSignInUriLabel");
        Step2Text.Text = Strings.Get("MpSignInStep2");
        CopyButton.Content = Strings.Get("MpSignInCopy");
        CopyLinkButton.Content = Strings.Get("MpSignInCopyChip");
        BuildBrowserHint();
        BuildPrivacyConsent();
        ModDialogChrome.SetLineHeight(1.25, HeadlineText);
        ModDialogChrome.SetLineHeight(BodyLineHeight, LeadText, Step2Text);
        ModDialogChrome.SetLineHeight(1.45, BrowserHintText, ErrorBodyText, StatusText, PrivacyConsentBlock);
        ModDialogChrome.SetLineHeight(1.4, ErrorDetailText);
        ShowPreparing();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>The domain inside the advice is drawn like the one in the link: that is the comparison
    /// the player is being asked to make.</summary>
    private void BuildBrowserHint()
    {
        var parts = Strings.Get("MpSignInBrowserHint").Split("{0}");
        BrowserHintText.Inlines.Clear();
        BrowserHintText.Inlines.Add(new Run(parts[0]));
        BrowserHintText.Inlines.Add(new Run("discord.com")
        {
            FontFamily = VerificationUriText.FontFamily,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        if (parts.Length > 1) BrowserHintText.Inlines.Add(new Run(parts[1]));
    }

    /// <summary>
    /// The "by signing in you agree to the privacy policy" line. In code so the localized prefix
    /// and link compose in the right order for both languages.
    /// </summary>
    private void BuildPrivacyConsent()
    {
        PrivacyConsentBlock.Inlines.Clear();
        PrivacyConsentBlock.Inlines.Add(new Run(Strings.Get("MpSignInPrivacyPrefix")));
        var link = new Hyperlink(new Run(Strings.Get("MpSignInPrivacyLink")))
        {
            Foreground = (Brush)FindResource("UiInfoText"),
            FontWeight = FontWeights.SemiBold,
            TextDecorations = null,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        link.Click += PrivacyLink_Click;
        PrivacyConsentBlock.Inlines.Add(link);
        PrivacyConsentBlock.Inlines.Add(new Run("."));
    }

    private void PrivacyLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HarnessMode.Start(new ProcessStartInfo
            {
                FileName = Models.LauncherConfig.PrivacyPolicyUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DiscordLoginDialog: open privacy policy failed: {ex.Message}");
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_previewVariant != null) ShowPreview(_previewVariant);
        else await RunSignInAsync();
    }

    private async Task RunSignInAsync()
    {
        ShowPreparing();
        var waitingForBrowser = false;
        try
        {
            var start = await _session.StartSignInAsync(_cts.Token);
            ShowWaiting(start);
            waitingForBrowser = true;

            // PollDeviceFlowAsync waits `interval` between attempts itself.
            CompletedSession = await _session.CompleteSignInAsync(start, _cts.Token);
            TrySetDialogResult(true);
            Close();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            DiagnosticLog.Write("DiscordLoginDialog: cancelled by user");
            TrySetDialogResult(false);
            Close();
        }
        catch (Exception ex)
        {
            // Not a Cancel: a network timeout is an OperationCanceledException too, and it used to
            // close the window as if the player had asked for it.
            var details = SignInFailure.Describe(ex);
            DiagnosticLog.Write(ex is LobbyApiException api
                ? $"DiscordLoginDialog: LobbyApiException status={api.Status} code={api.Code} msg={api.Message}"
                : $"DiscordLoginDialog: {details}");
            ShowFailure(SignInFailure.Classify(ex, waitingForBrowser), waitingForBrowser, details);
        }
    }

    /// <summary>97a: nothing to open or copy yet, and the window says why.</summary>
    internal void ShowPreparing()
    {
        _primaryRetries = false;
        HeadlineText.Text = Strings.Get("MpSignInHeadline");
        LeadText.Text = Strings.Get("MpSignInLead");
        Show(CautionBox, LinkBox, LinkPlaceholder, StatusRow);
        Hide(VerificationUriText, CopyLinkButton, Step2Text, UserCodeBorder, ErrorBox);
        StatusText.Text = Strings.Get("MpSignInPreparing");
        CancelButton.Content = Strings.Get("MpSignInCancel");
        OpenBrowserButton.Content = Strings.Get("MpSignInOpenBrowser");
        OpenBrowserButton.IsEnabled = false;
        OpenBrowserButton.ToolTip = TooltipHelper.Wrap(Strings.Get("MpSignInNotReady"));
    }

    /// <summary>97b, and 97e when the server sends a code to type.</summary>
    internal void ShowWaiting(DeviceFlowStart start)
    {
        _start = start;
        SetLink(start.VerificationUri);
        Show(VerificationUriText, CopyLinkButton);
        Hide(LinkPlaceholder);

        var hasCode = !string.IsNullOrEmpty(start.UserCode);
        LeadText.Text = Strings.Get(hasCode ? "MpSignInLeadCode" : "MpSignInLead");
        UserCodeText.Text = start.UserCode;
        if (hasCode) Show(Step2Text, UserCodeBorder);
        else Hide(Step2Text, UserCodeBorder);

        var minutes = SignInFailure.LinkMinutes(start.ExpiresInSeconds);
        StatusText.Text = minutes is { } m
            ? Strings.Get("MpSignInWaiting") + " " + Strings.Format("MpSignInLinkLasts", m)
            : Strings.Get("MpSignInWaiting");

        OpenBrowserButton.IsEnabled = true;
        OpenBrowserButton.ToolTip = null;
    }

    /// <summary>97c and 97d: the reason in a box, and the primary button runs the flow again.</summary>
    internal void ShowFailure(SignInFailureKind kind, bool waitingForBrowser, string detail)
    {
        _primaryRetries = true;
        Hide(CautionBox, LinkBox, Step2Text, UserCodeBorder, StatusRow);
        Show(ErrorBox);

        var expired = kind == SignInFailureKind.Expired;
        var refused = kind == SignInFailureKind.Refused;
        HeadlineText.Text = Strings.Get(expired ? "MpSignInExpiredHeadline"
            : waitingForBrowser ? "MpSignInFailFinish" : "MpSignInFailPrepare");
        LeadText.Text = expired
            ? (SignInFailure.LinkMinutes(_start?.ExpiresInSeconds ?? 0) is { } minutes
                ? Strings.Format("MpSignInExpiredLead", minutes)
                : Strings.Get("MpSignInExpiredLeadPlain"))
            : Strings.Get(refused ? "MpSignInRefusedLead" : "MpSignInFailLead");
        ErrorTitleText.Text = Strings.Get(expired ? "MpSignInExpiredTitle"
            : refused ? "MpSignInRefusedTitle" : "MpSignInFailTitle");
        ErrorBodyText.Text = Strings.Get(expired ? "MpSignInExpiredBody"
            : waitingForBrowser ? "MpSignInFailBodyWait" : "MpSignInFailBody");

        // An expired link has nothing technical to say.
        ErrorDetailText.Text = detail;
        ErrorDetailText.Visibility = expired || detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        CancelButton.Content = Strings.Get("DlgClose");
        OpenBrowserButton.Content = Strings.Get(expired ? "MpSignInNewLink" : "MpSignInRetry");
        OpenBrowserButton.IsEnabled = true;
        OpenBrowserButton.ToolTip = null;
    }

    /// <summary>The host in bold white, so the check the advice asks for takes one glance.</summary>
    private void SetLink(string url)
    {
        var (host, rest) = SignInFailure.SplitLink(url);
        var line = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = VerificationUriText.FontSize * 1.5,
        };
        if (host.Length > 0)
            line.Inlines.Add(new Run(host) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        line.Inlines.Add(new Run(rest));
        VerificationUriText.Document = new FlowDocument(line) { PagePadding = new Thickness(0) };
    }

    private static void Show(params UIElement[] elements)
    {
        foreach (var element in elements) element.Visibility = Visibility.Visible;
    }

    private static void Hide(params UIElement[] elements)
    {
        foreach (var element in elements) element.Visibility = Visibility.Collapsed;
    }

    private void ShowPreview(string variant)
    {
        const string link = "https://discord.com/oauth2/authorize?response_type=code&client_id=1289443820574511"
            + "&scope=identify&state=8f21c4d0&redirect_uri=https%3A%2F%2Flobby.warsofliberty.net%2Fauth%2Fcallback";
        var start = new DeviceFlowStart { VerificationUri = link, ExpiresInSeconds = 900 };
        switch (variant.ToLowerInvariant())
        {
            case "preparing":
                break;
            case "code":
                start.UserCode = "WDJB-MJHT";
                ShowWaiting(start);
                break;
            case "failed":
                ShowFailure(SignInFailureKind.Unreachable, false,
                    "HttpRequestException: No such host is known. || SocketException: No such host is known. (lobby.warsofliberty.net:443)");
                break;
            case "expired":
                _start = start;
                ShowFailure(SignInFailureKind.Expired, true, "");
                break;
            default:
                ShowWaiting(start);
                break;
        }
    }

    private async void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_primaryRetries)
        {
            if (_previewVariant == null) await RunSignInAsync();
            else ShowPreparing();
            return;
        }
        if (_start == null) return;
        try
        {
            HarnessMode.Start(new ProcessStartInfo
            {
                FileName = _start.VerificationUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DiscordLoginDialog: failed to open browser: {ex.Message}");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_start == null) return;
        try { Clipboard.SetText(_start.UserCode); }
        catch (Exception ex) { DiagnosticLog.Write($"DiscordLoginDialog: clipboard: {ex.Message}"); }
    }

    /// <summary>Copies the whole link, so it can be pasted into a browser the player trusts when
    /// "Open browser" opens an unfamiliar one, and says so for a moment.</summary>
    private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_start == null) return;
        try { Clipboard.SetText(_start.VerificationUri); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DiscordLoginDialog: clipboard (link): {ex.Message}");
            return;
        }

        CopyLinkButton.Content = Strings.Get("MpSignInCopyLinkDone");
        CopyLinkButton.SetResourceReference(ForegroundProperty, "WindowCopied");
        if (_copiedTimer == null)
        {
            _copiedTimer = new DispatcherTimer { Interval = CopiedFor };
            _copiedTimer.Tick += (_, _) =>
            {
                _copiedTimer.Stop();
                CopyLinkButton.Content = Strings.Get("MpSignInCopyChip");
                CopyLinkButton.ClearValue(ForegroundProperty);
            };
        }
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        TrySetDialogResult(false);
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _copiedTimer?.Stop();
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    /// Sets <see cref="Window.DialogResult"/> defensively. The player can press Cancel (which closes
    /// the window) before the <see cref="OperationCanceledException"/> has come back here; writing
    /// DialogResult then throws and would take the launcher down. The state the player sees is
    /// already correct, and <c>ShowDialog()</c> returning null reads the same as false.
    /// </summary>
    private void TrySetDialogResult(bool? value)
    {
        try { DialogResult = value; }
        catch (InvalidOperationException)
        {
            // Dialog already closed — nothing more to do.
        }
    }
}
