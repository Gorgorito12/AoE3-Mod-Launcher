using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

        // Auto-check for updates on startup
        Loaded += async (_, _) =>
        {
            LauncherUpdateService.CleanupOldVersion();
            await CheckForLauncherUpdateAsync();
            await CheckAsync();
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
        BrowseButton.Content = Strings.Get("ChangePathButton");
        NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");
        LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        LblOverall.Text = Strings.Get("ProgressOverall");
        PlayButton.Content = Strings.Get("BtnPlay");

        // Buttons that change content based on state — pick the right label
        if (!_modIsInstalled)
            UpdateButton.Content = Strings.Get("BtnInstall");
        else if (_pendingDownloads.Count > 0)
            UpdateButton.Content = Strings.Get("BtnUpdate");
        else
            UpdateButton.Content = Strings.Get("BtnVerify");

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

        var chosen = dialog.FolderName;

        if (!RegistryService.IsValidInstall(chosen))
        {
            MessageBox.Show(this,
                Strings.Get("DlgInvalidFolderBody"),
                Strings.Get("DlgInvalidFolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ModInstallPath = chosen.TrimEnd('\\', '/');
        _config.Save();
        await CheckAsync();
    }

    // ------------------------------------------------------------------------
    // Update flow
    // ------------------------------------------------------------------------

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_modIsInstalled)
            await InstallAsync();
        else if (_pendingDownloads.Count > 0)
            await ApplyUpdateWithElevationCheckAsync();
        else
            await CheckAsync();
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
        var result = await LauncherUpdateService.CheckAsync();
        if (!result.UpdateAvailable) return;

        var dialog = new LauncherUpdateDialog(result) { Owner = this };
        dialog.ShowDialog();
        // If the user accepted the update, the dialog itself starts the new
        // binary and shuts the app down — nothing else to do here.
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
                // so first-run users have a clear path forward.
                SetStatus(Strings.Get("StatusNotInstalled"));
                UpdateButton.Content = Strings.Get("BtnInstall");
                UpdateButton.Background = (System.Windows.Media.Brush)
                    new System.Windows.Media.BrushConverter().ConvertFromString("#c8102e")!;
                _pendingDownloads = new();
                return;
            }

            // Installed — restore the normal (gray) update button background
            UpdateButton.Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#3a3d44")!;

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
                UpdateButton.Content = Strings.Get("BtnVerify");
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

    /// <summary>
    /// Full first-time install flow (native — no Inno Setup). Walks the user through:
    ///   1. Detecting Age of Empires III: TAD installations (Steam/GOG/retail)
    ///   2. Choosing the destination folder (defaults to a "Wars of Liberty"
    ///      subfolder next to the detected AoE3 install)
    ///   3. Downloading the WoL payload ZIP (raw mod files)
    ///   4. Cloning AoE3 to the destination
    ///   5. Copying WoL files on top
    ///   6. Creating shortcuts + registry entries
    ///
    /// The result is a self-contained "Wars of Liberty" folder that has both
    /// AoE3:TAD and the WoL mod inside, independent of the original Steam/GOG
    /// install.
    /// </summary>
    private async Task InstallAsync()
    {
        if (_isBusy) return;

        // No URLs configured — fall back to opening the website
        var payloadUrls = _config.PayloadZipUrls;
        if (payloadUrls == null || payloadUrls.Length == 0)
        {
            // Fallback to legacy single URL if available
            if (!string.IsNullOrWhiteSpace(_config.InstallerZipUrl))
                payloadUrls = new[] { _config.InstallerZipUrl };
            else
            {
                var fallback = MessageBox.Show(this,
                    Strings.Get("DlgInstallNoUrlBody"),
                    Strings.Get("DlgInstallTitle"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (fallback == MessageBoxResult.OK)
                    InstallerService.OpenWebsite(_config.OfficialWebsite);
                return;
            }
        }

        // ---- Step 1: Detect AoE3 and pick install folder ----
        var aoe3Installs = AoE3Detector.FindAll();
        string? aoe3SourcePath = null;
        string? aoe3SourceLabel = null;
        string suggestedFolder;

        if (aoe3Installs.Count > 0)
        {
            aoe3SourcePath = aoe3Installs[0].GameFolder;
            aoe3SourceLabel = aoe3Installs[0].Source;
            suggestedFolder = aoe3Installs[0].ModRoot;
            DiagnosticLog.Write(
                $"AoE3 detected at: {aoe3SourcePath} (source: {aoe3SourceLabel})");
        }
        else
        {
            suggestedFolder = _config.DefaultInstallFolder;
            DiagnosticLog.Write("No AoE3 installation found; using configured default.");
        }

        // Ensure "Wars of Liberty" subfolder
        if (!suggestedFolder.EndsWith("Wars of Liberty", StringComparison.OrdinalIgnoreCase))
            suggestedFolder = Path.Combine(suggestedFolder, "Wars of Liberty");

        var folderDialog = new InstallFolderDialog(suggestedFolder, aoe3SourcePath, aoe3SourceLabel)
        {
            Owner = this
        };
        if (folderDialog.ShowDialog() != true) return;
        var installFolder = folderDialog.SelectedFolder;

        // If no AoE3 detected automatically, the user must have picked a folder
        // that IS an AoE3 install or the clone step won't have a source.
        // In that case, try to find AoE3 from the chosen path's parents.
        if (aoe3SourcePath == null)
        {
            // Try the parent of the chosen folder as AoE3 source
            var parentDir = Path.GetDirectoryName(installFolder);
            if (!string.IsNullOrEmpty(parentDir) && AoE3Detector.LooksLikeAoE3(parentDir))
            {
                aoe3SourcePath = parentDir;
            }
            else
            {
                // Last resort: ask user
                var noAoe3 = MessageBox.Show(this,
                    Strings.Get("DlgNoAoe3DetectedBody"),
                    Strings.Get("DlgNoAoe3DetectedTitle"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (noAoe3 != MessageBoxResult.OK) return;
                // Will proceed without clone (mod-only install)
            }
        }

        // Confirm install
        var confirm = MessageBox.Show(this,
            Strings.Format("DlgInstallNativeConfirmBody", installFolder,
                aoe3SourcePath ?? "(none - mod only)"),
            Strings.Get("DlgInstallConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        // ---- Begin installation ----
        SetBusy(true);
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        UpdateHeaderText.Text = Strings.Get("DlgInstallTitle");
        UpdateHeaderText.Visibility = Visibility.Visible;

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
            // Download progress
            var speed = new SpeedTracker();
            var dlProgress = new Progress<DownloadProgress>(p =>
            {
                speed.Sample(p.BytesReceived);
                bool knowTotal = p.TotalBytes > 0;

                if (knowTotal)
                {
                    var eta = speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
                    PatchProgress.Value = p.Percentage;
                    OverallProgress.Value = p.Percentage * 0.4; // download = 40% of total
                    PatchBytesText.Text = $"{p.Percentage:0.0}%";
                    OverallBytesText.Text =
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
                    OverallBytesText.Text = FormatBytes(p.BytesReceived);
                    EtaText.Text = "";
                }

                SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format("ProgressSpeed", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                LblCurrentPatch.Text = Strings.Get("StatusDownloadingInstaller");
            });

            // Clone progress
            var cloneProgress = new Progress<CloneProgress>(p =>
            {
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesCopied / p.BytesTotal * 100.0
                    : 0;
                PatchProgress.Value = pct;
                OverallProgress.Value = 40 + pct * 0.4; // clone = 40%-80% of total
                PatchBytesText.Text = $"{pct:0.0}%";
                OverallBytesText.Text =
                    $"{FormatBytes(p.BytesCopied)} / {FormatBytes(p.BytesTotal)}";
                LblCurrentPatch.Text = p.CurrentFile.Length > 80
                    ? "..." + p.CurrentFile[^80..]
                    : p.CurrentFile;
                SpeedText.Text = p.BytesPerSecond > 0
                    ? Strings.Format("ProgressSpeed", FormatBytes((long)p.BytesPerSecond))
                    : "";
            });

            // Status updates
            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                LblCurrentPatch.Text = s;
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
                    _cts.Token);
            }

            PatchProgress.Value = 100;
            OverallProgress.Value = 100;
            SetStatus(Strings.Get("StatusInstallSuccess"));

            // Point the launcher at the new install
            _config.ModInstallPath = installFolder;
            _config.Save();
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(this, ex.Message,
                Strings.Get("DlgUpdateErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cloneService = null;
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
            UpdateHeaderText.Visibility = Visibility.Collapsed;
        }

        // Re-check to detect the freshly installed mod
        await CheckAsync();
    }

    private async Task ApplyAsync()
    {
        if (_isBusy) return;
        SetBusy(true);
        _isUpdateInProgress = true;
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        // Show the "Updating X → Y" header for the duration of the update.
        var fromVersion = _updateService.CurrentVersion?.Ver ?? "?";
        var toVersion = _updateService.LatestVersion?.Ver ?? "?";
        UpdateHeaderText.Text = Strings.Format("ProgressUpdating", fromVersion, toVersion);
        UpdateHeaderText.Visibility = Visibility.Visible;

        bool succeeded = false;
        try
        {
            var statusReporter = new Progress<string>(SetStatus);
            var progressReporter = new Progress<UpdateProgress>(OnProgress);
            await _updateService.ApplyUpdatesAsync(
                _pendingDownloads, progressReporter, statusReporter, _cts.Token);
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
        // Per-patch progress
        if (p.PatchBytesTotal > 0)
        {
            PatchProgress.Value = (double)p.PatchBytesDone / p.PatchBytesTotal * 100.0;
            PatchBytesText.Text = $"{FormatBytes(p.PatchBytesDone)} / {FormatBytes(p.PatchBytesTotal)}";
        }
        else
        {
            PatchProgress.Value = 0;
            PatchBytesText.Text = "";
        }

        // Overall progress
        if (p.OverallBytesTotal > 0)
        {
            OverallProgress.Value = (double)p.OverallBytesDone / p.OverallBytesTotal * 100.0;
            OverallBytesText.Text = $"{FormatBytes(p.OverallBytesDone)} / {FormatBytes(p.OverallBytesTotal)}";
        }

        // Patch transition header — "Patch 5 of 26: 1.0.15d → 1.0.15e"
        LblCurrentPatch.Text = Strings.Format("ProgressPatchOf",
            p.CurrentStep, p.TotalSteps, p.PatchFromVersion, p.PatchToVersion);

        // Speed and ETA
        SpeedText.Text = p.BytesPerSecond > 0
            ? Strings.Format("ProgressSpeed", FormatBytes((long)p.BytesPerSecond))
            : "";
        EtaText.Text = p.Eta.HasValue
            ? Strings.Format("ProgressEta", FormatDuration(p.Eta.Value))
            : (p.BytesPerSecond > 0 ? Strings.Format("ProgressEta", Strings.Get("ProgressEtaCalculating")) : "");
    }

    // ------------------------------------------------------------------------
    // Game launch
    // ------------------------------------------------------------------------

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GameLauncher.Launch(_config, _updateService.InstallPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Strings.Get("DlgGameLaunchErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        // Play is only available when the mod is installed AND we're not busy.
        PlayButton.IsEnabled = !busy && _modIsInstalled;
        BrowseButton.IsEnabled = !busy;
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
        UpdateHeaderText.Visibility = Visibility.Collapsed;
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
}
