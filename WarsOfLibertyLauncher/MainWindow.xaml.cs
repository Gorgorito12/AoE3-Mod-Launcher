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
        NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");
        LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        LblOverall.Text = Strings.Get("ProgressOverall");
        PlayButton.Content = _isGameRunning
            ? Strings.Get("BtnPlaying")
            : Strings.Get("BtnPlay");
        VerifyButton.Content = Strings.Get("BtnVerify");
        StopButton.Content = Strings.Get("BtnStop");
        UninstallMenuItem.Header = Strings.Get("MenuUninstall");
        MenuSelectModFolder.Header = Strings.Get("MenuSelectModFolder");
        MenuSelectAoE3Folder.Header = Strings.Get("MenuSelectAoE3Folder");
        LblGamePath.Text = Strings.Get("LblGamePath");
        MoreButton.ToolTip = Strings.Get("TooltipSettings");

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

            // Mod-only install on top of existing (overwrites damaged files)
            await nativeInstaller.InstallModOnlyAsync(
                payloadUrls,
                installPath,
                dlProgress,
                statusProgress,
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
            UpdateHeaderText.Visibility = Visibility.Collapsed;
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
                _pendingDownloads = new();
                UpdateAoE3PathUI();
                return;
            }

            // Installed — restore the normal (gray) update button
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

    // ------------------------------------------------------------------------
    // More menu / Uninstall
    // ------------------------------------------------------------------------

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu == null) return;
        // Only let the user open the menu when nothing is in flight
        MenuSelectModFolder.IsEnabled = !_isBusy;
        MenuSelectAoE3Folder.IsEnabled = !_isBusy;
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

        if (plan.Mode == UninstallMode.RefusedMergedWithAoe3 ||
            plan.Mode == UninstallMode.NothingToDo)
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
