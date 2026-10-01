using System;
using System.Collections.Generic;
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
    private readonly string _modId;
    private readonly TranslationService _translationService;
    private readonly TranslationRegistryService _registry;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// A downloaded pack verified and waiting in a scratch folder. Kept across the
    /// "apply anyway" second click so the pack isn't downloaded twice, and discarded when the
    /// dialog closes without it having been promoted.
    /// </summary>
    private StagedPack? _staged;

    /// <summary>
    /// True after this dialog successfully applied the translation. The
    /// caller (MainWindow) reads this to update its config + status text.
    /// </summary>
    public bool AppliedSuccessfully { get; private set; }

    /// <summary>Compiled twins that will keep the game from showing what was just copied.</summary>
    public IReadOnlyList<string> ShadowingFiles { get; private set; } = Array.Empty<string>();

    /// <summary>The manifest of the pack that was applied (from the download, or the installed copy).</summary>
    public TranslationManifest? AppliedManifest { get; private set; }

    /// <summary>
    /// Content hash of the applied pack — what the Language tab marks "in use" by, since the
    /// version text can't tell two packs apart.
    /// </summary>
    public string AppliedContentHash { get; private set; } = "";

    /// <summary>
    /// Tracks whether the user already saw + acknowledged the "this isn't
    /// declared compatible with your mod version" warning. The first Apply
    /// click shows the warning inline; the second proceeds anyway.
    /// </summary>
    private bool _userAcknowledgedIncompatibility;

    public TranslationApplyDialog(
        TranslationIndexEntry entry,
        string? currentModVersion,
        string modId,
        TranslationService translationService,
        TranslationRegistryService registry)
    {
        InitializeComponent();
        _entry = entry;
        _currentModVersion = currentModVersion;
        _modId = modId ?? "";
        _translationService = translationService;
        _registry = registry;

        ApplyLanguage();
        PopulateForm();

        Closed += (_, _) =>
        {
            try { _translationService.DiscardStaged(_staged); }
            catch (Exception ex) { DiagnosticLog.Write($"Apply: could not discard the staged pack: {ex.Message}"); }
        };
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgLangApplyTitle");
        TitleBarControl.Title = Strings.Get("DlgLangApplyTitle");
        LblModVersions.Text = Strings.Get("DlgLangApplyModVersionsLabel");
        LblSize.Text = Strings.Get("DlgLangApplySizeLabel");
        DescriptionLabel.Text = Strings.Get("DlgLangApplyDescriptionLabel");
        ProgressLabelText.Text = Strings.Get("DlgLangApplyDownloading");
        ApplyButton.Content = Strings.Get("DlgLangApplyBtnApply");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void PopulateForm()
    {
        FlagText.Text = LanguageCode(_entry.Id);
        var displayName = _entry.Name;
        if (!string.IsNullOrEmpty(_entry.Version))
            displayName += $"  v{_entry.Version}";
        NameText.Text = displayName;

        // Who made it and where it comes from — and, for a source the player added, that it is
        // not the mod's own. The version may come from a different translator than the card's
        // newest, so this is the chosen version's own credit.
        var credit = new List<string>();
        if (!string.IsNullOrEmpty(_entry.Author))
            credit.Add(Strings.Format("DlgLangApplyByAuthor", _entry.Author));
        if (!string.IsNullOrEmpty(_entry.SourceLabel))
            credit.Add(Strings.Format("LangCardSource", _entry.SourceLabel));
        if (!_entry.IsOfficial && _entry.SourceKind != TranslationSourceKind.Local)
            credit.Add(Strings.Get("LangCardUnofficial"));
        AuthorText.Text = string.Join("  ·  ", credit);

        ModVersionsText.Text = _entry.CompatibleWith.Count > 0
            ? string.Join(", ", _entry.CompatibleWith)
            : "—";
        SizeText.Text = _entry.Size > 0 ? FormatBytes(_entry.Size) : "—";

        if (!string.IsNullOrEmpty(_entry.Description))
        {
            DescriptionText.Text = _entry.Description;
            DescriptionPanel.Visibility = Visibility.Visible;
        }

        // Initial compatibility badge — based on the index entry's declared
        // compatibleWith list. We can't do the precise hash check yet because
        // we haven't downloaded the pack.
        bool declaredCompatible = string.IsNullOrEmpty(_currentModVersion)
            || _entry.CompatibleWith.Count == 0
            || _entry.CompatibleWith.Contains(_currentModVersion);

        if (declaredCompatible)
        {
            // When we don't know the user's installed mod version, show a
            // version-less message instead of an empty "(...)" — the metadata
            // grid below still lists the pack's compatible versions.
            SetCompatBadge(
                BadgeKind.Ok,
                "✓",
                string.IsNullOrEmpty(_currentModVersion)
                    ? Strings.Get("DlgLangApplyCompatOkNoVer")
                    : Strings.Format("DlgLangApplyCompatOk", _currentModVersion));
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
            // ---- 1. Download (skip if the installed copy IS this pack) ----
            // "This pack" is decided by content hash: two different packs can carry the same
            // version text, and every translator's pack for one language shares the same
            // translations\<id>\ folder.
            if (_staged == null)
            {
                var local = _translationService.GetInstalled(_entry.Id);
                if (local == null || !IsSamePack(local, _entry))
                {
                    if (string.IsNullOrEmpty(_entry.DownloadUrl))
                    {
                        DiagnosticLog.Write("Apply: aborted — no DownloadUrl in index entry.");
                        ShowMessage(MessageKind.Error, Strings.Get("DlgLangNoDownloadUrlBody"));
                        return;
                    }
                    // An index source must publish the zip's SHA-256; a version that lost it
                    // (whatever path built this entry) is refused rather than trusted.
                    if (_entry.SourceKind == TranslationSourceKind.Index && string.IsNullOrWhiteSpace(_entry.Sha256))
                    {
                        DiagnosticLog.Write("Apply: aborted — index-source version without a SHA-256.");
                        ShowMessage(MessageKind.Error, Strings.Get("DlgLangNoSha256"));
                        return;
                    }
                    _staged = await DownloadAndStageAsync(_cts.Token);
                }
                else
                {
                    DiagnosticLog.Write($"Apply: pack '{_entry.Id}' v{local.Version} is already installed; skipping download.");
                }
            }

            // ---- 2. Hash-level compatibility check (now that the pack is
            // on disk we can compare originalHash against the snapshot).
            // Async on purpose — calling the sync overload from the UI thread
            // would deadlock the launcher (the MD5 hashing's continuation
            // can't resume on the UI thread we'd be blocking with .Result). ----
            DiagnosticLog.Write("Apply: running compatibility check.");
            var manifest = _staged?.Manifest
                ?? _translationService.GetInstalled(_entry.Id)
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
            var staged = _staged;
            var apply = await Task.Run(() =>
            {
                if (staged == null) return _translationService.Apply(manifest.Id);
                var result = _translationService.ApplyStaged(staged);
                // Only an applied pack becomes the installed one, so translations\<id>\ always
                // holds what is live — the "in use" check compares the disk against it.
                if (result.Success)
                {
                    try { _translationService.PromoteStaged(staged); }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Write($"Apply: the pack was applied but could not replace the installed copy: {ex.Message}");
                    }
                }
                return result;
            }, _cts.Token);
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
            ShadowingFiles = apply.ShadowingFiles;
            AppliedManifest = manifest;
            AppliedContentHash = TranslationCompat.EffectiveContentHash(manifest);
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

    /// <summary>
    /// Whether the installed copy is already the pack this entry offers. By content hash when the
    /// listing advertises one; an index source without one is always downloaded (nothing else
    /// identifies it); otherwise, for old listings, by version text.
    /// </summary>
    internal static bool IsSamePack(TranslationManifest local, TranslationIndexEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ContentHash))
            return string.Equals(TranslationCompat.EffectiveContentHash(local), entry.ContentHash.Trim(),
                StringComparison.OrdinalIgnoreCase);
        if (entry.SourceKind == TranslationSourceKind.Index) return false;
        return string.IsNullOrEmpty(entry.Version) || string.Equals(local.Version, entry.Version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Downloads the pack (SHA-256-checked when the listing publishes one) and verifies it into a
    /// scratch folder. Nothing installed or live is touched here.
    /// </summary>
    private async Task<StagedPack> DownloadAndStageAsync(CancellationToken ct)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = true;
        ProgressPercentText.Text = "";
        ProgressBytesText.Text = "";

        // Room for it? The pack lands in %TEMP% once and in the install TWICE — extracted into
        // translations\<id>\ and then copied over data\. A pack discovered through the repo's
        // folder listing reports Size = 0 (the registry hard-codes it there), which Check reads
        // as unmeasurable and stays quiet about, exactly like every other unknown here.
        var spaceShortfall = DiskSpaceService.Check(
            _translationService.TranslationsRoot,
            Math.Max(0, _entry.Size) * DiskSpaceService.TranslationInstallFactor,
            Path.GetTempPath(), Math.Max(0, _entry.Size));
        if (!DiskSpacePrompt.ConfirmOrCancel(this, spaceShortfall, "DiskSpaceConfirmDownloadBody"))
            throw new OperationCanceledException();

        // Download to a temp file
        var tempZip = Path.Combine(Path.GetTempPath(),
            $"wol-translation-{_entry.Id}-{Guid.NewGuid():N}.zip");
        try
        {
            // The registry's download method doesn't currently expose progress;
            // wrap it so we at least show "downloading..." indefinitely. Future
            // improvement: thread per-byte progress through.
            await _registry.DownloadPackAsync(_entry.DownloadUrl, tempZip, _entry.Sha256, ct);

            // Show a fake "100%" tick before switching to install
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = 100;
            ProgressPercentText.Text = "100%";
            ProgressBytesText.Text = FormatBytes(_entry.Size);

            ProgressLabelText.Text = Strings.Get("DlgLangApplyInstalling");
            DownloadProgress.IsIndeterminate = true;
            // The pack must be the one that was listed: its id (a mismatch would land in a folder
            // this dialog never reads), the mod it was made for, and the content hash the listing
            // advertised. A source the player added must also say which mod the pack is for.
            var staged = await _translationService.StagePackFromZipAsync(tempZip,
                new PackExpectation(_entry.Id, _modId, _entry.ContentHash, RequireTargetMod: !_entry.IsOfficial), ct);
            DownloadProgress.IsIndeterminate = false;
            return staged;
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
                CompatBadge.Background = Res("StatusInstalledBg");
                CompatBadge.BorderBrush = Res("StatusInstalledFg");
                CompatIcon.Foreground = Res("StatusInstalledFg");
                CompatText.Foreground = Res("StatusInstalledFg");
                break;
            case BadgeKind.Warn:
                CompatBadge.Background = Res("StatusUpdateBg");
                CompatBadge.BorderBrush = Res("StatusUpdateFg");
                CompatIcon.Foreground = Res("StatusUpdateFg");
                CompatText.Foreground = Res("StatusUpdateFg");
                break;
            case BadgeKind.Bad:
                CompatBadge.Background = Res("StatusErrorBg");
                CompatBadge.BorderBrush = Res("StatusErrorFg");
                CompatIcon.Foreground = Res("StatusErrorFg");
                CompatText.Foreground = Res("StatusErrorFg");
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
                MessagePanel.Background = Res("CatalogBlueSubtle");
                MessagePanel.BorderBrush = Res("InfoBrush");
                MessageText.Foreground = Res("InfoBrush");
                break;
            case MessageKind.Warn:
                MessagePanel.Background = Res("StatusUpdateBg");
                MessagePanel.BorderBrush = Res("StatusUpdateFg");
                MessageText.Foreground = Res("StatusUpdateFg");
                break;
            case MessageKind.Error:
                MessagePanel.Background = Res("StatusErrorBg");
                MessagePanel.BorderBrush = Res("StatusErrorFg");
                MessageText.Foreground = Res("StatusErrorFg");
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

    /// <summary>Resolve a theme brush from the merged resource dictionaries.</summary>
    private System.Windows.Media.Brush Res(string key) =>
        (System.Windows.Media.Brush)FindResource(key);

    /// <summary>
    /// Two-letter language badge for the monogram chip ("es" → "ES",
    /// "pt-br" → "PT"). Windows can't render flag emojis (it falls back to
    /// the bare regional-indicator letters anyway), so a coloured letter
    /// chip is both more legible and on-theme.
    /// </summary>
    private static string LanguageCode(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "??";
        var sb = new System.Text.StringBuilder(2);
        foreach (var ch in id)
        {
            if (!char.IsLetter(ch)) continue;
            sb.Append(char.ToUpperInvariant(ch));
            if (sb.Length == 2) break;
        }
        return sb.Length > 0 ? sb.ToString() : "??";
    }

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
