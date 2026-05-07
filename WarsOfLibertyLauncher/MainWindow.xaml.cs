using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly UpdateService _updateService;
    private readonly InstallerService _installerService;
    private List<DownloadInfo> _pendingDownloads = new();
    private CancellationTokenSource? _cts;
    private FolderCloneService? _cloneService;
    private bool _isBusy;
    private bool _isUpdateInProgress;     // true while ApplyUpdatesAsync is running
    private bool _modIsInstalled = true;  // false when no valid install detected
    private bool _isPaused;
    private bool _warnedAboutBrokenInstall;
    private bool _isGameRunning;
    private DispatcherTimer? _gameMonitorTimer;

    /// <summary>
    /// Current install phase. Drives both the breadcrumb visual and the
    /// speed-label text (so "5 MB/s" gets prefixed with "Download" during
    /// the download phase but "Copy" during the clone phase).
    /// </summary>
    private InstallPhase _currentInstallPhase = InstallPhase.None;

    /// <summary>
    /// Current update sub-phase (Download / Verify / Apply). Drives the
    /// 3-dot mini-breadcrumb in the update overlay AND the speed label.
    /// </summary>
    private UpdatePhase _currentUpdatePhase = UpdatePhase.None;

    public MainWindow()
    {
        InitializeComponent();
        DiagnosticLog.Reset();
        DiagnosticLog.Write("MainWindow initialized.");

        _config = LauncherConfig.Load();
        Strings.SetLanguage(_config.Language);
        Strings.LanguageChanged += ApplyLanguage;

        DiagnosticLog.Write($"Config loaded. updateInfoUrl={_config.UpdateInfoUrl}");
        DiagnosticLog.Write($"  modInstallPath={_config.ModInstallPath}");
        DiagnosticLog.Write($"  gameExecutable={_config.GameExecutable}");
        DiagnosticLog.Write($"  language={_config.Language}");

        _updateService = new UpdateService(_config);
        _installerService = new InstallerService();

        ApplyLanguage();
        ResetProgressUI();

        // Check for --update-now flag from elevated relaunch
        var args = Environment.GetCommandLineArgs();
        bool autoUpdate = args.Any(a => string.Equals(a, "--update-now", StringComparison.OrdinalIgnoreCase));
        if (autoUpdate)
            DiagnosticLog.Write("Started with --update-now: will auto-apply updates after check.");

        // Auto-check for updates on startup. Run the launcher self-update
        // check and the mod-version check IN PARALLEL — they hit different
        // servers (GitHub vs aoe3wol.com) and each typically takes ~1 second,
        // so doing both concurrently roughly halves the time the UI sits in
        // a "busy" state right after the user opens the launcher.
        Loaded += async (_, _) =>
        {
            LauncherUpdateService.CleanupOldVersion();
            await Task.WhenAll(
                CheckForLauncherUpdateAsync(),
                CheckAsync());
            if (_modIsInstalled)
            {
                _ = Task.Run(InstallerService.TryCleanupTemp);
                _ = Task.Run(NativeInstallService.TryCleanupTemp);
            }
            if (autoUpdate && _modIsInstalled && _pendingDownloads.Count > 0)
            {
                await ApplyAsync();
            }
        };
    }

    // ------------------------------------------------------------------------
    // Language
    // ------------------------------------------------------------------------

    private void LangEnButton_Click(object sender, RoutedEventArgs e) => SwitchLanguage(Strings.LangEn);
    private void LangEsButton_Click(object sender, RoutedEventArgs e) => SwitchLanguage(Strings.LangEs);

    private void SwitchLanguage(string lang)
    {
        if (_config.Language == lang) return;
        _config.Language = lang;
        _config.Save();
        Strings.SetLanguage(lang);
    }

    /// <summary>Refresh every translatable string in the UI from the table.</summary>
    private void ApplyLanguage()
    {
        Title = Strings.Get("WindowTitle");

        // Show "(running as administrator)" suffix in the subtitle when the
        // launcher is elevated. Useful as a visual confirmation after UAC.
        var subtitle = Strings.Get("Subtitle");
        if (ElevationService.IsRunningAsAdmin())
            subtitle += "  " + Strings.Get("StatusRunningAsAdmin");
        SubtitleText.Text = subtitle;
        LblInstalledVersion.Text = Strings.Get("InstalledVersion");
        LblLatestVersion.Text = Strings.Get("LatestVersion");
        LblModPath.Text = Strings.Get("ModPath");
        NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");
        LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        LblOverall.Text = Strings.Get("ProgressOverall");
        PlayButton.Content = _isGameRunning
            ? Strings.Get("BtnPlaying")
            : Strings.Get("BtnPlay");
        VerifyButton.Content = Strings.Get("BtnVerify");
        StopButton.Content = Strings.Get("BtnStop");
        // Headers (the visible label of each item)
        UninstallMenuItem.Header = Strings.Get("MenuUninstall");
        MenuFolders.Header = Strings.Get("MenuFolders");
        MenuOpenModFolder.Header = Strings.Get("MenuOpenModFolder");
        MenuOpenAoE3Folder.Header = Strings.Get("MenuOpenAoE3Folder");
        MenuSelectModFolder.Header = Strings.Get("MenuSelectModFolder");
        MenuSelectAoE3Folder.Header = Strings.Get("MenuSelectAoE3Folder");
        MenuUserData.Header = Strings.Get("MenuUserData");
        MenuOpenUserDataFolder.Header = Strings.Get("MenuOpenUserDataFolder");
        MenuCreateBackupNow.Header = Strings.Get("MenuCreateBackupNow");
        MenuRestoreUserData.Header = Strings.Get("MenuRestoreUserData");
        MenuCheckForUpdates.Header = Strings.Get("MenuCheckForUpdates");
        MenuVerifyFiles.Header = Strings.Get("MenuVerifyFiles");

        // Tooltips on LEAF items only — items with submenus (Carpetas,
        // Datos de usuario) are self-explanatory once the submenu opens,
        // and showing a tooltip on top of the submenu just causes visual
        // conflict. Same pattern as VS Code, Notion, native OS menus.
        MoreButton.ToolTip = BuildMenuTooltip(
            Strings.Get("TooltipSettings"), Strings.Get("TooltipSettingsBody"));
        MenuOpenModFolder.ToolTip = BuildMenuTooltip(
            (string)MenuOpenModFolder.Header, Strings.Get("TooltipMenuOpenModFolder"));
        MenuOpenAoE3Folder.ToolTip = BuildMenuTooltip(
            (string)MenuOpenAoE3Folder.Header, Strings.Get("TooltipMenuOpenAoE3Folder"));
        MenuSelectModFolder.ToolTip = BuildMenuTooltip(
            (string)MenuSelectModFolder.Header, Strings.Get("TooltipMenuSelectModFolder"));
        MenuSelectAoE3Folder.ToolTip = BuildMenuTooltip(
            (string)MenuSelectAoE3Folder.Header, Strings.Get("TooltipMenuSelectAoE3Folder"));
        MenuOpenUserDataFolder.ToolTip = BuildMenuTooltip(
            (string)MenuOpenUserDataFolder.Header, Strings.Get("TooltipMenuOpenUserDataFolder"));
        MenuCreateBackupNow.ToolTip = BuildMenuTooltip(
            (string)MenuCreateBackupNow.Header, Strings.Get("TooltipMenuCreateBackupNow"));
        MenuRestoreUserData.ToolTip = BuildMenuTooltip(
            Strings.Get("MenuRestoreUserData"), Strings.Get("TooltipMenuRestoreUserData"));
        MenuCheckForUpdates.ToolTip = BuildMenuTooltip(
            (string)MenuCheckForUpdates.Header, Strings.Get("TooltipMenuCheckForUpdates"));
        MenuVerifyFiles.ToolTip = BuildMenuTooltip(
            (string)MenuVerifyFiles.Header, Strings.Get("TooltipMenuVerifyFiles"));
        UninstallMenuItem.ToolTip = BuildMenuTooltip(
            (string)UninstallMenuItem.Header, Strings.Get("TooltipMenuUninstall"));

        LblGamePath.Text = Strings.Get("LblGamePath");

        // Buttons that change content based on state — pick the right label
        if (!_modIsInstalled)
            UpdateButton.Content = Strings.Get("BtnInstall");
        else if (_pendingDownloads.Count > 0)
            UpdateButton.Content = Strings.Get("BtnUpdate");
        else
            UpdateButton.Content = Strings.Get("BtnCheckUpdates");

        // Highlight the active language toggle
        LangEnButton.Foreground = Strings.Language == Strings.LangEn
            ? System.Windows.Media.Brushes.White
            : System.Windows.Media.Brushes.Gray;
        LangEsButton.Foreground = Strings.Language == Strings.LangEs
            ? System.Windows.Media.Brushes.White
            : System.Windows.Media.Brushes.Gray;
    }

    // ------------------------------------------------------------------------
    // Folder browse
    // ------------------------------------------------------------------------

    private void BrowseAoE3Button_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgAoE3FolderPickerTitle"),
            Multiselect = false
        };

        // Pre-fill with a sensible starting directory
        if (!string.IsNullOrEmpty(_config.GameExecutable)
            && Directory.Exists(Path.GetDirectoryName(_config.GameExecutable)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_config.GameExecutable)!;
        }

        if (dialog.ShowDialog(this) != true) return;

        var chosen = dialog.FolderName.TrimEnd('\\', '/');

        // Try to find age3y.exe in the selected folder or its bin\ subfolder
        string? resolvedExe = null;
        var candidates = new[]
        {
            Path.Combine(chosen, "age3y.exe"),
            Path.Combine(chosen, "bin", "age3y.exe"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                resolvedExe = candidate;
                break;
            }
        }

        if (resolvedExe == null)
        {
            MessageBox.Show(this,
                Strings.Get("DlgInvalidAoE3FolderBody"),
                Strings.Get("DlgInvalidAoE3FolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save and update UI
        _config.GameExecutable = resolvedExe;
        _config.Save();
        DiagnosticLog.Write($"User manually set AoE3 path: {resolvedExe}");

        UpdateAoE3PathUI();
        SetStatus(Strings.Get("StatusAoE3Configured"));
    }

    /// <summary>
    /// Checks whether AoE3 is reachable and updates the AoE3 path row visibility.
    /// Call after CheckAsync or after the user manually selects a folder.
    /// </summary>
    private void UpdateAoE3PathUI()
    {
        var exePath = GameLauncher.Find(_config, _updateService.InstallPath);
        AoE3PathRow.Visibility = Visibility.Visible;

        if (exePath != null)
        {
            // AoE3 found — show path in normal (white) style
            GamePathText.Text = Path.GetDirectoryName(exePath) ?? exePath;
            GamePathText.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#ccc")!;
        }
        else
        {
            // AoE3 NOT found — show red message; user can fix via gear menu
            GamePathText.Text = Strings.Get("StatusAoE3NotDetected");
            GamePathText.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#e63950")!;
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgFolderPickerTitle"),
            Multiselect = false
        };

        if (!string.IsNullOrEmpty(_updateService.InstallPath)
            && Directory.Exists(_updateService.InstallPath))
        {
            dialog.InitialDirectory = _updateService.InstallPath;
        }

        if (dialog.ShowDialog(this) != true) return;

        var chosen = dialog.FolderName.TrimEnd('\\', '/');

        // Be tolerant: if the user picked the AoE3 root by mistake, try
        // common WoL subfolders inside it before failing.
        string? resolved = null;
        var candidates = new[]
        {
            chosen,
            Path.Combine(chosen, "Wars of Liberty"),
            Path.Combine(chosen, "WarsOfLiberty"),
        };
        foreach (var candidate in candidates)
        {
            if (RegistryService.IsValidInstall(candidate))
            {
                resolved = candidate.TrimEnd('\\', '/');
                break;
            }
        }

        if (resolved == null)
        {
            MessageBox.Show(this,
                Strings.Get("DlgInvalidFolderBody"),
                Strings.Get("DlgInvalidFolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ModInstallPath = resolved;
        _config.Save();
        await CheckAsync();
    }

    // ------------------------------------------------------------------------
    // Update flow
    // ------------------------------------------------------------------------

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_modIsInstalled)
        {
            await InstallAsync();
        }
        else if (_pendingDownloads.Count > 0)
        {
            if (!EnsureGameNotRunning()) return;
            await ApplyUpdateWithElevationCheckAsync();
        }
        else
        {
            await CheckAsync();
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_modIsInstalled || string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }

        if (_isBusy) return;

        SetStatus(Strings.Get("StatusVerifying"));
        var result = await Task.Run(() => VerifyInstallation(_updateService.InstallPath));

        if (result.MissingItems.Count == 0 && result.CorruptItems.Count == 0)
        {
            SetStatus(Strings.Format("StatusVerifyOk", result.TotalFilesChecked));
            return;
        }

        // Build a report
        var problems = new List<string>();
        problems.AddRange(result.MissingItems.Select(m => $"[missing] {m}"));
        problems.AddRange(result.CorruptItems.Select(c => $"[empty] {c}"));
        var problemList = string.Join(", ", problems.Take(10));
        int totalProblems = result.MissingItems.Count + result.CorruptItems.Count;

        SetStatus(Strings.Format("StatusVerifyMissing", totalProblems, problemList));
        DiagnosticLog.Write($"Verification: {totalProblems} problems found:");
        foreach (var p in problems) DiagnosticLog.Write($"  {p}");

        // Offer to repair
        var repair = MessageBox.Show(this,
            Strings.Format("DlgVerifyRepairBody", totalProblems),
            Strings.Get("DlgVerifyRepairTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (repair == MessageBoxResult.Yes)
            await RepairInstallAsync();
    }

    /// <summary>
    /// Repairs the installation by re-downloading the WoL payload and
    /// re-copying the mod files over the existing install.
    /// </summary>
    private async Task RepairInstallAsync()
    {
        if (_isBusy) return;

        var payloadUrls = _config.PayloadZipUrls;
        if (payloadUrls == null || payloadUrls.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(_config.InstallerZipUrl))
                payloadUrls = new[] { _config.InstallerZipUrl };
            else
            {
                SetStatus(Strings.Get("DlgInstallNoUrlBody"));
                return;
            }
        }

        if (!EnsureGameNotRunning()) return;

        var installPath = _updateService.InstallPath!;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        UpdateHeaderText.Text = Strings.Get("StatusRepairing");
        UpdateSubtitleText.Visibility = Visibility.Collapsed;
        UpdateHeaderPanel.Visibility = Visibility.Visible;
        LblCurrentPatch.Text = Strings.Get("StatusDownloadingInstaller");
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchBytesText.Text = "0%";
        OverallBytesText.Text = "...";
        SpeedText.Text = "";
        EtaText.Text = "";

        var nativeInstaller = new NativeInstallService();

        try
        {
            var speed = new SpeedTracker();
            var dlProgress = new Progress<DownloadProgress>(p =>
            {
                speed.Sample(p.BytesReceived);
                if (p.TotalBytes > 0)
                {
                    var eta = speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
                    PatchProgress.Value = p.Percentage;
                    OverallProgress.Value = p.Percentage * 0.6;
                    PatchBytesText.Text = $"{p.Percentage:0.0}%";
                    OverallBytesText.Text = $"{FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)}";
                    EtaText.Text = eta.HasValue
                        ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
                        : "";
                }
                else
                {
                    PatchBytesText.Text = FormatBytes(p.BytesReceived);
                }
                SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format("ProgressSpeed", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                LblCurrentPatch.Text = Strings.Get("StatusDownloadingInstaller");
            });

            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                LblCurrentPatch.Text = s;
            });

            // Mod-only install on top of existing (overwrites damaged files).
            // No phase reporter here — repair doesn't show the breadcrumb.
            await nativeInstaller.InstallModOnlyAsync(
                payloadUrls,
                installPath,
                dlProgress,
                statusProgress,
                phaseProgress: null,
                extractProgress: null,
                overlayProgress: null,
                _cts.Token);

            PatchProgress.Value = 100;
            OverallProgress.Value = 100;

            // Re-verify
            var recheck = await Task.Run(() => VerifyInstallation(installPath));
            if (recheck.MissingItems.Count == 0 && recheck.CorruptItems.Count == 0)
                SetStatus(Strings.Get("StatusRepairSuccess"));
            else
                SetStatus(Strings.Format("StatusRepairPartial", recheck.MissingItems.Count + recheck.CorruptItems.Count));
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            DiagnosticLog.Write($"Repair failed: {ex}");
        }
        finally
        {
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
            UpdateHeaderPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Runs the update flow, but first checks whether the launcher has write
    /// permission on the mod's install folder. If it doesn't (typical for
    /// installs under C:\Program Files), prompts the user to consent to a
    /// UAC elevation and relaunches the app with admin privileges.
    /// </summary>
    private async Task ApplyUpdateWithElevationCheckAsync()
    {
        if (string.IsNullOrEmpty(_updateService.InstallPath))
        {
            await ApplyAsync();
            return;
        }

        // If we already have write access, just proceed normally
        if (ElevationService.CanWriteTo(_updateService.InstallPath))
        {
            await ApplyAsync();
            return;
        }

        // We don't have permission. Ask the user to elevate.
        DiagnosticLog.Write(
            $"Cannot write to install folder '{_updateService.InstallPath}'. " +
            "Prompting user to relaunch elevated.");

        var result = MessageBox.Show(
            this,
            Strings.Format("DlgElevationRequiredBody", _updateService.InstallPath),
            Strings.Get("DlgElevationRequiredTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.OK)
        {
            SetStatus(Strings.Get("StatusElevationDenied"));
            return;
        }

        // Relaunch ourselves elevated, passing --update-now so the elevated
        // instance starts the update flow automatically (instead of waiting
        // for the user to click UPDATE again).
        var relaunched = ElevationService.RelaunchElevated("--update-now");
        if (relaunched)
        {
            // The elevated process is starting; close this one to avoid
            // having two launchers open simultaneously.
            Application.Current.Shutdown();
        }
        else
        {
            // User declined UAC, or relaunch failed for some other reason
            SetStatus(Strings.Get("StatusElevationDenied"));
        }
    }

    private Task CheckForLauncherUpdateAsync()
    {
        try
        {
            return CheckForLauncherUpdateInnerAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Launcher self-update error: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    private async Task CheckForLauncherUpdateInnerAsync()
    {
        var result = await LauncherUpdateService.CheckAsync(
            lastInstalledTag: _config.LastInstalledLauncherTag,
            skippedTag: _config.SkippedLauncherTag);
        if (!result.UpdateAvailable) return;

        // Pass the config to the dialog so it can persist the new tag right
        // before relaunching (avoids a brief race where the new binary boots
        // and re-checks before this process has saved).
        var dialog = new LauncherUpdateDialog(result, _config) { Owner = this };
        var accepted = dialog.ShowDialog();

        // If the user dismissed the update (closed it or clicked "Later"),
        // remember this tag so we don't keep prompting until a different
        // tag appears on GitHub.
        if (accepted != true)
        {
            _config.SkippedLauncherTag = result.RemoteTag;
            _config.Save();
            DiagnosticLog.Write($"User dismissed launcher update {result.RemoteTag}; saved as skipped.");
        }
        // If accepted, the dialog already saved LastInstalledLauncherTag and
        // launched the new binary — nothing else to do.
    }

    private async Task CheckAsync()
    {
        if (_isBusy) return;
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            var statusReporter = new Progress<string>(SetStatus);
            var result = await _updateService.CheckAsync(statusReporter, _cts.Token);

            InstallPathText.Text = _updateService.InstallPath ?? "(not detected)";
            CurrentVersionText.Text = result.CurrentVersion?.Ver ?? "?";
            LatestVersionText.Text = result.LatestVersion?.Ver ?? "—";

            _modIsInstalled = result.IsValidInstall;

            if (!result.IsValidInstall)
            {
                // No installation detected — switch the main button to INSTALL MOD
                SetStatus(Strings.Get("StatusNotInstalled"));
                UpdateButton.Content = Strings.Get("BtnInstall");
                UpdateButton.Background = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString("#c8102e")!;
                UpdateButton.Visibility = Visibility.Visible;
                _pendingDownloads = new();
                UpdateAoE3PathUI();
                return;
            }

            // Installed — restore the normal (gray) update button (visibility
            // is decided below based on whether there are pending updates).
            UpdateButton.Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#3a3d44")!;
            UpdateAoE3PathUI();

            // Sanity check: WoL is installed, but is age3y.exe reachable?
            // If the user installed WoL outside of an AoE3 folder, the mod
            // files are on disk but the engine will never load them. Warn
            // them once per session so they can fix it.
            if (!_warnedAboutBrokenInstall
                && !Services.AoE3Detector.LooksLikeInsideAoE3(_updateService.InstallPath!)
                && GameLauncher.Find(_config, _updateService.InstallPath) == null)
            {
                _warnedAboutBrokenInstall = true;
                MessageBox.Show(this,
                    Strings.Format("DlgBrokenInstallBody", _updateService.InstallPath),
                    Strings.Get("DlgBrokenInstallTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _pendingDownloads = result.PendingDownloads;

            if (_pendingDownloads.Count == 0)
            {
                bool onLatest = result.CurrentVersion?.Ver == result.LatestVersion?.Ver;
                if (onLatest)
                {
                    SetStatus(Strings.Format("StatusUpToDate", result.CurrentVersion?.Ver));
                }
                else
                {
                    SetStatus(Strings.Format(
                        "StatusVersionTooOld",
                        result.CurrentVersion?.Ver,
                        result.LatestVersion?.Ver));
                }
                // Hide the UpdateButton entirely when there's nothing to do —
                // the bottom bar shows just JUGAR (and the gear menu still has
                // Verify files for occasional integrity checks).
                UpdateButton.Visibility = Visibility.Collapsed;
                ResetProgressUI();
            }
            else
            {
                long totalBytes = 0;
                foreach (var d in _pendingDownloads) totalBytes += d.Size;
                SetStatus(Strings.Format(
                    "StatusUpdatesAvailable",
                    _pendingDownloads.Count,
                    FormatBytes(totalBytes)));
                UpdateButton.Content = Strings.Get("BtnUpdate");
                UpdateButton.Visibility = Visibility.Visible;
                ResetProgressUI();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledCheck"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------------------
    // Pause / Cancel — only shown while a long operation is in progress
    // ------------------------------------------------------------------------

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        _installerService.IsPaused = _isPaused;
        _updateService.IsPaused = _isPaused;
        if (_cloneService != null) _cloneService.Pause = _isPaused;

        if (_isPaused)
        {
            PauseButton.Content = Strings.Get("BtnResume");
            SetStatus(Strings.Get("StatusPaused"));
        }
        else
        {
            PauseButton.Content = Strings.Get("BtnPause");
            // Status will be overwritten by the next progress callback
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // If currently paused, un-pause first so the cancellation can propagate
        if (_isPaused)
        {
            _isPaused = false;
            _installerService.IsPaused = false;
            _updateService.IsPaused = false;
        }
        _cts?.Cancel();
    }

    /// <summary>
    /// Show or hide the pause/cancel buttons. Called at the start and end of
    /// long-running download operations.
    /// </summary>
    private void ShowDownloadControls(bool show)
    {
        if (show)
        {
            _isPaused = false;
            _installerService.IsPaused = false;
            _updateService.IsPaused = false;
            PauseButton.Content = Strings.Get("BtnPause");
            CancelButton.Content = Strings.Get("BtnCancel");
            PauseButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Visible;
        }
        else
        {
            PauseButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _isPaused = false;
            _installerService.IsPaused = false;
            _updateService.IsPaused = false;
        }
    }

    // ------------------------------------------------------------------------
    // Game process detection
    // ------------------------------------------------------------------------

    /// <summary>
    /// Checks if the AoE3 game is currently running. If so, offers to close it.
    /// Returns true if it's safe to proceed (game not running, or user closed it).
    /// </summary>
    private bool EnsureGameNotRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("age3y");
        if (processes.Length == 0) return true;

        var result = MessageBox.Show(this,
            Strings.Get("DlgGameRunningBody"),
            Strings.Get("DlgGameRunningTitle"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                    DiagnosticLog.Write($"Closed game process (PID {p.Id}).");
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Failed to close game: {ex.Message}");
                }
            }
            return true;
        }

        return result != MessageBoxResult.Cancel;
    }

    // ------------------------------------------------------------------------
    // Install verification
    // ------------------------------------------------------------------------

    /// <summary>Result of a verification scan.</summary>
    private record VerifyResult(
        List<string> MissingItems,
        List<string> CorruptItems,
        int TotalFilesChecked);

    /// <summary>
    /// Deep verification of the mod installation.
    /// Checks critical folders, files, and looks for zero-byte files that
    /// indicate a broken copy.
    /// </summary>
    private static VerifyResult VerifyInstallation(string installPath)
    {
        var missing = new List<string>();
        var corrupt = new List<string>();
        int totalChecked = 0;

        // --- Required mod directories ---
        string[] requiredDirs = new[]
        {
            @"art\zulushield",
            @"data",
            @"sound",
            @"AI3",
        };
        foreach (var rel in requiredDirs)
        {
            totalChecked++;
            var full = Path.Combine(installPath, rel);
            if (!Directory.Exists(full))
                missing.Add(rel + @"\");
        }

        // --- Check data files (the large .bar archives) ---
        var dataDir = Path.Combine(installPath, "data");
        if (Directory.Exists(dataDir))
        {
            var barFiles = Directory.GetFiles(dataDir, "*.bar", SearchOption.TopDirectoryOnly);
            totalChecked += barFiles.Length;
            if (barFiles.Length == 0)
            {
                missing.Add(@"data\*.bar (no data archives found)");
            }
            else
            {
                foreach (var bar in barFiles)
                {
                    var info = new FileInfo(bar);
                    // .bar files should be at least 1 MB; zero or tiny means broken
                    if (info.Length < 1024)
                        corrupt.Add(@"data\" + info.Name);
                }
            }
        }

        // --- Check sound files ---
        var soundDir = Path.Combine(installPath, "sound");
        if (Directory.Exists(soundDir))
        {
            var soundFiles = Directory.GetFiles(soundDir, "*", SearchOption.AllDirectories);
            totalChecked += soundFiles.Length;
            if (soundFiles.Length < 5)
                missing.Add(@"sound\ (too few files: " + soundFiles.Length + ")");
        }

        // --- Check art\zulushield contents (WoL marker) ---
        var zulushield = Path.Combine(installPath, "art", "zulushield");
        if (Directory.Exists(zulushield))
        {
            var artFiles = Directory.GetFiles(zulushield, "*", SearchOption.AllDirectories);
            totalChecked += artFiles.Length;
            if (artFiles.Length == 0)
                missing.Add(@"art\zulushield\ (empty — mod marker missing)");
        }

        // --- Spot-check: random sample of files for zero-byte ---
        try
        {
            var allFiles = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
            totalChecked += Math.Min(allFiles.Length, 200);
            var sample = allFiles.Length > 200
                ? allFiles.OrderBy(_ => Guid.NewGuid()).Take(200)
                : allFiles.AsEnumerable();

            foreach (var file in sample)
            {
                var info = new FileInfo(file);
                // Skip known-empty files like markers, but flag actual content files
                if (info.Length == 0 && !info.Name.StartsWith(".")
                    && info.Extension is ".bar" or ".xml" or ".xmb" or ".dll" or ".exe" or ".ddt")
                {
                    var rel = Path.GetRelativePath(installPath, file);
                    corrupt.Add(rel);
                }
            }
        }
        catch { /* non-fatal */ }

        return new VerifyResult(missing, corrupt, totalChecked);
    }

    /// <summary>
    /// Full first-time install flow (native — no Inno Setup):
    ///   1. Shows a styled dialog to pick destination folder
    ///   2. Downloads the WoL payload ZIP parts
    ///   3. Clones AoE3 to the destination (if detected)
    ///   4. Copies WoL files on top
    ///   5. Creates shortcuts + registry entries
    ///   6. Verifies the installation
    /// </summary>
    private async Task InstallAsync()
    {
        if (_isBusy) return;

        // Check if game is running first
        if (!EnsureGameNotRunning()) return;

        // Resolve payload URLs
        var payloadUrls = _config.PayloadZipUrls;
        if (payloadUrls == null || payloadUrls.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(_config.InstallerZipUrl))
                payloadUrls = new[] { _config.InstallerZipUrl };
            else
            {
                SetStatus(Strings.Get("DlgInstallNoUrlBody"));
                return;
            }
        }

        // Detect AoE3
        var aoe3Installs = AoE3Detector.FindAll();
        string? aoe3SourcePath = null;
        string? aoe3SourceLabel = null;
        string suggestedFolder;

        if (aoe3Installs.Count > 0)
        {
            // Use ModRoot (full AoE3 install root) as the clone source, NOT
            // GameFolder. For Steam, GameFolder is the `bin\` subfolder which
            // contains only the executable — cloning that would skip data\,
            // sound\, art\ and the rest of the game files the mod needs.
            aoe3SourcePath = aoe3Installs[0].ModRoot;
            aoe3SourceLabel = aoe3Installs[0].Source;
            suggestedFolder = aoe3Installs[0].ModRoot;
        }
        else
        {
            suggestedFolder = _config.DefaultInstallFolder;
        }

        if (!suggestedFolder.EndsWith("Wars of Liberty", StringComparison.OrdinalIgnoreCase))
            suggestedFolder = Path.Combine(suggestedFolder, "Wars of Liberty");

        // Surface the user-data alert BEFORE the install dialog. We're about
        // to lay down the 1.0.15d base — if the user has saves/metropolises
        // from a newer install in Documents\My Games\Wars of Liberty, this
        // is the right time to offer a backup. Doing it after the install
        // makes the user wait through a 4 GB download just to learn about
        // a problem they could have addressed up-front.
        ShowUserDataAlertIfNeeded();

        // Show the styled install dialog (single popup, no MessageBoxes)
        var dialog = new InstallFolderDialog(suggestedFolder, aoe3SourcePath, aoe3SourceLabel)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        var installFolder = dialog.SelectedFolder;
        aoe3SourcePath = dialog.Aoe3SourcePath; // may have been inferred

        // ---- Permission check ----
        // Locations like C:\Program Files (x86)\... need admin to write to.
        // We probe the parent folder (the install folder doesn't exist yet).
        var probeFolder = Directory.Exists(installFolder)
            ? installFolder
            : Path.GetDirectoryName(installFolder) ?? installFolder;

        if (!ElevationService.CanWriteTo(probeFolder))
        {
            DiagnosticLog.Write(
                $"Cannot write to install folder '{probeFolder}'. " +
                "Prompting user to relaunch elevated.");

            var elevateResult = MessageBox.Show(
                this,
                Strings.Format("DlgElevationRequiredBody", probeFolder),
                Strings.Get("DlgElevationRequiredTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (elevateResult != MessageBoxResult.OK)
            {
                SetStatus(Strings.Get("StatusElevationDenied"));
                return;
            }

            // Relaunch elevated; the new instance will start fresh and the
            // user can click "Install" again with admin privileges.
            var relaunched = ElevationService.RelaunchElevated();
            if (relaunched)
            {
                Application.Current.Shutdown();
                return;
            }
            SetStatus(Strings.Get("StatusElevationDenied"));
            return;
        }

        // ---- Begin installation ----
        SetBusy(true);
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        UpdateHeaderText.Text = Strings.Get("DlgInstallTitle");
        UpdateSubtitleText.Visibility = Visibility.Collapsed;
        UpdateHeaderPanel.Visibility = Visibility.Visible;

        LblCurrentPatch.Text = Strings.Get("StatusDownloadingInstaller");
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchBytesText.Text = "0%";
        OverallBytesText.Text = "...";
        SpeedText.Text = "";
        EtaText.Text = "";

        var nativeInstaller = new NativeInstallService();

        try
        {
            // Each phase contributes a slice of the overall install bar. Tweaked
            // by feel: download dominates wall-clock when bandwidth is slow,
            // clone dominates when bandwidth is fast — but on average these
            // weights produce a bar that feels honest.
            //   Full install:  DL 40% | Extract 15% | Clone 25% | Mod 15% | Final 5%
            //   Mod-only:      DL 50% | Extract 20% |            Mod 25% | Final 5%
            bool isModOnly = aoe3SourcePath == null;
            double weightDownload = isModOnly ? 50 : 40;
            double weightExtract  = isModOnly ? 20 : 15;
            double weightClone    = isModOnly ?  0 : 25;
            double weightOverlay  = isModOnly ? 25 : 15;
            // weightFinalize = 100 - (sum of above) = 5% in both cases

            // SpeedTracker per phase so the figure resets between phases instead
            // of inheriting an inflated value from the prior step.
            var speed = new SpeedTracker();

            var dlProgress = new Progress<DownloadProgress>(p =>
            {
                speed.Sample(p.BytesReceived);
                bool knowTotal = p.TotalBytes > 0;

                if (knowTotal)
                {
                    var eta = speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
                    PatchProgress.Value = p.Percentage;
                    OverallProgress.Value = (p.Percentage / 100.0) * weightDownload;
                    PatchBytesText.Text =
                        $"{FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)}";
                    EtaText.Text = eta.HasValue
                        ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
                        : (speed.BytesPerSecond > 0
                            ? Strings.Format("ProgressEta", Strings.Get("ProgressEtaCalculating"))
                            : "");
                }
                else
                {
                    PatchProgress.Value = 0;
                    PatchBytesText.Text = FormatBytes(p.BytesReceived);
                    EtaText.Text = "";
                }
                OverallBytesText.Text = $"{OverallProgress.Value:0}%";

                SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)speed.BytesPerSecond))
                    : "";
            });

            // Extract progress — fires while ZipFile entries are being decompressed
            // to the temp folder. Bytes are uncompressed sizes; the speed tracker
            // is reset at phase boundary to give an accurate decompression rate.
            var extractProgress = new Progress<NativeInstallService.ExtractProgress>(p =>
            {
                speed.Sample(p.BytesDone);
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesDone / p.BytesTotal * 100.0
                    : (p.EntriesTotal > 0 ? (double)p.EntriesDone / p.EntriesTotal * 100.0 : 0);
                PatchProgress.Value = pct;
                OverallProgress.Value = weightDownload + (pct / 100.0) * weightExtract;
                PatchBytesText.Text = $"{p.EntriesDone}/{p.EntriesTotal} files";
                OverallBytesText.Text = $"{OverallProgress.Value:0}%";
                LblCurrentPatch.Text = Strings.Format("StatusExtractingPayload", p.EntriesDone, p.EntriesTotal);
                SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)speed.BytesPerSecond))
                    : "";
                EtaText.Text = "";
            });

            // Clone progress — fires while AoE3 files are being cloned.
            var cloneProgress = new Progress<CloneProgress>(p =>
            {
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesCopied / p.BytesTotal * 100.0
                    : 0;
                PatchProgress.Value = pct;
                OverallProgress.Value = weightDownload + weightExtract + (pct / 100.0) * weightClone;
                PatchBytesText.Text =
                    $"{FormatBytes(p.BytesCopied)} / {FormatBytes(p.BytesTotal)}";
                OverallBytesText.Text = $"{OverallProgress.Value:0}%";
                // Show "💾 <relative file path>" so the line stays consistent
                // with the emoji-prefixed status used by other phases.
                var displayFile = p.CurrentFile.Length > 80
                    ? "..." + p.CurrentFile[^80..]
                    : p.CurrentFile;
                LblCurrentPatch.Text = $"💾 {displayFile}";
                SpeedText.Text = p.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)p.BytesPerSecond))
                    : "";
            });

            // Mod overlay progress — fires while extracted mod files are being
            // copied on top of the cloned AoE3 destination.
            var overlayProgress = new Progress<NativeInstallService.ModOverlayProgress>(p =>
            {
                speed.Sample(p.BytesDone);
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesDone / p.BytesTotal * 100.0
                    : (p.FilesTotal > 0 ? (double)p.FilesDone / p.FilesTotal * 100.0 : 0);
                PatchProgress.Value = pct;
                OverallProgress.Value =
                    weightDownload + weightExtract + weightClone + (pct / 100.0) * weightOverlay;
                PatchBytesText.Text = $"{p.FilesDone}/{p.FilesTotal} files";
                OverallBytesText.Text = $"{OverallProgress.Value:0}%";
                LblCurrentPatch.Text = Strings.Format("StatusInstallingMod", p.FilesDone, p.FilesTotal);
                SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)speed.BytesPerSecond))
                    : "";
                EtaText.Text = "";
            });

            // Status updates
            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                LblCurrentPatch.Text = s;
            });

            // Phase changes: swap the speed label so it accurately describes
            // what the bytes/sec figure represents at each stage, and reset
            // the speed tracker so we start each phase with a clean slate.
            // The current phase also drives the emoji prefix on status text.
            var phaseProgress = new Progress<InstallPhase>(phase =>
            {
                _currentInstallPhase = phase;
                speed.Reset();
                // Clear stale per-phase labels until the next progress event arrives
                SpeedText.Text = "";
                EtaText.Text = "";
            });

            // Wire up pause to native installer
            _cloneService = nativeInstaller.CloneService;

            if (aoe3SourcePath != null)
            {
                // Full install: clone AoE3 + overlay WoL
                await nativeInstaller.InstallAsync(
                    payloadUrls,
                    aoe3SourcePath,
                    installFolder,
                    dlProgress,
                    cloneProgress,
                    statusProgress,
                    phaseProgress,
                    extractProgress,
                    overlayProgress,
                    _cts.Token);
            }
            else
            {
                // Mod-only: just download and copy WoL files
                await nativeInstaller.InstallModOnlyAsync(
                    payloadUrls,
                    installFolder,
                    dlProgress,
                    statusProgress,
                    phaseProgress,
                    extractProgress,
                    overlayProgress,
                    _cts.Token);
            }

            PatchProgress.Value = 100;
            OverallProgress.Value = 100;

            // Point the launcher at the new install
            _config.ModInstallPath = installFolder;
            _config.Save();

            // Verify installation
            var verifyResult = await Task.Run(() => VerifyInstallation(installFolder));
            int totalProblems = verifyResult.MissingItems.Count + verifyResult.CorruptItems.Count;
            if (totalProblems == 0)
            {
                SetStatus(Strings.Format("StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
            }
            else
            {
                SetStatus(Strings.Format("StatusInstallIncomplete", totalProblems));
                DiagnosticLog.Write($"Install verification: {totalProblems} problems found.");
                // Log every missing/corrupt item so the user can see what's
                // wrong without having to re-run a separate verify pass.
                foreach (var m in verifyResult.MissingItems)
                    DiagnosticLog.Write($"  [missing] {m}");
                foreach (var c in verifyResult.CorruptItems)
                    DiagnosticLog.Write($"  [corrupt/empty] {c}");
            }

            // Note: the user-data alert was already shown BEFORE the install
            // started — see the call site at the top of this method. Showing
            // it again here would be redundant and confusing.
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            DiagnosticLog.Write($"Install failed: {ex}");
        }
        finally
        {
            _cloneService = null;
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
            UpdateHeaderPanel.Visibility = Visibility.Collapsed;
            _currentInstallPhase = InstallPhase.None;
        }

        // Re-check to detect the freshly installed mod
        await CheckAsync();
    }

    /// <summary>
    /// If the user has a populated WoL data folder under Documents from a
    /// previous (possibly newer) install, show a styled dialog explaining
    /// the risk and offering to back it up. We never delete user data —
    /// the worst we do is rename the folder to ".bak.&lt;timestamp&gt;".
    ///
    /// Called once per fresh install. Doesn't run on plain updates.
    /// </summary>
    /// <summary>
    /// Show the user-data backup alert if there's pre-existing data under
    /// Documents\My Games\Wars of Liberty. Called only after a fresh
    /// install — that's the only deterministic moment where the version
    /// risk applies (the install always brings back the 1.0.15d base).
    /// </summary>
    private void ShowUserDataAlertIfNeeded()
    {
        var folder = UserDataService.GetUserDataFolder();
        DiagnosticLog.Write(
            $"User-data alert check. Probing path: '{folder ?? "(null)"}'");

        if (string.IsNullOrEmpty(folder))
        {
            DiagnosticLog.Write("  -> Documents path could not be resolved; skipping alert.");
            return;
        }

        if (!Directory.Exists(folder))
        {
            DiagnosticLog.Write("  -> Folder does not exist; skipping alert.");
            return;
        }

        if (!UserDataService.HasExistingUserData())
        {
            DiagnosticLog.Write("  -> Folder exists but is empty; skipping alert.");
            return;
        }

        var savegameCount = UserDataService.CountSavegameFiles();
        DiagnosticLog.Write(
            $"Pre-existing WoL user data detected. Savegame files: {savegameCount}. Showing alert.");

        var dialog = new UserDataAlertDialog(folder) { Owner = this };
        var backedUp = dialog.ShowDialog() == true;

        if (backedUp && !string.IsNullOrEmpty(dialog.BackupPath))
        {
            SetStatus(Strings.Format("StatusUserDataBackedUp", dialog.BackupPath));
        }
    }

    private async Task ApplyAsync()
    {
        if (_isBusy) return;
        SetBusy(true);
        _isUpdateInProgress = true;
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        // Same header pattern as install: title + optional subtitle. The
        // subtitle is what shows the per-patch transition during update.
        var fromVersion = _updateService.CurrentVersion?.Ver ?? "?";
        var toVersion = _updateService.LatestVersion?.Ver ?? "?";
        UpdateHeaderText.Text = Strings.Format("ProgressUpdating", fromVersion, toVersion);
        UpdateSubtitleText.Text = "";
        UpdateSubtitleText.Visibility = Visibility.Collapsed;
        UpdateHeaderPanel.Visibility = Visibility.Visible;

        // No fancy chain or breadcrumb — the patch counter in the subtitle
        // and the phase emoji in the status text carry all the info.

        bool succeeded = false;
        try
        {
            var statusReporter = new Progress<string>(SetStatus);
            var progressReporter = new Progress<UpdateProgress>(OnProgress);
            var phaseReporter = new Progress<UpdatePhase>(phase =>
            {
                _currentUpdatePhase = phase;
            });
            await _updateService.ApplyUpdatesAsync(
                _pendingDownloads, progressReporter, statusReporter, phaseReporter, _cts.Token);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(this, ex.ToString(),
                Strings.Get("DlgUpdateErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isUpdateInProgress = false;
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
            UpdateHeaderPanel.Visibility = Visibility.Collapsed;
            UpdateSubtitleText.Visibility = Visibility.Collapsed;
            _currentUpdatePhase = UpdatePhase.None;
        }

        // Re-check AFTER releasing busy state, otherwise the new CheckAsync
        // call would short-circuit on its own _isBusy guard. This refreshes
        // the version info card, the status message, and the pending list.
        if (succeeded)
        {
            await CheckAsync();
        }
    }

    private void OnProgress(UpdateProgress p)
    {
        // Per-patch progress (current operation — same as install)
        double patchPct = 0;
        if (p.PatchBytesTotal > 0)
        {
            patchPct = (double)p.PatchBytesDone / p.PatchBytesTotal * 100.0;
            PatchProgress.Value = patchPct;
            PatchBytesText.Text = $"{FormatBytes(p.PatchBytesDone)} / {FormatBytes(p.PatchBytesTotal)}";
        }
        else
        {
            PatchProgress.Value = 0;
            PatchBytesText.Text = "";
        }

        // Total progress (overall update — same as install)
        if (p.OverallBytesTotal > 0)
        {
            OverallProgress.Value = (double)p.OverallBytesDone / p.OverallBytesTotal * 100.0;
            OverallBytesText.Text = $"{OverallProgress.Value:0}%";
        }

        // Status line under the bars — phase-aware so the user always knows
        // whether we're downloading, verifying, or applying.
        LblCurrentPatch.Text = Strings.Format(
            UpdateStatusKeyForPhase(_currentUpdatePhase),
            p.PatchToVersion,
            p.CurrentStep, p.TotalSteps);

        // Subtitle in the header — "Patch 13/26: 1.1.1c → 1.1.1d"
        UpdateSubtitleText.Text = Strings.Format("ProgressPatchSubtitle",
            p.CurrentStep, p.TotalSteps, p.PatchFromVersion, p.PatchToVersion);
        UpdateSubtitleText.Visibility = Visibility.Visible;

        // Speed label — phase-aware (Download / Verify / Apply)
        SpeedText.Text = p.BytesPerSecond > 0
            ? Strings.Format(UpdateSpeedLabelKeyForPhase(_currentUpdatePhase),
                FormatBytes((long)p.BytesPerSecond))
            : "";
        EtaText.Text = p.Eta.HasValue
            ? Strings.Format("ProgressEta", FormatDuration(p.Eta.Value))
            : (p.BytesPerSecond > 0 ? Strings.Format("ProgressEta", Strings.Get("ProgressEtaCalculating")) : "");
    }

    /// <summary>
    /// Picks the status-line text key for the current update sub-phase, so
    /// the line just above the bars reads naturally:
    ///   "📥 Downloading 1.1.1d (13/26)..."
    ///   "✓ Verifying 1.1.1d (13/26)..."
    ///   "🔧 Applying 1.1.1d (13/26)..."
    /// </summary>
    private static string UpdateStatusKeyForPhase(UpdatePhase phase) => phase switch
    {
        UpdatePhase.Download => "ProgressPatchStatusDownloading",
        UpdatePhase.Verify   => "ProgressPatchStatusVerifying",
        UpdatePhase.Apply    => "ProgressPatchStatusApplying",
        _                    => "ProgressPatchOf",
    };

    /// <summary>Helper to build a SolidColorBrush from a hex string.</summary>
    private static System.Windows.Media.Brush Brush(string color) =>
        (System.Windows.Media.Brush)
            new System.Windows.Media.BrushConverter().ConvertFromString(color)!;

    /// <summary>
    /// Builds a styled menu-item tooltip with a bold title and a regular
    /// description line below it. The outer chrome (dark background, border,
    /// shadow, rounded corners) comes from the implicit ToolTip Style in
    /// MainWindow.xaml — this method only fills in the content.
    /// </summary>
    private static System.Windows.Controls.ToolTip BuildMenuTooltip(string title, string description)
    {
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = Brush("White"),
            FontSize = 13,
            FontWeight = System.Windows.FontWeights.SemiBold,
        };
        var descBlock = new System.Windows.Controls.TextBlock
        {
            Text = description,
            Foreground = Brush("#aaa"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var stack = new System.Windows.Controls.StackPanel();
        stack.Children.Add(titleBlock);
        stack.Children.Add(descBlock);

        return new System.Windows.Controls.ToolTip { Content = stack };
    }

    // ------------------------------------------------------------------------
    // Game launch
    // ------------------------------------------------------------------------

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameRunning) return; // Already running

        try
        {
            GameLauncher.Launch(_config, _updateService.InstallPath);
            StartGameMonitor();
        }
        catch (FileNotFoundException)
        {
            // age3y.exe not found — offer to browse instead of just showing an error
            UpdateAoE3PathUI();
            var result = MessageBox.Show(this,
                Strings.Get("DlgInvalidAoE3FolderBody"),
                Strings.Get("DlgGameLaunchErrorTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.OK)
                BrowseAoE3Button_Click(sender, e);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Strings.Get("DlgGameLaunchErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        var processes = Process.GetProcessesByName("age3y");
        if (processes.Length == 0)
        {
            // Game already closed — update UI
            OnGameExited();
            return;
        }

        foreach (var p in processes)
        {
            try
            {
                p.Kill();
                p.WaitForExit(5000);
                DiagnosticLog.Write($"Stopped game process (PID {p.Id}).");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Failed to stop game: {ex.Message}");
            }
        }
        OnGameExited();
    }

    /// <summary>
    /// Starts a timer that polls every 2 seconds to detect when the game exits.
    /// </summary>
    private void StartGameMonitor()
    {
        _isGameRunning = true;
        UpdateGameUI();

        _gameMonitorTimer?.Stop();
        _gameMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gameMonitorTimer.Tick += (_, _) =>
        {
            var processes = Process.GetProcessesByName("age3y");
            if (processes.Length == 0)
            {
                OnGameExited();
            }
        };
        _gameMonitorTimer.Start();
        DiagnosticLog.Write("Game monitor started.");
    }

    private void OnGameExited()
    {
        _gameMonitorTimer?.Stop();
        _gameMonitorTimer = null;
        _isGameRunning = false;
        UpdateGameUI();
        DiagnosticLog.Write("Game exited.");
    }

    /// <summary>
    /// Updates button states and status text based on whether the game is running.
    /// </summary>
    private void UpdateGameUI()
    {
        if (_isGameRunning)
        {
            PlayButton.Content = Strings.Get("BtnPlaying");
            PlayButton.IsEnabled = false;
            PlayButton.Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#2a6e2a")!;
            StopButton.Visibility = Visibility.Visible;
            SetStatus(Strings.Get("StatusPlaying"));
        }
        else
        {
            PlayButton.Content = Strings.Get("BtnPlay");
            PlayButton.IsEnabled = !_isBusy && _modIsInstalled;
            PlayButton.Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#c8102e")!;
            StopButton.Visibility = Visibility.Collapsed;
            if (!_isBusy)
                SetStatus(Strings.Get("StatusGameClosed"));
        }
    }

    // ------------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------------

    private void SetStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
            Dispatcher.Invoke(() => StatusText.Text = message);
        else
            StatusText.Text = message;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateButton.IsEnabled = !busy;
        VerifyButton.IsEnabled = !busy && _modIsInstalled;
        // Play is only available when the mod is installed, not busy, and game not already running.
        PlayButton.IsEnabled = !busy && _modIsInstalled && !_isGameRunning;
    }

    private void ResetProgressUI()
    {
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchBytesText.Text = "";
        OverallBytesText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";
        LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        UpdateHeaderPanel.Visibility = Visibility.Collapsed;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
    }

    /// <summary>
    /// Picks the right localized label for the speed text based on what the
    /// current bytes-per-second figure actually represents:
    ///   - Download:    network throughput from GitHub
    ///   - Extract:     decompression speed from the temp ZIP
    ///   - Clone / Mod: SSD copy speed
    ///   - everything else: generic "Speed:" fallback
    /// </summary>
    private static string SpeedLabelKeyForPhase(InstallPhase phase) => phase switch
    {
        InstallPhase.Download   => "ProgressSpeedDownload",
        InstallPhase.Extract    => "ProgressSpeedExtract",
        InstallPhase.Clone      => "ProgressSpeedCopy",
        InstallPhase.ModOverlay => "ProgressSpeedCopy",
        _                       => "ProgressSpeed",
    };

    // ------------------------------------------------------------------------
    // Update flow helpers (speed label, status text)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Same idea as <see cref="SpeedLabelKeyForPhase"/> but for the update
    /// flow's sub-phases: bytes/sec means network throughput while
    /// downloading and disk read while applying. "Verify" doesn't get its
    /// own label because it's instantaneous and visible only as a brief
    /// flash on the dot.
    /// </summary>
    private static string UpdateSpeedLabelKeyForPhase(UpdatePhase phase) => phase switch
    {
        UpdatePhase.Download => "ProgressSpeedDownload",
        UpdatePhase.Apply    => "ProgressSpeedCopy",
        UpdatePhase.Verify   => "ProgressSpeedVerify",
        _                    => "ProgressSpeed",
    };

    // ------------------------------------------------------------------------
    // More menu / Uninstall
    // ------------------------------------------------------------------------

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu == null) return;

        // Folders submenu — Open variants are enabled only when the path is
        // resolvable on disk; Select variants are always available unless
        // we're busy (the user might be trying to fix a detection problem).
        bool wolDetected = !string.IsNullOrEmpty(_updateService.InstallPath)
            && Directory.Exists(_updateService.InstallPath);
        bool aoe3Detected = GameLauncher.Find(_config, _updateService.InstallPath) != null;

        MenuOpenModFolder.IsEnabled = wolDetected;
        MenuOpenAoE3Folder.IsEnabled = aoe3Detected;
        MenuSelectModFolder.IsEnabled = !_isBusy;
        MenuSelectAoE3Folder.IsEnabled = !_isBusy;

        // User data submenu — same pattern as before
        var hasUserData = UserDataService.HasExistingUserData();
        var backups = UserDataService.ListBackups();

        MenuOpenUserDataFolder.IsEnabled = UserDataService.GetUserDataFolder() != null;
        MenuCreateBackupNow.IsEnabled = !_isBusy && hasUserData;
        MenuRestoreUserData.IsEnabled = !_isBusy && backups.Count > 0;

        // Append the count of available backups to the Restore label so the
        // user knows at a glance whether they have anything to restore.
        var restoreLabel = Strings.Get("MenuRestoreUserData");
        if (backups.Count > 0)
            restoreLabel = $"{restoreLabel}  ({backups.Count})";
        MenuRestoreUserData.Header = restoreLabel;

        // Health-check actions
        MenuCheckForUpdates.IsEnabled = !_isBusy && _modIsInstalled;
        MenuVerifyFiles.IsEnabled = !_isBusy && _modIsInstalled;

        UninstallMenuItem.IsEnabled = !_isBusy && _modIsInstalled;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void MenuSelectModFolder_Click(object sender, RoutedEventArgs e)
    {
        // Reuse the existing browse-mod logic
        BrowseButton_Click(sender, e);
    }

    private void MenuSelectAoE3Folder_Click(object sender, RoutedEventArgs e)
    {
        // Reuse the existing browse-AoE3 logic
        BrowseAoE3Button_Click(sender, e);
    }

    /// <summary>
    /// Opens the active Wars of Liberty install folder in Explorer. Doesn't
    /// change the saved path — just lets the user inspect what's there.
    /// </summary>
    private void MenuOpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _updateService.InstallPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this,
                Strings.Get("DlgOpenFolderNotFoundBody"),
                Strings.Get("DlgOpenFolderNotFoundTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenFolderInExplorer(path);
    }

    /// <summary>
    /// Opens the detected Age of Empires III install folder in Explorer.
    /// Walks up from the detected age3y.exe so the user lands on the AoE3
    /// root instead of `bin\` (Steam layout).
    /// </summary>
    private void MenuOpenAoE3Folder_Click(object sender, RoutedEventArgs e)
    {
        var exePath = GameLauncher.Find(_config, _updateService.InstallPath);
        if (string.IsNullOrEmpty(exePath))
        {
            MessageBox.Show(this,
                Strings.Get("DlgOpenAoE3NotFoundBody"),
                Strings.Get("DlgOpenFolderNotFoundTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Walk up from age3y.exe to find the AoE3 install root (the folder
        // that contains data\, sound\, art\, etc., one level above bin\ on
        // Steam layouts).
        var folder = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(folder)
            && string.Equals(Path.GetFileName(folder), "bin", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(folder);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                folder = parent;
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this,
                Strings.Get("DlgOpenAoE3NotFoundBody"),
                Strings.Get("DlgOpenFolderNotFoundTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenFolderInExplorer(folder);
    }

    /// <summary>Verify files menu item — same operation as the old toolbar button.</summary>
    private void MenuVerifyFiles_Click(object sender, RoutedEventArgs e)
    {
        VerifyButton_Click(sender, e);
    }

    /// <summary>
    /// Manually re-checks the server for new patches. Same flow as the
    /// automatic check on startup — refreshes version info, pending
    /// downloads, and the UI's Update / Install button state.
    /// </summary>
    private async void MenuCheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        await CheckAsync();
    }

    /// <summary>Helper for opening a folder in Windows Explorer.</summary>
    private static void OpenFolderInExplorer(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to open folder '{folder}': {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the styled restore dialog so the user can pick a specific
    /// backup to restore. The dialog handles the swap; we just surface
    /// the resulting status text in the main window.
    /// </summary>
    private void MenuRestoreUserData_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var backups = UserDataService.ListBackups();
        if (backups.Count == 0)
        {
            MessageBox.Show(this,
                Strings.Get("DlgRestoreNoBackupsBody"),
                Strings.Get("DlgRestoreNoBackupsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new UserDataRestoreDialog(backups) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.RestoredBackup == null) return;

        if (!string.IsNullOrEmpty(dialog.PreviousDataSnapshotPath))
        {
            SetStatus(Strings.Format("StatusRestoreSuccessWithSnapshot",
                Path.GetFileName(dialog.RestoredBackup.Path),
                Path.GetFileName(dialog.PreviousDataSnapshotPath)));
        }
        else
        {
            SetStatus(Strings.Format("StatusRestoreSuccess",
                Path.GetFileName(dialog.RestoredBackup.Path)));
        }
    }

    /// <summary>
    /// Opens the user-data folder in Explorer. If the active folder doesn't
    /// exist, falls back to the parent My Games folder so backups stay
    /// reachable.
    /// </summary>
    private void MenuOpenUserDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = UserDataService.GetUserDataFolder();
        if (string.IsNullOrEmpty(folder)) return;

        try
        {
            var target = Directory.Exists(folder)
                ? folder
                : Path.GetDirectoryName(folder) ?? folder;
            if (!Directory.Exists(target)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to open user-data folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually backs up the active user-data folder. Same operation as the
    /// post-install alert, but available on demand from the gear menu.
    /// </summary>
    private void MenuCreateBackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (!UserDataService.HasExistingUserData())
        {
            MessageBox.Show(this,
                Strings.Get("DlgBackupNothingBody"),
                Strings.Get("DlgBackupNothingTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            Strings.Get("DlgBackupConfirmBody"),
            Strings.Get("DlgBackupConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var path = UserDataService.BackupUserData();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show(this,
                Strings.Get("DlgUserDataAlertBackupFailedBody"),
                Strings.Get("DlgUserDataAlertBackupFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetStatus(Strings.Format("StatusUserDataBackedUp", path));
    }

    private async void UninstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }

        if (!EnsureGameNotRunning()) return;

        var uninstaller = new UninstallService();
        var plan = uninstaller.Plan(_updateService.InstallPath);

        var dialog = new UninstallDialog(plan) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (plan.Mode != UninstallMode.Valid)
            return;

        SetBusy(true);
        SetStatus(Strings.Get("StatusUninstalling"));
        ResetProgressUI();

        var progress = new Progress<(double Pct, string Step)>(p =>
        {
            OverallProgress.Value = p.Pct;
            SetStatus(p.Step);
        });

        try
        {
            var result = await uninstaller.UninstallAsync(plan, dialog.Options, progress);

            if (result.Success)
            {
                SetStatus(Strings.Format("StatusUninstallSuccess", result.FilesDeleted));
            }
            else
            {
                SetStatus(Strings.Format("StatusUninstallPartial", result.Errors.Count));
                foreach (var err in result.Errors)
                    DiagnosticLog.Write($"  uninstall error: {err}");
            }

            // Clear the saved path so re-detection runs from scratch
            if (dialog.Options.ResetConfig || result.Success)
            {
                _config.ModInstallPath = "";
                _config.GameExecutable = "";
                _config.Save();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Uninstall failed: {ex}");
            SetStatus($"Uninstall failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            // Re-check so the UI flips back to "Install" mode
            await CheckAsync();
        }
    }
}
