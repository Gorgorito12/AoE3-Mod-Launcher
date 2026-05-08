using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Single styled dialog that walks the user through applying a community
/// translation: shows pack metadata + compatibility, downloads the .zip
/// with inline progress, runs the apply, and surfaces any error inline
/// (no separate MessageBox popups). Replaces the three Windows message
/// boxes the old flow used (confirm + warning + error).
/// </summary>
public partial class TranslationApplyDialog : Window
{
    private readonly TranslationIndexEntry _entry;
    private readonly string? _currentModVersion;
    private readonly TranslationService _translationService;
    private readonly TranslationRegistryService _registry;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// True after this dialog successfully applied the translation. The
    /// caller (MainWindow) reads this to update its config + status text.
    /// </summary>
    public bool AppliedSuccessfully { get; private set; }

    /// <summary>
    /// Tracks whether the user already saw + acknowledged the "this isn't
    /// declared compatible with your mod version" warning. The first Apply
    /// click shows the warning inline; the second proceeds anyway.
    /// </summary>
    private bool _userAcknowledgedIncompatibility;

    public TranslationApplyDialog(
        TranslationIndexEntry entry,
        string? currentModVersion,
        TranslationService translationService,
        TranslationRegistryService registry)
    {
        InitializeComponent();
        _entry = entry;
        _currentModVersion = currentModVersion;
        _translationService = translationService;
        _registry = registry;

        ApplyLanguage();
        PopulateForm();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgLangApplyTitle");
        LblModVersions.Text = Strings.Get("DlgLangApplyModVersionsLabel");
        LblSize.Text = Strings.Get("DlgLangApplySizeLabel");
        ProgressLabelText.Text = Strings.Get("DlgLangApplyDownloading");
        ApplyButton.Content = Strings.Get("DlgLangApplyBtnApply");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void PopulateForm()
    {
        FlagText.Text = LanguageFlag(_entry.Id);
        var displayName = _entry.Name;
        if (!string.IsNullOrEmpty(_entry.Version))
            displayName += $"  v{_entry.Version}";
        NameText.Text = displayName;

        AuthorText.Text = string.IsNullOrEmpty(_entry.Author)
            ? ""
            : Strings.Format("DlgLangApplyByAuthor", _entry.Author);

        ModVersionsText.Text = _entry.CompatibleWith.Count > 0
            ? string.Join(", ", _entry.CompatibleWith)
            : "—";
        SizeText.Text = _entry.Size > 0 ? FormatBytes(_entry.Size) : "—";

        if (!string.IsNullOrEmpty(_entry.Description))
        {
            DescriptionText.Text = _entry.Description;
            DescriptionText.Visibility = Visibility.Visible;
        }

        // Initial compatibility badge — based on the index entry's declared
        // compatibleWith list. We can't do the precise hash check yet because
        // we haven't downloaded the pack.
        bool declaredCompatible = string.IsNullOrEmpty(_currentModVersion)
            || _entry.CompatibleWith.Count == 0
            || _entry.CompatibleWith.Contains(_currentModVersion);

        if (declaredCompatible)
        {
            SetCompatBadge(
                BadgeKind.Ok,
                "✓",
                Strings.Format("DlgLangApplyCompatOk", _currentModVersion ?? ""));
        }
        else
        {
            SetCompatBadge(
                BadgeKind.Warn,
                "⚠",
                Strings.Format("DlgLangApplyCompatWarn",
                    _currentModVersion ?? "?",
                    string.Join(", ", _entry.CompatibleWith)));
        }
    }

    // ------------------------------------------------------------------------
    // Apply button — main action. Goes through download → compatibility
    // recheck (with hashes this time) → apply → close.
    // ------------------------------------------------------------------------

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        // Soft-block when not declared compatible. First click surfaces
        // the warning; second click ("Aplicar igual") proceeds.
        bool declaredCompatible = string.IsNullOrEmpty(_currentModVersion)
            || _entry.CompatibleWith.Count == 0
            || _entry.CompatibleWith.Contains(_currentModVersion);
        if (!declaredCompatible && !_userAcknowledgedIncompatibility)
        {
            ShowMessage(MessageKind.Warn, Strings.Get("DlgLangIncompatibleBody"));
            ApplyButton.Content = Strings.Get("DlgLangApplyBtnForce");
            _userAcknowledgedIncompatibility = true;
            return;
        }

        ClearMessage();
        ApplyButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        DiagnosticLog.Write($"Apply translation '{_entry.Id}' v{_entry.Version} clicked.");

        try
        {
            // ---- 1. Download (skip if already installed at this version) ----
            var local = _translationService.GetInstalled(_entry.Id);
            bool needsDownload = local == null
                || (!string.IsNullOrEmpty(_entry.Version) && _entry.Version != local.Version);

            if (needsDownload)
            {
                if (string.IsNullOrEmpty(_entry.DownloadUrl))
                {
                    DiagnosticLog.Write("Apply: aborted — no DownloadUrl in index entry.");
                    ShowMessage(MessageKind.Error, Strings.Get("DlgLangNoDownloadUrlBody"));
                    return;
                }
                await DownloadAndInstallAsync(_cts.Token);
            }
            else
            {
                DiagnosticLog.Write($"Apply: pack '{_entry.Id}' already at v{local!.Version}; skipping download.");
            }

            // ---- 2. Hash-level compatibility check (now that the pack is
            // on disk we can compare originalHash against the snapshot).
            // Async on purpose — calling the sync overload from the UI thread
            // would deadlock the launcher (the MD5 hashing's continuation
            // can't resume on the UI thread we'd be blocking with .Result). ----
            DiagnosticLog.Write("Apply: running compatibility check.");
            var manifest = _translationService.GetInstalled(_entry.Id)
                ?? throw new InvalidOperationException("Pack didn't install correctly.");
            var compat = await _translationService.CheckCompatibilityAsync(
                manifest, _currentModVersion, _cts.Token);
            DiagnosticLog.Write($"Apply: compatibility = {compat}.");

            if (compat == CompatibilityResult.Unknown && !_userAcknowledgedIncompatibility)
            {
                ShowMessage(MessageKind.Warn, Strings.Get("DlgLangIncompatibleBody"));
                ApplyButton.Content = Strings.Get("DlgLangApplyBtnForce");
                ApplyButton.IsEnabled = true;
                _userAcknowledgedIncompatibility = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // ---- 3. Apply ----
            ProgressLabelText.Text = Strings.Get("DlgLangApplyApplying");
            DownloadProgress.IsIndeterminate = true;
            DiagnosticLog.Write($"Apply: copying pack files for '{manifest.Id}'.");
            // Run the file copies on the threadpool — they're tiny but doing
            // them on the UI thread can lock up the dialog if Defender or
            // Steam holds a brief lock on the destination XML.
            var apply = await Task.Run(
                () => _translationService.Apply(manifest.Id), _cts.Token);
            DownloadProgress.IsIndeterminate = false;

            if (!apply.Success)
            {
                DiagnosticLog.Write($"Apply: failed — {apply.ErrorMessage}");
                ShowMessage(MessageKind.Error,
                    apply.ErrorMessage ?? Strings.Get("DlgLangApplyFailedBody"));
                ApplyButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // ---- 4. Done ----
            DiagnosticLog.Write($"Apply: done — translation '{manifest.Id}' v{manifest.Version} active.");
            AppliedSuccessfully = true;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            // User clicked Cancel during the download — just restore the form.
            ProgressPanel.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translation apply error: {ex}");
            ShowMessage(MessageKind.Error,
                Strings.Format("DlgLangApplyFailedBodyDetail", ex.Message));
            ApplyButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task DownloadAndInstallAsync(CancellationToken ct)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = true;
        ProgressPercentText.Text = "";
        ProgressBytesText.Text = "";

        // Download to a temp file
        var tempZip = Path.Combine(Path.GetTempPath(),
            $"wol-translation-{_entry.Id}-{Guid.NewGuid():N}.zip");
        try
        {
            // The registry's download method doesn't currently expose progress;
            // wrap it so we at least show "downloading..." indefinitely. Future
            // improvement: thread per-byte progress through.
            await _registry.DownloadPackAsync(_entry.DownloadUrl, tempZip, ct);

            // Show a fake "100%" tick before switching to install
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = 100;
            ProgressPercentText.Text = "100%";
            ProgressBytesText.Text = FormatBytes(_entry.Size);

            ProgressLabelText.Text = Strings.Get("DlgLangApplyInstalling");
            DownloadProgress.IsIndeterminate = true;
            await _translationService.InstallPackFromZipAsync(tempZip, ct);
            DownloadProgress.IsIndeterminate = false;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    // ------------------------------------------------------------------------
    // Cancel — also serves as "abort download" while one is in flight
    // ------------------------------------------------------------------------

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            return;
        }
        DialogResult = false;
    }

    // ------------------------------------------------------------------------
    // Inline message + badge helpers
    // ------------------------------------------------------------------------

    private enum BadgeKind { Ok, Warn, Bad }
    private enum MessageKind { Info, Warn, Error }

    private void SetCompatBadge(BadgeKind kind, string icon, string text)
    {
        CompatIcon.Text = icon;
        CompatText.Text = text;
        switch (kind)
        {
            case BadgeKind.Ok:
                CompatBadge.Background = Brush("#1f3a1f");
                CompatBadge.BorderBrush = Brush("#3a8c3a");
                CompatIcon.Foreground = Brush("#9bd99b");
                CompatText.Foreground = Brush("#9bd99b");
                break;
            case BadgeKind.Warn:
                CompatBadge.Background = Brush("#3a2a1a");
                CompatBadge.BorderBrush = Brush("#8c6c3a");
                CompatIcon.Foreground = Brush("#d4a04a");
                CompatText.Foreground = Brush("#d4a04a");
                break;
            case BadgeKind.Bad:
                CompatBadge.Background = Brush("#3a1a1a");
                CompatBadge.BorderBrush = Brush("#8c3a3a");
                CompatIcon.Foreground = Brush("#e63950");
                CompatText.Foreground = Brush("#e63950");
                break;
        }
        CompatBadge.BorderThickness = new Thickness(1);
    }

    private void ShowMessage(MessageKind kind, string text)
    {
        MessageText.Text = text;
        switch (kind)
        {
            case MessageKind.Info:
                MessagePanel.Background = Brush("#1f2a3a");
                MessagePanel.BorderBrush = Brush("#3a5c8c");
                MessageText.Foreground = Brush("#9bb8d9");
                break;
            case MessageKind.Warn:
                MessagePanel.Background = Brush("#3a2a1a");
                MessagePanel.BorderBrush = Brush("#8c6c3a");
                MessageText.Foreground = Brush("#d4a04a");
                break;
            case MessageKind.Error:
                MessagePanel.Background = Brush("#3a1a1a");
                MessagePanel.BorderBrush = Brush("#8c3a3a");
                MessageText.Foreground = Brush("#e63950");
                break;
        }
        MessagePanel.BorderThickness = new Thickness(1);
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void ClearMessage()
    {
        MessagePanel.Visibility = Visibility.Collapsed;
        MessageText.Text = "";
    }

    // ------------------------------------------------------------------------
    // Static helpers
    // ------------------------------------------------------------------------

    private static System.Windows.Media.Brush Brush(string color) =>
        (System.Windows.Media.Brush)
            new System.Windows.Media.BrushConverter().ConvertFromString(color)!;

    private static string LanguageFlag(string id) => id.ToLowerInvariant() switch
    {
        "es" or "es-es" or "es-mx" or "es-ar" => "🇪🇸",
        "fr" or "fr-fr" => "🇫🇷",
        "de" or "de-de" => "🇩🇪",
        "it" or "it-it" => "🇮🇹",
        "pt" or "pt-pt" => "🇵🇹",
        "pt-br" => "🇧🇷",
        "ru" or "ru-ru" => "🇷🇺",
        "zh" or "zh-cn" or "zh-tw" => "🇨🇳",
        "ja" or "ja-jp" => "🇯🇵",
        "ko" or "ko-kr" => "🇰🇷",
        "pl" or "pl-pl" => "🇵🇱",
        _ => "🌐",
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}
