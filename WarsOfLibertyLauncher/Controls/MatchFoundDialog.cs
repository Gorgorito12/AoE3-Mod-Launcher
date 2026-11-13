using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// "Rival found": the quick-match queue matched two players and each has a few seconds to say
/// yes. It is the one moment of the search that needs the player, so it comes forward on its own
/// — over the desktop, with its own sound and the taskbar flashing (the caller does those) — and
/// counts down where it can be seen.
///
/// <para><b>Non-modal on purpose.</b> It must open with the launcher in the tray, and a modal
/// needs an owner that is on screen. Escape and the ✕ DECLINE, which costs a strike on the
/// server, so the footer says so before anybody reaches for them.</para>
///
/// <para>The answer leaves through <see cref="Accepted"/> / <see cref="Declined"/>; the server's
/// reply (both accepted, somebody declined, the clock ran out) is what closes the window, through
/// <see cref="CloseQuietly"/>, so a click and a timeout never race each other into two answers.</para>
/// </summary>
internal sealed class MatchFoundDialog : BaseDialog
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DateTime _deadlineUtc;
    private readonly int _totalMs;
    private readonly ProgressBar _bar = new() { Height = 6, Minimum = 0, Maximum = 1, Value = 1, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _clock = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _progress = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly string _rival;
    private bool _answered;
    private bool _closingQuietly;

    /// <summary>The player pressed Accept.</summary>
    public event Action? Accepted;

    /// <summary>The player pressed Decline, Escape or the ✕.</summary>
    public event Action? Declined;

    /// <param name="rivalCard">The rival's face, badge and rating, built by the tab (it owns the badge rules).</param>
    /// <param name="iChallenged">I pressed "Play" on his row: the card says I am waiting for him.</param>
    public MatchFoundDialog(string rival, FrameworkElement rivalCard, string kindLine, string? hostLine,
        int acceptMs, bool iChallenged, ModProfile? mod)
    {
        _rival = rival;
        _totalMs = Math.Max(1000, acceptMs);
        _deadlineUtc = DateTime.UtcNow.AddMilliseconds(_totalMs);
        Tone = DialogTone.Info;
        Topmost = true;
        ShowInTaskbar = true;
        Width = 460;
        SetBarTitle(Strings.Get("MmFoundBarTitle"));
        SetHeadline(iChallenged ? Strings.Format("MmChallengeSentHeadline", rival) : Strings.Get("MmFoundHeadline"));
        SetLead(kindLine);

        var body = new StackPanel();
        rivalCard.Margin = new Thickness(0, 0, 0, 0);
        body.Children.Add(rivalCard);
        if (!string.IsNullOrEmpty(hostLine))
        {
            var host = new TextBlock { Text = hostLine, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            host.SetResourceReference(TextBlock.ForegroundProperty, "MpTextMuted");
            host.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
            body.Children.Add(host);
        }
        _bar.SetResourceReference(ForegroundProperty, "MpAction");
        _bar.SetResourceReference(BackgroundProperty, "MpField");
        _bar.BorderThickness = new Thickness(0);
        body.Children.Add(_bar);
        var under = new Grid();
        _progress.SetResourceReference(TextBlock.ForegroundProperty, "MpTextMuted");
        _progress.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "MpTextBody");
        _clock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
        _clock.FontWeight = FontWeights.SemiBold;
        under.Children.Add(_progress);
        under.Children.Add(_clock);
        body.Children.Add(under);
        Body = body;

        FootNote.Text = Strings.Get("MmFoundDeclineNote");
        FootNote.Visibility = Visibility.Visible;
        FootNote.SetResourceReference(TextBlock.ForegroundProperty, "MpTextFaint");
        FootNote.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");

        PrimaryAction.Content = Strings.Get("MmAccept");
        CancelAction.Content = Strings.Get("MmDecline");
        AutomationProperties_SetIds();
        UseMod(mod);

        // A challenger has already said yes by challenging; he can still withdraw.
        if (iChallenged) MarkAccepted();

        _tick.Tick += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); _tick.Start(); };
        Closed += (_, _) => _tick.Stop();
        Closing += (_, e) =>
        {
            // The ✕ and Alt+F4 are a decline, unless the server already decided.
            if (!_closingQuietly && !_answered) { _answered = true; Declined?.Invoke(); }
        };
    }

    private void AutomationProperties_SetIds()
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(PrimaryAction, "MatchFoundAccept");
        System.Windows.Automation.AutomationProperties.SetAutomationId(CancelAction, "MatchFoundDecline");
    }

    /// <summary>How many of the two have said yes (<c>mm_accept_progress</c>).</summary>
    public void SetProgress(int accepted, int total)
        => _progress.Text = Strings.Format("MmAcceptedOf", accepted, total);

    /// <summary>The server decided: close without answering again.</summary>
    public void CloseQuietly()
    {
        _closingQuietly = true;
        try { Close(); } catch (InvalidOperationException) { /* already closing */ }
    }

    protected override void OnPrimary()
    {
        if (_answered) return;
        _answered = true;
        MarkAccepted();
        Accepted?.Invoke();
    }

    protected override void OnCancel()
    {
        // Accepted and then thought better of it is a decline too: the server takes the last word.
        _answered = true;
        Declined?.Invoke();
        CloseQuietly();
    }

    private void MarkAccepted()
    {
        _answered = true;
        PrimaryAction.IsEnabled = false;
        PrimaryAction.Content = Strings.Format("MmWaitingFor", _rival);
        CancelAction.Content = Strings.Get("MmWithdraw");
    }

    private void Paint()
    {
        var left = Math.Max(0, (_deadlineUtc - DateTime.UtcNow).TotalMilliseconds);
        _bar.Value = left / _totalMs;
        _clock.Text = Strings.Format("MmSecondsLeft", (int)Math.Ceiling(left / 1000));
        if (left <= 0) _tick.Stop();
    }
}
