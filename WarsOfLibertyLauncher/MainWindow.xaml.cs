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
    // True when _isBusy is held by a read-only CheckAsync (background
    // refresh, no install / download / uninstall). Mod-switch pre-flight
    // ignores this kind of busy so the user can keep clicking mods without
    // the "operation in progress" popup.
    private bool _isCheckOnly;
    private bool _modIsInstalled = true;  // false when no valid install detected
    // Cached result of GameLauncher.FindAoe3Install — the full registry +
    // VDF + all-drive scan can be 50-150ms on machines where AoE3 isn't
    // installed at all, which RefreshStatusCard (called on every mod
    // switch) was paying every single time. AoE3 doesn't install/uninstall
    // while the launcher is open, so a per-session cache is safe; we
    // invalidate it whenever the user manually picks a path or an install
    // flow completes.
    private bool? _aoe3DetectedCache;

    // Per-mod cache of the last successful CheckAsync result. Lets the
    // second-and-subsequent visits to a mod in the same session replay
    // the saved manifest / pending-downloads / version data synchronously
    // instead of paying the 1-2 second network round-trip every time.
    // Invalidated on install / uninstall / update so a state-changing
    // action forces a fresh check next time the user lands on that mod.
    private readonly Dictionary<string, UpdateService.CheckResult> _checkResultCache =
        new(StringComparer.OrdinalIgnoreCase);
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
        StatusCardControl.BrowseAoE3Click += BrowseAoE3Button_Click;
        ProgressPanelControl.PauseButton.Click += PauseButton_Click;
        ProgressPanelControl.CancelButton.Click += CancelButton_Click;
        ProgressPanelControl.ProgressActionRetry.Click += ProgressActionRetry_Click;
        MainTabsControl.TabNoticias.Click += TabNoticias_Click;
        MainTabsControl.TabChangelog.Click += TabChangelog_Click;
        MainTabsControl.TabAyuda.Click += TabAyuda_Click;
        ActionPanelControl.PlayButton.Click += PlayButton_Click;
        ActionPanelControl.StopButton.Click += StopButton_Click;
        ActionPanelControl.UpdateButton.Click += UpdateButton_Click;
        ActionPanelControl.MoreButton.Click += MoreButton_Click;
        ActionPanelControl.OpenFolderButton.Click += OpenFolderButton_Click;
        ActionPanelControl.MenuOpenAoE3Folder.Click += MenuOpenAoE3Folder_Click;
        ActionPanelControl.MenuSelectModFolder.Click += MenuSelectModFolder_Click;
        ActionPanelControl.MenuSelectAoE3Folder.Click += MenuSelectAoE3Folder_Click;
        ActionPanelControl.MenuOpenUserDataFolder.Click += MenuOpenUserDataFolder_Click;
        ActionPanelControl.MenuCreateBackupNow.Click += MenuCreateBackupNow_Click;
        ActionPanelControl.MenuRestoreUserData.Click += MenuRestoreUserData_Click;
        ActionPanelControl.MenuCheckForUpdates.Click += MenuCheckForUpdates_Click;
        ActionPanelControl.MenuRepairInstall.Click += MenuRepairInstall_Click;
        ActionPanelControl.MenuVerifyFiles.Click += MenuVerifyFiles_Click;
        ActionPanelControl.MenuViewLogs.Click += MenuViewLogs_Click;
        ActionPanelControl.UninstallMenuItem.Click += UninstallMenuItem_Click;
        DiagnosticLog.Reset();
        DiagnosticLog.Write("MainWindow initialized.");

        _config = LauncherConfig.Load();
        Strings.SetLanguage(_config.Language);
        Strings.LanguageChanged += ApplyLanguage;
        RestoreWindowState();

        var activeProfile = _config.GetActiveProfile();
        DiagnosticLog.Write(
            $"Active mod profile: '{activeProfile.Id}' ({activeProfile.DisplayName}).");
        DiagnosticLog.Write($"Config loaded. updateInfoUrl={_config.UpdateInfoUrl}");
        DiagnosticLog.Write($"  modInstallPath={_config.GetActiveState().InstallPath}");
        DiagnosticLog.Write($"  gameExecutable={_config.GameExecutable}");
        DiagnosticLog.Write($"  language={_config.Language}");

        _updateService = new UpdateService(_config, activeProfile);
        _installerService = new InstallerService();
        UpdateAccentResources(activeProfile);

        ApplyLanguage();
        RefreshModCards();
        ResetProgressUI();
        // Sync the right-content tab visibility with the saved _activeTab
        // restored by RestoreWindowState. SwitchContentTab needs Strings
        // (ApplyLanguage above) and the profile to be ready, so it can't
        // run from RestoreWindowState itself.
        SwitchContentTab(_activeTab);

        // Surface the tray icon if either MinimizeToTray or
        // ShowToastNotifications is on — the icon is the anchor point
        // for both flows. Recomputed every time Launcher Settings closes.
        UpdateTrayIconVisibility();

        // Check for --update-now flag from elevated relaunch
        var args = Environment.GetCommandLineArgs();
        bool autoUpdate = args.Any(a => string.Equals(a, "--update-now", StringComparison.OrdinalIgnoreCase));
        if (autoUpdate)
            DiagnosticLog.Write("Started with --update-now: will auto-apply updates after check.");

        // Auto-check for updates on startup. Run all four checks IN PARALLEL
        // — launcher self-update (GitHub), mod patch check (aoe3wol.com),
        // translations index (GitHub) and mods catalog (GitHub) hit different
        // servers, so doing them concurrently roughly cuts the busy state to
        // the slowest one. Pre-fetching the translations index here means the
        // gear menu opens with the language list already populated — no
        // "Refresh" needed for the common case. Pre-fetching the catalog
        // means community mods appear in the top bar without a launcher
        // restart.
        Loaded += async (_, _) =>
        {
            LauncherUpdateService.CleanupOldVersion();

            // CheckUpdatesOnStartup gates the four parallel network calls
            // that happen at boot. When the user has it off — typically
            // because they're on a metered connection or just want the
            // launcher to be silent — we skip the entire WhenAll. The
            // launcher still works fully from cached state (catalog
            // cache, last-known mod version, etc., all populated by my
            // earlier phases). The --update-now command-line flag below
            // is honoured regardless: it's an explicit user/elevated-
            // relaunch request, not a passive auto-check.
            if (_config.CheckUpdatesOnStartup)
            {
                await Task.WhenAll(
                    CheckForLauncherUpdateAsync(),
                    CheckAsync(),
                    RefreshTranslationIndexAsync(),
                    RefreshCatalogAsync(),
                    RefreshNewsAsync());

                // The catalog fetch may have surfaced new community mods
                // that weren't visible during the initial RefreshModCards()
                // call in the constructor. Re-render so they show up.
                // Cheap no-op for the common case where the catalog
                // returned the same set (RefreshModCards just rebuilds
                // the panel from ModRegistry.All which is idempotent).
                RefreshModCards();
            }
            else
            {
                DiagnosticLog.Write(
                    "Startup auto-check disabled (CheckUpdatesOnStartup=false); using cached state.");
                // News still loads from cache when auto-check is off.
                _ = RefreshNewsAsync();
            }

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

        // Icon resolution priority:
        //   1. Cached community icon (profile.LocalIconPath, populated by
        //      EnsureModAssetsAsync once the catalog's icon.png lands in
        //      %LocalAppData%\AoE3ModLauncher\mod-assets\).
        //   2. Built-in pack URI (profile.BannerImage — historical name; for
        //      WoL it's the .ico embedded as a pack resource).
        //   3. Fallback: monogram (accent-coloured disc + first letter of
        //      DisplayName) — handled below in the else branch.
        // For community mods that have an IconUrl but haven't been cached
        // yet, we kick off the download here so the next render swaps the
        // monogram for the real icon.
        var iconBrush = TryLoadTileImage(ResolveModIcon(profile));
        if (iconBrush == null && !string.IsNullOrEmpty(profile.IconUrl))
            _ = EnsureModAssetsAsync(profile);
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
            Tag = profile,
            ToolTip = $"{profile.DisplayName} — {ProbeInstalledState(profile)}",
        };

        // Handlers attached unconditionally so an in-place highlight switch
        // (UpdateActiveModHighlight) doesn't strand a tile without hover/
        // click behaviour. Each handler short-circuits when the card
        // represents the currently active mod, so the active tile stays
        // calm (no hover flicker, no self-switch click).
        card.MouseEnter += (_, _) =>
        {
            if (IsActiveModCard(card)) return;
            card.Background = hoverBg;
        };
        card.MouseLeave += (_, _) =>
        {
            if (IsActiveModCard(card)) return;
            card.Background = inactiveBg;
        };
        // Fire the switch on mouse-DOWN, not up. Natural clicks have ~50 ms
        // between press and release; using ButtonDown collapses that window
        // so visual feedback shows as soon as the user presses the tile.
        card.MouseLeftButtonDown += (_, _) =>
        {
            if (IsActiveModCard(card)) return;
            LoadModProfile(profile);
        };

        return card;
    }

    /// <summary>Card is currently displaying the active mod's profile.</summary>
    private bool IsActiveModCard(System.Windows.Controls.Border card)
    {
        return card.Tag is ModProfile p
            && string.Equals(p.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cheap mod-switch repaint: walks the existing tiles in
    /// <see cref="ModCardsPanel"/> and updates only the active highlight
    /// (BorderBrush + Background) on each, skipping the full rebuild's
    /// expensive work (per-tile <see cref="ProbeInstalledState"/> disk
    /// probes, image loads, allocations). Falls back to
    /// <see cref="RefreshModCards"/> when the tile count doesn't match
    /// the registry — e.g. a community mod showed up between switches.
    /// </summary>
    private void UpdateActiveModHighlight()
    {
        var allProfiles = ModRegistry.All.ToList();
        if (ModCardsPanel.Children.Count != allProfiles.Count)
        {
            // Tile set drifted from registry (catalog refresh added/removed
            // mods) — full rebuild is the only safe option.
            RefreshModCards();
            return;
        }

        var activeId = _updateService.Profile.Id;
        foreach (var child in ModCardsPanel.Children)
        {
            if (child is not System.Windows.Controls.Border card) continue;
            if (card.Tag is not ModProfile cardProfile) continue;

            bool isActive = string.Equals(cardProfile.Id, activeId, StringComparison.OrdinalIgnoreCase);
            if (isActive)
            {
                card.BorderBrush = SafeBrush(cardProfile.AccentColor, "#3a3d44");
                card.Background = SafeBrush(BlendWithBase(cardProfile.AccentColor, 0.18), "#3a1f24");
            }
            else
            {
                card.BorderBrush = Brush("#3a3d44");
                card.Background = Brush("#22252c");
            }
        }
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

            // Saved per-mod state from a previous session. Validate it
            // properly — Directory.Exists alone is too loose: a stale
            // pointer at "...\Age Of Empires 3\bin" passes that check
            // because vanilla AoE3 has the folder. Require the probe file
            // to be there AND (for IsolatedFolder mods) the leaf folder
            // name to look like the mod's expected folder.
            var saved = _config.GetState(profile.Id).InstallPath;
            if (!string.IsNullOrEmpty(saved)
                && Directory.Exists(saved)
                && SavedPathLooksValid(saved, profile))
            {
                return Strings.Get("ModSelectorInstalledNoVersion");
            }

            // One-shot probe at the obvious locations.
            var probe = ResolveProbedInstallPath(profile);
            if (!string.IsNullOrEmpty(probe))
                return Strings.Get("ModSelectorInstalledNoVersion");
        }
        catch { /* probes must never throw */ }
        return Strings.Get("ModSelectorNotInstalled");
    }

    /// <summary>
    /// Tile-side equivalent of <see cref="Services.UpdateService"/>'s
    /// <c>CachedPathLeafLooksValid</c>: rejects a cached install path that
    /// happens to satisfy <see cref="Directory.Exists(string)"/> but doesn't
    /// look like a real install of this mod. Two signals:
    /// <list type="bullet">
    ///   <item>For IsolatedFolder mods, the leaf folder name must match
    ///   <see cref="ModProfile.DisplayName"/> or the leaf of
    ///   <see cref="ModProfile.DefaultInstallFolder"/>.</item>
    ///   <item>The probe file must exist inside.</item>
    /// </list>
    /// </summary>
    private static bool SavedPathLooksValid(string saved, ModProfile profile)
    {
        if (string.IsNullOrEmpty(profile.InstallProbeFile))
            return true;

        var probe = Path.Combine(saved, profile.InstallProbeFile);
        if (!File.Exists(probe)) return false;

        if (profile.InstallType != ModInstallType.IsolatedFolder)
            return true;

        var leaf = Path.GetFileName(saved.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(leaf)) return false;

        string[] expected = new[]
        {
            profile.DisplayName,
            Path.GetFileName(profile.DefaultInstallFolder?.TrimEnd('\\', '/') ?? ""),
        };
        return expected.Any(e =>
            !string.IsNullOrEmpty(e)
            && string.Equals(leaf, e, StringComparison.OrdinalIgnoreCase));
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

        // Pre-flight: only real operations (install / update / uninstall /
        // verify) block a mod switch. The read-only CheckAsync that refreshes
        // version + pending-downloads info also flips _isBusy=true, but it's
        // safe to switch over — we cancel that previous check below and the
        // new switch starts its own. Without this distinction, clicking mods
        // faster than the background check completes fired the "operation in
        // progress" popup even though nothing the user cared about was
        // actually running.
        if (_isBusy && !_isCheckOnly)
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

        // Cancel any in-flight background check so the previous profile's
        // CheckAsync exits quickly. The result-writeback inside CheckAsync
        // also re-checks the active profile so even if the old call wins
        // the race, it won't trample the new mod's UI.
        if (_isCheckOnly)
        {
            try { _cts?.Cancel(); } catch { /* already disposed — ignore */ }
        }

        DiagnosticLog.Write(
            $"Switching active mod profile in place: '{_updateService.Profile.Id}' -> '{target.Id}'");

        // Persist the choice — fire-and-forget so the UI thread doesn't
        // block on disk I/O for what should feel like an instant tile
        // click. We've already mutated _config.ActiveModId in memory, so
        // every code path that reads it sees the new value. The worst case
        // (process kill before the write finishes) means the launcher
        // comes up on the previous mod on next launch, which is fine —
        // not a data-loss scenario.
        _config.ActiveModId = target.Id;
        _ = Task.Run(() =>
        {
            try { _config.Save(); }
            catch (Exception ex) { DiagnosticLog.Write($"Async config save after mod switch failed: {ex.Message}"); }
        });

        // Fresh service bound to the new profile. Per-mod state in
        // _config.Mods[target.Id] keeps the install path / translation
        // separate from any previously active mod. The constructor does a
        // synchronous fast-path lookup of the cached install path, so
        // _updateService.InstallPath is already populated by the time we
        // get here for mods seen in a previous session.
        _updateService = new UpdateService(_config, target);
        UpdateAccentResources(target);

        // Reset session caches that were tied to the old mod.
        _pendingDownloads = new();
        // Trust the synchronous cache check the UpdateService constructor
        // just did. Avoids the "Not installed (red) → Installed (green)"
        // flicker every time the user switches to a previously-detected
        // mod. CheckAsync runs right after and refines the rest (current
        // version, pending updates, etc.); if the install was uninstalled
        // out-of-band between sessions, CheckAsync will clear this flag.
        _modIsInstalled = !string.IsNullOrEmpty(_updateService.InstallPath);
        _warnedAboutBrokenInstall = false;
        _cachedTranslationIndex = null;

        // Repaint static UI under the new profile (title, subtitle, accent).
        // Only the mod-specific subset of ApplyLanguage runs here — the rest
        // (language-only labels, tray strings, section headers) didn't
        // change so re-touching ~40 controls every switch was wasted work.
        RefreshActiveModUi();
        // In-place highlight swap on the existing tiles instead of a full
        // rebuild (which re-runs per-tile disk probes and image loads).
        // The post-CheckAsync rebuild below still does the full pass once
        // the manifest fetch finishes, so any state-text drift gets fixed.
        UpdateActiveModHighlight();
        ResetProgressUI();

        // Sync the primary button (Play / Install / Update / Stop) with the
        // new profile's install state. ApplyLanguage above repaints it but
        // uses the cached _primaryAction from the PREVIOUS profile, which
        // means switching from a not-installed mod to an installed one (or
        // vice-versa) would otherwise show the wrong label until CheckAsync
        // finishes. UpdateGameUI reads _modIsInstalled (just refreshed from
        // the cached install path) and picks the right action.
        UpdateGameUI();
        // Refresh the StatusCard (state badge, version rows) synchronously
        // from cached values so the user sees the new mod's status right
        // after the click, not 1-2 seconds later when CheckAsync's network
        // call returns. Cheap now that the AoE3 detection inside is
        // memoised; before, this would have re-scanned every drive on
        // every switch.
        RefreshIdlePanel();

        // Re-detect install path + version + pending updates for the new
        // profile. CheckAsync already short-circuits for non-WolPatcher
        // profiles, so this is fast for IM and full-fat for WoL.
        await CheckAsync();

        // CheckAsync may have corrected the install path or version we
        // initially rendered from cache (e.g. detected a stale "\bin"
        // pointer and cleared it). Re-render the tile row so the secondary
        // state text ("Installed · vX" / "Not installed") matches the
        // freshly-detected reality of the active profile.
        RefreshModCards();

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
    /// Sync Application-level AccentBrush + AccentBrushHover with the active
    /// mod's accent so DynamicResource consumers (dialog XAMLs) repaint on
    /// mod switch. Called on startup and on every mod switch.
    /// </summary>
    private static void UpdateAccentResources(ModProfile profile)
    {
        var accent = ParseAccentColor(profile.AccentColor);

        // Bail if the resource brush already matches. Both built-in profiles
        // happen to use #c8102e, so every switch between them was rebuilding
        // identical brushes and triggering DynamicResource invalidation in
        // every consumer in the visual tree (tabs, dialog buttons, scrollbars,
        // status card, …) for nothing. Comparing the Color value avoids that.
        if (Application.Current?.Resources["AccentBrush"]
                is System.Windows.Media.SolidColorBrush existing
            && existing.Color == accent)
        {
            return;
        }

        var hover = LightenColor(accent, 0.18);
        var accentBrush = new System.Windows.Media.SolidColorBrush(accent);
        var hoverBrush = new System.Windows.Media.SolidColorBrush(hover);
        accentBrush.Freeze();
        hoverBrush.Freeze();
        Application.Current!.Resources["AccentBrush"] = accentBrush;
        Application.Current.Resources["AccentBrushHover"] = hoverBrush;
    }

    private static System.Windows.Media.Color ParseAccentColor(string hex)
    {
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex)
                is System.Windows.Media.Color c)
                return c;
        }
        catch { }
        return System.Windows.Media.Color.FromRgb(0xC8, 0x10, 0x2E);
    }

    private static System.Windows.Media.Color LightenColor(System.Windows.Media.Color c, double amount)
    {
        double r = c.R + (255.0 - c.R) * amount;
        double g = c.G + (255.0 - c.G) * amount;
        double b = c.B + (255.0 - c.B) * amount;
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Min(255.0, r),
            (byte)Math.Min(255.0, g),
            (byte)Math.Min(255.0, b));
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
    // Memo cache for loaded ImageBrush instances. Keyed by uri so repeated
    // switches to the same mod re-use the already-decoded BitmapSource
    // instead of re-running IconBitmapDecoder / BitmapImage every time.
    // Brushes are frozen at load time so they can be shared across threads
    // / UI elements without copying. Null is cached too — once we've
    // determined a uri can't be loaded, repeat probes return immediately.
    private static readonly Dictionary<string, System.Windows.Media.ImageBrush?> s_tileImageCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static System.Windows.Media.ImageBrush? TryLoadTileImage(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (s_tileImageCache.TryGetValue(uri, out var cached)) return cached;
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
            var brush = new System.Windows.Media.ImageBrush(source)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };
            if (brush.CanFreeze) brush.Freeze();
            s_tileImageCache[uri] = brush;
            return brush;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Mod tile image load failed for '{uri}': {ex.Message}");
            s_tileImageCache[uri] = null;
            return null;
        }
    }

    // ------------------------------------------------------------------------
    // Launcher Settings — global preferences dialog (scope: the whole
    // launcher, not the active mod). The per-mod gear menu in the sidebar
    // handles mod-specific concerns.
    // ------------------------------------------------------------------------

    private void LauncherSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LauncherSettingsDialog(_config) { Owner = this };
        var ok = dialog.ShowDialog();
        if (ok == true)
        {
            // The dialog already applied the language change live (via
            // Strings.SetLanguage), persisted the config, and pushed the
            // autostart registration. Refresh anything that depends on
            // catalog repo / language that might not be wired through
            // events yet.
            RefreshIdlePanel();
            UpdateGameUI();
            // The tray tooltip + menu labels follow the launcher language;
            // re-localise them so the user sees their new choice if they
            // right-click the tray icon.
            RefreshTrayLabels();
            // Tray icon's visibility depends on MinimizeToTray /
            // ShowToastNotifications — both may have flipped in the
            // dialog. Recompute so the icon appears/disappears from the
            // notification area without needing a restart.
            UpdateTrayIconVisibility();
        }
    }

    // ------------------------------------------------------------------------
    // System tray — used when the user has MinimizeToTray enabled and
    // closes the main window. Code lives here (not in a service) because
    // it touches WPF Window state, dispatcher, and the TrayIcon control
    // directly — pulling it out would require routing events back through
    // the window anyway.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Set to true whenever something has decided "this Close is a real
    /// exit, not a minimize-to-tray hide". Read by <see cref="OnClosing"/>
    /// to bypass the MinimizeToTray interception. Drivers:
    ///   * Tray menu → Exit
    ///   * Game launched while CloseLauncherOnGameStart is on
    /// </summary>
    private bool _requestedHardExit;

    /// <summary>
    /// Window.OnClosing override (wired up in the constructor). When
    /// MinimizeToTray is on, swallow the close request and hide the
    /// window instead, leaving the tray icon as the way back. When off,
    /// fall through to the default close behaviour.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_requestedHardExit && _config.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        // Real close — persist size/position/tab so the next launch comes
        // up where the user left it.
        SaveWindowState();
        base.OnClosing(e);
    }

    /// <summary>
    /// Restores window dimensions, position, maximised flag and the last
    /// active tab from config. Called from the constructor right after
    /// the config loads so the window paints at the saved geometry on
    /// first frame. Off-screen saved positions get clamped to the primary
    /// screen so a vanished secondary monitor doesn't strand the window.
    /// </summary>
    private void RestoreWindowState()
    {
        if (_config.WindowWidth >= MinWidth) Width = _config.WindowWidth;
        if (_config.WindowHeight >= MinHeight) Height = _config.WindowHeight;

        if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
        {
            var screen = SystemParameters.WorkArea;
            var l = _config.WindowLeft.Value;
            var t = _config.WindowTop.Value;
            bool onScreen =
                l + Width > screen.Left + 40 &&
                l < screen.Right - 40 &&
                t + 40 < screen.Bottom &&
                t >= screen.Top - 8;
            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l;
                Top = t;
            }
        }

        if (_config.WindowMaximized)
            WindowState = WindowState.Maximized;

        _activeTab = _config.LastActiveTab switch
        {
            "Changelog" => ContentTab.Changelog,
            "Ayuda" => ContentTab.Ayuda,
            _ => ContentTab.Noticias,
        };
    }

    /// <summary>
    /// Snapshot the current window geometry + active tab into the config
    /// and flush to disk. Uses RestoreBounds when maximised so the saved
    /// W/H/Left/Top describe the un-maximised window — restoring then sets
    /// the geometry first and the maximised flag second.
    /// </summary>
    private void SaveWindowState()
    {
        var bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new System.Windows.Rect(Left, Top, Width, Height);

        if (!double.IsNaN(bounds.Width) && bounds.Width > 0)
            _config.WindowWidth = bounds.Width;
        if (!double.IsNaN(bounds.Height) && bounds.Height > 0)
            _config.WindowHeight = bounds.Height;
        if (!double.IsNaN(bounds.Left)) _config.WindowLeft = bounds.Left;
        if (!double.IsNaN(bounds.Top)) _config.WindowTop = bounds.Top;
        _config.WindowMaximized = WindowState == WindowState.Maximized;
        _config.LastActiveTab = _activeTab.ToString();

        try { _config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Save window state failed: {ex.Message}"); }
    }

    /// <summary>
    /// Hide the main window and surface the tray icon. Called from the
    /// Closing handler when MinimizeToTray is on. Idempotent — calling
    /// twice in a row is a no-op.
    /// </summary>
    private void HideToTray()
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
        RefreshTrayLabels();
    }

    /// <summary>
    /// Recomputes the tray icon's visibility from the current settings.
    /// Visible when either MinimizeToTray is on (so the user can restore
    /// after closing) or ShowToastNotifications is on (so the launcher
    /// has an icon attached to fire balloon tips from). Hidden when both
    /// are off — no point cluttering the user's tray.
    ///
    /// Safe to call anytime: it doesn't toggle a tray icon that's
    /// currently being used to keep a hidden window discoverable, because
    /// HideToTray flips Visibility to Visible directly and only "Exit"
    /// from the tray menu shuts the icon down.
    /// </summary>
    private void UpdateTrayIconVisibility()
    {
        bool keepResident = _config.MinimizeToTray || _config.ShowToastNotifications;
        // If the window is currently hidden (we're sitting in the tray),
        // never hide the icon — the user would have no way back.
        if (!IsVisible) keepResident = true;
        TrayIcon.Visibility = keepResident ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Show a system-tray balloon notification — used at the tail end of
    /// install / update operations. Suppressed when:
    ///   * The user disabled notifications in Launcher Settings.
    ///   * The main window is visible and active — the user can already
    ///     see whatever the launcher is reporting in its own UI.
    ///   * The tray icon isn't visible (because both MinimizeToTray and
    ///     ShowToastNotifications are off, so there's no surface to
    ///     attach a balloon to). In that case the user opted out.
    ///
    /// The balloon uses Windows' built-in info icon. Hardcodet's
    /// TaskbarIcon falls back to a plain notification on systems where
    /// balloons are disabled by group policy.
    /// </summary>
    private void ShowToast(string title, string message)
    {
        if (!_config.ShowToastNotifications) return;

        // If the user is looking at the launcher (window visible and not
        // minimised), they already see whatever we'd put in a toast.
        // Toasts are about catching attention away from the window.
        bool userIsLooking = IsVisible
            && WindowState != WindowState.Minimized
            && IsActive;
        if (userIsLooking) return;

        // The tray icon must be visible (== resident in the notification
        // area) for ShowBalloonTip to render — Hardcodet attaches the
        // balloon to the icon, not to the window.
        if (TrayIcon.Visibility != Visibility.Visible) return;

        try
        {
            TrayIcon.ShowBalloonTip(
                title,
                message,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        catch (Exception ex)
        {
            // ShowBalloonTip can throw on some locked-down systems where
            // notifications are policy-disabled. Silent fail — the user
            // explicitly enabled this and we don't have a better place to
            // surface the error.
            DiagnosticLog.Write($"ShowToast failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Bring the window back into view. Restores from minimised state if
    /// needed and gives it focus.
    /// </summary>
    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        // The tray icon stays visible so the user can re-hide via Close;
        // hiding it on restore would force them to discover the close-to-
        // tray flow over and over. Their preference, sticky behaviour.
    }

    /// <summary>
    /// Update the tray tooltip + context-menu labels from the current
    /// localisation table. Called on construction, language change, and
    /// after the Settings dialog closes.
    /// </summary>
    private void RefreshTrayLabels()
    {
        TrayIcon.ToolTipText = WarsOfLibertyLauncher.Localization.Strings.Get("TrayTooltip");
        TrayMenuShow.Header = WarsOfLibertyLauncher.Localization.Strings.Get("TrayMenuShow");
        TrayMenuExit.Header = WarsOfLibertyLauncher.Localization.Strings.Get("TrayMenuExit");
    }

    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
        => ShowFromTray();

    private void TrayMenuShow_Click(object sender, RoutedEventArgs e)
        => ShowFromTray();

    private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        => RequestHardExit();

    /// <summary>
    /// Tear the launcher down for real. Used by:
    ///   * Tray menu → Exit (user said "stop running, even from the tray").
    ///   * ExecutePlay when CloseLauncherOnGameStart is on (game took
    ///     over, the launcher steps aside).
    /// Sets the bypass flag so OnClosing doesn't intercept us into the
    /// tray instead. Disposes the tray icon first so Windows removes it
    /// from the notification area immediately — some shells otherwise
    /// leave a phantom icon around until the user hovers.
    /// </summary>
    private void RequestHardExit()
    {
        _requestedHardExit = true;
        try { TrayIcon.Dispose(); } catch { /* best-effort */ }
        System.Windows.Application.Current.Shutdown();
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

    /// <summary>
    /// Mod-specific UI refresh: only the bits that actually differ between
    /// profiles (window title, banner, accent-tinted PLAY button, banner
    /// image, tab/top-tab underlines that use the accent). Mod switches
    /// call this directly to skip the full ApplyLanguage cost (~40
    /// control updates and a tray-label rebuild) on what should feel
    /// like an instant tile click. ApplyLanguage calls it too as its
    /// first step, so a language change keeps the same surface.
    /// </summary>
    private void RefreshActiveModUi()
    {
        var profile = _updateService.Profile;
        Title = Strings.Format("WindowTitle", profile.DisplayName);
        ActiveModBanner.Title = profile.DisplayName.ToUpperInvariant();

        var subtitle = string.IsNullOrWhiteSpace(profile.Subtitle)
            ? Strings.Get("Subtitle")
            : profile.Subtitle;
        if (ElevationService.IsRunningAsAdmin())
            subtitle += "  " + Strings.Get("StatusRunningAsAdmin");
        ActiveModBanner.Subtitle = subtitle;

        try { ActionPanelControl.PlayButton.Background = Brush(profile.AccentColor); }
        catch { /* bad hex in a profile — keep the XAML default */ }

        RefreshActiveModBanner();
        RefreshTabsHighlight();
        RefreshTopTabHighlight();
    }

    /// <summary>Refresh every translatable string in the UI from the table.</summary>
    private void ApplyLanguage()
    {
        // Mod-dependent UI (window title, banner, accent-tinted PLAY button,
        // banner image) factored out so mod-switch can call just that
        // without paying the cost of re-localising every label in the
        // window. The split keeps ApplyLanguage as the single source of
        // truth for language changes — full re-localisation still touches
        // both halves here.
        RefreshActiveModUi();
        // Sidebar text (status box, actions, game footer, tabs)
        ModsBarLabel.Text = Strings.Get("ModsBarLabel");
        ActionPanelControl.ActionsLabel.Text = Strings.Get("ActionsLabel");
        // The "INSTALLED VERSION / LATEST AVAILABLE" labels lived in the
        // top-of-sidebar status box that was removed; the ProgressPanel
        // at the bottom now covers the same info via RefreshIdlePanel.
        MainTabsControl.NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");
        NewsLargePlaceholderText.Text = Strings.Get("NewsPlaceholder");

        // Top-level tab labels.
        TopTabPlay.Content = Strings.Get("TopTabPlay");
        TopTabMods.Content = Strings.Get("TopTabMods");
        TopTabMultiplayer.Content = Strings.Get("TopTabMultiplayer");
        TopTabNews.Content = Strings.Get("TopTabNews");
        TopTabSettings.Content = Strings.Get("TopTabSettings");

        // Placeholder copy for the v0.9 / v1.0 tabs and the Settings teaser.
        ModsBrowserTeaserText.Text = Strings.Get("ModsBrowserComingSoon");
        MultiplayerTeaserText.Text = Strings.Get("MultiplayerComingSoon");
        SettingsTeaserText.Text = Strings.Get("SettingsTabTeaser");
        OpenSettingsTabButton.Content = Strings.Get("SettingsTabOpen");

        RefreshTopTabHighlight();
        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
        ProgressPanelControl.LblOverall.Text = Strings.Get("ProgressOverall");
        // Sidebar buttons. Each one's Content is a Grid/StackPanel with an
        // icon + a named TextBlock — we update only the TextBlock so the
        // icon survives a language change. Verify/Repair/Uninstall live
        // in the gear menu now and aren't rebound here.
        ActionPanelControl.StopButton.Content = Strings.Get("BtnStop");
        ActionPanelControl.UpdateButtonText.Text = Strings.Get("BtnUpdate");
        ActionPanelControl.MoreButtonText.Text = Strings.Get("BtnConfig");
        ActionPanelControl.OpenFolderButtonText.Text = Strings.Get("BtnOpenFolder");
        LauncherSettingsButtonText.Text = Strings.Get("BtnLauncherSettings");
        // Tray labels follow the launcher language. Safe to call even
        // when the tray icon is hidden; ContextMenu lives on the XAML
        // element regardless of Visibility.
        RefreshTrayLabels();
        // Re-paint the primary button under the new locale: SetPrimaryAction
        // pulls each label from Strings, so calling it with the current
        // action key picks up the translated text.
        SetPrimaryAction(_primaryAction);
        // Tab labels (right-pane tabs)
        MainTabsControl.TabNoticias.Content = Strings.Get("TabNoticias");
        MainTabsControl.TabChangelog.Content = Strings.Get("TabChangelog");
        MainTabsControl.TabAyuda.Content = Strings.Get("TabAyuda");
        RefreshTabsHighlight();
        // Game footer + banner background tied to the active profile
        RefreshActiveModBanner();
        RefreshIdlePanel();
        // Headers (the visible label of each item)
        ActionPanelControl.UninstallMenuItem.Header = Strings.Get("MenuUninstall");
        ActionPanelControl.MenuFolders.Header = Strings.Get("MenuManagePaths");
        ActionPanelControl.MenuOpenAoE3Folder.Header = Strings.Get("MenuOpenAoE3Folder");
        ActionPanelControl.MenuSelectModFolder.Header = Strings.Format(
            "MenuSelectModFolder", _updateService.Profile.DisplayName);
        ActionPanelControl.MenuSelectAoE3Folder.Header = Strings.Get("MenuSelectAoE3Folder");
        ActionPanelControl.MenuUserData.Header = Strings.Get("MenuUserData");
        ActionPanelControl.MenuOpenUserDataFolder.Header = Strings.Get("MenuOpenUserDataFolder");
        ActionPanelControl.MenuCreateBackupNow.Header = Strings.Get("MenuCreateBackupNow");
        ActionPanelControl.MenuRestoreUserData.Header = Strings.Get("MenuRestoreUserData");
        ActionPanelControl.MenuCheckForUpdates.Header = Strings.Get("MenuCheckForUpdates");
        ActionPanelControl.MenuGameLanguage.Header = Strings.Get("MenuGameLanguage");
        ActionPanelControl.MenuRepairInstall.Header = Strings.Get("MenuRepairInstall");
        ActionPanelControl.MenuVerifyFiles.Header = Strings.Get("MenuVerifyFiles");
        ActionPanelControl.MenuViewLogs.Header = Strings.Get("MenuViewLogs");

        // Section headers — small-caps gray labels grouping items in the
        // Settings menu. Not clickable; just visual organization.
        ActionPanelControl.MenuSectionPaths.Header = Strings.Get("MenuSectionPaths");
        ActionPanelControl.MenuSectionUserData.Header = Strings.Get("MenuSectionUserData");
        ActionPanelControl.MenuSectionLanguage.Header = Strings.Get("MenuSectionLanguage");
        ActionPanelControl.MenuSectionMaintenance.Header = Strings.Get("MenuSectionMaintenance");
        ActionPanelControl.MenuSectionAdvanced.Header = Strings.Get("MenuSectionAdvanced");
        ActionPanelControl.MenuSectionDanger.Header = Strings.Get("MenuSectionDanger");

        // Tooltips on LEAF items only — items with submenus (Carpetas,
        // Datos de usuario) are self-explanatory once the submenu opens,
        // and showing a tooltip on top of the submenu just causes visual
        // conflict. Same pattern as VS Code, Notion, native OS menus.
        ActionPanelControl.MoreButton.ToolTip = BuildMenuTooltip(
            Strings.Get("TooltipSettings"), Strings.Get("TooltipSettingsBody"));
        ActionPanelControl.MenuOpenAoE3Folder.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuOpenAoE3Folder.Header, Strings.Get("TooltipMenuOpenAoE3Folder"));
        ActionPanelControl.MenuSelectModFolder.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuSelectModFolder.Header,
            Strings.Format("TooltipMenuSelectModFolder", _updateService.Profile.DisplayName));
        ActionPanelControl.MenuSelectAoE3Folder.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuSelectAoE3Folder.Header, Strings.Get("TooltipMenuSelectAoE3Folder"));
        ActionPanelControl.MenuOpenUserDataFolder.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuOpenUserDataFolder.Header, Strings.Get("TooltipMenuOpenUserDataFolder"));
        ActionPanelControl.MenuCreateBackupNow.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuCreateBackupNow.Header, Strings.Get("TooltipMenuCreateBackupNow"));
        ActionPanelControl.MenuRestoreUserData.ToolTip = BuildMenuTooltip(
            Strings.Get("MenuRestoreUserData"), Strings.Get("TooltipMenuRestoreUserData"));
        ActionPanelControl.MenuCheckForUpdates.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuCheckForUpdates.Header, Strings.Get("TooltipMenuCheckForUpdates"));
        ActionPanelControl.MenuRepairInstall.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuRepairInstall.Header, Strings.Get("TooltipMenuRepairInstall"));
        ActionPanelControl.MenuVerifyFiles.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuVerifyFiles.Header, Strings.Get("TooltipMenuVerifyFiles"));
        ActionPanelControl.MenuViewLogs.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuViewLogs.Header, Strings.Get("TooltipMenuViewLogs"));
        ActionPanelControl.UninstallMenuItem.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.UninstallMenuItem.Header, Strings.Get("TooltipMenuUninstall"));

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
        // Manual path picker invalidates the cached AoE3 detection — the
        // next StatusCard refresh re-scans so the badge flips from
        // "AoE3 not found" to ready immediately.
        InvalidateAoe3DetectedCache();
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
    /// <summary>
    /// Cached AoE3-detected check. <see cref="GameLauncher.FindAoe3Install"/>
    /// does a multi-drive + Steam VDF + GOG registry + retail registry scan
    /// that's 50-150ms on a machine where AoE3 isn't installed. AoE3 install
    /// state is stable for the launcher's lifetime so we memoise the result
    /// and invalidate explicitly on the few events that can change it
    /// (Browse AoE3, uninstall completion).
    /// </summary>
    private bool IsAoe3Detected()
    {
        return _aoe3DetectedCache ??= GameLauncher.FindAoe3Install(_config) != null;
    }

    /// <summary>
    /// Drop the AoE3-detected cache so the next call re-scans. Cheap enough
    /// to invoke after any user action that might have changed where the
    /// launcher should look (manual path picker, uninstall flow).
    /// </summary>
    private void InvalidateAoe3DetectedCache() => _aoe3DetectedCache = null;

    /// <summary>
    /// Drop the cached CheckResult for the currently active mod so the next
    /// CheckAsync runs a fresh manifest fetch / install-state probe. Called
    /// from every flow that mutates the active mod's on-disk state
    /// (install, update, uninstall, manual folder pick) and from the user's
    /// explicit "Check for updates" entry so a refresh button doesn't just
    /// re-render stale cached data.
    /// </summary>
    private void InvalidateActiveModCheckCache()
    {
        _checkResultCache.Remove(_updateService.Profile.Id);
    }

    private void RefreshStatusCard()
    {
        var profile = _updateService.Profile;
        // AoE3 detection is GLOBAL, not per-mod: we're asking "is Age of
        // Empires III installed on this machine at all?", not "does the
        // active mod's specific .exe exist?". Otherwise switching to a
        // community mod whose GameExecutable is some custom .exe (e.g. a
        // demo mod with executable="test.exe") would flip the badge to
        // "AoE3 not found" even when the user has AoE3 perfectly installed.
        // The launcher-state below (mod-installed / version / pending
        // updates) still uses mod-specific lookups; only the AoE3 badge is
        // global.
        bool aoe3Detected = IsAoe3Detected();

        // Default labels for the two version rows. The actual numbers come
        // from CurrentVersion / LatestVersion below.
        StatusCardControl.InstalledLabel = Strings.Get("StatusCardCurrentVersion");
        StatusCardControl.LatestLabel = Strings.Get("StatusCardLatestVersion");

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
        StatusCardControl.StateText = Strings.Get(stateKey);
        StatusCardControl.StateForeground = stateBrush;

        // ---- Version numbers ----
        StatusCardControl.CurrentVersion = _updateService.CurrentVersion?.Ver ?? "—";
        StatusCardControl.LatestVersion = _updateService.LatestVersion?.Ver ?? "—";

        // ---- AoE3 missing row ----
        if (!aoe3Detected)
        {
            StatusCardControl.AoE3MissingVisible = true;
            StatusCardControl.AoE3MissingMessage = Strings.Get("IdleStateGameMissing");
            StatusCardControl.BrowseAoE3ButtonContent = Strings.Get("BtnFindAoE3");
        }
        else
        {
            StatusCardControl.AoE3MissingVisible = false;
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

        ProgressPanelControl.ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Collapsed;

        // Neutral idle: gray panel chrome, gray dot icon, generic message.
        ProgressPanelControl.OuterBorder.Background = SafeBrush("#22252c", "#22252c");
        ProgressPanelControl.OuterBorder.BorderBrush = SafeBrush("#3a3d44", "#3a3d44");
        ProgressPanelControl.ProgressIcon.Text = "○";
        ProgressPanelControl.ProgressIcon.Foreground = SafeBrush("#888", "#888");
        // Idle layout: the dot stays put on the left as a decoration,
        // and the title block (title + subtitle) floats to the centre of
        // the whole panel for true visual symmetry. The Grid that holds
        // them is a single-cell overlay (see XAML), so centering the
        // title means "centred in the panel," not "centred in a column."
        // StartProgressPanel switches the title block back to Left with a
        // left-margin so the operation title sits next to the dot.
        ProgressPanelControl.ProgressTitleStack.HorizontalAlignment = HorizontalAlignment.Center;
        ProgressPanelControl.ProgressTitleStack.Margin = new Thickness(0);
        ProgressPanelControl.ProgressPanelLabel.Foreground = SafeBrush("#888", "#888");
        ProgressPanelControl.ProgressPanelLabel.Text = Strings.Get("ProgressIdleHeader");
        ProgressPanelControl.ProgressTitleText.Text = Strings.Get("ProgressIdleTitle");
        ProgressPanelControl.ProgressStepText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";
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
    /// Resolution order: cached community banner (1200×300) → built-in
    /// pack URI (used by WoL — strictly speaking it's the icon, but it
    /// fills the tile cleanly when there's no purpose-built banner) →
    /// synthetic gradient driven by the profile's accent colour, so the
    /// area always feels mod-specific even when no image is present.
    /// The TitleText/SubtitleText overlay sits on top, set elsewhere
    /// from <see cref="ApplyLanguage"/>.
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

        // Prefer the community banner if cached; otherwise the built-in
        // tile image (BannerImage). Both paths flow through TryLoadTileImage.
        var imgBrush = TryLoadTileImage(profile.LocalBannerPath ?? profile.BannerImage);
        if (imgBrush != null)
        {
            // Show the image plus a dark vignette gradient for legible text.
            ActiveModBanner.HostBackground = imgBrush;
        }
        else
        {
            ActiveModBanner.HostBackground = gradient;
        }

        // Lazy fetch: the active profile may be a community mod whose
        // banner hasn't been cached yet. Kicking the download here means
        // the next switch back to this mod (or the next RefreshModCards
        // call) will pick up the real banner.
        if (string.IsNullOrEmpty(profile.LocalBannerPath) && !string.IsNullOrEmpty(profile.BannerUrl))
            _ = EnsureModAssetsAsync(profile);
    }

    // ------------------------------------------------------------------------
    // News feed — fetched from the catalog repo's news.json on startup and
    // rendered as cards in the Noticias tab. Defensive: a failed fetch just
    // leaves the existing placeholder in place.
    // ------------------------------------------------------------------------

    private async Task RefreshNewsAsync()
    {
        try
        {
            var feed = await new NewsService().FetchAsync(_config.NewsUrl);
            await Dispatcher.InvokeAsync(() => RenderNews(feed));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RefreshNewsAsync failed: {ex.Message}");
        }
    }

    private void RenderNews(NewsFeed? feed)
    {
        MainTabsControl.NewsCardsPanel.Children.Clear();
        NewsLargeCardsPanel.Children.Clear();

        var items = feed?.Items;
        if (items == null || items.Count == 0)
        {
            MainTabsControl.NewsPlaceholderText.Visibility = Visibility.Visible;
            NewsLargePlaceholderText.Visibility = Visibility.Visible;
            return;
        }

        // Filter by active mod when the entry has a modIds list; sort
        // newest-first by PublishedAt (ISO-8601 strings sort correctly).
        var activeId = _updateService.Profile.Id;
        var filtered = items
            .Where(i => i.ModIds == null || i.ModIds.Count == 0 ||
                        i.ModIds.Exists(m => string.Equals(m, activeId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => i.PublishedAt, StringComparer.Ordinal)
            .ToList();

        if (filtered.Count == 0)
        {
            MainTabsControl.NewsPlaceholderText.Visibility = Visibility.Visible;
            NewsLargePlaceholderText.Visibility = Visibility.Visible;
            return;
        }

        MainTabsControl.NewsPlaceholderText.Visibility = Visibility.Collapsed;
        NewsLargePlaceholderText.Visibility = Visibility.Collapsed;
        foreach (var item in filtered)
        {
            // Each panel needs its own card instances — a FrameworkElement
            // can only have one parent in WPF, so we build the card twice.
            MainTabsControl.NewsCardsPanel.Children.Add(BuildNewsCard(item));
            NewsLargeCardsPanel.Children.Add(BuildNewsCard(item));
        }
    }

    private FrameworkElement BuildNewsCard(NewsItem item)
    {
        // Locale-aware title / body. Falls back to the top-level values when
        // the requested language isn't present in item.Locale.
        var lang = _config.Language ?? "en";
        var title = item.Title;
        var body = item.Body;
        if (item.Locale != null && item.Locale.TryGetValue(lang, out var loc))
        {
            if (!string.IsNullOrWhiteSpace(loc.Title)) title = loc.Title;
            if (!string.IsNullOrWhiteSpace(loc.Body)) body = loc.Body;
        }

        var stack = new System.Windows.Controls.StackPanel();

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
            TextWrapping = TextWrapping.Wrap,
        });

        var when = FormatPublishedAt(item.PublishedAt, lang);
        if (!string.IsNullOrEmpty(when))
        {
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = when,
                FontSize = 10,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 2, 0, 6),
            });
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = body,
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        // Optional "Read more" link — http/https only so a malformed feed
        // can't ship a file:// or javascript: URL that opens elsewhere.
        if (!string.IsNullOrWhiteSpace(item.Link) &&
            Uri.TryCreate(item.Link, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = Strings.Get("NewsReadMore"),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (System.Windows.Media.Brush)FindResource("InfoBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            btn.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"News link open failed: {ex.Message}");
                }
            };
            stack.Children.Add(btn);
        }

        return new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("BgPanel"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtle"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack,
        };
    }

    /// <summary>
    /// "5 days ago", "Today", or the absolute date — depending on age and
    /// whether the timestamp parsed. Falls back to the raw string when
    /// PublishedAt isn't a valid ISO-8601.
    /// </summary>
    private static string FormatPublishedAt(string isoTimestamp, string lang)
    {
        if (string.IsNullOrWhiteSpace(isoTimestamp)) return "";
        if (!DateTime.TryParse(isoTimestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var when))
        {
            return isoTimestamp;
        }

        var days = (int)(DateTime.UtcNow - when).TotalDays;
        bool es = string.Equals(lang, "es", StringComparison.OrdinalIgnoreCase);
        if (days < 1) return es ? "Hoy" : "Today";
        if (days == 1) return es ? "Ayer" : "Yesterday";
        if (days < 30) return es ? $"Hace {days} días" : $"{days} days ago";
        return when.ToLocalTime().ToString(es ? "d MMM yyyy" : "MMM d, yyyy",
            System.Globalization.CultureInfo.GetCultureInfo(es ? "es" : "en"));
    }

    // ------------------------------------------------------------------------
    // Mod assets — lazy download + cache for community-mod icons/banners.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Profile ids we've already kicked an asset fetch for in this session.
    /// Keeps us from hammering GitHub each time RefreshModCards re-runs
    /// (every mod switch, every catalog refresh) while a download is in
    /// flight or after one cleanly failed (network down, rate-limited,
    /// HTTP 404). The cache itself is durable — a second launcher session
    /// finds the file already on disk via <see cref="ModAssetCacheService"/>
    /// and skips the network entirely.
    /// </summary>
    private readonly HashSet<string> _assetFetchAttempted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves which icon URI the UI should hand to <c>TryLoadTileImage</c>
    /// for the given profile.
    ///   * Community mod with a cached icon → the local file path.
    ///   * Built-in mod (or community mod whose icon is still being fetched)
    ///     → <c>BannerImage</c>, which for built-ins is the pack URI of
    ///     the embedded .ico.
    ///   * Otherwise null → caller renders the monogram fallback.
    /// </summary>
    private static string? ResolveModIcon(ModProfile profile)
    {
        if (!string.IsNullOrEmpty(profile.LocalIconPath)
            && System.IO.File.Exists(profile.LocalIconPath))
            return profile.LocalIconPath;
        if (!string.IsNullOrEmpty(profile.BannerImage))
            return profile.BannerImage;
        return null;
    }

    /// <summary>
    /// Fire-and-forget background task that downloads any missing icon and
    /// banner for <paramref name="profile"/> into the on-disk cache, then
    /// re-renders the affected UI on completion. Idempotent on repeated
    /// calls within a session — only one fetch attempt is made per profile
    /// (see <see cref="_assetFetchAttempted"/>); the on-disk cache means
    /// subsequent launcher sessions short-circuit to the local file
    /// without hitting the network.
    ///
    /// All errors are silently swallowed (logged via DiagnosticLog).
    /// Failure leaves <c>LocalIconPath</c> / <c>LocalBannerPath</c> null
    /// and the UI stays on its monogram / gradient fallbacks — the
    /// launcher remains fully functional offline.
    /// </summary>
    private async Task EnsureModAssetsAsync(ModProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Id)) return;
        // Don't pile up parallel fetches for the same profile if
        // BuildModCard/RefreshActiveModBanner both fire it within a
        // single session, or if RefreshModCards re-runs while a fetch
        // is still in flight.
        if (!_assetFetchAttempted.Add(profile.Id)) return;

        var cache = new ModAssetCacheService();
        bool changed = false;

        try
        {
            if (string.IsNullOrEmpty(profile.LocalIconPath)
                && !string.IsNullOrEmpty(profile.IconUrl))
            {
                var path = await cache.GetIconPathAsync(profile.Id, profile.IconUrl);
                if (!string.IsNullOrEmpty(path))
                {
                    profile.LocalIconPath = path;
                    changed = true;
                }
            }

            if (string.IsNullOrEmpty(profile.LocalBannerPath)
                && !string.IsNullOrEmpty(profile.BannerUrl))
            {
                var path = await cache.GetBannerPathAsync(profile.Id, profile.BannerUrl);
                if (!string.IsNullOrEmpty(path))
                {
                    profile.LocalBannerPath = path;
                    changed = true;
                }
            }
        }
        catch (Exception ex)
        {
            // ModAssetCacheService catches its own HTTP/IO errors and
            // returns null; this branch is for anything truly unexpected.
            DiagnosticLog.Write($"EnsureModAssets '{profile.Id}': {ex.Message}");
            return;
        }

        if (!changed) return;

        // Bring the new assets to the screen. We may not be on the UI
        // thread (the await above hops off), so go through the dispatcher.
        // Refreshing the cards re-runs BuildModCard for every profile,
        // including the one whose icon just landed; refreshing the active
        // banner is only needed if the just-fetched mod is the active one.
        await Dispatcher.InvokeAsync(() =>
        {
            RefreshModCards();
            if (string.Equals(_updateService.Profile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
                RefreshActiveModBanner();
        });
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

        Paint(MainTabsControl.TabNoticias, _activeTab == ContentTab.Noticias);
        Paint(MainTabsControl.TabChangelog, _activeTab == ContentTab.Changelog);
        Paint(MainTabsControl.TabAyuda, _activeTab == ContentTab.Ayuda);
    }

    private enum ContentTab { Noticias, Changelog, Ayuda }
    private ContentTab _activeTab = ContentTab.Noticias;

    // ------------------------------------------------------------------------
    // Top-level tabs (Play / Mods / Multiplayer / News / Settings). Active
    // tab toggles which view Grid is Visible. Play wraps the original layout
    // so the existing flow is unchanged when the user lands there.
    // ------------------------------------------------------------------------

    private enum TopTab { Play, Mods, Multiplayer, News, Settings }
    private TopTab _activeTopTab = TopTab.Play;

    private void TopTabPlay_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Play);
    private void TopTabMods_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Mods);
    private void TopTabMultiplayer_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Multiplayer);
    private void TopTabNews_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.News);
    private void TopTabSettings_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Settings);

    private void SwitchTopTab(TopTab tab)
    {
        _activeTopTab = tab;
        PlayView.Visibility = tab == TopTab.Play ? Visibility.Visible : Visibility.Collapsed;
        ModsBrowserView.Visibility = tab == TopTab.Mods ? Visibility.Visible : Visibility.Collapsed;
        MultiplayerView.Visibility = tab == TopTab.Multiplayer ? Visibility.Visible : Visibility.Collapsed;
        NewsLargeView.Visibility = tab == TopTab.News ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tab == TopTab.Settings ? Visibility.Visible : Visibility.Collapsed;
        RefreshTopTabHighlight();
    }

    /// <summary>
    /// Paints the underline + brighter foreground on the active top tab,
    /// mirroring RefreshTabsHighlight's approach but using the active mod's
    /// accent so the colour follows the theme/profile.
    /// </summary>
    private void RefreshTopTabHighlight()
    {
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var dim = Brush("#888");
        var transparent = System.Windows.Media.Brushes.Transparent;

        void Paint(System.Windows.Controls.Button btn, bool active)
        {
            btn.Foreground = active ? Brush("#fff") : dim;
            if (btn.Template?.FindName("border", btn) is System.Windows.Controls.Border b)
                b.BorderBrush = active ? accent : transparent;
        }

        Paint(TopTabPlay, _activeTopTab == TopTab.Play);
        Paint(TopTabMods, _activeTopTab == TopTab.Mods);
        Paint(TopTabMultiplayer, _activeTopTab == TopTab.Multiplayer);
        Paint(TopTabNews, _activeTopTab == TopTab.News);
        Paint(TopTabSettings, _activeTopTab == TopTab.Settings);
    }

    /// <summary>
    /// "Open settings" button inside the Settings top-tab. Routes through
    /// the existing LauncherSettingsButton_Click so the dialog wiring (and
    /// the refresh-after-close logic) stays in one place.
    /// </summary>
    private void OpenSettingsTabButton_Click(object sender, RoutedEventArgs e)
    {
        LauncherSettingsButton_Click(sender, e);
    }

    private void TabNoticias_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Noticias);
    private void TabChangelog_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Changelog);
    private void TabAyuda_Click(object sender, RoutedEventArgs e) => SwitchContentTab(ContentTab.Ayuda);

    private void SwitchContentTab(ContentTab tab)
    {
        _activeTab = tab;
        MainTabsControl.NoticiasContent.Visibility = tab == ContentTab.Noticias ? Visibility.Visible : Visibility.Collapsed;
        MainTabsControl.ChangelogContent.Visibility = tab == ContentTab.Changelog ? Visibility.Visible : Visibility.Collapsed;
        MainTabsControl.AyudaContent.Visibility = tab == ContentTab.Ayuda ? Visibility.Visible : Visibility.Collapsed;

        // Lazy-fill changelog and help so an empty profile shows a friendly
        // message instead of nothing.
        if (tab == ContentTab.Changelog)
            MainTabsControl.ChangelogText.Text = Strings.Get("ChangelogPlaceholder");
        else if (tab == ContentTab.Ayuda)
            MainTabsControl.HelpText.Text = Strings.Get("HelpDefaultBody");

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

        var profile = _updateService.Profile;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Format("DlgFolderPickerTitle", profile.DisplayName),
            Multiselect = false
        };

        if (!string.IsNullOrEmpty(_updateService.InstallPath)
            && Directory.Exists(_updateService.InstallPath))
        {
            dialog.InitialDirectory = _updateService.InstallPath;
        }

        if (dialog.ShowDialog(this) != true) return;

        var chosen = dialog.FolderName.TrimEnd('\\', '/');

        // Be tolerant: if the user picked the AoE3 root by mistake, try a
        // mod-named subfolder inside it before failing. The candidate names
        // come from the active profile, not from hardcoded WoL strings, so
        // this works for any mod whose install convention is a separate
        // folder under AoE3. "Validity" of a candidate is verified by the
        // mod's own probe file (e.g. age3y.exe / age3m.exe / etc.).
        var candidates = new List<string> { chosen };
        if (!string.IsNullOrEmpty(profile.DisplayName))
            candidates.Add(Path.Combine(chosen, profile.DisplayName));
        var defaultLeaf = Path.GetFileName(
            profile.DefaultInstallFolder?.TrimEnd('\\', '/') ?? "");
        if (!string.IsNullOrEmpty(defaultLeaf)
            && !candidates.Contains(Path.Combine(chosen, defaultLeaf),
                StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(chosen, defaultLeaf));
        }

        string? resolved = null;
        foreach (var candidate in candidates)
        {
            if (LooksLikeModInstall(candidate, profile))
            {
                resolved = candidate.TrimEnd('\\', '/');
                break;
            }
        }

        if (resolved == null)
        {
            MessageBox.Show(this,
                Strings.Format(
                    "DlgInvalidFolderBody",
                    profile.DisplayName,
                    string.IsNullOrEmpty(profile.InstallProbeFile)
                        ? "(unknown probe file)"
                        : profile.InstallProbeFile),
                Strings.Get("DlgInvalidFolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.GetActiveState().InstallPath = resolved;
        _config.Save();
        InvalidateActiveModCheckCache();
        await CheckAsync();
    }

    /// <summary>
    /// Lightweight "is this folder an install of the given mod" check used
    /// by the manual folder picker. Primary signal is the profile's probe
    /// file — same one the install pipeline writes and the uninstaller
    /// looks for. For WoL specifically (the only mod that also has an Inno
    /// Setup registry footprint) we fall back to <see cref="RegistryService.IsValidInstall"/>
    /// so existing legacy installs without the modern probe file still
    /// resolve.
    /// </summary>
    private static bool LooksLikeModInstall(string candidate, ModProfile profile)
    {
        if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            return false;

        if (!string.IsNullOrEmpty(profile.InstallProbeFile)
            && File.Exists(Path.Combine(candidate, profile.InstallProbeFile)))
        {
            return true;
        }

        // Backwards-compat for WoL installs from the Inno-installer era,
        // which may predate the probe-file-based detection. Other mods don't
        // touch this registry path so the check is cheap and silent for them.
        if (string.Equals(profile.Id, ModRegistry.WolId, StringComparison.OrdinalIgnoreCase)
            && RegistryService.IsValidInstall(candidate))
        {
            return true;
        }

        return false;
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
            // User pressed the refresh action — force a fresh manifest fetch
            // rather than replaying whatever's cached.
            InvalidateActiveModCheckCache();
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
        ProgressPanelControl.PatchProgress.IsIndeterminate = true;
        ProgressPanelControl.OverallProgress.IsIndeterminate = true;
        SetStatus(Strings.Get("StatusVerifying"));

        try
        {
            var verifyProfile = _updateService.Profile;
            var result = await Task.Run(
                () => VerifyInstallation(_updateService.InstallPath, verifyProfile));
            ProgressPanelControl.PatchProgress.IsIndeterminate = false;
            ProgressPanelControl.OverallProgress.IsIndeterminate = false;
            ProgressPanelControl.PatchProgress.Value = 100;
            ProgressPanelControl.OverallProgress.Value = 100;

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

        // Resolve payload via the same helper InstallAsync uses so Repair
        // works for every mechanism the launcher can install (WolPatcher,
        // GitHubReleases). DelegatedExternal / Manual mods hit the empty-
        // URL branch and surface "no install URL" — the menu gating in
        // ApplyMenuVisibility hides Repair for them anyway, this is just
        // belt-and-braces.
        var payload = await ResolvePayloadUrlsAsync();
        if (payload == null) return;
        var payloadUrls = payload.Urls;
        var payloadSha256 = payload.Sha256;

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
        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressBarDownload");
        ProgressPanelControl.PatchProgress.Value = 0;
        ProgressPanelControl.OverallProgress.Value = 0;
        ProgressPanelControl.PatchBytesText.Text = "";
        ProgressPanelControl.OverallBytesText.Text = "";

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
                    ProgressPanelControl.PatchProgress.Value = p.Percentage;
                    ProgressPanelControl.OverallProgress.Value = p.Percentage * 0.6;
                    ProgressPanelControl.PatchBytesText.Text = $"{p.Percentage:0.0}%";
                    ProgressPanelControl.OverallBytesText.Text = $"{FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)}";
                    ProgressPanelControl.EtaText.Text = eta.HasValue
                        ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
                        : "";
                }
                else
                {
                    ProgressPanelControl.PatchBytesText.Text = FormatBytes(p.BytesReceived);
                }
                ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format("ProgressSpeed", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Format(
                    "StatusDownloadingInstaller", _updateService.Profile.DisplayName);
            });

            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                ProgressPanelControl.LblCurrentPatch.Text = s;
            });

            // Mod-only install on top of existing (overwrites damaged files).
            // Repair re-stamps the manifest with the version we just verified.
            // No phase reporter here — repair doesn't show the breadcrumb.
            await nativeInstaller.InstallModOnlyAsync(
                _updateService.Profile,
                ResolveInstallVersion(),
                payloadUrls,
                installPath,
                dlProgress,
                statusProgress,
                phaseProgress: null,
                extractProgress: null,
                overlayProgress: null,
                payloadSha256: payloadSha256,
                ct: _cts.Token);

            ProgressPanelControl.PatchProgress.Value = 100;
            ProgressPanelControl.OverallProgress.Value = 100;

            // Re-verify (profile passed so non-WoL repairs don't get false
            // positives on the WoL-specific markers).
            var recheckProfile = _updateService.Profile;
            var recheck = await Task.Run(() => VerifyInstallation(installPath, recheckProfile));
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
        // Only a real (non-checkOnly) busy state blocks re-entry. Rapid mod
        // switches need to start a fresh CheckAsync even if the previous
        // mod's CheckAsync hasn't fully unwound yet — the writeback guard
        // below skips stale results so the second call wins safely.
        if (_isBusy && !_isCheckOnly) return;
        // Snapshot the active profile so a mod switch mid-await can be
        // detected before we write check results back to UI fields that
        // would otherwise belong to the new mod.
        var profileAtStart = _updateService.Profile.Id;

        // Fast path: if we've already run a full check for this mod in this
        // session, replay the cached result synchronously. Skips the 1-2s
        // network round-trip on every revisit so rapid mod switching is
        // genuinely instant after the first visit to each mod.
        if (_checkResultCache.TryGetValue(profileAtStart, out var cached))
        {
            // Sanity: drop the cache entry if the on-disk state visibly
            // disagrees (cache said "installed" but the new UpdateService's
            // constructor fast-path didn't find the install — user moved
            // or deleted files since). Falling through to the network
            // path re-discovers the truth.
            bool diskAgrees = !cached.IsValidInstall
                || !string.IsNullOrEmpty(_updateService.InstallPath);
            if (diskAgrees)
            {
                DiagnosticLog.Write($"CheckAsync cache hit for '{profileAtStart}'.");
                ApplyCheckResult(cached);
                return;
            }
            DiagnosticLog.Write(
                $"CheckAsync cache evicted for '{profileAtStart}': install path missing on disk.");
            _checkResultCache.Remove(profileAtStart);
        }

        SetBusy(true, checkOnly: true);
        _cts = new CancellationTokenSource();

        try
        {
            var statusReporter = new Progress<string>(SetStatus);
            var result = await _updateService.CheckAsync(statusReporter, _cts.Token);

            if (!string.Equals(_updateService.Profile.Id, profileAtStart, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write(
                    $"CheckAsync stale: started for '{profileAtStart}', " +
                    $"active is now '{_updateService.Profile.Id}'. Skipping result writeback.");
                return;
            }

            // Cache so the next visit to this mod is sync.
            _checkResultCache[profileAtStart] = result;
            ApplyCheckResult(result);
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

    /// <summary>
    /// Writes a CheckResult's data into all the UI controls + state fields
    /// that depend on it. Pulled out of CheckAsync so the cached-replay
    /// fast path and the post-network slow path share exactly the same
    /// rendering logic — no chance of drift between the two.
    /// </summary>
    private void ApplyCheckResult(UpdateService.CheckResult result)
    {
        InstallPathText.Text = _updateService.InstallPath ?? "(not detected)";

        _modIsInstalled = result.IsValidInstall;

            // Non-WoL-style mods don't have the WoL updater pipeline (version
            // detection, UpdateInfo.xml, .tar.xz patches), so we short-circuit
            // before any of the WoL-specific branching. Inside this branch the
            // Install button's enabled state depends on whether the launcher
            // can actually perform the install:
            //
            //   * GitHubReleases  — yes, the launcher downloads the modder's
            //                       pinned release .zip and overlays it. The
            //                       Install button is enabled and routes
            //                       through InstallGitHubReleasesAsync.
            //   * DelegatedExternal / Manual — no, the mod's own installer or
            //                       a manual download is the right channel.
            //                       Install is shown disabled so the user
            //                       knows it's not actionable from here.
            //
            // Verify/Repair are gated by _modIsInstalled inside MoreButton_Click
            // — we don't touch their visibility here.
        if (_updateService.Profile.UpdateMechanism != ModUpdateMechanism.WolPatcher)
        {
            ActionPanelControl.UpdateButton.Visibility = Visibility.Collapsed;
            RefreshIdlePanel();

            bool launcherCanInstall =
                _updateService.Profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases;

            if (result.IsValidInstall)
            {
                SetPrimaryAction(PrimaryAction.Play);
                SetStatus(Strings.Format(
                    "StatusReadyExternalUpdates", _updateService.Profile.DisplayName));
            }
            else if (launcherCanInstall)
            {
                SetPrimaryAction(PrimaryAction.Install);
                SetStatus(Strings.Get("StatusNotInstalled"));
            }
            else
            {
                SetPrimaryAction(PrimaryAction.Install, enabled: false);
                SetStatus(Strings.Format(
                    "StatusModNotInstalledExternal", _updateService.Profile.DisplayName));
            }

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
            ActionPanelControl.UpdateButton.Visibility = Visibility.Collapsed;
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
                Strings.Format(
                    "DlgBrokenInstallBody",
                    _updateService.InstallPath,
                    _updateService.Profile.DisplayName),
                Strings.Format(
                    "DlgBrokenInstallTitle", _updateService.Profile.DisplayName),
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
        ActionPanelControl.UpdateButton.Visibility = (versionKnown && _pendingDownloads.Count > 0)
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
                    _updateService.Profile.DisplayName,
                    result.CurrentVersion?.Ver,
                    result.LatestVersion?.Ver,
                    _updateService.Profile.OfficialWebsite));
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
            ProgressPanelControl.PauseButton.Content = Strings.Get("BtnResume");
            SetStatus(Strings.Get("StatusPaused"));
        }
        else
        {
            ProgressPanelControl.PauseButton.Content = Strings.Get("BtnPause");
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
            ProgressPanelControl.PauseButton.Content = Strings.Get("BtnPause");
            ProgressPanelControl.CancelButton.Content = Strings.Get("BtnCancel");
            // Pause + Cancel live inside the progress panel now. We toggle
            // the whole row; the individual button visibilities don't need
            // to be touched (they're always Visible inside the row).
            ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Visible;
        }
        else
        {
            ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Collapsed;
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
        ProgressPanelControl.ProgressTitleStack.HorizontalAlignment = HorizontalAlignment.Left;
        ProgressPanelControl.ProgressTitleStack.Margin = new Thickness(20, 0, 0, 0);

        ProgressPanelControl.ProgressPanelLabel.Text = LabelKeyForOperation(op);
        ProgressPanelControl.ProgressTitleText.Text = title;
        ProgressPanelControl.ProgressSubtitleText.Text = subtitle;
        ProgressPanelControl.ProgressStepText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";

        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get(bar1Label ?? "ProgressBarDownload");
        ProgressPanelControl.LblOverall.Text = Strings.Get(bar2Label ?? "ProgressBarInstall");
        ProgressPanelControl.PatchBytesText.Text = "";
        ProgressPanelControl.OverallBytesText.Text = "";
        ProgressPanelControl.PatchProgress.Value = 0;
        ProgressPanelControl.OverallProgress.Value = 0;
        ProgressPanelControl.PatchProgress.IsIndeterminate = false;
        ProgressPanelControl.OverallProgress.IsIndeterminate = false;

        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Visible;
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Visibility = Visibility.Collapsed;
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
        ProgressPanelControl.ProgressTitleText.Text = Strings.Get(headlineKey);
        ProgressPanelControl.ProgressSubtitleText.Text = detail ?? "";
        ProgressPanelControl.ProgressStepText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";

        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#1f3a1f", "#3a8c3a", "#9bd99b");
        ProgressPanelControl.ProgressMessageText.Text = Strings.Get("ProgressCompletedMessage");
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Visible;

        ProgressPanelControl.ProgressActionsRow.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Visibility = Visibility.Collapsed;

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
        ProgressPanelControl.ProgressTitleText.Text = Strings.Get("ProgressTitleError");
        ProgressPanelControl.ProgressSubtitleText.Text = "";
        ProgressPanelControl.ProgressStepText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";

        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#3a1a1a", "#8c3a3a", "#e63950");
        ProgressPanelControl.ProgressMessageText.Text = errorMessage;
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Visible;

        bool canRetry = _progressRetryAction != null;
        ProgressPanelControl.ProgressActionsRow.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Content = Strings.Get("BtnRetry");

        ScheduleAutoRevert(ErrorHoldSeconds);
    }

    /// <summary>
    /// Transitions the panel to "Cancelled" — yellow banner + Retry
    /// button. Same auto-revert behaviour as Error.
    /// </summary>
    private void ShowProgressCancelled()
    {
        _progressState = ProgressState.Cancelled;
        ProgressPanelControl.ProgressTitleText.Text = Strings.Get("ProgressTitleCancelled");
        ProgressPanelControl.ProgressSubtitleText.Text = "";
        ProgressPanelControl.ProgressStepText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";

        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressRunningActions.Visibility = Visibility.Collapsed;
        PaintProgressMessage("#3a2a1a", "#8c6c3a", "#d4a04a");
        ProgressPanelControl.ProgressMessageText.Text = Strings.Get("ProgressCancelledMessage");
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Visible;

        bool canRetry = _progressRetryAction != null;
        ProgressPanelControl.ProgressActionsRow.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanelControl.ProgressActionRetry.Content = Strings.Get("BtnRetry");

        ScheduleAutoRevert(ErrorHoldSeconds);
    }

    private void PaintProgressMessage(string bg, string border, string fg)
    {
        ProgressPanelControl.ProgressMessagePanel.Background = SafeBrush(bg, "#1f3a1f");
        ProgressPanelControl.ProgressMessagePanel.BorderBrush = SafeBrush(border, "#3a8c3a");
        ProgressPanelControl.ProgressMessagePanel.BorderThickness = new Thickness(1);
        ProgressPanelControl.ProgressMessageText.Foreground = SafeBrush(fg, "#9bd99b");
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
        ProgressPanelControl.ProgressBarsGroup.Visibility = Visibility.Visible;
        ProgressPanelControl.ProgressMessagePanel.Visibility = Visibility.Collapsed;
        ProgressPanelControl.ProgressActionsRow.Visibility = Visibility.Collapsed;
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
        ProgressPanelControl.OuterBorder.Background = SafeBrush(tone.Background, "#1a2a3a");
        ProgressPanelControl.OuterBorder.BorderBrush = SafeBrush(tone.Border, "#3a8cd9");
        ProgressPanelControl.ProgressPanelLabel.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        ProgressPanelControl.PatchProgress.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        ProgressPanelControl.OverallProgress.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
        ProgressPanelControl.ProgressIcon.Text = tone.Icon;
        ProgressPanelControl.ProgressIcon.Foreground = SafeBrush(tone.Accent, "#5b9bd5");
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
    /// <summary>
    /// Sanity-checks a mod installation on disk. Two layers:
    ///
    ///   * <b>Generic</b> (every profile): the mod's
    ///     <see cref="ModProfile.InstallProbeFile"/> must exist, and a random
    ///     sample of content files must not be zero-byte.
    ///   * <b>WoL-specific</b> (only when the active profile uses
    ///     <see cref="ModUpdateMechanism.WolPatcher"/>): the legacy markers
    ///     this verifier was originally written for — <c>art\zulushield\</c>,
    ///     <c>data\*.bar</c>, <c>sound\</c>, <c>AI3\</c>.
    ///
    /// For non-WoL mods we skip the WoL layer instead of reporting false
    /// positives (those folders don't exist in e.g. an Improvement Mod
    /// install on top of vanilla AoE3).
    /// </summary>
    private static VerifyResult VerifyInstallation(string installPath, ModProfile profile)
    {
        var missing = new List<string>();
        var corrupt = new List<string>();
        int totalChecked = 0;

        // --- Generic: probe file ---
        if (!string.IsNullOrEmpty(profile.InstallProbeFile))
        {
            totalChecked++;
            var probe = Path.Combine(installPath, profile.InstallProbeFile);
            if (!File.Exists(probe))
                missing.Add(profile.InstallProbeFile);
        }

        // --- WoL-specific layer ---
        // Only applied when the mod uses the WoL-style updater. Skipped for
        // GitHubReleases / DelegatedExternal mods that don't ship the WoL
        // file layout (zulushield, .bar archives, etc.).
        bool isWolStyle = profile.UpdateMechanism == ModUpdateMechanism.WolPatcher;
        if (isWolStyle)
        {
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
        }

        // --- Generic: spot-check zero-byte content files (random sample) ---
        // Applies to every mod — a content file at 0 bytes is almost always
        // a broken download/extract regardless of which mod produced it.
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
    /// Output of <see cref="ResolvePayloadUrlsAsync"/>. <see cref="Sha256"/>
    /// is a parallel array to <see cref="Urls"/> (same length) when any
    /// hash is pinned; null when no hashes are available for this
    /// mechanism. Individual array slots may be empty strings if only
    /// some parts have a pin. <see cref="NativeInstallService"/>
    /// skips verification for empty slots, so passing this through
    /// is safe even for legacy paths that don't ship hashes.
    /// </summary>
    private record PayloadResolution(string[] Urls, string[]? Sha256);

    /// <summary>
    /// Resolves the download URLs for the active mod's install/repair payload.
    /// Shared by <see cref="InstallAsync"/> and <see cref="RepairInstallAsync"/>
    /// so both flows handle every mechanism identically.
    ///
    /// On failure the helper sets a user-facing status and returns null —
    /// callers should bail (no return-bool noise needed).
    /// </summary>
    private async Task<PayloadResolution?> ResolvePayloadUrlsAsync()
    {
        var profile = _updateService.Profile;
        if (profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases)
        {
            var ghs = profile.GitHubReleases;
            if (ghs == null
                || string.IsNullOrEmpty(ghs.SourceRepo)
                || string.IsNullOrEmpty(ghs.ApprovedReleaseTag))
            {
                SetStatus(Strings.Get("DlgInstallNoUrlBody"));
                return null;
            }
            try
            {
                var asset = await new GitHubReleaseDownloader()
                    .ResolveAssetAsync(ghs, default);
                // External-hosting path carries a SHA-256 pin (required by
                // ResolveAssetAsync itself). Regular GitHub-asset path
                // carries no SHA — we trust the asset CDN, like before.
                var shas = asset.ExpectedSha256 != null
                    ? new[] { asset.ExpectedSha256 }
                    : null;
                return new PayloadResolution(new[] { asset.Url }, shas);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                DiagnosticLog.Write($"GitHubReleases asset resolution failed: {ex}");
                return null;
            }
        }

        // Non-GitHubReleases path: legacy WoL multipart URLs from the profile
        // or LauncherConfig.InstallerZipUrl as a last-resort override.
        // SHA-256 is not yet wired through the WolPatcher catalog entries
        // surfaced here — keeping it null preserves the previous behaviour
        // for WoL while leaving the door open for a follow-up that lifts
        // ModCatalogWolSettings.PayloadSha256 through ModProfile.
        var payloadUrls = _updateService.EffectivePayloadZipUrls();
        if (payloadUrls != null && payloadUrls.Length > 0)
            return new PayloadResolution(payloadUrls, null);

        if (!string.IsNullOrWhiteSpace(_config.InstallerZipUrl))
            return new PayloadResolution(new[] { _config.InstallerZipUrl }, null);

        SetStatus(Strings.Get("DlgInstallNoUrlBody"));
        return null;
    }

    /// <summary>
    /// Picks the right version string to stamp into the install manifest /
    /// registry based on the active profile's update mechanism. Shared by
    /// <see cref="InstallAsync"/> and <see cref="RepairInstallAsync"/>.
    /// </summary>
    private string ResolveInstallVersion()
    {
        var profile = _updateService.Profile;
        return profile.UpdateMechanism switch
        {
            ModUpdateMechanism.GitHubReleases =>
                profile.GitHubReleases?.ApprovedReleaseTag ?? "",
            _ => _updateService.CurrentVersion?.Ver
                ?? _updateService.LatestVersion?.Ver ?? "",
        };
    }

    /// <summary>
    /// Full first-time install flow (native — no Inno Setup):
    ///   1. Shows a styled dialog to pick destination folder
    ///   2. Downloads the mod's payload (multipart for WoL, single asset for
    ///      GitHubReleases — see <see cref="ResolvePayloadUrlsAsync"/>)
    ///   3. Clones AoE3 to the destination (if detected)
    ///   4. Copies the mod's files on top
    ///   5. Creates shortcuts + registry entries
    ///   6. Verifies the installation
    /// </summary>
    private async Task InstallAsync()
    {
        if (_isBusy) return;

        // Check if game is running first
        if (!EnsureGameNotRunning()) return;

        var payload = await ResolvePayloadUrlsAsync();
        if (payload == null) return;
        var payloadUrls = payload.Urls;
        var payloadSha256 = payload.Sha256;

        // Detect AoE3
        var aoe3Installs = AoE3Detector.FindAll();
        string? aoe3SourcePath = null;
        string? aoe3SourceLabel = null;

        if (aoe3Installs.Count > 0)
        {
            // Use ModRoot (full AoE3 install root) as the clone source, NOT
            // GameFolder. For Steam, GameFolder is the `bin\` subfolder which
            // contains only the executable — cloning that would skip data\,
            // sound\, art\ and the rest of the game files the mod needs.
            aoe3SourcePath = aoe3Installs[0].ModRoot;
            aoe3SourceLabel = aoe3Installs[0].Source;
        }

        // Default destination depends on InstallType:
        //   * InPlaceOverlay → the AoE3 folder itself (mod files extracted on
        //                      top of vanilla AoE3).
        //   * IsolatedFolder → in priority order:
        //       1. the path the user installed THIS mod to last time (per-mod
        //          state — remembers their pick across reinstalls), as long
        //          as it still looks like a real install of this mod;
        //       2. <detected AoE3 folder>\<DisplayName> — the same convention
        //          for every IsolatedFolder mod (e.g. "…\Age Of Empires 3\
        //          Wars of Liberty"), keeping mod installs alongside the base
        //          game they're cloning;
        //       3. the profile's DefaultInstallFolder, only when AoE3 isn't
        //          detected (rare — IsolatedFolder needs AoE3 to clone anyway,
        //          so this is just a sensible last hint);
        //       4. <DisplayName> as a final fallback.
        // Deliberately NOT using LauncherConfig.DefaultInstallFolder — that
        // legacy setting is WoL-specific and would leak the WoL path into
        // unrelated mod installs.
        // The user can still override the suggested folder in the dialog.
        string suggestedFolder;
        var profile = _updateService.Profile;
        if (profile.InstallType == ModInstallType.InPlaceOverlay)
        {
            suggestedFolder = aoe3SourcePath
                ?? (string.IsNullOrEmpty(profile.DefaultInstallFolder)
                    ? profile.DisplayName
                    : profile.DefaultInstallFolder);
        }
        else
        {
            var lastInstallPath = _config.GetState(profile.Id).InstallPath;

            // Defensive validation: only trust the per-mod cached install path
            // if its leaf folder looks like THIS mod's folder (DisplayName or
            // DefaultInstallFolder leaf). Otherwise the cached value may be a
            // vanilla-AoE3 leak (e.g. the Steam "...\Age Of Empires 3\bin")
            // saved by an earlier detection that fell through to the AoE3
            // fallback candidates. Suggesting that as the install destination
            // for an IsolatedFolder mod would overwrite the user's AoE3.
            string[] expectedLeafs = new[]
            {
                profile.DisplayName,
                Path.GetFileName(profile.DefaultInstallFolder?.TrimEnd('\\', '/') ?? ""),
            };
            bool lastIsTrustworthy =
                !string.IsNullOrEmpty(lastInstallPath)
                && expectedLeafs.Any(leaf =>
                    !string.IsNullOrEmpty(leaf)
                    && string.Equals(
                        Path.GetFileName(lastInstallPath.TrimEnd('\\', '/')),
                        leaf,
                        StringComparison.OrdinalIgnoreCase));

            if (lastIsTrustworthy)
            {
                suggestedFolder = lastInstallPath;
            }
            else if (!string.IsNullOrEmpty(aoe3SourcePath))
            {
                // Uniform convention for every IsolatedFolder mod: install
                // INSIDE the detected AoE3 folder, in a subfolder named
                // after the mod. Overrides any profile.DefaultInstallFolder
                // the modder may have set in the catalog — the launcher's
                // policy is "the clone source's parent is the right home for
                // the cloned copy." Modders who really need an absolute path
                // elsewhere would need an explicit opt-in flag (not added
                // yet — agree on the use case before exposing it).
                suggestedFolder = Path.Combine(aoe3SourcePath, profile.DisplayName);
            }
            else if (!string.IsNullOrEmpty(profile.DefaultInstallFolder))
            {
                suggestedFolder = profile.DefaultInstallFolder;
            }
            else
            {
                suggestedFolder = profile.DisplayName;
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
        var dialog = new InstallFolderDialog(
            suggestedFolder, aoe3SourcePath, aoe3SourceLabel,
            _updateService.Profile.DisplayName)
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

        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressBarDownload");
        ProgressPanelControl.PatchProgress.Value = 0;
        ProgressPanelControl.OverallProgress.Value = 0;
        ProgressPanelControl.PatchBytesText.Text = "";
        ProgressPanelControl.OverallBytesText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";

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
                    ProgressPanelControl.PatchProgress.Value = p.Percentage;
                    ProgressPanelControl.OverallProgress.Value = (p.Percentage / 100.0) * weightDownload;
                    ProgressPanelControl.PatchBytesText.Text =
                        $"{FormatBytes(p.BytesReceived)} / {FormatBytes(p.TotalBytes)}";
                    ProgressPanelControl.EtaText.Text = eta.HasValue
                        ? Strings.Format("ProgressEta", FormatDuration(eta.Value))
                        : (speed.BytesPerSecond > 0
                            ? Strings.Format("ProgressEta", Strings.Get("ProgressEtaCalculating"))
                            : "");
                }
                else
                {
                    ProgressPanelControl.PatchProgress.Value = 0;
                    ProgressPanelControl.PatchBytesText.Text = FormatBytes(p.BytesReceived);
                    ProgressPanelControl.EtaText.Text = "";
                }
                ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";

                ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
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
                ProgressPanelControl.PatchProgress.Value = pct;
                ProgressPanelControl.OverallProgress.Value = weightDownload + (pct / 100.0) * weightExtract;
                ProgressPanelControl.PatchBytesText.Text = $"{p.EntriesDone}/{p.EntriesTotal} files";
                ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Format("StatusExtractingPayload", p.EntriesDone, p.EntriesTotal);
                ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Clone progress — fires while AoE3 files are being cloned.
            var cloneProgress = new Progress<CloneProgress>(p =>
            {
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesCopied / p.BytesTotal * 100.0
                    : 0;
                ProgressPanelControl.PatchProgress.Value = pct;
                ProgressPanelControl.OverallProgress.Value = weightDownload + weightExtract + (pct / 100.0) * weightClone;
                ProgressPanelControl.PatchBytesText.Text =
                    $"{FormatBytes(p.BytesCopied)} / {FormatBytes(p.BytesTotal)}";
                ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";
                // Show "💾 <relative file path>" so the line stays consistent
                // with the emoji-prefixed status used by other phases.
                var displayFile = p.CurrentFile.Length > 80
                    ? "..." + p.CurrentFile[^80..]
                    : p.CurrentFile;
                ProgressPanelControl.LblCurrentPatch.Text = $"💾 {displayFile}";
                ProgressPanelControl.SpeedText.Text = p.BytesPerSecond > 0
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
                ProgressPanelControl.PatchProgress.Value = pct;
                ProgressPanelControl.OverallProgress.Value =
                    weightDownload + weightExtract + weightClone + (pct / 100.0) * weightOverlay;
                ProgressPanelControl.PatchBytesText.Text = $"{p.FilesDone}/{p.FilesTotal} files";
                ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Format("StatusInstallingMod", p.FilesDone, p.FilesTotal);
                ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format(SpeedLabelKeyForPhase(_currentInstallPhase),
                        FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Status updates
            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                ProgressPanelControl.LblCurrentPatch.Text = s;
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
                ProgressPanelControl.SpeedText.Text = "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Wire up pause to native installer
            _cloneService = nativeInstaller.CloneService;

            var installVersion = ResolveInstallVersion();

            if (aoe3SourcePath != null)
            {
                // Full install: clone AoE3 + overlay mod
                await nativeInstaller.InstallAsync(
                    _updateService.Profile,
                    installVersion,
                    payloadUrls,
                    aoe3SourcePath,
                    installFolder,
                    dlProgress,
                    cloneProgress,
                    statusProgress,
                    phaseProgress,
                    extractProgress,
                    overlayProgress,
                    payloadSha256: payloadSha256,
                    ct: _cts.Token);
            }
            else
            {
                // Mod-only: just download and copy mod files
                await nativeInstaller.InstallModOnlyAsync(
                    _updateService.Profile,
                    installVersion,
                    payloadUrls,
                    installFolder,
                    dlProgress,
                    statusProgress,
                    phaseProgress,
                    extractProgress,
                    overlayProgress,
                    payloadSha256: payloadSha256,
                    ct: _cts.Token);
            }

            ProgressPanelControl.PatchProgress.Value = 100;
            ProgressPanelControl.OverallProgress.Value = 100;

            // Point the launcher at the new install and remember which
            // version we just laid down. For WolPatcher mods CheckAsync
            // re-hashes the data files on next launch and may refine the
            // version string, so this is just a hint. For GitHubReleases
            // mods the tag IS the version, so persisting it here is what
            // makes the StatusCard render "Installed v1.5" on next launch
            // without waiting for a manifest fetch.
            var installState = _config.GetActiveState();
            installState.InstallPath = installFolder;
            if (!string.IsNullOrEmpty(installVersion))
                installState.LastKnownVersion = installVersion;
            _config.Save();

            // Verify installation. VerifyInstallation is now profile-aware —
            // it always checks the probe file + spot-checks zero-byte content
            // files, and additionally applies the WoL-specific markers
            // (art\zulushield\, .bar archives, sound\, AI3\) when the active
            // profile uses the WolPatcher mechanism. Non-WoL mods get the
            // generic layer only, so no false positives.
            var postInstallProfile = _updateService.Profile;
            var verifyResult = await Task.Run(
                () => VerifyInstallation(installFolder, postInstallProfile));
            int totalProblems = verifyResult.MissingItems.Count + verifyResult.CorruptItems.Count;
            if (totalProblems == 0)
            {
                SetStatus(Strings.Format(
                    "StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format(
                        "StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
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
        InvalidateActiveModCheckCache();
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
    /// <c>Documents\My Games\&lt;profile.UserDataFolder&gt;</c>. Skipped
    /// when the active mod doesn't declare a user-data folder. Called only
    /// before a fresh install — that's the only deterministic moment where
    /// the version risk applies.
    /// </summary>
    private void ShowUserDataAlertIfNeeded()
    {
        var profile = _updateService.Profile;
        var userDataFolderName = profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName))
        {
            DiagnosticLog.Write(
                $"User-data alert: profile '{profile.Id}' has no userDataFolder; skipping.");
            return;
        }

        var folder = UserDataService.GetUserDataFolder(userDataFolderName);
        DiagnosticLog.Write(
            $"User-data alert check ({profile.DisplayName}). Probing path: '{folder ?? "(null)"}'");

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

        if (!UserDataService.HasExistingUserData(userDataFolderName))
        {
            DiagnosticLog.Write("  -> Folder exists but is empty; skipping alert.");
            return;
        }

        var savegameCount = UserDataService.CountSavegameFiles(userDataFolderName);
        DiagnosticLog.Write(
            $"Pre-existing user data detected for '{profile.Id}'. " +
            $"Savegame files: {savegameCount}. Showing alert.");

        var dialog = new UserDataAlertDialog(
            folder, profile.DisplayName, userDataFolderName) { Owner = this };
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

            // Tray notification — only fires if the user is away from the
            // launcher (window hidden/minimised/inactive). If they're
            // watching, ShowProgressCompleted above already painted the
            // "Update complete" state in the sidebar.
            ShowToast(
                Strings.Get("ToastUpdateCompleteTitle"),
                Strings.Format("ToastUpdateCompleteBody", _updateService.Profile.DisplayName));
        }

        // Re-check AFTER releasing busy state, otherwise the new CheckAsync
        // call would short-circuit on its own _isBusy guard. This refreshes
        // the version info card, the status message, and the pending list.
        if (succeeded)
        {
            InvalidateActiveModCheckCache();
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
            ProgressPanelControl.PatchProgress.Value = patchPct;
            ProgressPanelControl.PatchBytesText.Text = $"{FormatBytes(p.PatchBytesDone)} / {FormatBytes(p.PatchBytesTotal)}";
        }
        else
        {
            ProgressPanelControl.PatchProgress.Value = 0;
            ProgressPanelControl.PatchBytesText.Text = "";
        }

        // Total progress (overall update — same as install)
        if (p.OverallBytesTotal > 0)
        {
            ProgressPanelControl.OverallProgress.Value = (double)p.OverallBytesDone / p.OverallBytesTotal * 100.0;
            ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";
        }

        // Status line under the bars — phase-aware so the user always knows
        // whether we're downloading, verifying, or applying.
        ProgressPanelControl.LblCurrentPatch.Text = Strings.Format(
            UpdateStatusKeyForPhase(_currentUpdatePhase),
            p.PatchToVersion,
            p.CurrentStep, p.TotalSteps);

        // Drive the progress-panel subtitle and step counter — these are
        // what the user sees in the inline panel at the bottom of the
        // sidebar.
        ProgressPanelControl.ProgressSubtitleText.Text = Strings.Format("ProgressPatchSubtitle",
            p.CurrentStep, p.TotalSteps, p.PatchFromVersion, p.PatchToVersion);
        ProgressPanelControl.ProgressStepText.Text = Strings.Format(
            "ProgressStepFormat", p.CurrentStep, p.TotalSteps);

        // Speed label — phase-aware (Download / Verify / Apply)
        ProgressPanelControl.SpeedText.Text = p.BytesPerSecond > 0
            ? Strings.Format(UpdateSpeedLabelKeyForPhase(_currentUpdatePhase),
                FormatBytes((long)p.BytesPerSecond))
            : "";
        ProgressPanelControl.EtaText.Text = p.Eta.HasValue
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

            // CloseLauncherOnGameStart: user opted to fully quit the
            // launcher once the game's running. Skip StartGameMonitor
            // (no point monitoring something we won't react to) and
            // call RequestHardExit so OnClosing doesn't divert us to
            // the system tray. The user reopens the launcher manually
            // after the game closes.
            if (_config.CloseLauncherOnGameStart)
            {
                DiagnosticLog.Write(
                    "CloseLauncherOnGameStart=true; launcher exiting now that the game has started.");
                RequestHardExit();
                return;
            }

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
                ActionPanelControl.PlayButtonText.Text = Strings.Get("BtnInstall");
                ActionPanelControl.PlayButton.Background = accent;
                ActionPanelControl.PlayButton.Visibility = Visibility.Visible;
                ActionPanelControl.PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Play:
                ActionPanelControl.PlayButtonText.Text = _isGameRunning
                    ? Strings.Get("BtnPlaying")
                    : Strings.Get("BtnPlay");
                ActionPanelControl.PlayButton.Background = accent;
                ActionPanelControl.PlayButton.Visibility = Visibility.Visible;
                ActionPanelControl.PlayButton.IsEnabled = enabled && !_isGameRunning;
                break;
            case PrimaryAction.Update:
                ActionPanelControl.PlayButtonText.Text = Strings.Get("BtnUpdate");
                ActionPanelControl.PlayButton.Background = Brush("#d4a04a");
                ActionPanelControl.PlayButton.Visibility = Visibility.Visible;
                ActionPanelControl.PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Stop:
                ActionPanelControl.PlayButtonText.Text = Strings.Get("BtnStop");
                ActionPanelControl.PlayButton.Background = Brush("#8b0000");
                ActionPanelControl.PlayButton.Visibility = Visibility.Visible;
                ActionPanelControl.PlayButton.IsEnabled = enabled;
                break;
            case PrimaryAction.Hidden:
                ActionPanelControl.PlayButton.Visibility = Visibility.Collapsed;
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
            Dispatcher.Invoke(() => MainTabsControl.StatusText.Text = message);
        else
            MainTabsControl.StatusText.Text = message;
    }

    private void SetBusy(bool busy, bool checkOnly = false)
    {
        _isBusy = busy;
        _isCheckOnly = busy && checkOnly;
        // Update button is always disabled while busy — we genuinely don't
        // know what's available to download until the manifest fetch
        // completes, so letting the user click "Update" early would be
        // racy.
        ActionPanelControl.UpdateButton.IsEnabled = !busy;

        if (busy && checkOnly)
        {
            // Read-only check (CheckAsync's MD5 + manifest fetch): doesn't
            // modify the install, doesn't download anything. There's no
            // reason to lock Play or Settings during it — blocking them
            // just makes every mod switch feel sluggish (Play stays grey
            // for a second or two while the launcher is silently sniffing
            // the disk). Keep them live; the operation is reentrancy-safe
            // because CheckAsync's "if (_isBusy) return" guard at the top
            // still prevents a second check from piling on top of the
            // first.
            ActionPanelControl.MoreButton.IsEnabled = true;
            ActionPanelControl.PlayButton.IsEnabled = PrimaryActionEnabled();
        }
        else
        {
            // Verify / Repair / Uninstall live in the gear menu now and are
            // gated by their own MenuItem.IsEnabled inside MoreButton_Click.
            // The gear button itself stays live — its menu items handle their
            // own disabled states — but we lock it during ops so the user can't
            // fire a second flow on top of the running one.
            ActionPanelControl.MoreButton.IsEnabled = !busy;
            ActionPanelControl.PlayButton.IsEnabled = !busy && PrimaryActionEnabled();
        }
    }

    /// <summary>
    /// Whether the primary button is actionable for its CURRENT mode. The
    /// PlayButton element does multiple jobs (Install / Update / Play /
    /// Stop), so a blanket "_modIsInstalled" gate would wrongly grey out the
    /// Install button on a fresh install. Gate each mode by what actually
    /// needs to be true:
    ///   * Install — always actionable (the whole point is to install)
    ///   * Update  — actionable; pending-downloads count gates visibility,
    ///               not enabled state
    ///   * Play    — needs an installed mod and the game not already running
    ///   * Stop    — only meaningful while the game is running
    ///   * Hidden  — N/A
    /// </summary>
    private bool PrimaryActionEnabled() => _primaryAction switch
    {
        PrimaryAction.Install => true,
        PrimaryAction.Update  => true,
        PrimaryAction.Play    => _modIsInstalled && !_isGameRunning,
        PrimaryAction.Stop    => _isGameRunning,
        _                     => false,
    };

    private void ResetProgressUI()
    {
        ProgressPanelControl.PatchProgress.Value = 0;
        ProgressPanelControl.OverallProgress.Value = 0;
        ProgressPanelControl.PatchBytesText.Text = "";
        ProgressPanelControl.OverallBytesText.Text = "";
        ProgressPanelControl.SpeedText.Text = "";
        ProgressPanelControl.EtaText.Text = "";
        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressCurrentPatch");
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
        if (ActionPanelControl.MoreButton.ContextMenu == null) return;

        // Folders submenu — Open variants are enabled only when the path is
        // resolvable on disk; Select variants are always available unless
        // we're busy (the user might be trying to fix a detection problem).
        // Use the GLOBAL AoE3 detection (not the active mod's executable
        // lookup) so "Open AoE3 folder" stays enabled even when the active
        // mod is one whose executable doesn't exist (e.g. a community mod
        // the user hasn't installed yet).
        bool aoe3Detected = GameLauncher.FindAoe3Install(_config) != null;

        ActionPanelControl.MenuOpenAoE3Folder.IsEnabled = aoe3Detected;
        ActionPanelControl.MenuSelectModFolder.IsEnabled = !_isBusy;
        ActionPanelControl.MenuSelectAoE3Folder.IsEnabled = !_isBusy;

        // User data submenu — gated by whether the active mod declares a
        // user-data folder. Mods that don't (e.g. overlay mods sharing the
        // AoE3 vanilla folder) hide the whole submenu so the items don't
        // suggest a feature that wouldn't do anything.
        var userDataFolderName = _updateService.Profile.UserDataFolder;
        var userDataActive = !string.IsNullOrEmpty(userDataFolderName);
        ActionPanelControl.MenuUserData.Visibility = userDataActive
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (userDataActive)
        {
            var hasUserData = UserDataService.HasExistingUserData(userDataFolderName);
            var backups = UserDataService.ListBackups(userDataFolderName);

            ActionPanelControl.MenuOpenUserDataFolder.IsEnabled =
                UserDataService.GetUserDataFolder(userDataFolderName) != null;
            ActionPanelControl.MenuCreateBackupNow.IsEnabled = !_isBusy && hasUserData;
            ActionPanelControl.MenuRestoreUserData.IsEnabled = !_isBusy && backups.Count > 0;

            // Append the count of available backups to the Restore label so
            // the user knows at a glance whether they have anything to
            // restore.
            var restoreLabel = Strings.Get("MenuRestoreUserData");
            if (backups.Count > 0)
                restoreLabel = $"{restoreLabel}  ({backups.Count})";
            ActionPanelControl.MenuRestoreUserData.Header = restoreLabel;
        }

        // Health-check + maintenance actions
        ActionPanelControl.MenuCheckForUpdates.IsEnabled = !_isBusy && _modIsInstalled;
        // Repair re-runs the install pipeline, so it only makes sense for
        // mechanisms the launcher knows how to install from. DelegatedExternal
        // / Manual mods (updated by the mod's own tool) get their entry
        // disabled — clicking it would otherwise fail with "no payload URL".
        bool launcherCanInstall =
            _updateService.Profile.UpdateMechanism == ModUpdateMechanism.WolPatcher
            || _updateService.Profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases;
        ActionPanelControl.MenuRepairInstall.IsEnabled = !_isBusy && _modIsInstalled && launcherCanInstall;

        // Verify is now profile-aware: it only enforces the WoL-specific
        // markers when the active mod actually uses the WolPatcher pipeline;
        // for every other mod it falls back to "probe file present + no
        // zero-byte content files". Safe to leave clickable for any
        // installed mod.
        ActionPanelControl.MenuVerifyFiles.IsEnabled = !_isBusy && _modIsInstalled;
        // ViewLogs is always available — useful even when no mod is installed.
        ActionPanelControl.MenuViewLogs.IsEnabled = true;

        // Game-language submenu — populated each time the menu opens so
        // the available list reflects the latest registry state and the
        // active translation indicator is up to date.
        ActionPanelControl.MenuGameLanguage.IsEnabled = !_isBusy && _modIsInstalled;
        PopulateGameLanguageMenu();

        ActionPanelControl.UninstallMenuItem.IsEnabled = !_isBusy && _modIsInstalled;
        // Open to the right of the Settings button, with a small gap so the
        // menu doesn't visually touch the button. WPF auto-flips to the
        // left side if there's not enough room (e.g. very narrow window).
        ActionPanelControl.MoreButton.ContextMenu.PlacementTarget = ActionPanelControl.MoreButton;
        ActionPanelControl.MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        ActionPanelControl.MoreButton.ContextMenu.HorizontalOffset = 8;
        ActionPanelControl.MoreButton.ContextMenu.VerticalOffset = 0;
        ActionPanelControl.MoreButton.ContextMenu.IsOpen = true;
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
    ///
    /// Uses the global AoE3 detector (not the active mod's executable
    /// lookup), so this opens the user's AoE3 folder even when the active
    /// mod ships a different .exe name or isn't installed yet.
    /// </summary>
    private void MenuOpenAoE3Folder_Click(object sender, RoutedEventArgs e)
    {
        var exePath = GameLauncher.FindAoe3Install(_config);
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
        ActionPanelControl.MenuGameLanguage.Items.Clear();

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
        ActionPanelControl.MenuGameLanguage.Items.Add(english);

        ActionPanelControl.MenuGameLanguage.Items.Add(new System.Windows.Controls.Separator
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
            ActionPanelControl.MenuGameLanguage.Items.Add(new System.Windows.Controls.MenuItem
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
                ActionPanelControl.MenuGameLanguage.Items.Add(item);
            }
        }

        ActionPanelControl.MenuGameLanguage.Items.Add(new System.Windows.Controls.Separator
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
        ActionPanelControl.MenuGameLanguage.Items.Add(refresh);

        // Translator tool — turns a folder of translated XMLs into a ready-
        // to-publish .zip + JSON snippet. Only useful for translators, but
        // harmless for regular users (they just won't have anything to package).
        var packager = new System.Windows.Controls.MenuItem
        {
            Header = $"📦  {Strings.Get("MenuLangPackager")}",
        };
        packager.Click += (_, _) => OpenTranslationPackager();
        ActionPanelControl.MenuGameLanguage.Items.Add(packager);
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
    /// Pulls the community mods catalog at startup — runs alongside
    /// <see cref="CheckAsync"/>, <see cref="CheckForLauncherUpdateAsync"/>,
    /// and <see cref="RefreshTranslationIndexAsync"/> in
    /// <c>Task.WhenAll</c>, so the four together take only as long as the
    /// slowest of the lot.
    ///
    /// Resolves the repo to query from <c>launcher-config.json</c>:
    ///   * empty / unset → use the launcher's default catalog
    ///                     (<c>Gorgorito12/aoe3-mods-catalog</c>).
    ///   * <c>"none"</c>  → opt-out, skip the fetch entirely. Lets a user
    ///                     disable the catalog without un-shipping the
    ///                     field from their config.
    ///   * <c>"owner/repo"</c> → use that repo (forks, mirrors, private
    ///                     test catalogs).
    ///
    /// Failures are swallowed inside
    /// <see cref="ModRegistry.RefreshFromCatalogAsync"/> — the launcher
    /// always falls back to the built-in mods, so this method never
    /// throws and never blocks the UI on a bad network.
    /// </summary>
    private async Task RefreshCatalogAsync()
    {
        const string defaultRepo = "Gorgorito12/aoe3-mods-catalog";

        var raw = _config.ModsCatalogRepo;
        string? repo;
        if (string.IsNullOrEmpty(raw))
            repo = defaultRepo;
        else if (string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase))
            repo = null;
        else
            repo = raw;

        await ModRegistry.RefreshFromCatalogAsync(repo);
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
        // "Check for updates" must mean a real check, not a cache replay.
        InvalidateActiveModCheckCache();
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

        var userDataFolderName = _updateService.Profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName)) return;

        var backups = UserDataService.ListBackups(userDataFolderName);
        if (backups.Count == 0)
        {
            MessageBox.Show(this,
                Strings.Get("DlgRestoreNoBackupsBody"),
                Strings.Get("DlgRestoreNoBackupsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new UserDataRestoreDialog(backups, userDataFolderName) { Owner = this };
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
        var userDataFolderName = _updateService.Profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName)) return;

        var folder = UserDataService.GetUserDataFolder(userDataFolderName);
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

        var userDataFolderName = _updateService.Profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName)) return;

        if (!UserDataService.HasExistingUserData(userDataFolderName))
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

        var path = UserDataService.BackupUserData(userDataFolderName);
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
        var plan = uninstaller.Plan(_updateService.Profile, _updateService.InstallPath);

        // Pass the active profile's display name + probe file into the dialog
        // so every visible string is templated correctly for THIS mod (e.g.
        // "Uninstall Improvement Mod", "does not contain the Improvement Mod
        // marker (age3m.exe)") instead of the WoL fallback.
        var dialog = new UninstallDialog(
            plan,
            _updateService.Profile.DisplayName,
            _updateService.Profile.InstallProbeFile)
        {
            Owner = this
        };
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
        SetStatus(Strings.Format("StatusUninstalling", _updateService.Profile.DisplayName));

        // The uninstall service emits a single "Pct/Step" tuple per phase.
        // Map to the two bars: top bar follows the percentage, bottom bar
        // tracks the same so the user sees both filling in tandem (the
        // service doesn't have separate process vs cleanup metrics, but
        // showing two bars matches the rest of the operation panels).
        var progress = new Progress<(double Pct, string Step)>(p =>
        {
            ProgressPanelControl.PatchProgress.Value = p.Pct;
            ProgressPanelControl.OverallProgress.Value = p.Pct;
            ProgressPanelControl.PatchBytesText.Text = $"{p.Pct:0}%";
            ProgressPanelControl.OverallBytesText.Text = $"{p.Pct:0}%";
            ProgressPanelControl.ProgressSubtitleText.Text = p.Step;
            SetStatus(p.Step);
        });

        try
        {
            var result = await uninstaller.UninstallAsync(
                _updateService.Profile, plan, dialog.Options, progress);

            // Clear the saved path so re-detection runs from scratch
            if (dialog.Options.ResetConfig || result.Success)
            {
                _config.GetActiveState().InstallPath = "";
                _config.GameExecutable = "";
                _config.Save();
            }

            if (result.Success)
            {
                SetStatus(Strings.Format(
                    "StatusUninstallSuccess",
                    _updateService.Profile.DisplayName, result.FilesDeleted));
                // Uninstall: nothing to play, nothing to open — just Close.
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format(
                        "StatusUninstallSuccess",
                        _updateService.Profile.DisplayName, result.FilesDeleted));
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
            InvalidateActiveModCheckCache();
            await CheckAsync();
        }
    }
}
