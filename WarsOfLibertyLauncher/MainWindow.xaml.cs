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
    // Not readonly — mod switching swaps this for a new instance bound to
    // the chosen profile so we don't have to restart the whole process.
    private UpdateService _updateService;
    private readonly InstallerService _installerService;
    private List<DownloadInfo> _pendingDownloads = new();
    private CancellationTokenSource? _cts;
    private FolderCloneService? _cloneService;
    private bool _isBusy;
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

    /// <summary>
    /// Last fetched translations index. Cached for the lifetime of the
    /// launcher session so we don't re-fetch every time the user opens
    /// the gear menu. Invalidated by clicking "Actualizar lista".
    /// </summary>
    private TranslationIndex? _cachedTranslationIndex;

    public MainWindow()
    {
        InitializeComponent();
        DiagnosticLog.Reset();
        DiagnosticLog.Write("MainWindow initialized.");

        _config = LauncherConfig.Load();
        Strings.SetLanguage(_config.Language);
        Strings.LanguageChanged += ApplyLanguage;

        var activeProfile = _config.GetActiveProfile();
        DiagnosticLog.Write(
            $"Active mod profile: '{activeProfile.Id}' ({activeProfile.DisplayName}).");
        DiagnosticLog.Write($"Config loaded. updateInfoUrl={_config.UpdateInfoUrl}");
        DiagnosticLog.Write($"  modInstallPath={_config.GetActiveState().InstallPath}");
        DiagnosticLog.Write($"  gameExecutable={_config.GameExecutable}");
        DiagnosticLog.Write($"  language={_config.Language}");

        _updateService = new UpdateService(_config, activeProfile);
        _installerService = new InstallerService();

        ApplyLanguage();
        RefreshModCards();
        ResetProgressUI();

        // Check for --update-now flag from elevated relaunch
        var args = Environment.GetCommandLineArgs();
        bool autoUpdate = args.Any(a => string.Equals(a, "--update-now", StringComparison.OrdinalIgnoreCase));
        if (autoUpdate)
            DiagnosticLog.Write("Started with --update-now: will auto-apply updates after check.");

        // Auto-check for updates on startup. Run all three checks IN PARALLEL
        // — the launcher self-update (GitHub), the mod patch check (aoe3wol.com)
        // and the translations index (GitHub) hit different servers, so doing
        // them concurrently roughly cuts the busy state to the slowest one.
        // Pre-fetching the translations index here means the gear menu opens
        // with the language list already populated — no "Refresh" needed for
        // the common case.
        Loaded += async (_, _) =>
        {
            LauncherUpdateService.CleanupOldVersion();
            await Task.WhenAll(
                CheckForLauncherUpdateAsync(),
                CheckAsync(),
                RefreshTranslationIndexAsync());
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
    // Mod selector — horizontal cards in the top "Mods" bar. Each card is a
    // pill-shaped Border with a small icon on the left + the mod's name on
    // the right; the active card uses the profile's accent for both border
    // and background, the inactive cards stay muted. Click switches the
    // active mod in place (no process restart).
    // ------------------------------------------------------------------------

    private const double ModCardIconSize = 30;
    private const double ModCardHeight = 56;
    private const double ModCardMinWidth = 200;

    /// <summary>
    /// (Re)builds the row of mod cards in the top bar. Called on first load
    /// and again after every mod switch so the highlighted card tracks the
    /// active profile.
    /// </summary>
    private void RefreshModCards()
    {
        ModCardsPanel.Children.Clear();
        var activeId = _updateService.Profile.Id;
        foreach (var profile in ModRegistry.All)
        {
            ModCardsPanel.Children.Add(BuildModCard(profile, activeId));
        }
    }

    private FrameworkElement BuildModCard(ModProfile profile, string activeId)
    {
        bool isActive = string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase);
        var accent = SafeBrush(profile.AccentColor, "#3a3d44");

        // Icon: prefer the profile's banner image (round, image-filled);
        // fall back to an accent-colored disc with the display name's first
        // letter in white.
        var iconBrush = TryLoadTileImage(profile.BannerImage);
        UIElement iconChild;
        System.Windows.Media.Brush iconBg;
        if (iconBrush != null)
        {
            iconChild = new System.Windows.Controls.Border();
            iconBg = iconBrush;
        }
        else
        {
            iconChild = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrEmpty(profile.DisplayName)
                    ? "?"
                    : profile.DisplayName[..1].ToUpperInvariant(),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            iconBg = accent;
        }
        var icon = new System.Windows.Controls.Border
        {
            Width = ModCardIconSize,
            Height = ModCardIconSize,
            CornerRadius = new CornerRadius(ModCardIconSize / 2),
            Background = iconBg,
            Child = iconChild,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        // Card body: title + secondary state line.
        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = profile.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White,
        };
        var stateText = new System.Windows.Controls.TextBlock
        {
            Text = ProbeInstalledState(profile),
            FontSize = 10,
            Foreground = SafeBrush(isActive ? profile.AccentColor : "#888", "#888"),
            Margin = new Thickness(0, 1, 0, 0),
        };
        var labels = new System.Windows.Controls.StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        labels.Children.Add(titleText);
        labels.Children.Add(stateText);

        var inner = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        inner.Children.Add(icon);
        inner.Children.Add(labels);

        // Card frame. Active = filled subtly with accent + accent border;
        // inactive = neutral dark with a transparent border that lights up
        // on hover so the card looks tappable.
        var inactiveBg = Brush("#22252c");
        var hoverBg = Brush("#2d3038");
        var activeBg = SafeBrush(BlendWithBase(profile.AccentColor, 0.18), "#3a1f24");

        var card = new System.Windows.Controls.Border
        {
            MinWidth = ModCardMinWidth,
            Height = ModCardHeight,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = isActive ? accent : Brush("#3a3d44"),
            Background = isActive ? activeBg : inactiveBg,
            Padding = new Thickness(14, 0, 16, 0),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = inner,
            ToolTip = $"{profile.DisplayName} — {ProbeInstalledState(profile)}",
        };

        if (!isActive)
        {
            card.MouseEnter += (_, _) => card.Background = hoverBg;
            card.MouseLeave += (_, _) => card.Background = inactiveBg;
            card.MouseLeftButtonUp += (_, _) => LoadModProfile(profile);
        }

        return card;
    }

    /// <summary>
    /// Mixes <paramref name="hexColor"/> with the dark theme background to
    /// produce a soft tinted shade — used for the active mod card's
    /// background. <paramref name="amount"/> in [0..1]: 0 = pure base color,
    /// 1 = pure accent. ~0.18 keeps the accent recognisable without
    /// overpowering the dark theme.
    /// </summary>
    private static string BlendWithBase(string hexColor, double amount)
    {
        try
        {
            var c = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            byte r = (byte)(0x22 + (c.R - 0x22) * amount);
            byte g = (byte)(0x25 + (c.G - 0x25) * amount);
            byte b = (byte)(0x2c + (c.B - 0x2c) * amount);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return "#22252c"; }
    }

    /// <summary>
    /// Cheap "is this mod sitting where we'd expect" probe used only for
    /// the tile tooltip. The full detection (registry, AoE3 walk, etc.)
    /// runs in <see cref="UpdateService.CheckAsync"/> after the user
    /// actually switches profiles.
    /// </summary>
    private string ProbeInstalledState(ModProfile profile)
    {
        try
        {
            // Reuse the active profile's already-detected install path when
            // we're describing the active profile — saves a redundant probe.
            if (string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(_updateService.InstallPath))
            {
                if (_updateService.CurrentVersion != null)
                    return Strings.Format("ModSelectorInstalled", _updateService.CurrentVersion.Ver);
                return Strings.Get("ModSelectorInstalledNoVersion");
            }

            // Saved per-mod state from a previous session.
            var saved = _config.GetState(profile.Id).InstallPath;
            if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved))
                return Strings.Get("ModSelectorInstalledNoVersion");

            // One-shot probe at the obvious locations.
            var probe = ResolveProbedInstallPath(profile);
            if (!string.IsNullOrEmpty(probe))
                return Strings.Get("ModSelectorInstalledNoVersion");
        }
        catch { /* probes must never throw */ }
        return Strings.Get("ModSelectorNotInstalled");
    }

    /// <summary>
    /// Best-effort path resolution: for in-place mods we look inside every
    /// detected AoE3 install for the profile's <c>InstallProbeFile</c>; for
    /// isolated-folder mods we fall back to the profile's
    /// <c>DefaultInstallFolder</c>. Returns null if the probe fails.
    /// </summary>
    private static string? ResolveProbedInstallPath(ModProfile profile)
    {
        if (string.IsNullOrEmpty(profile.InstallProbeFile)) return null;

        if (profile.InstallType == ModInstallType.InPlaceOverlay)
        {
            foreach (var aoe3 in AoE3Detector.FindAll())
            {
                foreach (var root in new[] { aoe3.GameFolder, aoe3.ModRoot })
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var probe = Path.Combine(root, profile.InstallProbeFile);
                    if (File.Exists(probe)) return root;
                }
            }
            return null;
        }

        if (!string.IsNullOrEmpty(profile.DefaultInstallFolder))
        {
            var probe = Path.Combine(profile.DefaultInstallFolder, profile.InstallProbeFile);
            if (File.Exists(probe)) return profile.DefaultInstallFolder;
        }
        return null;
    }

    /// <summary>
    /// Switches the launcher to the given mod profile in place, without
    /// restarting the process. Pre-flights against busy state (download in
    /// progress, game running) and surfaces a friendly message instead of
    /// silently dropping the request.
    /// </summary>
    private async void LoadModProfile(ModProfile target)
    {
        if (string.Equals(_updateService.Profile.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            return;

        // Pre-flight: don't switch mid-operation.
        if (_isBusy)
        {
            MessageBox.Show(this,
                Strings.Get("DlgModSwitchBusyBody"),
                Strings.Get("DlgModSwitchBlockedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_isGameRunning)
        {
            MessageBox.Show(this,
                Strings.Get("DlgModSwitchGameRunningBody"),
                Strings.Get("DlgModSwitchBlockedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DiagnosticLog.Write(
            $"Switching active mod profile in place: '{_updateService.Profile.Id}' -> '{target.Id}'");

        // Persist the choice first so a crash mid-switch comes back up
        // pointing at the correct profile.
        _config.ActiveModId = target.Id;
        _config.Save();

        // Fresh service bound to the new profile. Per-mod state in
        // _config.Mods[target.Id] keeps the install path / translation
        // separate from any previously active mod.
        _updateService = new UpdateService(_config, target);

        // Reset session caches that were tied to the old mod.
        _pendingDownloads = new();
        _modIsInstalled = false;
        _warnedAboutBrokenInstall = false;
        _cachedTranslationIndex = null;

        // Repaint static UI under the new profile (title, subtitle, accent).
        ApplyLanguage();
        RefreshModCards();
        ResetProgressUI();

        // Re-detect install path + version + pending updates for the new
        // profile. CheckAsync already short-circuits for non-WolPatcher
        // profiles, so this is fast for IM and full-fat for WoL.
        await CheckAsync();

        // The gear menu's translation list is per-mod — repopulate from
        // the new profile's config before the user opens it.
        try { PopulateGameLanguageMenu(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"PopulateGameLanguageMenu after switch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Color parser that doesn't throw on a malformed hex string in a
    /// profile — falls back to <paramref name="fallback"/> instead. Used
    /// in the tile builder so a typo in a profile's accent color can
    /// never crash the launcher at startup.
    /// </summary>
    private static System.Windows.Media.Brush SafeBrush(string color, string fallback)
    {
        try { return Brush(color); }
        catch { return Brush(fallback); }
    }

    /// <summary>
    /// Loads a profile's <see cref="ModProfile.BannerImage"/> URI as an
    /// <c>ImageBrush</c> ready to be assigned to a tile's background.
    /// Accepts both <c>pack://application:,,,/file</c> URIs (embedded
    /// resources, like the WoL icon) and <c>file:///</c> URIs (per-user
    /// artwork on disk). Returns null when the URI is empty, unreadable,
    /// or in a format we don't support — the caller renders the placeholder
    /// initial in that case.
    /// </summary>
    private static System.Windows.Media.ImageBrush? TryLoadTileImage(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        try
        {
            var sourceUri = new Uri(uri, UriKind.RelativeOrAbsolute);

            // .ico files contain multiple frames at different sizes. WPF's
            // generic BitmapImage decoder often picks the smallest frame
            // (e.g. 16×16) which looks awful at 44 px. Use IconBitmapDecoder
            // explicitly so we can pick the largest frame ourselves.
            bool isIco = uri.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
            System.Windows.Media.Imaging.BitmapSource source;

            if (isIco)
            {
                using var stream = sourceUri.IsAbsoluteUri && sourceUri.Scheme == "pack"
                    ? System.Windows.Application.GetResourceStream(sourceUri)?.Stream
                    : System.IO.File.OpenRead(sourceUri.LocalPath);
                if (stream == null)
                {
                    DiagnosticLog.Write($"Mod tile image: pack stream null for '{uri}'.");
                    return null;
                }
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                source = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .First();
            }
            else
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = sourceUri;
                bmp.EndInit();
                source = bmp;
            }

            if (source.CanFreeze) source.Freeze();
            DiagnosticLog.Write(
                $"Mod tile image loaded: '{uri}' ({source.PixelWidth}×{source.PixelHeight}).");
            return new System.Windows.Media.ImageBrush(source)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Mod tile image load failed for '{uri}': {ex.Message}");
            return null;
        }
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
        // The window title and the big header are both driven by the active
        // mod profile, not a fixed "WARS OF LIBERTY" literal — so swapping
        // the active mod re-renders both with no other changes.
        var profile = _updateService.Profile;
        Title = Strings.Format("WindowTitle", profile.DisplayName);
        TitleText.Text = profile.DisplayName.ToUpperInvariant();

        // Subtitle: prefer the profile's own (e.g. "AoE3:TAD overhaul") and
        // fall back to the localized "Launcher" when the profile didn't set
        // one. Append "(running as administrator)" when elevated — useful
        // as a visual confirmation after UAC.
        var subtitle = string.IsNullOrWhiteSpace(profile.Subtitle)
            ? Strings.Get("Subtitle")
            : profile.Subtitle;
        if (ElevationService.IsRunningAsAdmin())
            subtitle += "  " + Strings.Get("StatusRunningAsAdmin");
        SubtitleText.Text = subtitle;

        // Tint the PLAY button with the active profile's accent color so
        // switching mods gives instant visual feedback ("am I in WoL or
        // IM right now?"). The XAML style sets a default background; this
        // overrides the base layer only — the hover/disabled states stay
        // driven by the template triggers for now.
        try
        {
            PlayButton.Background = Brush(profile.AccentColor);
        }
        catch
        {
            // Bad color string in a profile shouldn't crash the launcher —
            // just keep the XAML default.
        }
        // Sidebar text (status box, actions, game footer, tabs)
        ModsBarLabel.Text = Strings.Get("ModsBarLabel");
        ActionsLabel.Text = Strings.Get("ActionsLabel");
        // The "INSTALLED VERSION / LATEST AVAILABLE" labels lived in the
        // top-of-sidebar status box that was removed; the ProgressPanel
        // at the bottom now covers the same info via RefreshIdlePanel.
        NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");
        LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        LblOverall.Text = Strings.Get("ProgressOverall");
        // Sidebar buttons. Each one's Content is a Grid/StackPanel with an
        // icon + a named TextBlock — we update only the TextBlock so the
        // icon survives a language change. Verify/Repair/Uninstall live
        // in the gear menu now and aren't rebound here.
        StopButton.Content = Strings.Get("BtnStop");
        UpdateButtonText.Text = Strings.Get("BtnUpdate");
        MoreButtonText.Text = Strings.Get("BtnConfig");
        OpenFolderButtonText.Text = Strings.Get("BtnOpenFolder");
        // Re-paint the primary button under the new locale: SetPrimaryAction
        // pulls each label from Strings, so calling it with the current
        // action key picks up the translated text.
        SetPrimaryAction(_primaryAction);
        // Tab labels (right-pane tabs)
        TabNoticias.Content = Strings.Get("TabNoticias");
        TabChangelog.Content = Strings.Get("TabChangelog");
        TabAyuda.Content = Strings.Get("TabAyuda");
        RefreshTabsHighlight();
        // Game footer + banner background tied to the active profile
        RefreshActiveModBanner();
        RefreshIdlePanel();
        // Headers (the visible label of each item)
        UninstallMenuItem.Header = Strings.Get("MenuUninstall");
        MenuFolders.Header = Strings.Get("MenuManagePaths");
        MenuOpenAoE3Folder.Header = Strings.Get("MenuOpenAoE3Folder");
        MenuSelectModFolder.Header = Strings.Get("MenuSelectModFolder");
        MenuSelectAoE3Folder.Header = Strings.Get("MenuSelectAoE3Folder");
        MenuUserData.Header = Strings.Get("MenuUserData");
        MenuOpenUserDataFolder.Header = Strings.Get("MenuOpenUserDataFolder");
        MenuCreateBackupNow.Header = Strings.Get("MenuCreateBackupNow");
        MenuRestoreUserData.Header = Strings.Get("MenuRestoreUserData");
        MenuCheckForUpdates.Header = Strings.Get("MenuCheckForUpdates");
        MenuGameLanguage.Header = Strings.Get("MenuGameLanguage");
        MenuRepairInstall.Header = Strings.Get("MenuRepairInstall");
        MenuVerifyFiles.Header = Strings.Get("MenuVerifyFiles");
        MenuViewLogs.Header = Strings.Get("MenuViewLogs");

        // Section headers — small-caps gray labels grouping items in the
        // Settings menu. Not clickable; just visual organization.
        MenuSectionPaths.Header = Strings.Get("MenuSectionPaths");
        MenuSectionUserData.Header = Strings.Get("MenuSectionUserData");
        MenuSectionLanguage.Header = Strings.Get("MenuSectionLanguage");
        MenuSectionMaintenance.Header = Strings.Get("MenuSectionMaintenance");
        MenuSectionAdvanced.Header = Strings.Get("MenuSectionAdvanced");
        MenuSectionDanger.Header = Strings.Get("MenuSectionDanger");

        // Tooltips on LEAF items only — items with submenus (Carpetas,
        // Datos de usuario) are self-explanatory once the submenu opens,
        // and showing a tooltip on top of the submenu just causes visual
        // conflict. Same pattern as VS Code, Notion, native OS menus.
        MoreButton.ToolTip = BuildMenuTooltip(
            Strings.Get("TooltipSettings"), Strings.Get("TooltipSettingsBody"));
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
        MenuRepairInstall.ToolTip = BuildMenuTooltip(
            (string)MenuRepairInstall.Header, Strings.Get("TooltipMenuRepairInstall"));
        MenuVerifyFiles.ToolTip = BuildMenuTooltip(
            (string)MenuVerifyFiles.Header, Strings.Get("TooltipMenuVerifyFiles"));
        MenuViewLogs.ToolTip = BuildMenuTooltip(
            (string)MenuViewLogs.Header, Strings.Get("TooltipMenuViewLogs"));
        UninstallMenuItem.ToolTip = BuildMenuTooltip(
            (string)UninstallMenuItem.Header, Strings.Get("TooltipMenuUninstall"));

        // The UpdateButton's label is fixed at "Update" — its visibility (not
        // its label) is what tracks state: it only shows when the active mod
        // is detected with a known version AND has pending patches. The old
        // multi-purpose flow that switched the label between Install / Update
        // / Check is gone; the unified primary button (PlayButton) covers
        // Install, and Check lives in the gear menu.

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

        RefreshIdlePanel();
        SetStatus(Strings.Get("StatusAoE3Configured"));
    }

    /// <summary>
    /// Drives the StatusCard at the top of the sidebar (the three labelled
    /// rows: state badge, installed version, latest version, plus the
    /// optional "AoE3 missing" row). The progress panel below has its own
    /// idle look (<see cref="RefreshIdleProgressPanel"/>) — this one is
    /// purely about the mod's status.
    /// </summary>
    private void RefreshStatusCard()
    {
        var profile = _updateService.Profile;
        var exePath = GameLauncher.Find(_config, _updateService.InstallPath, profile);
        bool aoe3Detected = exePath != null;

        // Default labels for the two version rows. The actual numbers come
        // from CurrentVersion / LatestVersion below.
        StatusInstalledLabel.Text = Strings.Get("StatusCardCurrentVersion");
        StatusLatestLabel.Text = Strings.Get("StatusCardLatestVersion");

        // ---- Pick state badge text + color ----
        // Priority: AoE3 missing > Not installed > Update interrupted >
        // Update available > Ready.
        string stateKey;
        string stateColor;
        if (!aoe3Detected)
        {
            stateKey = "IdleStateGameMissing";
            stateColor = "#e63950";
        }
        else if (!_modIsInstalled)
        {
            stateKey = "StatusCardNotInstalled";
            stateColor = "#e63950";
        }
        else if (_modIsInstalled
            && _updateService.CurrentVersion == null
            && _updateService.Profile.UpdateMechanism == ModUpdateMechanism.WolPatcher)
        {
            stateKey = "IdleStateUnknownVersion";
            stateColor = "#e63950";
        }
        else if (_pendingDownloads.Count > 0
            && _updateService.CurrentVersion?.Ver != null
            && _updateService.LatestVersion?.Ver != null)
        {
            stateKey = "IdleStateUpdateAvailable";
            stateColor = "#d4a04a";
        }
        else
        {
            stateKey = "StatusCardInstalled";
            stateColor = "#9bd99b";
        }
        var stateBrush = SafeBrush(stateColor, "#9bd99b");
        StatusValueText.Text = Strings.Get(stateKey);
        StatusValueText.Foreground = stateBrush;
        StatusRowIcon.Fill = stateBrush;

        // ---- Version numbers ----
        CurrentVersionText.Text = _updateService.CurrentVersion?.Ver ?? "—";
        LatestVersionText.Text = _updateService.LatestVersion?.Ver ?? "—";

        // ---- AoE3 missing row ----
        if (!aoe3Detected)
        {
            AoE3MissingRow.Visibility = Visibility.Visible;
            AoE3MissingText.Text = Strings.Get("IdleStateGameMissing");
            BrowseAoE3Button.Content = Strings.Get("BtnFindAoE3");
        }
        else
        {
            AoE3MissingRow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Paints the bottom progress panel in its idle look — a neutral
    /// "Listo para operaciones" message with a gray dot. The panel only
    /// transforms into the colored running state when an operation
    /// actually starts (StartProgressPanel).
    /// </summary>
    private void RefreshIdleProgressPanel()
    {
        // While an op is running we don't repaint the idle look — the
        // operation tone owns the panel until it auto-reverts.
        if (_progressState != ProgressState.Idle) return;

        ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressActionRetry.Visibility = Visibility.Collapsed;
        ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressRunningActions.Visibility = Visibility.Collapsed;

        // Neutral idle: gray panel chrome, gray dot icon, generic message.
        ProgressPanel.Background = SafeBrush("#22252c", "#22252c");
        ProgressPanel.BorderBrush = SafeBrush("#3a3d44", "#3a3d44");
        ProgressIcon.Text = "○";
        ProgressIcon.Foreground = SafeBrush("#888", "#888");
        // Idle layout: the dot stays put on the left as a decoration,
        // and the title block (title + subtitle) floats to the centre of
        // the whole panel for true visual symmetry. The Grid that holds
        // them is a single-cell overlay (see XAML), so centering the
        // title means "centred in the panel," not "centred in a column."
        // StartProgressPanel switches the title block back to Left with a
        // left-margin so the operation title sits next to the dot.
        ProgressTitleStack.HorizontalAlignment = HorizontalAlignment.Center;
        ProgressTitleStack.Margin = new Thickness(0);
        ProgressPanelLabel.Foreground = SafeBrush("#888", "#888");
        ProgressPanelLabel.Text = Strings.Get("ProgressIdleHeader");
        ProgressTitleText.Text = Strings.Get("ProgressIdleTitle");
        ProgressStepText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";
    }

    /// <summary>
    /// Convenience: refreshes both the status card and the idle progress
    /// panel together. Most callers want both updated whenever mod state
    /// changes (CheckAsync result, mod switch, install/uninstall finish).
    /// </summary>
    private void RefreshIdlePanel()
    {
        RefreshStatusCard();
        RefreshIdleProgressPanel();
    }

    /// <summary>
    /// Paints the active mod's banner area at the top of the left sidebar.
    /// Tries the profile's <c>BannerImage</c>; if that's missing, falls back
    /// to a tinted gradient using the profile's accent color so the area
    /// still feels mod-specific. The TitleText/SubtitleText overlay sits
    /// on top, set elsewhere from <see cref="ApplyLanguage"/>.
    /// </summary>
    private void RefreshActiveModBanner()
    {
        var profile = _updateService.Profile;
        // Subtle dark-to-accent gradient as the default. If the profile ships
        // a banner image, layer the image on top with UniformToFill stretch.
        var accent = SafeBrush(profile.AccentColor, "#3a3d44");
        var gradient = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
        };
        gradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            ((System.Windows.Media.SolidColorBrush)Brush("#22252c")).Color, 0));
        gradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            ((System.Windows.Media.SolidColorBrush)accent).Color, 1));

        var imgBrush = TryLoadTileImage(profile.BannerImage);
        if (imgBrush != null)
        {
            // Show the image plus a dark vignette gradient for legible text.
            ModBannerHost.Background = imgBrush;
        }
        else
        {
            ModBannerHost.Background = gradient;
        }
    }

    /// <summary>
    /// Paints the active tab's underline + brighter foreground; the others
    /// fade out. Driven from <see cref="ApplyLanguage"/> on language change
    /// and from the tab handlers on click.
    /// </summary>
    private void RefreshTabsHighlight()
    {
        var profile = _updateService.Profile;
        var accent = SafeBrush(profile.AccentColor, "#c8102e");
        var dim = Brush("#888");
        var transparent = System.Windows.Media.Brushes.Transparent;

        void Paint(System.Windows.Controls.Button btn, bool active)
        {
            btn.Foreground = active ? Brush("#fff") : dim;
            // The tab template's Border (named "border") owns the underline.
            // Walk the visual tree to grab it; do nothing if the template
            // hasn't applied yet (first frame race).
            if (btn.Template?.FindName("border", btn) is System.Windows.Controls.Border b)
                b.BorderBrush = active ? accent : transparent;
        }

        Paint(TabNoticias, _activeTab == ContentTab.Noticias);
        Paint(TabChangelog, _activeTab == ContentTab.Changelog);
        Paint(TabAyuda, _activeTab == ContentTab.Ayuda);
    }

    private enum ContentTab { Noticias, Changelog, Ayuda }
    private ContentTab _activeTab = ContentTab.Noticias;

    private void TabNoticias_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Noticias);
    private void TabChangelog_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Changelog);
    private void TabAyuda_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Ayuda);

    private void SwitchContentTab(ContentTab tab)
    {
        _activeTab = tab;
        NoticiasContent.Visibility = tab == ContentTab.Noticias ? Visibility.Visible : Visibility.Collapsed;
        ChangelogContent.Visibility = tab == ContentTab.Changelog ? Visibility.Visible : Visibility.Collapsed;
        AyudaContent.Visibility = tab == ContentTab.Ayuda ? Visibility.Visible : Visibility.Collapsed;

        // Lazy-fill changelog and help so an empty profile shows a friendly
        // message instead of nothing.
        if (tab == ContentTab.Changelog)
            ChangelogText.Text = Strings.Get("ChangelogPlaceholder");
        else if (tab == ContentTab.Ayuda)
            HelpText.Text = Strings.Get("HelpDefaultBody");

        RefreshTabsHighlight();
    }

    /// <summary>
    /// Click handler for the "Open folder" sidebar button — opens the active
    /// mod's install path (or the AoE3 root for in-place mods, since that's
    /// where the mod's files live).
    /// </summary>
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _updateService.InstallPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }
        OpenFolderInExplorer(path);
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

        _config.GetActiveState().InstallPath = resolved;
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

        // Verification is short and has no per-file progress reporter, so the
        // panel runs in indeterminate mode while VerifyInstallation does its
        // sweep. The end state turns into Completed (green) when clean or
        // Error (red) when missing/corrupt files were found, with the Retry
        // button wired to RepairInstallAsync — no MessageBox detour anymore.
        SetBusy(true);
        StartProgressPanel(
            ProgressOperation.Verify,
            title: Strings.Format("ProgressTitleVerifying", _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubVerifying"),
            bar1Label: "ProgressBarVerify",
            bar2Label: "ProgressBarProcess",
            retry: RepairInstallAsync);
        PatchProgress.IsIndeterminate = true;
        OverallProgress.IsIndeterminate = true;
        SetStatus(Strings.Get("StatusVerifying"));

        try
        {
            var result = await Task.Run(() => VerifyInstallation(_updateService.InstallPath));
            PatchProgress.IsIndeterminate = false;
            OverallProgress.IsIndeterminate = false;
            PatchProgress.Value = 100;
            OverallProgress.Value = 100;

            if (result.MissingItems.Count == 0 && result.CorruptItems.Count == 0)
            {
                SetStatus(Strings.Format("StatusVerifyOk", result.TotalFilesChecked));
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format("StatusVerifyOk", result.TotalFilesChecked));
                return;
            }

            // Build a report
            var problems = new List<string>();
            problems.AddRange(result.MissingItems.Select(m => $"[missing] {m}"));
            problems.AddRange(result.CorruptItems.Select(c => $"[empty] {c}"));
            int totalProblems = result.MissingItems.Count + result.CorruptItems.Count;

            SetStatus(Strings.Format("StatusVerifyMissing", totalProblems,
                string.Join(", ", problems.Take(10))));
            DiagnosticLog.Write($"Verification: {totalProblems} problems found:");
            foreach (var p in problems) DiagnosticLog.Write($"  {p}");

            // Surface as an Error in the panel — Retry button calls Repair,
            // which is exactly what the old MessageBox offered.
            ShowProgressError(Strings.Format("DlgVerifyRepairBody", totalProblems));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Verify failed: {ex}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Repairs the installation by re-downloading the WoL payload and
    /// re-copying the mod files over the existing install.
    /// </summary>
    private async Task RepairInstallAsync()
    {
        if (_isBusy) return;

        var payloadUrls = _updateService.EffectivePayloadZipUrls();
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

        StartProgressPanel(
            ProgressOperation.Repair,
            title: Strings.Format("ProgressTitleRepairing", _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubVerifying"),
            bar1Label: "ProgressBarVerify",
            bar2Label: "ProgressBarRepair",
            retry: RepairInstallAsync);
        LblCurrentPatch.Text = Strings.Get("ProgressBarDownload");
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchBytesText.Text = "";
        OverallBytesText.Text = "";

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
            {
                SetStatus(Strings.Get("StatusRepairSuccess"));
                // Repair: the user could already play before; no need to
                // surface PLAY on completion (sidebar already has PLAY).
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Get("StatusRepairSuccess"));
            }
            else
            {
                var msg = Strings.Format("StatusRepairPartial",
                    recheck.MissingItems.Count + recheck.CorruptItems.Count);
                SetStatus(msg);
                ShowProgressError(msg);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
            ShowProgressCancelled();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            DiagnosticLog.Write($"Repair failed: {ex}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
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

            _modIsInstalled = result.IsValidInstall;

            // Non-WoL-style mods (e.g. Improvement Mod) don't have an updater
            // pipeline we can reason about — version detection, manifest fetch
            // and the Update button are all WoL-specific. We render a
            // stripped-down "ready to play" status and short-circuit before
            // any of the WoL-specific branching. Verify/Repair are gated by
            // _modIsInstalled inside MoreButton_Click — we don't touch
            // their visibility here.
            if (_updateService.Profile.UpdateMechanism != ModUpdateMechanism.WolPatcher)
            {
                UpdateButton.Visibility = Visibility.Collapsed;
                RefreshIdlePanel();
                // External-update mods (IM): we can't install for them, so
                // when not installed the primary is grayed out — the status
                // text tells the user to install via the mod's own channel.
                SetPrimaryAction(
                    result.IsValidInstall ? PrimaryAction.Play : PrimaryAction.Install,
                    enabled: result.IsValidInstall);

                SetStatus(result.IsValidInstall
                    ? Strings.Format("StatusReadyExternalUpdates", _updateService.Profile.DisplayName)
                    : Strings.Format("StatusModNotInstalledExternal", _updateService.Profile.DisplayName));
                _pendingDownloads = new();
                ResetProgressUI();
                return;
            }

            // From here down: WoL-specific path.

            if (!result.IsValidInstall)
            {
                // No installation detected — primary becomes "Install".
                SetStatus(Strings.Get("StatusNotInstalled"));
                SetPrimaryAction(PrimaryAction.Install);
                _pendingDownloads = new();
                UpdateButton.Visibility = Visibility.Collapsed;
                RefreshIdlePanel();
                return;
            }

            RefreshIdlePanel();

            // Sanity check: WoL is installed, but is age3y.exe reachable?
            // If the user installed WoL outside of an AoE3 folder, the mod
            // files are on disk but the engine will never load them. Warn
            // them once per session so they can fix it.
            if (!_warnedAboutBrokenInstall
                && !Services.AoE3Detector.LooksLikeInsideAoE3(_updateService.InstallPath!)
                && GameLauncher.Find(_config, _updateService.InstallPath, _updateService.Profile) == null)
            {
                _warnedAboutBrokenInstall = true;
                MessageBox.Show(this,
                    Strings.Format("DlgBrokenInstallBody", _updateService.InstallPath),
                    Strings.Get("DlgBrokenInstallTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _pendingDownloads = result.PendingDownloads;

            // Two distinct cases for "the user has the WoL folder on disk":
            //   a) Version detected (CurrentVersion != null): the install is
            //      legit — primary is Play, and Update appears as a separate
            //      secondary button when there are patches to apply.
            //   b) Version is null ("?"): the data files don't match any known
            //      version. Bringing them up to current means downloading the
            //      full chain — that's a fresh install, not an update. Primary
            //      becomes Install and the Update button stays hidden.
            bool versionKnown = result.CurrentVersion != null;
            UpdateButton.Visibility = (versionKnown && _pendingDownloads.Count > 0)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!versionKnown)
            {
                long totalBytes = 0;
                foreach (var d in _pendingDownloads) totalBytes += d.Size;
                SetStatus(Strings.Format(
                    "StatusUpdatesAvailable",
                    _pendingDownloads.Count,
                    FormatBytes(totalBytes)));
                SetPrimaryAction(PrimaryAction.Install);
                ResetProgressUI();
            }
            else if (_pendingDownloads.Count == 0)
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
                SetPrimaryAction(PrimaryAction.Play);
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
                // Version is known and patches are pending → primary is Play
                // (let the user launch the old version if they want); the
                // separate Update button (already shown above) handles the
                // "apply patches" path.
                SetPrimaryAction(PrimaryAction.Play);
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
            // Pause + Cancel live inside the progress panel now. We toggle
            // the whole row; the individual button visibilities don't need
            // to be touched (they're always Visible inside the row).
            ProgressRunningActions.Visibility = Visibility.Visible;
        }
        else
        {
            ProgressRunningActions.Visibility = Visibility.Collapsed;
            _isPaused = false;
            _installerService.IsPaused = false;
            _updateService.IsPaused = false;
            // Note: we don't hide ProgressPanel itself. The caller transitions
            // to a final state (Completed / Error / Cancelled) and that
            // state owns the panel until the auto-revert timer fires.
        }
    }

    // ------------------------------------------------------------------------
    // Progress panel state machine — drives the inline progress card at the
    // top of the right panel during install/update/verify/repair. The card
    // appears when an operation starts, shows live bars/speed/step while it
    // runs, and stays visible with a Completed / Error / Cancelled message
    // + action buttons until the user dismisses it.
    // ------------------------------------------------------------------------

    private enum ProgressState { Idle, Running, Completed, Error, Cancelled }
    private ProgressState _progressState = ProgressState.Idle;

    /// <summary>
    /// Action to re-invoke when the user clicks "Retry" on an Error or
    /// Cancelled state. Set by the operation that started the panel; null
    /// means the retry button hides.
    /// </summary>
    private Func<Task>? _progressRetryAction;

    /// <summary>
    /// Auto-revert timer for the end states. After Completed we hold the
    /// banner briefly so the user notices, then flip back to Idle. For
    /// Error / Cancelled we wait longer so the Retry button stays in
    /// reach. Cancelled mid-operation by starting a new op (the start
    /// path stops the timer first).
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _autoRevertTimer;

    /// <summary>
    /// Brings the progress panel into "Running" mode. Hides the idle
    /// content (game-detected info) and shows the progress bars + headline.
    /// The panel itself never disappears — the bottom-left slot is always
    /// occupied by either Idle or Operation content.
    /// </summary>
    private void StartProgressPanel(
        ProgressOperation op,
        string title, string subtitle,
        string? bar1Label = null, string? bar2Label = null,
        Func<Task>? retry = null)
    {
        _progressState = ProgressState.Running;
        _progressRetryAction = retry;

        // Cancel any pending auto-revert from the previous op so the new
        // op's panel doesn't get yanked back to Idle mid-flight.
        StopAutoRevertTimer();

        SetProgressTone(op);

        // Operation layout: dot is on the left, title sits to its right
        // (the idle "centred title" mode is undone here). The 20px left
        // margin equals dot width + spacing so the title hugs the dot
        // instead of floating into it.
        ProgressTitleStack.HorizontalAlignment = HorizontalAlignment.Left;
        ProgressTitleStack.Margin = new Thickness(20, 0, 0, 0);

        ProgressPanelLabel.Text = LabelKeyForOperation(op);
        ProgressTitleText.Text = title;
        ProgressSubtitleText.Text = subtitle;
        ProgressStepText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";

        LblCurrentPatch.Text = Strings.Get(bar1Label ?? "ProgressBarDownload");
        LblOverall.Text = Strings.Get(bar2Label ?? "ProgressBarInstall");
        PatchBytesText.Text = "";
        OverallBytesText.Text = "";
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchProgress.IsIndeterminate = false;
        OverallProgress.IsIndeterminate = false;

        ProgressBarsGroup.Visibility = Visibility.Visible;
        ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressActionRetry.Visibility = Visibility.Collapsed;
        // Note: we don't touch ProgressRunningActions here. The operation
        // flow calls ShowDownloadControls(true) BEFORE StartProgressPanel,
        // so the row is already visible by the time we get here — undoing
        // it now would hide Pause/Cancel for the whole run. End states
        // (Completed/Error/Cancelled) hide the row in their own setters.
    }

    /// <summary>
    /// Drops the panel out of any final-state banner and re-paints it
    /// with the current mod's idle status (Ready / Update available /
    /// Not installed / AoE3 missing). Called by the auto-revert timer
    /// or directly by the retry handler before starting a fresh run.
    /// </summary>
    private void RevertToIdle()
    {
        StopAutoRevertTimer();
        _progressState = ProgressState.Idle;
        _progressRetryAction = null;
        // Repaint the panel for the current mod state.
        RefreshIdlePanel();
    }

    /// <summary>
    /// Schedules a one-shot revert-to-Idle. Used after an end state
    /// (Completed / Error / Cancelled) so the panel doesn't stay frozen
    /// on the result forever.
    /// </summary>
    private void ScheduleAutoRevert(int seconds)
    {
        StopAutoRevertTimer();
        _autoRevertTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(seconds),
        };
        _autoRevertTimer.Tick += (_, _) => RevertToIdle();
        _autoRevertTimer.Start();
    }

    private void StopAutoRevertTimer()
    {
        _autoRevertTimer?.Stop();
        _autoRevertTimer = null;
    }

    /// <summary>
    /// Title-row label resource key for each operation — used in the small
    /// "PROGRESO DE …" header at the top of the panel.
    /// </summary>
    private static string LabelKeyForOperation(ProgressOperation op) => op switch
    {
        ProgressOperation.Install   => Strings.Get("ProgressLabelInstall"),
        ProgressOperation.Update    => Strings.Get("ProgressLabelUpdate"),
        ProgressOperation.Repair    => Strings.Get("ProgressLabelRepair"),
        ProgressOperation.Verify    => Strings.Get("ProgressLabelVerify"),
        ProgressOperation.Uninstall => Strings.Get("ProgressLabelUninstall"),
        _                           => Strings.Get("ProgressPanelHeader"),
    };

    /// <summary>
    /// How long the panel holds each end state before auto-reverting to
    /// Idle (game-detected info). Completed is brief — long enough to
    /// notice the green check; Error / Cancelled are longer so the user
    /// has time to click Retry.
    /// </summary>
    private const int CompletedHoldSeconds = 4;
    private const int ErrorHoldSeconds = 10;

    /// <summary>
    /// Transitions the panel to "Completed" — green banner. No action
    /// buttons (Play / Open folder are already in the sidebar). The panel
    /// auto-reverts to Idle after a brief hold.
    /// </summary>
    private void ShowProgressCompleted(string headlineKey, string? detail = null)
    {
        _progressState = ProgressState.Completed;
        ProgressTitleText.Text = Strings.Get(headlineKey);
        ProgressSubtitleText.Text = detail ?? "";
        ProgressStepText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";

        ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#1f3a1f", "#3a8c3a", "#9bd99b");
        ProgressMessageText.Text = Strings.Get("ProgressCompletedMessage");
        ProgressMessagePanel.Visibility = Visibility.Visible;

        ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressActionRetry.Visibility = Visibility.Collapsed;

        ScheduleAutoRevert(CompletedHoldSeconds);
    }

    /// <summary>
    /// Transitions the panel to "Error" — red banner + a single Retry
    /// button (which re-runs the captured retry action). Auto-reverts
    /// to Idle after a longer hold so the user has time to act.
    /// </summary>
    private void ShowProgressError(string errorMessage)
    {
        _progressState = ProgressState.Error;
        ProgressTitleText.Text = Strings.Get("ProgressTitleError");
        ProgressSubtitleText.Text = "";
        ProgressStepText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";

        ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#3a1a1a", "#8c3a3a", "#e63950");
        ProgressMessageText.Text = errorMessage;
        ProgressMessagePanel.Visibility = Visibility.Visible;

        bool canRetry = _progressRetryAction != null;
        ProgressActionsRow.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressActionRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressActionRetry.Content = Strings.Get("BtnRetry");

        ScheduleAutoRevert(ErrorHoldSeconds);
    }

    /// <summary>
    /// Transitions the panel to "Cancelled" — yellow banner + Retry
    /// button. Same auto-revert behaviour as Error.
    /// </summary>
    private void ShowProgressCancelled()
    {
        _progressState = ProgressState.Cancelled;
        ProgressTitleText.Text = Strings.Get("ProgressTitleCancelled");
        ProgressSubtitleText.Text = "";
        ProgressStepText.Text = "";
        SpeedText.Text = "";
        EtaText.Text = "";

        ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#3a2a1a", "#8c6c3a", "#d4a04a");
        ProgressMessageText.Text = Strings.Get("ProgressCancelledMessage");
        ProgressMessagePanel.Visibility = Visibility.Visible;

        bool canRetry = _progressRetryAction != null;
        ProgressActionsRow.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressActionRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressActionRetry.Content = Strings.Get("BtnRetry");

        ScheduleAutoRevert(ErrorHoldSeconds);
    }

    private void PaintProgressMessage(string bg, string border, string fg)
    {
        ProgressMessagePanel.Background = SafeBrush(bg, "#1f3a1f");
        ProgressMessagePanel.BorderBrush = SafeBrush(border, "#3a8c3a");
        ProgressMessagePanel.BorderThickness = new Thickness(1);
        ProgressMessageText.Foreground = SafeBrush(fg, "#9bd99b");
    }

    // ------------------------------------------------------------------------
    // Progress-panel button handlers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Retry handler for Error / Cancelled states. Cancels the auto-revert
    /// timer, re-enters Running mode so progress shows fresh, and re-invokes
    /// the captured retry action. Errors during the retry surface as a
    /// new Error state (which restarts the timer).
    /// </summary>
    private async void ProgressActionRetry_Click(object sender, RoutedEventArgs e)
    {
        var retry = _progressRetryAction;
        if (retry == null) return;
        StopAutoRevertTimer();
        _progressState = ProgressState.Running;
        ProgressBarsGroup.Visibility = Visibility.Visible;
        ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressActionsRow.Visibility = Visibility.Collapsed;
        try { await retry(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Retry failed: {ex}");
            ShowProgressError(ex.Message);
        }
    }

    // ------------------------------------------------------------------------
    // Gear menu: Maintenance + Advanced sections. Verify and Repair used to
    // be sidebar buttons; now they live here under "Mantenimiento". Logs
    // is new — under "Avanzado".
    // ------------------------------------------------------------------------

    private async void MenuRepairInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (!_modIsInstalled || string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }
        await RepairInstallAsync();
    }

    private void MenuVerifyFiles_Click(object sender, RoutedEventArgs e)
        => VerifyButton_Click(sender, e);

    /// <summary>
    /// Opens the diagnostic log file in the system's default text editor —
    /// useful for support / debugging without us having to surface every
    /// internal state in the UI.
    /// </summary>
    private void MenuViewLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "launcher-debug.log");
            if (!File.Exists(logPath)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to open log: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------------
    // Progress panel "tone" (colour theme per operation type). The compact
    // sidebar panel keeps its layout but swaps borders + accents based on
    // what's running.
    // ------------------------------------------------------------------------

    private enum ProgressOperation
    {
        Install, Update, Repair, Verify, Uninstall, Generic,
    }

    private record ProgressTone(
        string Background,
        string Border,
        string Accent,
        string Icon);

    private static ProgressTone ToneFor(ProgressOperation op) => op switch
    {
        ProgressOperation.Install   => new("#1a2a3a", "#3a8cd9", "#5b9bd5", "⬇"),
        ProgressOperation.Update    => new("#1a2a3a", "#3a8cd9", "#5b9bd5", "↻"),
        ProgressOperation.Repair    => new("#3a2a1a", "#d4a04a", "#d4a04a", "🔧"),
        ProgressOperation.Verify    => new("#2a1a3a", "#a060d4", "#a060d4", "✓"),
        ProgressOperation.Uninstall => new("#3a1a1a", "#c84a4a", "#e63950", "✕"),
        _                           => new("#1a2a3a", "#3a8cd9", "#5b9bd5", "↻"),
    };

    private void SetProgressTone(ProgressOperation op)
    {
        var tone = ToneFor(op);
        ProgressPanel.Background = SafeBrush(tone.Background, "#1a2a3a");
        ProgressPanel.BorderBrush = SafeBrush(tone.Border, "#3a8cd9");
        ProgressPanelLabel.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        PatchProgress.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        OverallProgress.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        ProgressIcon.Text = tone.Icon;
        ProgressIcon.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
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
        var processes = Process.GetProcessesByName(GameProcessName());
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
        var payloadUrls = _updateService.EffectivePayloadZipUrls();
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
            suggestedFolder = _updateService.EffectiveDefaultInstallFolder();
        }

        // Append the mod's folder name (e.g. "Wars of Liberty") if the user
        // pointed at a parent folder. Only applies to isolated-folder mods —
        // in-place mods (Improvement Mod) install directly into AoE3.
        if (_updateService.Profile.InstallType == ModInstallType.IsolatedFolder)
        {
            var modFolderName = Path.GetFileName(
                _updateService.EffectiveDefaultInstallFolder().TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(modFolderName)
                && !suggestedFolder.EndsWith(modFolderName, StringComparison.OrdinalIgnoreCase))
            {
                suggestedFolder = Path.Combine(suggestedFolder, modFolderName);
            }
        }

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

        // Open the colored progress panel in the sidebar with the right
        // title for an Install. The progress reporters below fill in the
        // bars / step counter / speed.
        StartProgressPanel(
            ProgressOperation.Install,
            title: Strings.Format("ProgressTitleInstalling", _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubDownloading"));

        LblCurrentPatch.Text = Strings.Get("ProgressBarDownload");
        PatchProgress.Value = 0;
        OverallProgress.Value = 0;
        PatchBytesText.Text = "";
        OverallBytesText.Text = "";
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
            _config.GetActiveState().InstallPath = installFolder;
            _config.Save();

            // Verify installation
            var verifyResult = await Task.Run(() => VerifyInstallation(installFolder));
            int totalProblems = verifyResult.MissingItems.Count + verifyResult.CorruptItems.Count;
            if (totalProblems == 0)
            {
                SetStatus(Strings.Format("StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format("StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
            }
            else
            {
                SetStatus(Strings.Format("StatusInstallIncomplete", totalProblems));
                DiagnosticLog.Write($"Install verification: {totalProblems} problems found.");
                foreach (var m in verifyResult.MissingItems)
                    DiagnosticLog.Write($"  [missing] {m}");
                foreach (var c in verifyResult.CorruptItems)
                    DiagnosticLog.Write($"  [corrupt/empty] {c}");
                ShowProgressError(Strings.Format("StatusInstallIncomplete", totalProblems));
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
            ShowProgressCancelled();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            DiagnosticLog.Write($"Install failed: {ex}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            _cloneService = null;
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
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
        _cts = new CancellationTokenSource();
        ShowDownloadControls(true);

        var fromVersion = _updateService.CurrentVersion?.Ver ?? "?";
        var toVersion = _updateService.LatestVersion?.Ver ?? "?";
        StartProgressPanel(
            ProgressOperation.Update,
            title: Strings.Format("ProgressTitleUpdating", _updateService.Profile.DisplayName),
            subtitle: Strings.Format("ProgressUpdating", fromVersion, toVersion),
            retry: ApplyAsync);

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
            ShowProgressCancelled();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            DiagnosticLog.Write($"Update failed: {ex}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ShowDownloadControls(false);
            ResetProgressUI();
            _currentUpdatePhase = UpdatePhase.None;
        }

        if (succeeded)
        {
            ShowProgressCompleted("ProgressTitleCompleted",
                Strings.Format("ProgressUpdating", fromVersion, toVersion));
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

        // Drive the progress-panel subtitle and step counter — these are
        // what the user sees in the inline panel at the bottom of the
        // sidebar.
        ProgressSubtitleText.Text = Strings.Format("ProgressPatchSubtitle",
            p.CurrentStep, p.TotalSteps, p.PatchFromVersion, p.PatchToVersion);
        ProgressStepText.Text = Strings.Format(
            "ProgressStepFormat", p.CurrentStep, p.TotalSteps);

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

    /// <summary>
    /// Process-name (no extension) of the active mod's game executable —
    /// what <c>Process.GetProcessesByName</c> wants when we need to
    /// detect the game running, kill it, or watch for it exiting.
    /// </summary>
    private string GameProcessName() =>
        Path.GetFileNameWithoutExtension(_updateService.Profile.GameExecutable);

    /// <summary>
    /// Click handler for the unified primary button. Dispatches based on
    /// the current <see cref="_primaryAction"/>: Install kicks off the
    /// install flow, Play launches the game, Update applies pending
    /// patches, Stop kills the running game. Setting the action lives in
    /// <see cref="SetPrimaryAction"/>; this handler is just routing.
    /// </summary>
    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_primaryAction)
        {
            case PrimaryAction.Install:
                await InstallAsync();
                break;
            case PrimaryAction.Update:
                if (!EnsureGameNotRunning()) return;
                await ApplyUpdateWithElevationCheckAsync();
                break;
            case PrimaryAction.Stop:
                StopButton_Click(sender, e);
                break;
            case PrimaryAction.Play:
                ExecutePlay(sender, e);
                break;
        }
    }

    /// <summary>The actual "launch the game" flow — extracted so the
    /// primary-button dispatcher can call it without re-running the
    /// state-mode check it already did.</summary>
    private void ExecutePlay(object sender, RoutedEventArgs e)
    {
        if (_isGameRunning) return; // Already running

        try
        {
            GameLauncher.Launch(_config, _updateService.InstallPath, _updateService.Profile);
            StartGameMonitor();
        }
        catch (FileNotFoundException)
        {
            // game .exe not found — offer to browse instead of an error
            RefreshIdlePanel();
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
        var processes = Process.GetProcessesByName(GameProcessName());
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
            var processes = Process.GetProcessesByName(GameProcessName());
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
    /// Possible meanings the primary sidebar button can take depending on
    /// whether the active mod is installed, has updates, the game is
    /// running, etc. The button itself is always <c>PlayButton</c> in
    /// XAML — its content/color/handler all flip via
    /// <see cref="SetPrimaryAction"/>.
    /// </summary>
    private enum PrimaryAction { Hidden, Install, Play, Update, Stop }

    private PrimaryAction _primaryAction = PrimaryAction.Hidden;

    /// <summary>
    /// Switches the sidebar's big primary button into a given mode. Each
    /// mode picks its own label, background color, and enabled state. The
    /// click dispatcher in <see cref="PlayButton_Click"/> reads this back
    /// to know which flow to invoke.
    /// </summary>
    private void SetPrimaryAction(PrimaryAction action, bool enabled = true)
    {
        _primaryAction = action;
        var profile = _updateService.Profile;
        var accent = SafeBrush(profile.AccentColor, "#c8102e");

        switch (action)
        {
            case PrimaryAction.Install:
                PlayButtonText.Text = Strings.Get("BtnInstall");
                PlayButton.Background = accent;
                PlayButton.Visibility = Visibility.Visible;
                PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Play:
                PlayButtonText.Text = _isGameRunning
                    ? Strings.Get("BtnPlaying")
                    : Strings.Get("BtnPlay");
                PlayButton.Background = accent;
                PlayButton.Visibility = Visibility.Visible;
                PlayButton.IsEnabled = enabled && !_isGameRunning;
                break;
            case PrimaryAction.Update:
                PlayButtonText.Text = Strings.Get("BtnUpdate");
                PlayButton.Background = Brush("#d4a04a");
                PlayButton.Visibility = Visibility.Visible;
                PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Stop:
                PlayButtonText.Text = Strings.Get("BtnStop");
                PlayButton.Background = Brush("#8b0000");
                PlayButton.Visibility = Visibility.Visible;
                PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Hidden:
                PlayButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    /// <summary>
    /// Updates button states and status text based on whether the game is
    /// running. With the unified primary button, this just flips between
    /// Stop (when running) and re-applies whatever was set before
    /// (when not running) by re-running the post-CheckAsync logic.
    /// </summary>
    private void UpdateGameUI()
    {
        if (_isGameRunning)
        {
            SetPrimaryAction(PrimaryAction.Stop);
            SetStatus(Strings.Get("StatusPlaying"));
        }
        else
        {
            // Restore the appropriate action based on install state. If a
            // CheckAsync hasn't run yet we default to Play (the mod must
            // have been installed for the user to have launched it).
            SetPrimaryAction(_modIsInstalled ? PrimaryAction.Play : PrimaryAction.Install);
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
        // Verify / Repair / Uninstall live in the gear menu now and are
        // gated by their own MenuItem.IsEnabled inside MoreButton_Click.
        // The gear button itself stays live — its menu items handle their
        // own disabled states — but we lock it during ops so the user can't
        // fire a second flow on top of the running one.
        MoreButton.IsEnabled = !busy;
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
        bool aoe3Detected = GameLauncher.Find(_config, _updateService.InstallPath, _updateService.Profile) != null;

        MenuOpenAoE3Folder.IsEnabled = aoe3Detected;
        MenuSelectModFolder.IsEnabled = !_isBusy;
        MenuSelectAoE3Folder.IsEnabled = !_isBusy;
        // Suppress the "wolDetected" unused-warning when the variable
        // isn't otherwise consumed in this method.
        _ = wolDetected;

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

        // Health-check + maintenance actions
        MenuCheckForUpdates.IsEnabled = !_isBusy && _modIsInstalled;
        MenuRepairInstall.IsEnabled = !_isBusy && _modIsInstalled;
        MenuVerifyFiles.IsEnabled = !_isBusy && _modIsInstalled;
        // ViewLogs is always available — useful even when no mod is installed.
        MenuViewLogs.IsEnabled = true;

        // Game-language submenu — populated each time the menu opens so
        // the available list reflects the latest registry state and the
        // active translation indicator is up to date.
        MenuGameLanguage.IsEnabled = !_isBusy && _modIsInstalled;
        PopulateGameLanguageMenu();

        UninstallMenuItem.IsEnabled = !_isBusy && _modIsInstalled;
        // Open to the right of the Settings button, with a small gap so the
        // menu doesn't visually touch the button. WPF auto-flips to the
        // left side if there's not enough room (e.g. very narrow window).
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        MoreButton.ContextMenu.HorizontalOffset = 8;
        MoreButton.ContextMenu.VerticalOffset = 0;
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
    /// Opens the detected Age of Empires III install folder in Explorer.
    /// Walks up from the detected age3y.exe so the user lands on the AoE3
    /// root instead of `bin\` (Steam layout).
    /// </summary>
    private void MenuOpenAoE3Folder_Click(object sender, RoutedEventArgs e)
    {
        var exePath = GameLauncher.Find(_config, _updateService.InstallPath, _updateService.Profile);
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

    // ------------------------------------------------------------------------
    // Game-language submenu (community translations)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Builds the contents of the "Idioma del juego" submenu. Always shows:
    ///   - "English (default)" with a check if no translation is active
    ///   - One entry per available pack from the index (or installed locally
    ///     if the index couldn't be fetched)
    ///   - "Refresh list" at the bottom
    /// </summary>
    private void PopulateGameLanguageMenu()
    {
        MenuGameLanguage.Items.Clear();

        var installPath = _updateService.InstallPath;
        var translationsService = !string.IsNullOrEmpty(installPath)
            ? new TranslationService(installPath)
            : null;

        var activeId = _config.GetActiveState().ActiveTranslationId ?? "";
        var installed = translationsService?.ListInstalled() ?? new List<TranslationManifest>();

        // English entry — always first, always available
        var english = new System.Windows.Controls.MenuItem
        {
            Header = string.IsNullOrEmpty(activeId)
                ? $"🇬🇧  {Strings.Get("MenuLangEnglish")}  ✓"
                : $"🇬🇧  {Strings.Get("MenuLangEnglish")}",
            Foreground = Brush(string.IsNullOrEmpty(activeId) ? "#9bd99b" : "White"),
        };
        english.Click += (_, _) => RevertToEnglish();
        MenuGameLanguage.Items.Add(english);

        MenuGameLanguage.Items.Add(new System.Windows.Controls.Separator
        {
            Background = Brush("#3a3d44"),
        });

        // Build the union of registry entries + locally installed packs.
        // Registry entry takes priority (has the downloadUrl for updates).
        var entries = new Dictionary<string, TranslationIndexEntry>(StringComparer.OrdinalIgnoreCase);
        if (_cachedTranslationIndex != null)
        {
            foreach (var e in _cachedTranslationIndex.Translations)
                entries[e.Id] = e;
        }
        foreach (var m in installed)
        {
            if (entries.ContainsKey(m.Id)) continue;
            // Local-only pack (sideloaded). Synthesize a minimal index entry
            // so we can show it in the menu even without a server listing.
            entries[m.Id] = new TranslationIndexEntry
            {
                Id = m.Id,
                Name = m.Name,
                Author = m.Author,
                Version = m.Version,
                CompatibleWith = m.CompatibleWith,
            };
        }

        if (entries.Count == 0)
        {
            // Nothing in the index and nothing locally installed — show a
            // disabled placeholder so the user knows the system is working
            // but there's just no content yet.
            MenuGameLanguage.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = Strings.Get("MenuLangNoneAvailable"),
                IsEnabled = false,
                Foreground = Brush("#888"),
            });
        }
        else
        {
            var currentMod = _updateService.CurrentVersion?.Ver;
            foreach (var entry in entries.Values.OrderBy(e => e.Name))
            {
                var item = BuildLanguageMenuItem(entry, installed, activeId, currentMod);
                MenuGameLanguage.Items.Add(item);
            }
        }

        MenuGameLanguage.Items.Add(new System.Windows.Controls.Separator
        {
            Background = Brush("#3a3d44"),
        });

        // Refresh list — fetches the index from GitHub again. StaysOpenOnClick
        // keeps the gear menu (and this submenu) open while the async fetch
        // runs, so the user can see the updated list and keep clicking other
        // options without having to reopen the menu.
        var refresh = new System.Windows.Controls.MenuItem
        {
            Header = $"🔄  {Strings.Get("MenuLangRefresh")}",
            StaysOpenOnClick = true,
        };
        refresh.Click += async (_, _) =>
        {
            // Briefly switch the label so the user sees "doing it now" — the
            // fetch is async and may take a second or two over the network.
            var originalHeader = refresh.Header;
            refresh.Header = $"⏳  {Strings.Get("MenuLangRefreshing")}";
            refresh.IsEnabled = false;

            await RefreshTranslationIndexAsync(reportStatus: true);

            // Rebuild the submenu so the new entries from the just-fetched
            // index appear immediately. The submenu Popup stays open across
            // this rebuild because StaysOpenOnClick suppresses auto-close.
            PopulateGameLanguageMenu();

            // PopulateGameLanguageMenu re-adds a fresh refresh item, so the
            // restoration of `originalHeader` here is only relevant if the
            // user clicked while the menu was about to close. Cheap to do.
            refresh.Header = originalHeader;
            refresh.IsEnabled = true;
        };
        MenuGameLanguage.Items.Add(refresh);

        // Translator tool — turns a folder of translated XMLs into a ready-
        // to-publish .zip + JSON snippet. Only useful for translators, but
        // harmless for regular users (they just won't have anything to package).
        var packager = new System.Windows.Controls.MenuItem
        {
            Header = $"📦  {Strings.Get("MenuLangPackager")}",
        };
        packager.Click += (_, _) => OpenTranslationPackager();
        MenuGameLanguage.Items.Add(packager);
    }

    /// <summary>
    /// Opens the translator-facing dialog that builds a .zip pack from a
    /// folder of translated XML files. The dialog handles all hash/manifest/
    /// zip logic so the translator only fills in a small form. Works even
    /// when the launcher's own _originals snapshot doesn't exist — the
    /// translator can point at their own backup of the English files in
    /// that case.
    /// </summary>
    private void OpenTranslationPackager()
    {
        if (string.IsNullOrEmpty(_updateService.InstallPath))
        {
            MessageBox.Show(this,
                Strings.Get("StatusNotInstalled"),
                Strings.Get("DlgPackagerTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var translations = new TranslationService(_updateService.InstallPath);
        var dialog = new TranslationPackagerDialog(translations, _updateService.CurrentVersion?.Ver)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private System.Windows.Controls.MenuItem BuildLanguageMenuItem(
        TranslationIndexEntry entry,
        List<TranslationManifest> installed,
        string activeId,
        string? currentModVersion)
    {
        bool isActive = string.Equals(activeId, entry.Id, StringComparison.OrdinalIgnoreCase);
        var local = installed.FirstOrDefault(m => string.Equals(m.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        bool isInstalled = local != null;
        bool hasUpdate = isInstalled
            && !string.IsNullOrEmpty(entry.Version)
            && !string.IsNullOrEmpty(local!.Version)
            && entry.Version != local.Version;

        // Build the header. Shows BOTH the pack version (so the user knows
        // which release of the translation they're looking at) AND the
        // compatible mod versions (so they know if it'll work with their
        // install). Format:
        //   <flag>  <name>  v<pack>  (mod <X>, <Y>)  · <author>  <indicators>
        string header = $"{LanguageFlag(entry.Id)}  {entry.Name}";
        if (!string.IsNullOrEmpty(entry.Version)) header += $"  v{entry.Version}";
        if (entry.CompatibleWith.Count > 0)
        {
            var label = Strings.Format(
                "MenuLangModVersionLabel",
                string.Join(", ", entry.CompatibleWith));
            header += $"  {label}";
        }
        if (!string.IsNullOrEmpty(entry.Author)) header += $"  · {entry.Author}";

        if (isActive) header += "  ✓";
        else if (hasUpdate) header += $"  🆕 → v{entry.Version}";

        // Compatibility hint (declared list — not authoritative, just a soft warning)
        bool incompatible = !string.IsNullOrEmpty(currentModVersion)
            && entry.CompatibleWith.Count > 0
            && !entry.CompatibleWith.Contains(currentModVersion);
        if (incompatible) header += "  ⚠";

        var item = new System.Windows.Controls.MenuItem
        {
            Header = header,
            Foreground = Brush(isActive ? "#9bd99b" : (incompatible ? "#888" : "White")),
        };
        item.Click += (_, _) => ApplyTranslationAsync(entry);
        return item;
    }

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

    /// <summary>
    /// Fetches the translation index from GitHub. Each release of the
    /// configured <c>config.TranslationsRepo</c> is one self-contained
    /// translation pack (its <c>translation.json</c> manifest plus the
    /// localized files in a <c>.zip</c>), so we just list releases via
    /// the GitHub API and read the manifests directly.
    /// </summary>
    /// <param name="reportStatus">
    /// True when the user explicitly asked for a refresh — surfaces a status
    /// message in the main window. False when called as part of the silent
    /// startup pre-fetch (we don't want it stomping over CheckAsync's output).
    /// </param>
    private async Task RefreshTranslationIndexAsync(bool reportStatus = false)
    {
        var registry = new TranslationRegistryService();
        try
        {
            TranslationIndex? index = null;
            var repo = _updateService.EffectiveTranslationsRepo();
            if (!string.IsNullOrWhiteSpace(repo))
            {
                index = await registry.FetchFromReleasesAsync(repo);
            }

            _cachedTranslationIndex = index;

            if (reportStatus)
            {
                if (_cachedTranslationIndex == null)
                    SetStatus(Strings.Get("StatusLangIndexUnavailable"));
                else
                    SetStatus(Strings.Format("StatusLangIndexLoaded",
                        _cachedTranslationIndex.Translations.Count));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Translation index refresh error: {ex.Message}");
            if (reportStatus)
                SetStatus(Strings.Get("StatusLangIndexUnavailable"));
        }
    }

    /// <summary>
    /// Opens the styled apply-translation dialog. The dialog handles the
    /// full lifecycle (download progress + compatibility check + apply +
    /// inline error display) with no separate MessageBox popups, matching
    /// the rest of the launcher's dark theme.
    /// </summary>
    private void ApplyTranslationAsync(TranslationIndexEntry entry)
    {
        if (_isBusy) return;
        if (string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }

        var translations = new TranslationService(_updateService.InstallPath);
        var registry = new TranslationRegistryService();

        var dialog = new TranslationApplyDialog(
            entry,
            _updateService.CurrentVersion?.Ver,
            translations,
            registry)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true && dialog.AppliedSuccessfully)
        {
            _config.GetActiveState().ActiveTranslationId = entry.Id;
            _config.Save();
            SetStatus(Strings.Format("StatusLangApplied", entry.Name));
        }
    }

    /// <summary>
    /// Reverts the install to canonical English by copying every file in the
    /// snapshot back over the live data folder. No download needed.
    /// </summary>
    private void RevertToEnglish()
    {
        if (_isBusy) return;
        if (string.IsNullOrEmpty(_updateService.InstallPath)) return;
        var activeState = _config.GetActiveState();
        if (string.IsNullOrEmpty(activeState.ActiveTranslationId)) return; // already EN

        var translations = new TranslationService(_updateService.InstallPath);
        if (translations.RevertToOriginal())
        {
            activeState.ActiveTranslationId = "";
            _config.Save();
            SetStatus(Strings.Get("StatusLangRevertedToEnglish"));
        }
        else
        {
            MessageBox.Show(this,
                Strings.Get("DlgLangRevertFailedBody"),
                Strings.Get("DlgLangApplyFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        StartProgressPanel(
            ProgressOperation.Uninstall,
            title: Strings.Format("ProgressTitleUninstalling", _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubRemoving"),
            bar1Label: "ProgressBarProcess",
            bar2Label: "ProgressBarCleanup");
        SetStatus(Strings.Get("StatusUninstalling"));

        // The uninstall service emits a single "Pct/Step" tuple per phase.
        // Map to the two bars: top bar follows the percentage, bottom bar
        // tracks the same so the user sees both filling in tandem (the
        // service doesn't have separate process vs cleanup metrics, but
        // showing two bars matches the rest of the operation panels).
        var progress = new Progress<(double Pct, string Step)>(p =>
        {
            PatchProgress.Value = p.Pct;
            OverallProgress.Value = p.Pct;
            PatchBytesText.Text = $"{p.Pct:0}%";
            OverallBytesText.Text = $"{p.Pct:0}%";
            ProgressSubtitleText.Text = p.Step;
            SetStatus(p.Step);
        });

        try
        {
            var result = await uninstaller.UninstallAsync(plan, dialog.Options, progress);

            // Clear the saved path so re-detection runs from scratch
            if (dialog.Options.ResetConfig || result.Success)
            {
                _config.GetActiveState().InstallPath = "";
                _config.GameExecutable = "";
                _config.Save();
            }

            if (result.Success)
            {
                SetStatus(Strings.Format("StatusUninstallSuccess", result.FilesDeleted));
                // Uninstall: nothing to play, nothing to open — just Close.
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format("StatusUninstallSuccess", result.FilesDeleted));
            }
            else
            {
                SetStatus(Strings.Format("StatusUninstallPartial", result.Errors.Count));
                foreach (var err in result.Errors)
                    DiagnosticLog.Write($"  uninstall error: {err}");
                ShowProgressError(Strings.Format("StatusUninstallPartial", result.Errors.Count));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Uninstall failed: {ex}");
            SetStatus($"Uninstall failed: {ex.Message}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            SetBusy(false);
            // Re-check so the UI flips back to "Install" mode
            await CheckAsync();
        }
    }
}
