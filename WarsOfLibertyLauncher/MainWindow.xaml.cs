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
// The verify result now lives in the testable VerifyService; alias keeps the
// many existing `VerifyResult` references in this file unchanged.
using VerifyResult = WarsOfLibertyLauncher.Services.VerifyService.VerifyResult;

namespace WarsOfLibertyLauncher;

public partial class MainWindow : Window
{
    private readonly LauncherConfig _config;
    // Not readonly — mod switching swaps this for a new instance bound to
    // the chosen profile so we don't have to restart the whole process.
    private UpdateService _updateService;
    private readonly InstallerService _installerService;
    // Steam-style notification bell backing store. Constructed once per run
    // from the loaded config (persisted history) and fed by the update /
    // translation detection hooks. Wired to the tray toast + bell pulse.
    private NotificationCenter _notifications = null!;
    // True while the cursor is over the bell — drives the white "illuminate".
    private bool _bellHover;
    // Frozen brushes for the bell fill by state (resting soft white, lit white,
    // and one per notification kind — same colours as the panel's row icons).
    private static readonly System.Windows.Media.Brush _bellSoftWhite = Frozen("#99FFFFFF");
    private static readonly System.Windows.Media.Brush _bellWhite = Frozen("#FFFFFF");
    private static readonly System.Windows.Media.Brush _bellBlue = Frozen("#5AA9E6");   // UpdateAvailable
    private static readonly System.Windows.Media.Brush _bellGreen = Frozen("#62C462");  // UpdateFinished
    private static readonly System.Windows.Media.Brush _bellGold = Frozen("#D8B66A");   // NewTranslation

    private static System.Windows.Media.Brush Frozen(string hex)
    {
        var b = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
    /// <summary>
    /// v1.0 multiplayer state. Constructed once per launcher run and
    /// attached to the Multiplayer tab UserControl. Disposed on window
    /// close so the WebSocket and ZeroTier auth resources are released
    /// even if the process is kept alive in the tray.
    /// </summary>
    private readonly Services.Multiplayer.MultiplayerSession _multiplayerSession;
    private List<DownloadInfo> _pendingDownloads = new();
    private CancellationTokenSource? _cts;
    private FolderCloneService? _cloneService;
    private bool _isBusy;
    // True when _isBusy is held by a read-only CheckAsync (background
    // refresh, no install / download / uninstall). Mod-switch pre-flight
    // ignores this kind of busy so the user can keep clicking mods without
    // the "operation in progress" popup.
    private bool _isCheckOnly;

    // ---- Background-operation ownership ----
    // A long op (install/update/repair/verify/uninstall) runs on ONE mod, but the user may
    // switch to VIEW/PLAY another installed mod while it runs. _operatingModId names the mod
    // the live op belongs to (null when idle); the button-gate + progress strip key off
    // whether the DISPLAYED mod is that one. _operatingCts is the op's OWN cancellation token
    // (separate from _cts, which a mod-switch's CheckAsync reassigns) so Pause/Cancel and a
    // switch never cross-cancel. Still ONE op at a time (each flow guards on _isBusy).
    private string? _operatingModId;
    // The specific install FOLDER the live op targets (a copy install writes a NEW folder,
    // not the active copy). Lets the gate keep a DIFFERENT already-installed copy of the SAME
    // mod playable while a new copy installs. Set with _operatingModId; see DisplayedModIsOperating.
    private string? _operatingInstallPath;
    private CancellationTokenSource? _operatingCts;
    // True only while a LONG, decoupled op (install / update) runs — those capture their
    // service/downloads/token locally, so the user may switch to another installed mod and
    // play it while they run in the background. The shorter ops (repair / verify / uninstall)
    // re-read _updateService, so a switch during them is blocked. Cleared when any op ends.
    private bool _operationIsBackgroundable;

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

    // --- Live catalog/asset refresh (so a catalog edit shows up without a
    // restart). The periodic timer revalidates the active mod's images every
    // 5 min (cheap raw ETag GETs); window focus + the Workshop "Actualizar"
    // button revalidate ALL mods; the catalog manifest re-fetch (one rate-
    // limited GitHub API call) is throttled to ≥5 min so polling can't burn
    // the 60/h budget. See RevalidateVisibleAssetsAsync.
    private DispatcherTimer? _catalogPollTimer;
    private int _pollTickCount;

    // Background lobby-creation notifier: polls /lobbies (a process-wide, tab-
    // independent DispatcherTimer) and fires a Windows notification when a NEW
    // room appears for a mod the user has installed. `_knownLobbyIds` is the
    // session's seen-set; the first tick seeds it silently (baseline) so
    // existing rooms don't flood. See the plan / CLAUDE.md.
    private DispatcherTimer? _lobbyNotifyTimer;
    private readonly HashSet<string> _knownLobbyIds = new(StringComparer.Ordinal);
    private bool _lobbyBaselineSeeded;
    private DateTime _lastFocusRevalidateUtc = DateTime.MinValue;
    private DateTime _lastCatalogRefreshUtc = DateTime.MinValue;
    private bool _revalidateInFlight;

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
        // v0.9 mod browser: card clicks open the detail panel inside the
        // UserControl; OpenWebsiteRequested opens the mod's official URL in the
        // default browser. Per-mod install/update/uninstall/repair/play/switch
        // actions live on the Dashboard now (PLAY state machine + gear menu) —
        // the Workshop only browses + toggles "my mods" (Add/RemoveFromCollection
        // below), so the old typed action events were removed.
        ModsBrowserView.OpenWebsiteRequested += ModsBrowserView_OpenWebsiteRequested;
        ModsBrowserView.RefreshCatalogRequested += ModsBrowserView_RefreshCatalogRequested;
        ModsBrowserView.AddLocalModRequested += ModsBrowserView_AddLocalModRequested;
        ModsBrowserView.PublishRequested += ModsBrowserView_PublishRequested;
        // Workshop redesign: per-row "Add to my mods" / "Remove from
        // my mods" toggle. Replaces the old install/update/repair
        // dispatch on the Workshop — those flows now live on the
        // Dashboard (PLAY state machine + gear menu).
        ModsBrowserView.AddToCollectionRequested += ModsBrowserView_AddToCollectionRequested;
        ModsBrowserView.RemoveFromCollectionRequested += ModsBrowserView_RemoveFromCollectionRequested;
        // Lazily fetch a mod's gallery screenshots when its detail panel opens.
        ModsBrowserView.ScreenshotRequester = p => _ = EnsureScreenshotsAsync(p);
        // (RightClicked subscription removed — right-click on Workshop
        // rows no longer does anything. Per-mod admin actions live in
        // the dashboard gear button now.)
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
        ActionPanelControl.MenuInstallAnotherCopy.Click += MenuInstallAnotherCopy_Click;
        ActionPanelControl.MenuVerifyFiles.Click += MenuVerifyFiles_Click;
        ActionPanelControl.MenuViewLogs.Click += MenuViewLogs_Click;
        ActionPanelControl.UninstallMenuItem.Click += UninstallMenuItem_Click;

        // Title-bar minimize/maximize/restore/close + the maximize-glyph
        // sync are now owned by the shared controls:TitleBar in Row 0.

        // Window-size UI scaling (Controls/UiScale.cs). The hero keeps its OWN
        // render transform pinned bottom-left over its full-bleed background
        // (Kind.Render, origin 0,1) and its hand-tuned 1500x760 reference, so
        // its behaviour is byte-for-byte what it was before the shared scaler.
        // Hooked to PlayView.SizeChanged (not the window's) so it re-fires when
        // the dashboard tab is re-shown: switching away collapses PlayView to a
        // 0-size (guarded no-op), switching back grows it from 0 — a size change
        // even though the window itself never resized.
        UiScale.Attach(HeroContentGrid, PlayView, 1500, 760,
            UiScale.Kind.Render, new System.Windows.Point(0, 1));
        // Drive the rotating-hero crossfade only while the dashboard is actually
        // on screen — stop the timer when another tab is shown, resume on return.
        PlayView.IsVisibleChanged += (_, __) => UpdateHeroRotationTimer();
        // Publish the general content factor (reference = the default content
        // footprint, so a default-sized window resolves to 1.0) for the code-
        // built brand / mod-switch popups, which live in their own top-level
        // visual tree and can't ride a content-root transform.
        UiScale.Track(ContentHost, 1100, 604);

        DiagnosticLog.Reset();
        DiagnosticLog.Write("MainWindow initialized.");

        _config = LauncherConfig.Load();

        // Register (or, if the user opted out, clear) the wol-launcher:// deep-link
        // scheme. Re-applying each launch self-heals the exe path for the portable
        // binary (the registered path follows wherever the user last ran it).
        if (_config.EnableJoinLinks) Services.DeepLinkService.EnsureRegistered();
        else Services.DeepLinkService.EnsureUnregistered();

        // Telemetry is opt-in (PRIVACY.md / SignPath Foundation terms):
        // MultiplayerTelemetry defaults to on in-process, but the launcher
        // must collect nothing until the user enables it in Launcher
        // Settings → Privacy. Apply the saved choice at startup;
        // LauncherSettingsDialog re-applies it on Save.
        Services.Multiplayer.MultiplayerTelemetry.Enabled = _config.MultiplayerTelemetryEnabled;

        // Workshop migration — first launch with the new UserModIds
        // field on an old config. Seed the personal collection from
        // whatever the user already had installed, so the Dashboard's
        // MODS popup doesn't suddenly lose their existing mods. After
        // this seeding, the list only changes via the Workshop's
        // Add/Remove buttons. Detection is "saved per-mod state has a
        // non-empty install path", which is the same signal
        // IsModInstalled / BuildModRowState use. Built-ins are
        // skipped — they're always implicit via IsUserMod() so
        // adding them explicitly would just bloat the JSON.
        if (_config.UserModIds.Count == 0 && _config.Mods.Count > 0)
        {
            bool seeded = false;
            foreach (var kvp in _config.Mods)
            {
                if (!string.IsNullOrEmpty(kvp.Value.InstallPath)
                    && !ModRegistry.IsBuiltIn(kvp.Key))
                {
                    _config.UserModIds.Add(kvp.Key);
                    seeded = true;
                }
            }
            if (seeded)
            {
                DiagnosticLog.Write(
                    $"Workshop migration: seeded UserModIds with {_config.UserModIds.Count} previously-installed mod(s).");
                try { _config.Save(); }
                catch (Exception ex) { DiagnosticLog.Write($"Workshop migration save failed: {ex.Message}"); }
            }
        }

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

        // Notification bell (Steam-style). Seeds its history from the persisted
        // config, refreshes the badge on any change, fires the one-shot pulse
        // when a new item arrives, and routes new items to the tray toast
        // (which itself no-ops when the user is already looking at the window).
        _notifications = new NotificationCenter(_config);
        _notifications.Changed += (_, _) => Dispatcher.Invoke(RefreshNotificationBadge);
        _notifications.ItemAdded += (_, _) => Dispatcher.Invoke(PulseNotificationBell);
        _notifications.ToastRequested += (title, body) => Dispatcher.Invoke(() => ShowToast(title, body));
        NotificationList.ItemsSource = _notifications.Items;
        // The popup is StaysOpen=True (never auto-closes) so the bell can be a
        // reliable click-toggle. We close it ourselves on an outside click or
        // when the window loses focus; those handlers are attached only while
        // it's open. Tag="open" drives the pressed-gold tint on the button.
        NotificationPopup.Opened += (_, _) =>
        {
            NotificationBellButton.Tag = "open";
            PreviewMouseDown += CloseNotifOnOutsideClick;
            Deactivated += CloseNotifOnDeactivate;
            // A WPF Popup renders in its own HWND and only computes placement when
            // it opens — it does NOT follow the owner window on drag/resize. Attach
            // these while open so the panel stays glued to the bell as the window
            // moves; detached on close.
            LocationChanged += RepositionNotifPopup;
            SizeChanged += RepositionNotifPopup;
            RefreshBellColor();
        };
        NotificationPopup.Closed += (_, _) =>
        {
            NotificationBellButton.Tag = null;
            PreviewMouseDown -= CloseNotifOnOutsideClick;
            Deactivated -= CloseNotifOnDeactivate;
            LocationChanged -= RepositionNotifPopup;
            SizeChanged -= RepositionNotifPopup;
            RefreshBellColor();
        };
        RefreshNotificationLabels();
        RefreshNotificationBadge();

        // v1.0 multiplayer. The session restores a saved JWT from config
        // if it's still valid; otherwise it stays in SignedOut and the
        // Multiplayer tab shows the sign-in gate. The hashing callback
        // points at ModHashService — kept as a delegate so the
        // UserControl doesn't need a direct dependency on the service.
        _multiplayerSession = new Services.Multiplayer.MultiplayerSession(_config);
        MultiplayerView.Attach(
            _multiplayerSession,
            () => _config.GetActiveProfile(),
            async profile =>
            {
                var installPath = _config.GetState(profile.Id).InstallPath;
                // The stock Age of Empires III profile is detect-only — it has
                // no saved install path because the launcher never installed
                // it. Resolve it from the detected AoE3 install so it can still
                // be fingerprinted for the version-parity check when hosting /
                // joining a stock-game lobby.
                if (string.IsNullOrEmpty(installPath) && profile.IsStockGame)
                    installPath = Services.AoE3Detector.FindInstallRoot();
                if (string.IsNullOrEmpty(installPath))
                    throw new InvalidOperationException(
                        "The active mod is not installed on this PC. Install it before joining or hosting.");
                var fp = await Services.Multiplayer.ModHashService.FingerprintAsync(profile, installPath);
                return fp.CombinedHash;
            },
            // Launch callback wired through to GameLauncher.LaunchAndWatch.
            // We expose the watched variant (vs Launch) so the multiplayer
            // flow can subscribe to Process.Exited for the post-game
            // replay/match flow. Returns null when GameLauncher can't
            // resolve the .exe, mirroring its existing FileNotFoundException
            // behaviour without leaking the exception up to UI code.
            //
            // The flag set itself is built by MultiplayerTab — it knows
            // whether we're hosting vs joining and what the virtual-LAN
            // IP allocator handed out for the current user. We just
            // plumb the resulting string through to GameLauncher.
            //
            // Real flag inventory (verified by string-searching the
            // age3y.exe binary; lowercase variants like +nointro / +mp /
            // +hostmpgame / +joinIPaddr do NOT exist — that's why the
            // earlier attempts silently no-op'd):
            //   +noIntroCinematics  — skip intro cinematics
            //   +disableESOProfile  — skip the long ESO login dialog
            //   +dontDetectNAT      — skip NAT probing delay
            //   +xres / +yres       — force resolution (unused here)
            (profile, onExited, extraArgs) =>
            {
                try
                {
                    var installPath = _config.GetState(profile.Id).InstallPath;
                    // Stock AoE3 is detect-only (no saved path) — resolve it from
                    // the detected install so the launch points at the real game,
                    // mirroring the fingerprint callback above.
                    if (string.IsNullOrEmpty(installPath) && profile.IsStockGame)
                        installPath = Services.AoE3Detector.FindInstallRoot();
                    // trustConfigCache:false — the room's mod may differ from the
                    // active dashboard mod, and both AoE3 and WoL ship age3y.exe,
                    // so the global GameExecutable cache (the active mod's) would
                    // launch the WRONG game (hosted a WoL room while AoE3 active →
                    // it opened AoE3). Resolve purely from this room mod's folder
                    // and don't write the result back to the shared cache.
                    return GameLauncher.LaunchAndWatch(
                        _config, installPath, profile, onExited,
                        extraArgs: extraArgs, trustConfigCache: false);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerTab launch hook: {ex.Message}");
                    return null;
                }
            },
            // Switch-active-mod hook. Used by the multiplayer join
            // flow when the user clicks Join on a room whose mod
            // doesn't match the current active profile: instead of
            // forcing them to navigate to the Play tab and switch
            // by hand, we run the exact same LoadModProfile path
            // here. Returns true on success / no-op when already
            // active; false when LoadModProfile bailed (e.g. install
            // currently in progress).
            switchActiveMod: target =>
            {
                try
                {
                    if (string.Equals(_updateService.Profile.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                        return true;
                    LoadModProfile(target);
                    // LoadModProfile is async-void and updates the
                    // active profile synchronously on the UI thread
                    // before kicking off the background CheckAsync,
                    // so by the time we return the new mod is the
                    // active one as far as the multiplayer flow can
                    // tell.
                    return string.Equals(_updateService.Profile.Id, target.Id, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"MultiplayerTab switchActiveMod hook: {ex.Message}");
                    return false;
                }
            },
            // Pass the config through so the Radmin assistant overlay
            // can honour the user's Mode preference + flip the
            // "don't show again" flag from inside the overlay.
            config: _config,
            // Rotate the active install copy from the create-room copy picker.
            // Multiplayer launches / fingerprints the ACTIVE copy, so choosing a
            // copy there is exactly an active-copy switch (single source of truth).
            switchActiveCopy: installId => SwitchActiveInstallAsync(installId));
        UpdateAccentResources(activeProfile);

        ApplyLanguage();

        // Apply the user's saved tab order to the nav bar and open the
        // first tab in that order (their "opens on launch" choice). Runs
        // here — after MultiplayerView.Attach() + ApplyLanguage() — so
        // that if the user put Multiplayer first, the view is already
        // wired to the session and its labels are localised before we
        // make it visible. Before the window shows, so there's no
        // visible flash of the default Library tab.
        ApplyTopTabOrder(switchToFirst: true);

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

        // Offline mode: reflect the observed connectivity state (title-bar chip +
        // greying online-only controls). Subscribe to the app-wide signal and apply
        // the current state once now (online at startup until a call proves otherwise).
        // Detach on real close (Closed, not OnClosing — the latter has minimize-to-tray
        // / match-active early-returns that don't actually close the window).
        ConnectivityState.OfflineChanged += OnConnectivityChanged;
        Closed += (_, _) => ConnectivityState.OfflineChanged -= OnConnectivityChanged;
        ApplyOfflineModeUi(ConnectivityState.IsOffline);

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
        // Auto-start-to-tray: when Windows launched us at login (the Run-key
        // registration appended --minimized, parsed into App.StartMinimized), hide
        // straight to the tray so the "run in background" experience doesn't pop a
        // window every login. Runs from Loaded (after App called Show()) so the
        // hide sticks; WindowState was pre-set to Minimized in App to avoid a flash.
        // A manual double-click carries no --minimized arg, so it shows normally.
        Loaded += (_, _) =>
        {
            if (App.StartMinimized)
            {
                DiagnosticLog.Write("Started with --minimized: hiding to tray at launch.");
                HideToTray();
            }
        };

        Loaded += async (_, _) =>
        {
            LauncherUpdateService.CleanupOldVersion();

            // Render the primary button from LOCAL install state before any network
            // work, mirroring the mod-switch path (ReloadActiveServiceAsync 1126+1137).
            // At cold start the button is otherwise Hidden until the (gated,
            // possibly-skipped) startup check runs — so offline, OR with
            // CheckUpdatesOnStartup off, PLAY would stay greyed for an already-installed
            // mod. _updateService resolved InstallPath synchronously in the ctor, so
            // this needs no await; the subsequent CheckAsync only refines it.
            _modIsInstalled = !string.IsNullOrEmpty(_updateService.InstallPath);
            UpdateGameUI();

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

                // Seed the catalog-refresh throttle: the startup fetch just ran,
                // so the first window focus shouldn't immediately FORCE another
                // (redundant) API fetch — wait the normal 5 min from here.
                _lastCatalogRefreshUtc = DateTime.UtcNow;

                // Fire-and-forget: sweep the OTHER installed mods for update /
                // new-translation notifications once at startup (the active mod
                // is already covered by CheckAsync / RefreshTranslationIndexAsync
                // above). Background so it never delays first paint.
                _ = SweepInstalledModsForNotificationsAsync();
            }
            else
            {
                DiagnosticLog.Write(
                    "Startup auto-check disabled (CheckUpdatesOnStartup=false); using cached state.");
                // News still loads from cache when auto-check is off.
                _ = RefreshNewsAsync();
            }

            // One-time-per-launch migration sweep for the "disk cache only for
            // installed + active" policy: older builds cached images for every
            // Workshop card ever rendered. Runs AFTER the catalog refresh above
            // (so community ids are known) and is the policy's single opt-out
            // point — remove this call to keep pre-existing cached assets.
            _ = PurgeNonEligibleModAssetsAsync();

            if (_modIsInstalled)
            {
                _ = Task.Run(InstallerService.TryCleanupTemp);
                _ = Task.Run(NativeInstallService.TryCleanupTemp);

                // Startup parity hook. RemoveStaleBuildArtifacts is now a
                // no-op (the launcher keeps the payload byte-faithful and
                // strips nothing); the call is kept as the single home for
                // the "strip nothing" policy. It used to strip "stale"
                // files here, but every one of them is also present in a
                // canonical original-installer install, so stripping them
                // diverged us from peers — the same lesson as the .xml.xmb bug.
                var activeProfile = _updateService.Profile;
                var activeInstall = _updateService.InstallPath;
                if (!string.IsNullOrEmpty(activeInstall))
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            NativeInstallService.RemoveStaleBuildArtifacts(activeProfile, activeInstall);
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Write($"Startup RemoveStaleBuildArtifacts: {ex.Message}");
                        }
                    });
                }
            }
            // --update-now (elevated relaunch) must apply the pending update even when
            // the user turned startup checks off — otherwise CheckAsync never ran, so
            // _pendingDownloads is empty and the elevated apply silently no-ops. Run a
            // one-off check here so the pending list is populated (matches the intent
            // of the "honoured regardless" comment on the CheckUpdatesOnStartup gate).
            if (autoUpdate && _modIsInstalled && !_config.CheckUpdatesOnStartup)
                await CheckAsync();

            if (autoUpdate && _modIsInstalled && _pendingDownloads.Count > 0)
            {
                await ApplyAsync();
            }

            // ---- Discord "Join" deep links (wol-launcher://join/<id>) ----
            // A later launch forwards its link over the single-instance pipe and
            // App raises JoinRequested on the UI thread; a cold-start link (this
            // very launch was the click) is stashed in App.PendingJoinLobbyId.
            WarsOfLibertyLauncher.App.JoinRequested += id => _ = HandleJoinDeepLink(id);
            if (WarsOfLibertyLauncher.App.PendingJoinLobbyId is { } coldStartJoin)
            {
                WarsOfLibertyLauncher.App.ClearPendingJoin();
                _ = HandleJoinDeepLink(coldStartJoin);
            }
        };

        // Live refresh — bring catalog edits in without a restart (user-chosen
        // "Ambos": periodic + on-focus + manual button).
        //
        // BOTH automatic triggers honour CheckUpdatesOnStartup: a user who turned
        // background checks off (metered connection / wants the launcher silent)
        // must NOT get continuous GitHub traffic. The manual "Actualizar" button
        // stays unconditional — an explicit action is always allowed.
        //
        // On focus: the user typically alt-tabs back to the launcher right after
        // editing the repo, so this is the most natural trigger. Throttled to
        // 60s so rapid focus changes (incl. closing a child dialog) don't spam
        // the network; a forced catalog re-fetch piggybacks only when ≥5 min
        // since the last one (API budget).
        Activated += async (_, _) =>
        {
            if (!_config.CheckUpdatesOnStartup) return;
            if ((DateTime.UtcNow - _lastFocusRevalidateUtc) < TimeSpan.FromSeconds(60))
                return;
            _lastFocusRevalidateUtc = DateTime.UtcNow;
            await MaybeForceCatalogRefreshAsync();
            await RevalidateVisibleAssetsAsync(activeOnly: false);
        };

        // Periodic: 5 min aligns with raw.githubusercontent's CDN cache, so
        // polling faster wouldn't see changes any sooner. Each tick revalidates
        // only the ACTIVE mod's images (cheap); every 3rd tick (~15 min) also
        // forces a catalog manifest re-fetch.
        if (_config.CheckUpdatesOnStartup)
        {
            _catalogPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _catalogPollTimer.Tick += async (_, _) =>
            {
                _pollTickCount++;
                if (_pollTickCount % 3 == 0)
                    await MaybeForceCatalogRefreshAsync();
                await RevalidateVisibleAssetsAsync(activeOnly: true);
                // Every 6th tick (~30 min): sweep the OTHER installed mods for
                // update / new-translation notifications. Throttled well below
                // the image revalidation so it stays within the GitHub API budget.
                if (_pollTickCount % 6 == 0)
                    await SweepInstalledModsForNotificationsAsync();
            };
            _catalogPollTimer.Start();
        }

        // Background "new room created" notifier. Created unconditionally so the
        // Settings toggle can enable it at runtime; the tick self-gates on the
        // feature toggle + metered mode + sign-in. 90 s ⇒ ~960 /lobbies calls/day,
        // well under the backend's 2000/day per-IP cap.
        _lobbyNotifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        _lobbyNotifyTimer.Tick += async (_, _) => await PollNewRoomsAsync();
        _lobbyNotifyTimer.Start();
    }

    /// <summary>
    /// Background poll of the lobby list to surface a Windows notification when
    /// ANY user creates a new room (for a mod the user has installed). Detection
    /// lives ONLY here (not in the MP tab's render poll). Non-fatal.
    /// </summary>
    private async Task PollNewRoomsAsync()
    {
        // Respect the feature toggle and the "metered / stay silent" setting; only
        // notify signed-in MP users (the audience who could actually join).
        if (!_config.NotifyNewRooms || !_config.CheckUpdatesOnStartup) return;
        if (_multiplayerSession.Status != Services.Multiplayer.MultiplayerSession.SessionStatus.SignedIn)
            return;

        try
        {
            var list = await _multiplayerSession.Api.ListLobbiesAsync();
            var lobbies = list?.Lobbies;
            if (lobbies == null) return;

            // First successful poll: seed the baseline silently so existing rooms
            // don't bell — only rooms created AFTER this fire a notification.
            if (!_lobbyBaselineSeeded)
            {
                foreach (var l in lobbies)
                    if (!string.IsNullOrEmpty(l.Id)) _knownLobbyIds.Add(l.Id);
                _lobbyBaselineSeeded = true;
                return;
            }

            var me = _multiplayerSession.CurrentUser;
            foreach (var l in lobbies)
            {
                if (string.IsNullOrEmpty(l.Id) || _knownLobbyIds.Contains(l.Id)) continue;
                _knownLobbyIds.Add(l.Id);

                // Skip my own room, and rooms for a mod I don't have (can't join).
                bool isMine = me != null && (
                    (!string.IsNullOrEmpty(l.Host?.Id) && string.Equals(l.Host.Id, me.Id, StringComparison.Ordinal))
                    || (!string.IsNullOrEmpty(l.Host?.DiscordUsername)
                        && string.Equals(l.Host.DiscordUsername, me.DiscordUsername, StringComparison.OrdinalIgnoreCase)));
                if (isMine) continue;
                if (!IsModInstalled(l.ModId)) continue;

                var hostName = l.Host?.DisplayName;
                if (string.IsNullOrWhiteSpace(hostName) || hostName == "-") hostName = l.Host?.DiscordUsername;
                if (string.IsNullOrWhiteSpace(hostName) || hostName == "-") hostName = "—";
                var title = string.IsNullOrWhiteSpace(l.Title) ? "—" : l.Title;

                // Room notifications no longer land in the bell — surface a Windows
                // toast plus a dot on the MULTIPLAYER tab / Rooms subtab instead.
                if (_notifications.TryMarkRoomNotified(l.Id))
                {
                    ShowToast(
                        Strings.Get("MpNotifRoomCreatedTitle"),
                        Strings.Format("MpNotifRoomCreatedBody", title, hostName));
                    if (_activeTopTab != TopTab.Multiplayer) SetMultiplayerTabDot(true);
                    MultiplayerView?.SetNewRoomIndicator(true);
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"PollNewRooms failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Is the given mod id installed on this PC? Ported from
    /// <c>MultiplayerTab.IsModInstalledLocally</c> so the background room-notifier
    /// only notifies about rooms the user could actually join.
    /// </summary>
    private bool IsModInstalled(string modId)
    {
        try
        {
            if (string.IsNullOrEmpty(modId)) return false;
            var state = _config.GetState(modId);
            if (!string.IsNullOrEmpty(state.InstallPath)) return true;
            var profile = ModRegistry.Find(modId);
            if (profile is { IsStockGame: true })
                return !string.IsNullOrEmpty(AoE3Detector.FindInstallRoot());
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Forces a catalog manifest re-fetch (skipping the 24h TTL) IF at least
    /// 5 minutes have passed since the last forced fetch — a single shared
    /// throttle so the focus handler and the periodic timer can't double up and
    /// blow the GitHub API budget (60/h per IP). Re-renders the cards on change.
    /// </summary>
    private bool _catalogRefreshInFlight;
    private async Task MaybeForceCatalogRefreshAsync()
    {
        // The throttle check and its set straddle an await, so a timer tick and
        // a focus event could both pass it and fire two FetchAsync calls. The
        // in-flight flag (set before any await) makes it single-shot.
        if (_catalogRefreshInFlight) return;
        if ((DateTime.UtcNow - _lastCatalogRefreshUtc) < TimeSpan.FromMinutes(5))
            return;
        _catalogRefreshInFlight = true;
        try
        {
            await RefreshCatalogAsync(force: true);   // updates _lastCatalogRefreshUtc
            RefreshModCards();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MaybeForceCatalogRefresh: {ex.Message}");
        }
        finally
        {
            _catalogRefreshInFlight = false;
        }
    }

    /// <summary>
    /// Re-runs the lazy asset fetch/revalidation so a catalog image change
    /// (replaced 200 / deleted 404) is reflected without a restart. Clears the
    /// per-session fetch guards and re-invokes <see cref="EnsureModAssetsAsync"/>
    /// (its Phase-2 conditional GETs do the detection + repaint). Calling
    /// RefreshModCards alone wouldn't work — BuildModCard only kicks the fetch
    /// when a card's icon is unloaded. <paramref name="activeOnly"/> limits the
    /// periodic timer to the dashboard mod (fewer background GETs); focus/button
    /// pass false to cover the Workshop grid too. Reentrancy-guarded.
    /// </summary>
    private async Task RevalidateVisibleAssetsAsync(bool activeOnly)
    {
        if (_revalidateInFlight) return;
        _revalidateInFlight = true;
        try
        {
            _assetFetchAttempted.Clear();
            _screenshotFetchAttempted.Clear();
            if (activeOnly)
            {
                await EnsureModAssetsAsync(_updateService.Profile);
            }
            else
            {
                foreach (var p in ModRegistry.All)
                    await EnsureModAssetsAsync(p);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RevalidateVisibleAssets: {ex.Message}");
        }
        finally
        {
            _revalidateInFlight = false;
        }
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
        // Keep the v0.9 browser grid in sync with the top strip — same
        // data source (ModRegistry.All), same active highlight, same
        // install-state probe. Cheap when the Mods tab isn't visible
        // (the UserControl just rebuilds its children off-screen).
        RefreshModsBrowser();
    }

    /// <summary>
    /// Re-renders the v0.9 mod browser cards from <see cref="ModRegistry.All"/>.
    /// Safe to call from any thread that already owns the dispatcher (we
    /// keep all UI-touching helpers single-threaded). Decoupled from
    /// <see cref="RefreshModCards"/> so future commits can call it
    /// independently after catalog filtering / search changes.
    /// </summary>
    private void RefreshModsBrowser()
    {
        ModsBrowserView.Populate(
            ModRegistry.All,
            _updateService.Profile.Id,
            _config.Language,
            BuildModRowState);
    }

    /// <summary>
    /// Per-profile structured state for the catalog view. Reads from the
    /// CheckResult cache when available (so revisits paint installed +
    /// versioned without disk I/O); falls back to the per-mod state +
    /// on-disk probe used by <see cref="ProbeInstalledState"/> otherwise.
    /// </summary>
    private Controls.ModRowState BuildModRowState(ModProfile profile)
    {
        bool isActive = string.Equals(
            profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase);

        string current = "";
        string available = "";
        bool installed;
        bool hasUpdate = false;

        if (_checkResultCache.TryGetValue(profile.Id, out var cached))
        {
            current = cached.CurrentVersion?.Ver ?? "";
            available = cached.LatestVersion?.Ver ?? "";
            installed = cached.IsValidInstall;
            hasUpdate = cached.PendingDownloads.Count > 0;
        }
        else
        {
            installed = IsProfileInstalledLocally(profile);
            current = _config.GetState(profile.Id).LastKnownVersion;
        }

        Controls.ModRowStatus status = installed
            ? (hasUpdate ? Controls.ModRowStatus.UpdateAvailable : Controls.ModRowStatus.Installed)
            : Controls.ModRowStatus.NotInstalled;

        return new Controls.ModRowState
        {
            Status = status,
            CurrentVersion = current,
            AvailableVersion = available,
            IsActive = isActive,
            // Workshop redesign: per-row Add/Remove toggle is driven
            // by the user's personal collection. Built-ins are always
            // implicitly "in collection" and rendered as a disabled
            // "Built-in" pill (handled by ModsBrowser.BuildRowAction).
            IsInUserCollection = _config.IsUserMod(profile.Id),
            IsBuiltIn = ModRegistry.IsBuiltIn(profile.Id),
        };
    }

    /// <summary>
    /// Is this mod installed on THIS machine? The single install-detection
    /// rule shared by <see cref="BuildModRowState"/> (row badges) and the
    /// disk-cache gate (<see cref="ShouldCacheAssetsToDisk"/>): the cached
    /// CheckResult when one exists, else the saved per-mod path validated by
    /// content (<see cref="SavedPathLooksValid"/>), else the disk probe.
    /// </summary>
    private bool IsProfileInstalledLocally(ModProfile profile)
    {
        if (_checkResultCache.TryGetValue(profile.Id, out var cached))
            return cached.IsValidInstall;

        bool isActive = string.Equals(
            profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase);
        var state = _config.GetState(profile.Id);
        string? path = isActive ? _updateService.InstallPath : state.InstallPath;
        if (!string.IsNullOrEmpty(path)
            && Directory.Exists(path)
            && SavedPathLooksValid(path, profile))
            return true;

        return !string.IsNullOrEmpty(ResolveProbedInstallPath(profile));
    }

    /// <summary>
    /// Disk-cache policy gate: only mods the user actually has — installed,
    /// the active dashboard mod (its dashboard must work offline and its
    /// shortcut icon needs a real file), or the mod with the operation in
    /// flight (so a fresh install has its icon on disk before
    /// <c>CreateShortcuts</c> runs) — get their images written to
    /// <c>mod-assets\</c>. Everything else paints live from the catalog URL
    /// so browsing the Workshop can't fill the disk.
    /// </summary>
    private bool ShouldCacheAssetsToDisk(ModProfile profile)
        => IsProfileInstalledLocally(profile)
           || string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase)
           || string.Equals(profile.Id, _operatingModId, StringComparison.OrdinalIgnoreCase);

    private FrameworkElement BuildModCard(ModProfile profile, string activeId)
    {
        bool isActive = string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase);
        var accent = SafeBrush(profile.AccentColor, "#3a3d44");

        // Icon resolution priority (ModProfile.ResolveIconSource):
        //   1. Cached community icon (profile.LocalIconPath — only installed/
        //      active mods get one; populated by EnsureModAssetsAsync).
        //   2. Live catalog URL (profile.IconUrl) — non-installed mods paint
        //      straight from the network, nothing written to disk.
        //   3. Built-in pack URI (profile.BannerImage — historical name; for
        //      WoL it's the .ico embedded as a pack resource).
        //   4. Fallback: monogram (accent-coloured disc + first letter of
        //      DisplayName) — handled below in the else branch.
        // The disk-cache kick is UNCONDITIONAL: with live URL painting the
        // brush is rarely null, so the old "kick only when unloaded" gate
        // would never fire for a newly-installed mod. EnsureModAssetsAsync
        // itself gates on installed/active/operating (cheap early return for
        // everything else), reconciles orphaned cache files, and its
        // per-session guard keeps this to one real pass per mod.
        var iconBrush = TryLoadTileImage(profile.ResolveIconSource());
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
            FontSize = (double)FindResource("FontSizeBodyStrong"),
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White,
        };
        var stateText = new System.Windows.Controls.TextBlock
        {
            Text = ProbeInstalledState(profile),
            FontSize = (double)FindResource("FontSizeCaption"),
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
    /// Tile-side equivalent of <see cref="Services.UpdateService"/>'s install
    /// detection: rejects a cached install path that satisfies
    /// <see cref="Directory.Exists(string)"/> but isn't a real install of this
    /// mod. Detection is by CONTENT (probe file + optional marker), never by
    /// folder name — see <see cref="Services.ModInstallProbe"/> for the rule.
    /// The marker is what tells a real WoL folder apart from vanilla AoE3,
    /// which also carries the probe file.
    /// </summary>
    private static bool SavedPathLooksValid(string saved, ModProfile profile)
        => ModInstallProbe.LooksLikeModInstall(saved, profile);

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
        // A LONG op (install / update) runs in the BACKGROUND: allow switching to another
        // installed mod (and playing it) while it continues. Only the shorter ops that
        // re-read _updateService (repair / verify / uninstall) still block a switch.
        if (_isBusy && !_isCheckOnly && !_operationIsBackgroundable)
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
        // GameLauncher caches the last-launched exe path in the GLOBAL
        // _config.GameExecutable, validated on a filename-only match. WoL and
        // the stock Asian Dynasties profile both declare
        // GameExecutable="age3y.exe", so a cached base-game path satisfies that
        // match for WoL too and the wrong game launches after a switch (play
        // AoE3, switch to WoL, PLAY -> AoE3 opened). Clear the global cache on
        // every switch so exe resolution falls back to the new mod's per-mod
        // install folder. Mirrors the clear-on-uninstall step above.
        _config.GameExecutable = "";
        _ = Task.Run(() =>
        {
            try { _config.Save(); }
            catch (Exception ex) { DiagnosticLog.Write($"Async config save after mod switch failed: {ex.Message}"); }
        });

        // Rebuild the active service + refresh all UI for the now-active profile.
        // (Shared with the install-switch path below.)
        await ReloadActiveServiceAsync(isModSwitch: true);
    }

    /// <summary>
    /// Rebuilds <see cref="_updateService"/> for the currently-active mod/install
    /// and refreshes every UI surface that depends on it, then runs the async
    /// <see cref="CheckAsync"/> re-detection. Shared by <see cref="LoadModProfile"/>
    /// (mod switch) and <see cref="SwitchActiveInstallAsync"/> (install switch
    /// within the same mod). Callers MUST set <c>_config.ActiveModId</c> /
    /// the active install + clear <c>_config.GameExecutable</c> + persist BEFORE
    /// calling — this only rebuilds and repaints.
    ///
    /// <paramref name="isModSwitch"/> distinguishes the two: a MOD switch resets
    /// and re-fetches the translation index (it's per-mod), while an INSTALL
    /// switch keeps it (the index doesn't change) and only re-applies the
    /// language menu (the active translation is per-install).
    /// </summary>
    private async Task ReloadActiveServiceAsync(bool isModSwitch)
    {
        var target = _config.GetActiveProfile();

        // Fresh service bound to the active profile + install. The constructor
        // does a synchronous fast-path lookup of the cached install path, so
        // _updateService.InstallPath is already populated for installs seen in a
        // previous session.
        _updateService = new UpdateService(_config, target);
        UpdateAccentResources(target);

        // Reset session caches tied to the previous service.
        _pendingDownloads = new();
        // Trust the synchronous cache check the constructor just did (avoids the
        // "Not installed → Installed" flicker). CheckAsync refines the rest.
        _modIsInstalled = !string.IsNullOrEmpty(_updateService.InstallPath);
        _warnedAboutBrokenInstall = false;
        // The translation INDEX is per-mod: reset it only on a mod switch. An
        // install switch keeps the same mod's index (re-fetching would be wasted
        // work and could clear a just-fetched list).
        if (isModSwitch)
            _cachedTranslationIndex = null;

        RefreshActiveModUi();
        UpdateActiveModHighlight();
        // Don't wipe a live background op's progress bars on a mod/copy switch.
        MaybeResetProgressUI();
        UpdateGameUI();
        RefreshIdlePanel();
        RefreshOperationGate();   // the displayed mod may differ from the operating one now

        if (_operatingModId != null)
        {
            // A background op is running on ANOTHER mod. Refresh the displayed mod from LOCAL
            // state only — the network CheckAsync toggles the shared _isBusy/_cts and would
            // disrupt the running op. The mod is installed (the user switched to it to play),
            // so a cached result or install-presence render is enough to make PLAY live.
            if (_checkResultCache.TryGetValue(target.Id, out var cachedB))
                ApplyCheckResult(cachedB);
            else
            {
                _modIsInstalled = !string.IsNullOrEmpty(_updateService.InstallPath);
                UpdateGameUI();
                RefreshOperationGate();
            }
        }
        else
        {
            // Re-detect install path + version + pending updates.
            await CheckAsync();
        }
        RefreshModCards();

        // Mod switch: re-fetch the per-mod translation index (gated like startup
        // so metered/offline users make no surprise network calls).
        if (isModSwitch && _config.CheckUpdatesOnStartup)
        {
            try { await RefreshTranslationIndexAsync(); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Translation index refresh after switch failed: {ex.Message}");
            }
        }

        // Repopulate the gear menu's translation list. The active-translation
        // indicator is per-INSTALL, so this runs for BOTH switch kinds.
        try { PopulateGameLanguageMenu(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"PopulateGameLanguageMenu after switch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Switches the active mod to a DIFFERENT registered install (a second copy
    /// in another folder) without restarting. Modelled on <see cref="LoadModProfile"/>:
    /// swaps the chosen <see cref="ModInstall"/> with the active flat fields,
    /// clears the global exe cache (both copies resolve the same exe name),
    /// persists, and rebuilds the service. No-op if the id is already active or
    /// unknown.
    /// </summary>
    private async Task SwitchActiveInstallAsync(string installId)
    {
        if (string.IsNullOrEmpty(installId)) return;

        // Block switching the ACTIVE install only while THIS mod is mid-op (a switch re-reads
        // its active install). A background op on ANOTHER mod doesn't block it.
        if (DisplayedModIsOperating)
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

        var state = _config.GetActiveState();
        if (string.Equals(state.ActiveInstallId, installId, StringComparison.OrdinalIgnoreCase))
            return; // already active

        var target = state.OtherInstalls
            .FirstOrDefault(i => string.Equals(i.Id, installId, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            DiagnosticLog.Write(
                $"SwitchActiveInstallAsync: unknown install id '{installId}' for '{_config.ActiveModId}'.");
            return;
        }

        if (_isCheckOnly)
        {
            try { _cts?.Cancel(); } catch { /* already disposed — ignore */ }
        }

        DiagnosticLog.Write(
            $"Switching active install for '{_config.ActiveModId}': '{state.InstallPath}' -> '{target.InstallPath}'");

        // Rotate the current active install into OtherInstalls and adopt the target.
        var previousActive = state.SnapshotActive();
        state.OtherInstalls.Remove(target);
        state.OtherInstalls.Add(previousActive);
        state.AdoptInstall(target);

        // Two copies of the SAME mod resolve the same exe name, so the global exe
        // cache must be cleared or PLAY could open the other copy (same trap as a
        // mod switch).
        _config.GameExecutable = "";
        try { _config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"Config save after install switch failed: {ex.Message}"); }

        // Invalidate the session check-result cache for THIS mod before reloading.
        // The cache is keyed by mod id (shared across copies), so without this the
        // reload's CheckAsync would replay the PREVIOUS copy's result for the new
        // copy — a different install/version — and never re-detect the copy's real
        // version from its own data\ files (so a behind copy would never surface its
        // Update CTA). Dropping it forces a full re-detection on the new copy's path.
        _checkResultCache.Remove(_config.ActiveModId);

        await ReloadActiveServiceAsync(isModSwitch: false);
        // The active copy just changed — reflect the new folder in the hero's copy chip
        // immediately (independent of whichever render path the reload took).
        RefreshActiveCopyChip();
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
    /// Publishes the active mod's accent colour into the App-level
    /// <c>ModAccentBrush</c> / <c>ModAccentBrushHover</c> resources so
    /// the few places that genuinely want per-mod identity (the legacy
    /// HeroBanner gradient fallback, dialog confirm buttons that opt
    /// into the mod-themed look) can pick it up via DynamicResource.
    ///
    /// IMPORTANT — Redesign 3 (dorado imperial):
    /// This method used to OVERWRITE the global <c>AccentBrush</c> with
    /// the per-mod colour. That turned the entire sidebar / brand title
    /// / progress strip / dashboard hover states red whenever WoL was
    /// the active mod (AccentColor = #c8102e), making the launcher look
    /// like a permanent error banner. The visual brief from the Stitch
    /// redesign is fixed dorado for chrome, per-mod red only where it
    /// reads as decoration (banner image fallback gradient). So
    /// <c>AccentBrush</c> now stays locked to the Stitch dorado defined
    /// in Styles/Colors.xaml (#e9c176) and per-mod identity lives on a
    /// separate brush key that the visible UI only consults in a few
    /// targeted spots.
    /// </summary>
    private static void UpdateAccentResources(ModProfile profile)
    {
        var accent = ParseAccentColor(profile.AccentColor);

        if (Application.Current?.Resources["ModAccentBrush"]
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
        Application.Current!.Resources["ModAccentBrush"] = accentBrush;
        Application.Current.Resources["ModAccentBrushHover"] = hoverBrush;
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
            bool isHttp = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                          || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            // .ico files contain multiple frames at different sizes. WPF's
            // generic BitmapImage decoder often picks the smallest frame
            // (e.g. 16×16) which looks awful at 44 px. Use IconBitmapDecoder
            // explicitly so we can pick the largest frame ourselves. An http
            // .ico can't go through File.OpenRead, so it takes the generic
            // async-download branch instead.
            bool isIco = !isHttp && uri.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
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
                // IgnoreImageCache: WPF caches decoded bitmaps by URI across the
                // app. Without this, a catalog image REPLACED under the same file
                // name (same on-disk path, new bytes) would repaint the stale
                // bitmap. We invalidate s_tileImageCache too (see
                // InvalidateTileImageCache); this covers WPF's own per-URI cache.
                // Local files only: for a remote URL that per-URI cache is the
                // session dedupe — bypassing it would re-download per render.
                if (!isHttp)
                    bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = sourceUri;
                bmp.EndInit();
                // A remote bitmap downloads async; if it fails, evict the memo so
                // the next render retries instead of serving an empty brush all
                // session (the memo caches the brush below before completion).
                if (isHttp && bmp.IsDownloading)
                    bmp.DownloadFailed += (_, _) => s_tileImageCache.Remove(uri);
                source = bmp;
            }

            if (source.CanFreeze) source.Freeze();
            if (!(source is System.Windows.Media.Imaging.BitmapImage { IsDownloading: true }))
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

    /// <summary>
    /// Drops the memoized brush for <paramref name="path"/> so the next
    /// <see cref="TryLoadTileImage"/> re-decodes from disk. Called when the
    /// background revalidation detects a catalog image was replaced under the
    /// same file name — the on-disk bytes changed but the path (cache key) did
    /// not, so the memo would otherwise serve the stale brush forever.
    /// </summary>
    private static void InvalidateTileImageCache(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            s_tileImageCache.Remove(path);
    }

    // ------------------------------------------------------------------------
    // Launcher Settings — global preferences dialog (scope: the whole
    // launcher, not the active mod). The per-mod gear menu in the sidebar
    // handles mod-specific concerns.
    // ------------------------------------------------------------------------

    // Single-instance reference for the non-modal Launcher Settings
    // dialog. The dialog moved from ShowDialog() to Show() so the user
    // can keep interacting with the main window while it's open — but
    // that means we have to guard against opening a second copy on top
    // of an existing one (each Show() creates a new Window with its own
    // HWND; clicking the gear three times shouldn't yield three settings
    // windows fighting for focus).
    private LauncherSettingsDialog? _launcherSettingsDialog;

    private void LauncherSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launcherSettingsDialog != null)
        {
            // Already open — focus + bring to front instead of opening
            // a duplicate. Activate() handles the case where the user
            // alt-tabbed away from it.
            _launcherSettingsDialog.Activate();
            return;
        }

        // NO Owner on purpose: an owned window with ShowInTaskbar=True (WPF
        // WS_EX_APPWINDOW) minimizes its owner when closed — that's the "closing
        // settings minimizes the launcher" bug. Like LobbyWindow, this is an
        // independent top-level window (its own taskbar button); Activate() below
        // brings it to front on first open.
        var dialog = new LauncherSettingsDialog(_config);
        _launcherSettingsDialog = dialog;

        // "Limpiar caché de iconos" wipes mod-assets\ but the window stays open;
        // re-download the images live so the user doesn't see monograms until a
        // restart. activeOnly:false covers the Workshop grid too.
        dialog.AssetsCleared = () => _ = RevalidateVisibleAssetsAsync(activeOnly: false);

        // "Clear translations cache" has no disk file to delete — invalidate the
        // in-memory index and re-fetch live so the list reflects the (possibly
        // just-changed) translations repo without a restart.
        dialog.TranslationsCacheCleared = () =>
        {
            _cachedTranslationIndex = null;
            _ = RefreshTranslationIndexAsync(reportStatus: false);
        };

        // Closed (fires for Save, Cancel, ✕, Esc, and Alt+F4) is the
        // single rendezvous point for post-dialog refresh. ChangesSaved
        // is the replacement for the old DialogResult — true only when
        // the user clicked Save and the dialog persisted its changes.
        dialog.Closed += (_, _) =>
        {
            // Race guard: if the user closed THIS dialog and a new one
            // has since been opened, don't clobber the new reference.
            if (ReferenceEquals(_launcherSettingsDialog, dialog))
                _launcherSettingsDialog = null;

            if (!dialog.ChangesSaved) return;

            // The dialog already applied the language change live (via
            // Strings.SetLanguage), persisted the config, and pushed the
            // autostart registration. Refresh anything that depends on
            // catalog repo / language that might not be wired through
            // events yet.
            RefreshIdlePanel();
            UpdateGameUI();
            // The translations source repo may have changed (new TRANSLATIONS
            // tab). Re-fetch the index so the language list reflects it — the
            // resolver reads config.TranslationsFolderRepo fresh each call.
            _ = RefreshTranslationIndexAsync(reportStatus: false);
            // Re-order the nav bar if the user changed the tab order in
            // the Interface section. switchToFirst:false so we don't
            // yank them off their current tab just for saving settings —
            // the "first tab opens" rule only applies at launch.
            ApplyTopTabOrder(switchToFirst: false);
            // The tray tooltip + menu labels follow the launcher language;
            // re-localise them so the user sees their new choice if they
            // right-click the tray icon.
            RefreshTrayLabels();
            // Tray icon's visibility depends on MinimizeToTray /
            // ShowToastNotifications — both may have flipped in the
            // dialog. Recompute so the icon appears/disappears from the
            // notification area without needing a restart.
            UpdateTrayIconVisibility();
        };

        dialog.Show();
        dialog.Activate(); // ownerless → ensure it comes to front on first open
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

        // Active multiplayer match? Ask before terminating — closing
        // the launcher kills AoE3 and (for hosts) drops every peer
        // mid-game. The dialog handles the cancel-broadcast cleanly
        // so we don't orphan room state on the Worker.
        try
        {
            if (MultiplayerView?.IsMatchActive == true)
            {
                // Run the dialog synchronously: OnClosing must complete
                // before WPF tears down the window. We can't await
                // inside OnClosing without re-entering, so we block
                // on the Task with a short timeout — the dialog runs
                // on the UI thread anyway, this is effectively
                // synchronous from the user's POV.
                var task = MultiplayerView.ConfirmCloseDuringMatchAsync();
                task.Wait(TimeSpan.FromSeconds(10));
                if (!task.IsCompleted || task.Result == false)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MainWindow.OnClosing: in-match confirm failed — {ex.Message}");
            // Fall through and let the close happen anyway — we'd
            // rather close than soft-lock the user out of the X.
        }
        // Real close — persist size/position/tab so the next launch comes
        // up where the user left it.
        SaveWindowState();

        // The Hardcodet TaskbarIcon creates a hidden Win32 window for
        // its shell-notification message loop. With WPF's default
        // ShutdownMode=OnLastWindowClose that hidden window would keep
        // the process alive even after MainWindow is gone — and the
        // user would see `Aoe3ModLauncher` stuck in Task Manager at
        // 0% CPU. App.xaml already sets ShutdownMode=OnMainWindowClose
        // to fix that; disposing the tray icon here is a belt-and-
        // suspenders so the icon also vanishes from the system tray
        // immediately rather than after Windows polls for it.
        try { TrayIcon?.Dispose(); } catch { /* shutdown path */ }

        // Polite multiplayer teardown — REST /leave so the Worker
        // marks the lobby `closed` and notifies other members.
        //
        // IMPORTANT: we run the leave on Task.Run, NOT directly on
        // the UI thread. `LeaveCurrentLobbyAsync` awaits HttpClient
        // which by default captures the current SynchronizationContext
        // for its continuations. If we blocked the UI thread on
        // GetResult(), the HTTP continuation would queue back onto
        // the (blocked) UI thread → classic deadlock. The user would
        // see the launcher freeze instead of closing.
        //
        // Task.Run runs the work on a thread-pool thread whose
        // SyncContext is null, so the continuations resume there
        // safely. We then Wait() with a tight 600 ms cap — long
        // enough to flush one HTTPS POST in normal conditions,
        // short enough not to feel hangy on a flaky network.
        try
        {
            var session = _multiplayerSession;
            if (session.Lobby != Services.Multiplayer.MultiplayerSession.LobbyStatus.Idle
                && session.CurrentLobbyId != null)
            {
                var leaveTask = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(550));
                    await session.LeaveCurrentLobbyAsync(cts.Token).ConfigureAwait(false);
                });
                leaveTask.Wait(TimeSpan.FromMilliseconds(600));
            }
        }
        catch (Exception ex) { DiagnosticLog.Write($"MP graceful leave: {ex.Message}"); }

        // Dispose is also fire-and-forget on the thread pool so it
        // never blocks the close.
        try { _ = Task.Run(async () => await _multiplayerSession.DisposeAsync().ConfigureAwait(false)); }
        catch (Exception ex) { DiagnosticLog.Write($"MP session dispose: {ex.Message}"); }

        base.OnClosing(e);

        // Last-resort guarantee that the process exits even if a
        // background thread we don't control (cloudflared HTTPS keep-
        // alive, a leftover Hardcodet timer) tried to keep it pinned.
        // OnLastWindowClose alone has been observed to miss this on
        // Windows 11 when third-party UI libraries register hidden
        // windows; an explicit Shutdown closes that gap.
        System.Windows.Application.Current.Shutdown();
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

    // ------------------------------------------------------------------------
    // Notification bell (Steam-style). The backing store is _notifications
    // (Services/NotificationCenter); these methods own the UI: badge, one-shot
    // pulse, dropdown panel, and click navigation.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Refresh the red unread badge + the panel's empty-state from the current
    /// notification state. Cheap; called on every <c>Changed</c>.
    /// </summary>
    private void RefreshNotificationBadge()
    {
        // Small unread indicator dot — shown whenever there's anything unread
        // (no count number; the panel carries the detail).
        NotificationDot.Visibility =
            _notifications.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationEmptyText.Visibility =
            _notifications.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshBellColor();
    }

    /// <summary>
    /// Colour the bell by state: hover OR panel-open → illuminate to white; else
    /// if there's an unread item → tint by the most-recent unread kind
    /// (blue=update available, green=update finished, gold=new translation);
    /// else a resting soft white. Driven entirely from code (not style triggers)
    /// so a local Fill can't get out-priced by a hover trigger.
    /// </summary>
    private void RefreshBellColor()
    {
        bool lit = _bellHover || NotificationPopup.IsOpen;
        var unread = _notifications.Items.FirstOrDefault(i => !i.Read);
        NotificationBellGlyph.Fill =
            lit ? _bellWhite
            : unread != null ? KindBrush(unread.Kind)
            : _bellSoftWhite;
    }

    private static System.Windows.Media.Brush KindBrush(NotificationKind kind) => kind switch
    {
        NotificationKind.UpdateAvailable => _bellBlue,
        NotificationKind.UpdateFinished => _bellGreen,
        NotificationKind.Installed => _bellGreen,
        NotificationKind.NewTranslation => _bellGold,
        NotificationKind.RoomCreated => _bellBlue,
        _ => _bellSoftWhite,
    };

    private void NotificationBell_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _bellHover = true;
        RefreshBellColor();
    }

    private void NotificationBell_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _bellHover = false;
        RefreshBellColor();
    }

    /// <summary>Localised labels for the bell tooltip + panel chrome.</summary>
    private void RefreshNotificationLabels()
    {
        NotifToolTip.Content = Strings.Get("NotifBellTooltip");
        NotificationPanelTitle.Text = Strings.Get("NotifPanelTitle");
        NotificationMarkAllRead.Content = Strings.Get("NotifMarkAllRead");
        NotificationClearAll.Content = Strings.Get("NotifClearAll");
        NotificationEmptyText.Text = Strings.Get("NotifEmpty");
    }

    /// <summary>
    /// One-shot attention pulse: a short bell "shake" played exactly once when a
    /// new notification arrives. No looping — the badge carries the persistent
    /// unread state; this is just the moment-of-arrival cue.
    /// </summary>
    private void PulseNotificationBell()
    {
        var shake = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(1300),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        void Key(double angle, double ms) => shake.KeyFrames.Add(
            new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                angle, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        Key(0, 0); Key(-18, 90); Key(15, 220); Key(-11, 360);
        Key(7, 500); Key(-4, 640); Key(0, 800);
        NotificationBellRotate.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty, shake);
    }

    private void NotificationBell_Click(object sender, RoutedEventArgs e)
    {
        // Clean toggle. The popup is StaysOpen=True (it never auto-closes), so
        // IsOpen always reflects reality here: 2nd click closes, 3rd reopens.
        // Outside clicks / window deactivation are handled in
        // CloseNotifOnOutsideClick / CloseNotifOnDeactivate (attached while open).
        bool open = !NotificationPopup.IsOpen;
        NotificationPopup.IsOpen = open;
        // Opening the panel clears the unread badge (the items stay in history).
        if (open) _notifications.MarkAllRead();
    }

    /// <summary>
    /// Closes the notification panel on a mouse-down anywhere outside it. The
    /// bell button itself is excluded so its own Click handler owns the toggle
    /// (otherwise the close here + the reopen there would fight). Clicks inside
    /// the panel don't reach this handler — the popup is its own visual tree.
    /// </summary>
    private void CloseNotifOnOutsideClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsWithin(e.OriginalSource as DependencyObject, NotificationBellButton))
            return;
        NotificationPopup.IsOpen = false;
    }

    private void CloseNotifOnDeactivate(object? sender, EventArgs e)
        => NotificationPopup.IsOpen = false;

    /// <summary>True if <paramref name="node"/> is <paramref name="ancestor"/> or a visual descendant of it.</summary>
    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void NotificationMarkAllRead_Click(object sender, RoutedEventArgs e)
        => _notifications.MarkAllRead();

    private void NotificationClearAll_Click(object sender, RoutedEventArgs e)
    {
        _notifications.Clear();
        NotificationPopup.IsOpen = false;
    }

    private void NotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not NotificationItem item) return;
        NotificationPopup.IsOpen = false;
        _notifications.MarkRead(item);
        NavigateToNotification(item);
    }

    /// <summary>
    /// Click navigation for a notification: switch to its mod and surface the
    /// relevant UI (Library for updates, the translations menu for a new pack).
    /// </summary>
    private void NavigateToNotification(NotificationItem item)
    {
        // Launcher-update: not tied to a mod — open the self-update dialog directly.
        if (item.Kind == NotificationKind.LauncherUpdate)
        {
            try { LauncherUpdatePill_Click(this, new RoutedEventArgs()); }
            catch (Exception ex) { DiagnosticLog.Write($"Notification → launcher update failed: {ex.Message}"); }
            return;
        }
        // Connectivity: informational only — nothing to navigate to.
        if (item.Kind == NotificationKind.Connectivity)
            return;

        // New room created: open Multiplayer → Rooms (before the profile guard,
        // since a room's mod need not resolve to a catalog profile here).
        if (item.Kind == NotificationKind.RoomCreated)
        {
            try { SwitchTopTab(TopTab.Multiplayer); MultiplayerView.ShowRooms(); }
            catch (Exception ex) { DiagnosticLog.Write($"Notification → rooms failed: {ex.Message}"); }
            return;
        }

        var profile = ModRegistry.Find(item.ModId);
        if (profile == null) return;

        // New mod in the catalog: open the Workshop with its detail panel.
        if (item.Kind == NotificationKind.NewMod)
        {
            try
            {
                SwitchTopTab(TopTab.Mods);
                ModsBrowserView?.ShowDetail(profile);
            }
            catch (Exception ex) { DiagnosticLog.Write($"Notification → new mod detail failed: {ex.Message}"); }
            return;
        }

        // Switch to the mod if it isn't already active (LoadModProfile is a
        // no-op-ish when already on it, but guard to avoid a needless reload).
        if (!string.Equals(_updateService.Profile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
            LoadModProfile(profile);

        // Bring the Library (dashboard) tab forward — it carries the PLAY /
        // UPDATE button the user wants to reach.
        SwitchTopTab(TopTab.Play);

        if (item.Kind == NotificationKind.NewTranslation)
        {
            // Open Mod Properties on the Language tab — the reliable "change
            // translation" surface (the full pack list with Apply). This replaces
            // the old attempt to force the gear ContextMenu's submenu open, which
            // did nothing because the parent menu was closed.
            try
            {
                OpenModPropertiesDialog(profile);
                _modPropertiesDialog?.ShowLanguageTab();
            }
            catch (Exception ex) { DiagnosticLog.Write($"Open translations tab failed: {ex.Message}"); }
        }
        // UpdateAvailable / UpdateFinished: landing on the Library/dashboard tab
        // above is the right target — the UPDATE / PLAY CTA lives right there.
    }

    /// <summary>
    /// Keeps the notification popup glued to the bell while the window is dragged or
    /// resized. A WPF Popup computes its placement only when it opens and doesn't
    /// track the owner window, so we nudge its offset to force a placement recompute
    /// against <c>NotificationBellButton</c>. Attached only while the popup is open.
    /// </summary>
    private void RepositionNotifPopup(object? sender, EventArgs e)
    {
        if (NotificationPopup == null || !NotificationPopup.IsOpen) return;
        var o = NotificationPopup.HorizontalOffset;
        NotificationPopup.HorizontalOffset = o + 1;
        NotificationPopup.HorizontalOffset = o;
    }

    /// <summary>
    /// Startup/refresh reconciliation for the "update finished" bell (called from
    /// ApplyCheckResult). Seeds a SILENT baseline the first time we see a version for
    /// this install; on a version ADVANCE since the last recorded value, raises the
    /// bell in the user's own session (idempotent with ApplyAsync's direct raise,
    /// which dedups on the visible list); then records the new version. Offline/
    /// degraded results are ignored.
    /// </summary>
    private void ReconcileUpdateFinishedNotification(UpdateService.CheckResult result)
    {
        if (result.Degraded || !result.IsValidInstall) return;
        var detected = result.CurrentVersion?.Ver;
        if (string.IsNullOrEmpty(detected)) return;

        var state = _config.GetState(_updateService.Profile.Id);
        var baseline = state.NotifiedInstalledVersion;
        if (string.IsNullOrEmpty(baseline))
        {
            state.NotifiedInstalledVersion = detected;     // silent baseline, no bell
            try { _config.Save(); } catch { /* best-effort */ }
            return;
        }
        if (string.Equals(baseline, detected, StringComparison.OrdinalIgnoreCase)) return;

        if (IsVersionAdvance(baseline, detected))
        {
            _notifications.RaiseUpdateFinished(
                _updateService.Profile.Id, detected,
                Strings.Get("NotifUpdateFinishedTitle"),
                Strings.Format("NotifUpdateFinishedBody",
                    _updateService.Profile.DisplayName, detected));
        }
        state.NotifiedInstalledVersion = detected;
        try { _config.Save(); } catch { /* best-effort */ }
    }

    /// <summary>
    /// True when <paramref name="to"/> is newer than <paramref name="from"/> using the
    /// shared "X.Y.Z[letter]" ordering (<see cref="LauncherUpdateService.TryParseSemVer"/>).
    /// If either doesn't parse, any change counts as an advance — better a rare wrong
    /// "finished" than silence after a real update.
    /// </summary>
    private static bool IsVersionAdvance(string from, string to)
    {
        if (LauncherUpdateService.TryParseSemVer(from, out var a)
            && LauncherUpdateService.TryParseSemVer(to, out var b))
            return b > a;
        return !string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
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
        if (DashboardCopyChip != null)
            DashboardCopyChip.ToolTip = Strings.Get("DashboardActiveCopyTooltip");

        // Localized hover tooltips for the dashboard's mod buttons (these were
        // previously hardcoded English or had none). The primary CTA tooltip is
        // dynamic (set per-state in SetPrimaryAction). Set here so they follow the
        // language switch. Null-guarded because ApplyLanguage can run early.
        if (DashboardSettingsButton != null)
            DashboardSettingsButton.ToolTip = TooltipHelper.Wrap(Strings.Get("TipGearButton"));
        if (DashboardChangeModButton != null)
            DashboardChangeModButton.ToolTip = TooltipHelper.Wrap(Strings.Get("TipChangeMod"));
        if (DashboardSearchInstallButton != null)
            DashboardSearchInstallButton.ToolTip = TooltipHelper.Wrap(Strings.Get("TipSearchInstall"));
        if (DashboardPauseButton != null)
            DashboardPauseButton.ToolTip = TooltipHelper.Wrap(Strings.Get("TipPauseResume"));
        if (DashboardCancelButton != null)
            DashboardCancelButton.ToolTip = TooltipHelper.Wrap(Strings.Get("TipCancel"));
        // The "INSTALLED VERSION / LATEST AVAILABLE" labels lived in the
        // top-of-sidebar status box that was removed; the ProgressPanel
        // at the bottom now covers the same info via RefreshIdlePanel.
        MainTabsControl.NewsPlaceholderText.Text = Strings.Get("NewsPlaceholder");

        // Sidebar nav labels (Redesign 2 — vertical sidebar). The Buttons
        // keep their old TopTab* x:Names so SwitchTopTab() and
        // RefreshTopTabHighlight() don't need to change, but the actual
        // visible label now lives in an inner TextBlock (SidebarLabel*)
        // because each button hosts an icon+label StackPanel as Content.
        SidebarLabelDashboard.Text = Strings.Get("TopTabPlay");
        SidebarLabelCatalog.Text = Strings.Get("TopTabMods");
        SidebarLabelMultiplayer.Text = Strings.Get("TopTabMultiplayer");
        // (SidebarLabelSettings / SidebarLabelSupport / SidebarLabelWiki
        // no longer exist in MainWindow.xaml — Support+Wiki were
        // collapsed into a single SETTINGS row, then SETTINGS itself
        // got pulled out of the sidebar and into the brand menu
        // (click "AOE3 LAUNCHER" wordmark). The orphan handlers and
        // string keys live on as dead code in case we want to revive
        // any of them.)

        // v1.0 multiplayer tab — propagate language changes into the
        // UserControl's own string cache. Layout strings (subtabs, sign-in
        // gate copy) are owned by the control itself.
        MultiplayerView.RefreshStrings();
        SettingsTeaserText.Text = Strings.Get("SettingsTabTeaser");
        OpenSettingsTabButton.Content = Strings.Get("SettingsTabOpen");

        // v0.9 mods browser: header strings + filter labels + empty-state.
        // Cards are (re)rendered by RefreshModsBrowser whenever ModRegistry
        // or the active mod changes; ApplyLanguage only updates static
        // chrome. NotInstalledStateText is the literal "Not installed"
        // string used by ProbeInstalledState — feeding it in here lets the
        // "only installed" toggle do a pure-string comparison instead of
        // duplicating the disk probe inside the UserControl.
        // Header chrome.
        ModsBrowserView.HeaderTitleText = Strings.Get("ModsBrowserHeaderTitle");
        ModsBrowserView.HeaderSubtitleText = Strings.Get("ModsBrowserHeaderSubtitle");
        ModsBrowserView.EmptyMessage = Strings.Get("ModsBrowserEmpty");
        ModsBrowserView.DetailEmptyMessage = Strings.Get("ModsBrowserDetailEmpty");
        ModsBrowserView.SearchPlaceholder = Strings.Get("ModsBrowserSearchPlaceholder");
        ModsBrowserView.ListSummaryFormat = Strings.Get("ModsBrowserListSummary");

        // Header action buttons.
        ModsBrowserView.RefreshCatalogLabel = Strings.Get("ModsBrowserRefreshCatalog");
        ModsBrowserView.RefreshCatalogTooltip = Strings.Get("TipWsRefreshCatalog");
        ModsBrowserView.AddLocalModLabel = Strings.Get("ModsBrowserAddLocal");
        ModsBrowserView.AddLocalModTooltip = Strings.Get("TipWsAddLocal");
        ModsBrowserView.PublishModLabel = Strings.Get("ModsBrowserMenuPublish");
        ModsBrowserView.PublishModTooltip = Strings.Get("TipWsPublish");
        ModsBrowserView.SubTabMyModsLabel = Strings.Get("ModsBrowserSubTabMyMods");
        ModsBrowserView.SubTabMyModsTooltip = Strings.Get("TipWsSubTabMyMods");
        ModsBrowserView.SubTabCatalogLabel = Strings.Get("ModsBrowserSubTabCatalog");
        ModsBrowserView.SubTabCatalogTooltip = Strings.Get("TipWsSubTabCatalog");

        // Filter chips + sort dropdown.
        ModsBrowserView.FiltersLabelText = Strings.Get("ModsBrowserFiltersLabel");
        ModsBrowserView.SortLabelText = Strings.Get("ModsBrowserSortLabel");
        ModsBrowserView.SetFilterLabels(
            Strings.Get("ModsBrowserFilterAll"),
            Strings.Get("ModsBrowserFilterInstalled"),
            Strings.Get("ModsBrowserFilterNotInstalled"),
            Strings.Get("ModsBrowserFilterUpdates"),
            Strings.Get("ModsBrowserFilterCompatible"));
        ModsBrowserView.SetFilterTooltips(
            Strings.Get("TipWsFilterAll"),
            Strings.Get("TipWsFilterInstalled"),
            Strings.Get("TipWsFilterNotInstalled"),
            Strings.Get("TipWsFilterUpdates"),
            Strings.Get("TipWsFilterCompatible"));
        ModsBrowserView.SetSortItems(
            Strings.Get("ModsBrowserSortRecent"),
            Strings.Get("ModsBrowserSortName"),
            Strings.Get("ModsBrowserSortStatus"));

        // Status badge labels.
        ModsBrowserView.BadgeNotInstalled = Strings.Get("ModsBrowserBadgeNotInstalled");
        ModsBrowserView.BadgeInstalled = Strings.Get("ModsBrowserBadgeInstalled");
        ModsBrowserView.BadgeUpdateAvailable = Strings.Get("ModsBrowserBadgeUpdate");
        ModsBrowserView.BadgeIncompatible = Strings.Get("ModsBrowserBadgeIncompatible");
        ModsBrowserView.BadgeError = Strings.Get("ModsBrowserBadgeError");

        // Detail panel labels.
        ModsBrowserView.DetailDeveloperLabel = Strings.Get("ModsBrowserDetailDeveloper");
        ModsBrowserView.DetailVersionLabel = Strings.Get("ModsBrowserDetailVersion");
        ModsBrowserView.DetailAvailableVersionLabel = Strings.Get("ModsBrowserDetailAvailable");
        ModsBrowserView.DetailInstallTypeLabel = Strings.Get("ModsBrowserDetailInstallType");
        ModsBrowserView.DetailUpdateMechLabel = Strings.Get("ModsBrowserDetailUpdates");
        ModsBrowserView.DetailWebsiteLabel = Strings.Get("ModsBrowserDetailWebsite");
        ModsBrowserView.DetailLanguagesLabel = Strings.Get("ModsBrowserDetailLanguages");
        ModsBrowserView.GalleryTitleText = Strings.Get("WorkshopGalleryTitle");
        ModsBrowserView.DetailInstallLabel = Strings.Get("ModsBrowserActionInstall");
        ModsBrowserView.DetailUpdateLabel = Strings.Get("ModsBrowserActionUpdate");
        ModsBrowserView.DetailPlayLabel = Strings.Get("ModsBrowserActionPlay");
        ModsBrowserView.DetailRepairLabel = Strings.Get("ModsBrowserActionRepair");
        ModsBrowserView.DetailIncompatibleLabel = Strings.Get("ModsBrowserActionIncompatible");
        ModsBrowserView.DetailViewWebsiteLabel = Strings.Get("ModsBrowserActionViewWebsite");
        ModsBrowserView.DetailSwitchActiveLabel = Strings.Get("ModsBrowserActionSwitchActive");
        ModsBrowserView.DetailUninstallLabel = Strings.Get("ModsBrowserActionUninstall");
        // Workshop redesign: per-row Add/Remove + Built-in labels.
        ModsBrowserView.BtnAddToCollectionLabel = Strings.Get("ModsBrowserBtnAdd");
        ModsBrowserView.BtnRemoveFromCollectionLabel = Strings.Get("ModsBrowserBtnRemove");
        ModsBrowserView.BtnBuiltinLabel = Strings.Get("ModsBrowserBtnBuiltin");

        // Header ⋯ menu is empty now — publish was promoted to its own
        // accent-outlined header button (PublishModButton), so the overflow
        // hides itself. SetMoreMenuItems stays wired for future secondary
        // actions; passing no items collapses the ⋮ button.
        ModsBrowserView.SetMoreMenuItems();

        RefreshModsBrowser();

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
        // (LauncherSettingsButtonText label retranslation removed — the
        // gear-cog button in the hero corner was retired; the brand-menu
        // entry that replaces it pulls its label from Strings on every
        // popup build, so no per-language UI pump is needed here.)
        // Tray labels follow the launcher language. Safe to call even
        // when the tray icon is hidden; ContextMenu lives on the XAML
        // element regardless of Visibility.
        RefreshTrayLabels();
        RefreshNotificationLabels();
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
        ActionPanelControl.MenuInstallAnotherCopy.Header = Strings.Get("MenuInstallAnotherCopy");
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
        ActionPanelControl.MenuInstallAnotherCopy.ToolTip = BuildMenuTooltip(
            (string)ActionPanelControl.MenuInstallAnotherCopy.Header, Strings.Get("TooltipMenuInstallAnotherCopy"));
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

        // (EN/ES toggle highlight removed — the inline language toggles
        // were retired from the hero corner. Users now pick the
        // launcher language from LauncherSettingsDialog's LanguageCombo,
        // which doesn't need an external "active state" highlight
        // because the combo itself shows the current selection.)
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
        // Keep an open Mod Properties window in sync with the new AoE3 path.
        _modPropertiesDialog?.RefreshData();
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

        // Resync the cinema-dashboard version chip — the StatusCard
        // re-paint flow is the canonical "we just learned a new
        // version" point (called by RefreshStatusCard which runs after
        // CheckAsync and after install/update/uninstall). Without this
        // the chip would stay hidden until the user manually switches
        // mods (which is when RefreshActiveModBanner gets called).
        if (DashboardVersionChip != null)
        {
            var ver = _updateService.CurrentVersion?.Ver;
            if (!string.IsNullOrWhiteSpace(ver))
            {
                DashboardVersionText.Text = ver.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? ver
                    : "v " + ver;
                DashboardVersionChip.Visibility = Visibility.Visible;
            }
            else
            {
                DashboardVersionChip.Visibility = Visibility.Collapsed;
            }
        }
        RefreshActiveCopyChip();

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

        // Idle state: revert the dashboard bar to its style-default
        // dorado gradient + reset the label/icon back to AccentBrush
        // gold so the strip doesn't keep the previous operation's
        // colour after it finishes. ClearValue drops the local
        // Foreground brush and falls back to the style setter.
        if (DashboardProgressBar != null)
        {
            DashboardProgressBar.ClearValue(System.Windows.Controls.ProgressBar.ForegroundProperty);
        }
        if (DashboardProgressIcon != null)
        {
            DashboardProgressIcon.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
        }
        if (DashboardProgressLabel != null)
        {
            DashboardProgressLabel.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
        }

        // Mirror into the visible cinema-dashboard progress strip.
        SyncDashboardProgressFromLegacyPanel();
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
    /// Copies the (hidden) legacy ProgressPanelControl state into the
    /// visible cinema-dashboard progress strip. The legacy panel still
    /// owns the source of truth for everything install/update related
    /// — call this whenever its values change so the dashboard mirrors
    /// it. Idle state shows "—" placeholders; running state shows live
    /// speed/eta/percent so the user can read progress without leaving
    /// the cinema view.
    /// </summary>
    private void SyncDashboardProgressFromLegacyPanel()
    {
        if (DashboardProgressLabel == null) return;

        // The strip ALWAYS shows the current op, on every mod's dashboard — its title already
        // names the mod ("Installing Wars of Liberty"), which is exactly the "what's installing
        // in the background" indicator. The displayed mod's PLAY stays live via the per-install
        // gate (RefreshOperationGate), so showing the op here doesn't block playing another mod.
        var idle = _progressState == ProgressState.Idle;
        DashboardProgressLabel.Text = idle
            ? Strings.Get("ProgressIdleTitle")
            : ProgressPanelControl.ProgressTitleText.Text ?? string.Empty;
        DashboardProgressSubtitle.Text = idle
            ? string.Empty
            : ProgressPanelControl.ProgressStepText.Text ?? string.Empty;
        DashboardProgressSpeed.Text = string.IsNullOrEmpty(ProgressPanelControl.SpeedText.Text)
            ? "—"
            : ProgressPanelControl.SpeedText.Text;
        DashboardProgressEta.Text = string.IsNullOrEmpty(ProgressPanelControl.EtaText.Text)
            ? "—"
            : ProgressPanelControl.EtaText.Text;
        DashboardProgressPercent.Text = string.IsNullOrEmpty(ProgressPanelControl.OverallBytesText.Text)
            ? "—"
            : ProgressPanelControl.OverallBytesText.Text;

        // ProgressBar mirrors the OverallProgress value (single-bar
        // progress in the cinema view — the legacy panel has both
        // patch + overall but the user-facing summary is "overall").
        DashboardProgressBar.IsIndeterminate = ProgressPanelControl.OverallProgress.IsIndeterminate;
        DashboardProgressBar.Value = ProgressPanelControl.OverallProgress.Value;

        // Pause/Cancel actions follow the legacy
        // ProgressRunningActions row exactly: visible whenever the
        // legacy panel would show its inline pause/cancel buttons.
        // Cancel is always enabled while visible; Pause inherits the
        // legacy button's enablement (some operations like extracting
        // or verifying can't be paused — only the download phase can).
        if (DashboardProgressActions != null)
        {
            // Pause/Cancel follow the op wherever it's shown, so the background op can be
            // paused/cancelled from any mod's dashboard.
            DashboardProgressActions.Visibility =
                ProgressPanelControl.ProgressRunningActions.Visibility;
            DashboardPauseButton.IsEnabled = ProgressPanelControl.PauseButton.IsEnabled;
            DashboardCancelButton.IsEnabled = ProgressPanelControl.CancelButton.IsEnabled;

            // Pause↔Resume icon flip. The legacy button stores the
            // current state in its Content (BtnResume vs BtnPause
            // string), but reading those localized strings back is
            // fragile, so we infer from IsEnabled + the bar's
            // indeterminate state. Simpler & more robust: when the
            // legacy panel labelled itself "Reanudar", show the
            // Play glyph; otherwise the Pause glyph.
            DashboardPauseButtonIcon.Text = string.Equals(
                ProgressPanelControl.PauseButton.Content?.ToString(),
                Strings.Get("BtnResume"), StringComparison.Ordinal)
                ? ""   // Play (we're currently paused)
                : "";  // Pause (we're currently running)
        }
    }

    // -- Rotating dashboard hero (crossfade) ---------------------------------
    //
    // The dashboard background is two stacked Borders: DashboardBgFill (base,
    // always opaque) and DashboardBgFillB (overlay, opacity 0 at rest). To
    // advance, we paint the next hero into the overlay and fade it in; when the
    // fade completes we copy that brush onto the base and snap the overlay back
    // to 0 with no flicker. A single mod with one hero (or none) never starts
    // the timer — byte-for-byte the old static behaviour.
    private const int HeroRotateSeconds = 7;
    private const double HeroCrossfadeSeconds = 1.1;
    private const int HeroDecodeWidth = 2560;   // cap 4K heroes so RAM stays bounded
    private System.Windows.Threading.DispatcherTimer? _heroRotateTimer;
    private List<string> _heroRotateList = new();
    private int _heroRotateIndex;

    /// <summary>
    /// Resolves the active mod's effective hero list and paints the dashboard
    /// background. Rotating heroes win; else the single hero; else the banner;
    /// else a neutral gradient — each preferring the cached local file and
    /// falling back to the live catalog URL
    /// (<see cref="ModProfile.ResolveHeroSources"/>, so a just-activated mod
    /// paints immediately while its disk cache fills). With 2+ heroes it
    /// (re)starts the crossfade rotation.
    /// </summary>
    private void ApplyDashboardHero(ModProfile profile)
    {
        if (DashboardBgFill == null) return;

        // Stop any in-flight rotation/animation before repainting (mod switch).
        StopHeroRotation();

        var heroes = profile.ResolveHeroSources().ToList();

        // Paint the first frame (or the gradient fallback) on the base layer.
        var first = heroes.Count > 0 ? BuildHeroFillBrush(heroes[0]) : null;
        DashboardBgFill.Background = (System.Windows.Media.Brush?)first ?? BuildNeutralHeroGradient();
        if (DashboardBgFillB != null)
        {
            DashboardBgFillB.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            DashboardBgFillB.Opacity = 0;
            DashboardBgFillB.Background = null;
        }

        // Rotate only when there are 2+ real heroes AND the overlay layer exists.
        _heroRotateList = (first != null && heroes.Count >= 2 && DashboardBgFillB != null)
            ? heroes
            : new List<string>();
        _heroRotateIndex = 0;
        UpdateHeroRotationTimer();
    }

    /// <summary>Starts the rotation timer when eligible (2+ heroes, dashboard visible); stops it otherwise.</summary>
    private void UpdateHeroRotationTimer()
    {
        bool eligible = _heroRotateList.Count >= 2 && PlayView != null && PlayView.IsVisible;
        if (eligible)
        {
            if (_heroRotateTimer == null)
            {
                _heroRotateTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(HeroRotateSeconds),
                };
                _heroRotateTimer.Tick += HeroRotateTimer_Tick;
            }
            if (!_heroRotateTimer.IsEnabled) _heroRotateTimer.Start();
        }
        else
        {
            _heroRotateTimer?.Stop();
        }
    }

    /// <summary>Stops the rotation and clears the list (used on mod switch / no-hero).</summary>
    private void StopHeroRotation()
    {
        _heroRotateTimer?.Stop();
        _heroRotateList = new List<string>();
        _heroRotateIndex = 0;
    }

    private void HeroRotateTimer_Tick(object? sender, EventArgs e)
    {
        if (_heroRotateList.Count < 2 || DashboardBgFill == null || DashboardBgFillB == null)
        {
            _heroRotateTimer?.Stop();
            return;
        }

        _heroRotateIndex = (_heroRotateIndex + 1) % _heroRotateList.Count;
        var next = BuildHeroFillBrush(_heroRotateList[_heroRotateIndex]);
        if (next == null) return;   // skip a bad frame; try again next tick

        DashboardBgFillB.Background = next;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(
            0.0, 1.0, TimeSpan.FromSeconds(HeroCrossfadeSeconds));
        fade.Completed += (_, __) =>
        {
            // Snap the base to the new image, then hide the overlay with no flicker.
            DashboardBgFill.Background = next;
            DashboardBgFillB.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
            DashboardBgFillB.Opacity = 0;
        };
        DashboardBgFillB.BeginAnimation(System.Windows.UIElement.OpacityProperty, fade);
    }

    /// <summary>Builds a Stretch.Fill hero brush with a decode cap (4K → bounded RAM, crisp downscale).</summary>
    private static System.Windows.Media.ImageBrush? BuildHeroFillBrush(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        try
        {
            var sourceUri = new Uri(uri, UriKind.RelativeOrAbsolute);
            bool isHttp = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                          || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            // IgnoreImageCache so a same-name catalog replacement repaints (mirrors
            // TryLoadTileImage) — local files only; for a remote hero WPF's per-URI
            // cache is the session dedupe. Only cap the decode when the source is
            // wider than the cap, so a 1080p/1440p hero decodes native (no
            // upscale); a 4K hero downscales to HeroDecodeWidth. HighQuality
            // scaling (set on the host Borders in XAML) keeps the result crisp.
            // (TryGetImageWidth is null for remote → a live-painted hero decodes
            // native, an accepted cost of the no-disk-cache policy.)
            if (!isHttp)
                bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            var nativeWidth = TryGetImageWidth(uri);
            if (nativeWidth is int w && w > HeroDecodeWidth
                && !uri.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                bmp.DecodePixelWidth = HeroDecodeWidth;
            bmp.UriSource = sourceUri;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            var brush = new System.Windows.Media.ImageBrush(bmp)
            {
                Stretch = System.Windows.Media.Stretch.Fill,
            };
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Hero image load failed for '{uri}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Cheap metadata-only read of an on-disk image's pixel width (null for pack:// / http(s) or on error).</summary>
    private static int? TryGetImageWidth(string uri)
    {
        try
        {
            if (uri.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            using var fs = System.IO.File.OpenRead(uri);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                fs,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            return frame.PixelWidth;
        }
        catch { return null; }
    }

    private System.Windows.Media.Brush BuildNeutralHeroGradient()
    {
        var neutralGradient = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
        };
        neutralGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            ((System.Windows.Media.SolidColorBrush)Brush("#131313")).Color, 0));
        neutralGradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            ((System.Windows.Media.SolidColorBrush)Brush("#201f1f")).Color, 1));
        neutralGradient.Freeze();
        return neutralGradient;
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

        // Prefer the community banner (cached local → live catalog URL);
        // otherwise the built-in tile image (BannerImage — this small sidebar
        // tile is the one banner surface where the packed .ico still looks
        // fine). All paths flow through TryLoadTileImage.
        var imgBrush = TryLoadTileImage(profile.ResolveBannerSource() ?? profile.BannerImage);
        if (imgBrush != null)
        {
            // Show the image plus a dark vignette gradient for legible text.
            ActiveModBanner.HostBackground = imgBrush;
        }
        else
        {
            ActiveModBanner.HostBackground = gradient;
        }

        // Mirror the active mod into the visible cinema dashboard. The
        // legacy HeroBanner stays alive inside LegacyPlayContent (hidden
        // — it owns a bunch of state references the rest of the code-
        // behind still touches), but the user actually sees DashboardBgFill
        // / DashboardTitleText / DashboardDescText. Title is upper-cased
        // for the cinematic feel; description falls back to the profile
        // subtitle when no description was supplied by the catalog.
        //
        // The cinema dashboard background fallback uses a NEUTRAL dark
        // gradient (BgBase → BgPanelAlt) — NOT the per-mod accent — so
        // a missing banner never paints the hero panel red when WoL is
        // active. The hero stays imperial/dorado regardless of which
        // mod is selected, matching the Stitch redesign brief. Per-mod
        // colour only shows up via the actual hero/banner image.
        //
        // Hero resolution priority for the dashboard (highest first, each
        // step preferring cached local over live catalog URL — see
        // ModProfile.ResolveHeroSources):
        //   1. Hero image(s) — 1920×1080 (the right tool for a full-bleed
        //      dashboard panel)
        //   2. Banner — 1200×300 (will look slightly stretched on the
        //      dashboard but better than no image)
        //   3. Neutral dark gradient — the fallback when the mod ships
        //      neither hero nor banner
        // BannerImage (the mod's .ico) is deliberately NOT used here
        // because a 256×256 square icon stretched to a 1920-wide panel
        // looks heavily distorted.
        ApplyDashboardHero(profile);
        if (DashboardTitleText != null)
        {
            // Stack a "Game: Subtitle"-style name onto two lines in the hero
            // so it reads vertically instead of sprawling across (and
            // clipping) the panel width — e.g. "Age of Empires III: The Asian
            // Dynasties" renders as "AGE OF EMPIRES III:" / "THE ASIAN
            // DYNASTIES". The colon stays on the first line (we break right
            // after it). Names without a "colon + space" (Wars of Liberty,
            // Improvement Mod) stay on a single line. The canonical
            // DisplayName is untouched; this only affects the hero render.
            DashboardTitleText.Text = (profile.DisplayName ?? string.Empty)
                .ToUpperInvariant()
                .Replace(": ", ":\n");
        }
        if (DashboardIconHost != null)
        {
            // Mod/game icon above the title. Same resolution as the Workshop
            // tiles (cached catalog icon.png → live catalog URL → built-in
            // packed icon); collapses to nothing when the mod ships no icon.
            var iconBrush = TryLoadTileImage(profile.ResolveIconSource());
            DashboardIconHost.Background = iconBrush;
            DashboardIconHost.Visibility = iconBrush != null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (DashboardDescText != null)
        {
            // Description is a Dictionary<lang, text> on the profile;
            // resolve against the active UI language with an "en" fallback,
            // then fall back to the short Subtitle ("Launcher", "AoE3:TAD
            // overhaul", …) if no localized description was supplied.
            var lang = _config.Language ?? "en";
            string? localized = null;
            if (profile.Description != null)
            {
                if (!profile.Description.TryGetValue(lang, out localized))
                    profile.Description.TryGetValue("en", out localized);
            }
            DashboardDescText.Text = !string.IsNullOrWhiteSpace(localized)
                ? localized!
                : (profile.Subtitle ?? string.Empty);
        }
        if (DashboardChangeModButtonText != null)
        {
            // Locale-aware "SWITCH GAME" / "CAMBIAR JUEGO" label.
            DashboardChangeModButtonText.Text = Strings.Get("DashboardChangeMod");
        }
        if (DashboardSearchInstallButtonText != null)
            DashboardSearchInstallButtonText.Text = Strings.Get("SearchInstallButtonShort");
        // Version chip — installed version (CurrentVersion) of the
        // active mod. Hidden when no version is known yet (fresh
        // machine before CheckAsync ran, or mod not installed). The
        // "v " prefix is added defensively when the version string
        // doesn't already start with one — some catalog manifests
        // ship "1.0.0", others "v1.0.0".
        if (DashboardVersionChip != null)
        {
            var ver = _updateService?.CurrentVersion?.Ver;
            if (!string.IsNullOrWhiteSpace(ver))
            {
                DashboardVersionText.Text = ver.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? ver
                    : "v " + ver;
                DashboardVersionChip.Visibility = Visibility.Visible;
            }
            else
            {
                DashboardVersionChip.Visibility = Visibility.Collapsed;
            }
        }
        RefreshActiveCopyChip();

        // Kick the disk-cache fill unconditionally: this runs on every
        // activation, and the ACTIVE mod is always cache-eligible, so a
        // just-activated mod that was painting live from its catalog URLs
        // gets its assets written to disk here (the "needs X" gates are gone —
        // with live URL painting the brushes are never null, so a needs-based
        // gate would never fire again). EnsureModAssetsAsync itself gates on
        // installed/active/operating and dedupes per session.
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

        var items = feed?.Items;
        if (items == null || items.Count == 0)
        {
            MainTabsControl.NewsPlaceholderText.Visibility = Visibility.Visible;
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
            return;
        }

        MainTabsControl.NewsPlaceholderText.Visibility = Visibility.Collapsed;
        foreach (var item in filtered)
        {
            MainTabsControl.NewsCardsPanel.Children.Add(BuildNewsCard(item));
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
            FontSize = (double)FindResource("FontSizeSubtitle"),
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
                FontSize = (double)FindResource("FontSizeCaption"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 2, 0, 6),
            });
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = body,
                FontSize = (double)FindResource("FontSizeBody"),
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
                FontSize = (double)FindResource("FontSizeCaption"),
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
    /// Per-session guard so a mod's gallery screenshots are fetched at most once
    /// (the detail panel can re-open many times). Separate from
    /// <see cref="_assetFetchAttempted"/> so opening a detail doesn't block the
    /// icon/banner fetch and vice-versa.
    /// </summary>
    private readonly HashSet<string> _screenshotFetchAttempted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Profile ids whose shortcut icons we've already tried to heal this
    /// session. Older installs wrote a <c>.png</c> path into the desktop /
    /// Start Menu <c>.lnk</c> IconLocation (which Windows can't render — it
    /// falls back to the exe icon); once per mod per session we repoint such
    /// shortcuts at a real <c>.ico</c>. See
    /// <see cref="Services.NativeInstallService.TryHealShortcutIcons"/>.
    /// </summary>
    private readonly HashSet<string> _shortcutHealAttempted = new(StringComparer.OrdinalIgnoreCase);

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
        // Disk-cache gate — BEFORE the session guard, so an ineligible mod
        // doesn't consume its one attempt: when it later becomes eligible
        // (installed / activated) the same call sites re-enter here and the
        // fetch actually runs. Non-eligible mods paint live from the catalog
        // URL (ModProfile.Resolve*Source) and never touch mod-assets\.
        if (!ShouldCacheAssetsToDisk(profile)) return;
        // Don't pile up parallel fetches for the same profile if
        // BuildModCard/RefreshActiveModBanner both fire it within a
        // single session, or if RefreshModCards re-runs while a fetch
        // is still in flight.
        if (!_assetFetchAttempted.Add(profile.Id)) return;

        var cache = new ModAssetCacheService();

        // --- Phase 1: fast resolve from disk (no network on a warm cache) ----
        // Reconcile EVERY role against the CURRENT url — including a null url,
        // which means the catalog removed that image: GetXPathAsync purges the
        // cached file and returns null, so the stale image disappears (the UI
        // falls back to the monogram / gradient). A changed extension/URL
        // re-downloads; an unchanged warm cache returns instantly with no net.
        bool changed = false;
        try
        {
            var icon = await cache.GetIconPathAsync(profile.Id, profile.IconUrl);
            if (!PathEquals(icon, profile.LocalIconPath)) { profile.LocalIconPath = icon; changed = true; }

            var banner = await cache.GetBannerPathAsync(profile.Id, profile.BannerUrl);
            if (!PathEquals(banner, profile.LocalBannerPath)) { profile.LocalBannerPath = banner; changed = true; }

            // Hero image — the large 1920×1080 dashboard background. When
            // present, RefreshActiveModBanner prefers it over the (smaller,
            // 1200×300) banner for the dashboard surface.
            var hero = await cache.GetHeroImagePathAsync(profile.Id, profile.HeroImageUrl);
            if (!PathEquals(hero, profile.LocalHeroImagePath)) { profile.LocalHeroImagePath = hero; changed = true; }

            // Rotating heroes — when the catalog declares heroImages, fetch the
            // whole set (cached as hero-{i}); the dashboard cycles them with a
            // crossfade. An empty list purges any previously-cached set.
            var heroPaths = await cache.GetHeroImagePathsAsync(profile.Id, profile.HeroImageUrls);
            if (!profile.LocalHeroImagePaths.SequenceEqual(heroPaths))
            { profile.LocalHeroImagePaths = heroPaths; changed = true; }
        }
        catch (Exception ex)
        {
            // ModAssetCacheService catches its own HTTP/IO errors and
            // returns null; this branch is for anything truly unexpected.
            DiagnosticLog.Write($"EnsureModAssets '{profile.Id}': {ex.Message}");
            return;
        }

        // Bring the resolved assets to the screen. We may not be on the UI
        // thread (the await above hops off), so go through the dispatcher.
        if (changed)
            await Dispatcher.InvokeAsync(() => RepaintModAssets(profile));

        // --- Phase 2: background revalidation (replacement / deletion) -------
        // Conditional GETs (If-None-Match). 304 / offline error = no-op (cached
        // copy kept). A 200 means the image was REPLACED under the same name; a
        // 404/410 means the file was DELETED at the source while the manifest
        // (or a hardcoded built-in URL) still references it. For BOTH we drop the
        // in-memory bitmap memo (the on-disk path didn't change) before
        // repainting; for a deletion we also null the local path so the UI falls
        // back to the monogram/gradient. Never blocks the Phase-1 paint.
        try
        {
            bool revalidated = false;

            var icon = await cache.RevalidateIconAsync(profile.Id, profile.IconUrl);
            if (icon != RevalidateOutcome.Unchanged)
            {
                InvalidateTileImageCache(profile.LocalIconPath);
                if (icon == RevalidateOutcome.Removed) profile.LocalIconPath = null;
                revalidated = true;
            }

            var banner = await cache.RevalidateBannerAsync(profile.Id, profile.BannerUrl);
            if (banner != RevalidateOutcome.Unchanged)
            {
                InvalidateTileImageCache(profile.LocalBannerPath);
                if (banner == RevalidateOutcome.Removed) profile.LocalBannerPath = null;
                revalidated = true;
            }

            var hero = await cache.RevalidateHeroAsync(profile.Id, profile.HeroImageUrl);
            if (hero != RevalidateOutcome.Unchanged)
            {
                InvalidateTileImageCache(profile.LocalHeroImagePath);
                if (hero == RevalidateOutcome.Removed) profile.LocalHeroImagePath = null;
                revalidated = true;
            }

            // Rotating heroes — a replacement (200) re-decodes on the next repaint
            // (BuildHeroFillBrush ignores WPF's cache); a deletion (404) purged the
            // file, so drop the now-missing paths from the rotation list.
            if (profile.HeroImageUrls.Count > 0
                && await cache.RevalidateHeroesAsync(profile.Id, profile.HeroImageUrls))
            {
                foreach (var p in profile.LocalHeroImagePaths) InvalidateTileImageCache(p);
                profile.LocalHeroImagePaths =
                    profile.LocalHeroImagePaths.Where(System.IO.File.Exists).ToList();
                revalidated = true;
            }

            if (revalidated)
                await Dispatcher.InvokeAsync(() => RepaintModAssets(profile));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"EnsureModAssets revalidate '{profile.Id}': {ex.Message}");
        }
    }

    /// <summary>
    /// Re-renders the surfaces that show a mod's icon/banner/hero after an
    /// asset lands or changes. Must run on the UI thread. Refreshing the cards
    /// re-runs BuildModCard for every profile; the active banner only needs a
    /// refresh when the affected mod is the active one.
    /// </summary>
    private void RepaintModAssets(ModProfile profile)
    {
        RefreshModCards();
        if (string.Equals(_updateService.Profile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
            RefreshActiveModBanner();
    }

    /// <summary>Ordinal-insensitive path compare treating null and "" as equal.</summary>
    private static bool PathEquals(string? a, string? b)
        => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Startup migration for the disk-cache policy: deletes the mod-asset
    /// files of every registry mod that is NOT cache-eligible
    /// (<see cref="ShouldCacheAssetsToDisk"/>) — older builds cached images
    /// for every Workshop card ever rendered. This does NOT violate the
    /// cache's offline-safe rule (never delete on a NETWORK error): it's a
    /// deterministic policy decision with no network involved, and installed/
    /// active mods are excluded by the gate. Orphaned ids that already left
    /// the catalog are ModRegistry.ApplyMerged's ClearVanishedAssets' job —
    /// no file-name parsing here. Eligibility is evaluated on the UI thread
    /// (it reads UI-owned state); only the file deletes hop to a worker.
    /// </summary>
    private async Task PurgeNonEligibleModAssetsAsync()
    {
        List<ModProfile> toPurge;
        try
        {
            toPurge = ModRegistry.All
                .Where(p => !string.IsNullOrEmpty(p.Id) && !ShouldCacheAssetsToDisk(p))
                .ToList();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Asset-cache migration sweep skipped: {ex.Message}");
            return;
        }
        if (toPurge.Count == 0) return;

        await Task.Run(() =>
        {
            var cache = new ModAssetCacheService();
            foreach (var p in toPurge)
            {
                try { cache.Clear(p.Id); }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Asset-cache migration purge '{p.Id}': {ex.Message}");
                }
            }
        });

        // Drop the now-dangling local paths so the resolvers fall straight to
        // the catalog URLs instead of File.Exists-probing deleted files.
        foreach (var p in toPurge)
        {
            p.LocalIconPath = null;
            p.LocalBannerPath = null;
            p.LocalHeroImagePath = null;
            p.LocalHeroImagePaths = new List<string>();
            p.LocalScreenshotPaths = new List<string>();
        }
        DiagnosticLog.Write(
            $"Asset-cache migration: purged cached images of {toPurge.Count} non-eligible mod(s).");
    }

    /// <summary>
    /// Lazily downloads a mod's gallery screenshots (only when its detail panel
    /// is opened — they're not needed for the card grid) and re-renders the
    /// gallery once they land. Separate from <see cref="EnsureModAssetsAsync"/>
    /// (its own per-session guard) so it never blocks the icon/banner fetch and
    /// isn't run eagerly for every card. Best-effort: failures are swallowed.
    /// </summary>
    private async Task EnsureScreenshotsAsync(ModProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Id)) return;
        // Disk-cache gate — INSTALLED mods only (stricter than the icon/banner
        // gate: screenshots are the heavy role, up to 8×5 MB, and a merely
        // ACTIVE-but-not-installed mod doesn't need them offline). Placed
        // before the session guard so a mod installed later this session still
        // gets its fetch. Non-installed detail panels paint the gallery live
        // from the catalog URLs (ModProfile.ResolveScreenshotSources).
        if (!IsProfileInstalledLocally(profile)) return;
        // No early-return on an empty set: an emptied gallery still needs the
        // call so GetScreenshotPathsAsync purges the now-orphaned shot files.
        if (!_screenshotFetchAttempted.Add(profile.Id)) return;

        var cache = new ModAssetCacheService();

        // Phase 1: resolve the current set from disk (purges surplus shots if
        // the gallery shrank; an empty/absent url list purges them all).
        List<string> paths;
        try
        {
            paths = await cache.GetScreenshotPathsAsync(profile.Id, profile.ScreenshotUrls);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"EnsureScreenshots '{profile.Id}': {ex.Message}");
            return;
        }
        profile.LocalScreenshotPaths = paths;
        // RefreshGallery is a no-op unless this profile is still selected.
        await Dispatcher.InvokeAsync(() => ModsBrowserView.RefreshGallery(profile));

        // Phase 2: background revalidation — refresh the strip if any shot was
        // replaced (same name, new bytes — TryLoadBitmap now ignores WPF's
        // per-URI cache so RefreshGallery re-decodes from disk) or removed (404:
        // file deleted at the source). A removed shot was purged from disk, so
        // drop it from the path list by an existence filter before repainting.
        try
        {
            if (await cache.RevalidateScreenshotsAsync(profile.Id, profile.ScreenshotUrls))
            {
                profile.LocalScreenshotPaths =
                    profile.LocalScreenshotPaths.Where(System.IO.File.Exists).ToList();
                await Dispatcher.InvokeAsync(() => ModsBrowserView.RefreshGallery(profile));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"EnsureScreenshots revalidate '{profile.Id}': {ex.Message}");
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

    private enum TopTab { Play, Mods, Multiplayer, Settings }
    private TopTab _activeTopTab = TopTab.Play;

    private void TopTabPlay_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Play);
    private void TopTabMods_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Mods);
    private void TopTabMultiplayer_Click(object sender, RoutedEventArgs e) => SwitchTopTab(TopTab.Multiplayer);
    /// <summary>
    /// Opens the mod's <c>OfficialWebsite</c> in the user's default browser.
    /// The url has already been validated by the catalog schema (or the
    /// hard-coded built-in profile) before getting to this point.
    /// </summary>
    private void ModsBrowserView_OpenWebsiteRequested(object? sender, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"OpenWebsite failed for '{url}': {ex.Message}");
        }
    }

    /// <summary>
    /// Refresh the community catalog and repaint the browser. Forwards to
    /// the existing periodic refresh path so we don't duplicate fetch /
    /// merge logic — the browser just rebuilds against the new
    /// <c>ModRegistry.All</c> snapshot afterwards.
    /// </summary>
    private async void ModsBrowserView_RefreshCatalogRequested(object? sender, EventArgs e)
    {
        try
        {
            // Manual "Actualizar" = a deliberate, immediate refresh: FORCE the
            // catalog re-fetch (skip the 24h TTL) and revalidate every mod's
            // images, so the user sees catalog edits right away.
            await RefreshCatalogAsync(force: true);
            await RevalidateVisibleAssetsAsync(activeOnly: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Browser RefreshCatalog failed: {ex.Message}");
        }
        finally
        {
            RefreshModsBrowser();
        }
    }

    /// <summary>
    /// Placeholder for the catalog redesign's "Add local mod" entry point.
    /// Wiring a real folder-picker → schema-validate → register flow is a
    /// future commit; for now we surface a friendly toast so the button
    /// doesn't feel dead.
    /// </summary>
    private void ModsBrowserView_AddLocalModRequested(object? sender, EventArgs e)
    {
        MessageBox.Show(this,
            Strings.Get("ModsBrowserAddLocalSoonBody"),
            Strings.Get("ModsBrowserAddLocalSoonTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Workshop "Add to my mods" click — adds the profile id to the
    /// user's personal collection in <see cref="LauncherConfig.UserModIds"/>,
    /// persists, and re-renders the browser so the row's button flips
    /// to "Remove from my mods". The mod immediately becomes available
    /// in the Dashboard's MODS popup; actual install still happens
    /// from the Dashboard when the user picks it (PLAY → INSTALL).
    /// </summary>
    private void ModsBrowserView_AddToCollectionRequested(object? sender, ModProfile profile)
    {
        if (profile == null) return;
        _config.AddUserMod(profile.Id);
        PersistConfigInBackground();
        RefreshModsBrowser();
    }

    /// <summary>
    /// Workshop "Remove from my mods" click — drops the profile id
    /// from <see cref="LauncherConfig.UserModIds"/>. The mod stops
    /// appearing in the Dashboard's MODS popup. Built-in profiles
    /// can't be removed (the Workshop renders them as a disabled
    /// "Built-in" pill and never raises this event), but
    /// LauncherConfig.RemoveUserMod no-ops on built-ins anyway as
    /// a defensive backstop.
    /// </summary>
    private void ModsBrowserView_RemoveFromCollectionRequested(object? sender, ModProfile profile)
    {
        if (profile == null) return;
        _config.RemoveUserMod(profile.Id);
        PersistConfigInBackground();
        RefreshModsBrowser();
    }

    /// <summary>
    /// Fire-and-forget config save. Used by Workshop add/remove so
    /// the UI thread doesn't block on disk I/O for what feels like
    /// an instant button click. Worst case (process kill mid-write)
    /// is a single lost add/remove on the next launch — acceptable,
    /// matches the same trade-off LoadModProfile makes for the
    /// active-mod-id write.
    /// </summary>
    private void PersistConfigInBackground()
    {
        _ = Task.Run(() =>
        {
            try { _config.Save(); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Async config save failed: {ex.Message}");
            }
        });
    }

    // Single-instance reference for the non-modal Mod Properties
    // dialog. Same rationale as _launcherSettingsDialog: Show() (vs
    // ShowDialog()) lets the user interact with the main window while
    // Properties is open, but we have to guard against duplicates. If
    // the user clicks the gear on a different mod while one is open,
    // we close the existing one first so they don't end up with stale
    // Properties for a mod they've moved away from.
    private ModPropertiesDialog? _modPropertiesDialog;

    // Single-instance reference for the dashboard MODS switcher popup. This is
    // what makes the MODS button a RELIABLE toggle — the same field-based model
    // the gear uses (_modPropertiesDialog above). It replaced the fragile
    // ChromePopups.ConsumeToggleOff 300 ms timing trick, which assumed the
    // popup's Closed fired before the opener's Click and failed at runtime
    // ("solo se vuelve a abrir"). See DashboardChangeModButton_Click.
    private System.Windows.Controls.Primitives.Popup? _modSwitchPopup;

    /// <summary>
    /// Opens the ModPropertiesDialog for a specific profile. For the
    /// active mod we reuse <c>_updateService</c> directly; for non-
    /// active mods we build a fresh UpdateService scoped to that
    /// profile so the dialog's "installed version" / install path /
    /// translation list resolve against the right mod's state.
    /// </summary>
    private void OpenModPropertiesDialog(ModProfile profile)
    {
        // If a Properties dialog is already open, close it before
        // opening the new one. We clear the field FIRST so the old
        // dialog's Closed handler (race guard below) sees a non-match
        // and skips clobbering the about-to-be-set new reference.
        if (_modPropertiesDialog != null)
        {
            var stale = _modPropertiesDialog;
            _modPropertiesDialog = null;
            stale.Close();
        }

        UpdateService service = string.Equals(
                profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase)
            ? _updateService
            : new UpdateService(_config, profile);

        var dialog = new ModPropertiesDialog(
            profile,
            service,
            _config,
            _cachedTranslationIndex,
            // Existing 4 callbacks
            applyTranslation: e => ApplyTranslationAsync(e),
            revertToEnglish: () => RevertToEnglish(),
            openVerify: () => RaiseMenuClick(ActionPanelControl.MenuVerifyFiles),
            openRepair: () => RaiseMenuClick(ActionPanelControl.MenuRepairInstall),
            installAnotherCopy: () => { _ = InstallAsync(addNewSlot: true); },
            // 8 new callbacks — folded in from the old SETTINGS popup.
            // Each one wraps a RaiseMenuClick on the legacy
            // ActionPanelControl menu item so the actual flows
            // (path picker dialog, backup dialog, uninstall
            // confirmation, etc.) keep owning the logic.
            checkForUpdates: async () =>
            {
                // Real check (not a cache replay) — refreshes the main
                // window's PLAY/UPDATE button + cache, then hands the
                // cached result back so the dialog can show the outcome
                // inline instead of closing.
                InvalidateActiveModCheckCache();
                await CheckAsync();
                return _checkResultCache.TryGetValue(_updateService.Profile.Id, out var r) ? r : null;
            },
            openAoE3Folder: () => RaiseMenuClick(ActionPanelControl.MenuOpenAoE3Folder),
            changeModFolder: () => RaiseMenuClick(ActionPanelControl.MenuSelectModFolder),
            changeAoE3Folder: () => RaiseMenuClick(ActionPanelControl.MenuSelectAoE3Folder),
            openUserDataFolder: () => RaiseMenuClick(ActionPanelControl.MenuOpenUserDataFolder),
            // Backup/restore return the localized result line so the dialog
            // can show it INLINE — the main-window status bar sits behind the
            // non-modal Properties window (same rationale as checkForUpdates).
            createBackup: CreateUserDataBackupCore,
            restoreBackup: RestoreUserDataCore,
            viewLogs: () => RaiseMenuClick(ActionPanelControl.MenuViewLogs),
            shareDiagnostics: () => ShareDiagnostics(),
            uninstall: () => RaiseMenuClick(ActionPanelControl.UninstallMenuItem),
            refreshTranslations: async () =>
            {
                await RefreshTranslationIndexAsync(reportStatus: true);
                return _cachedTranslationIndex;
            },
            // Pin/unpin "stay on this version" — re-apply the cached check result
            // so the PLAY/UPDATE button + status reflect the new policy instantly,
            // with no network round-trip.
            onUpdatePolicyChanged: () =>
            {
                if (_checkResultCache.TryGetValue(_updateService.Profile.Id, out var r))
                    ApplyCheckResult(r);
            },
            // Fase 1 — version picker (GitHubReleases only): list the repo's
            // releases, and install a chosen one through the shared update path.
            listVersions: () => ListGitHubVersionsAsync(),
            installVersion: tag => InstallGitHubVersionAsync(tag),
            // Multi-install management (the "Manage installs" section).
            switchInstall: async id =>
            {
                await SwitchActiveInstallAsync(id);
                // Pass the FRESH service: SwitchActiveInstallAsync swapped
                // _updateService for a new instance bound to the now-active copy.
                // The dialog must re-point at it AND rebuild the LANGUAGE tab, or
                // translation compat stays computed against the previous copy's
                // version until the dialog is reopened.
                _modPropertiesDialog?.OnActiveInstallSwitched(_updateService);
            },
            removeInstall: id =>
            {
                var st = _config.GetActiveState();
                if (st.RemoveInstall(id))
                {
                    _config.Save();
                    RefreshActiveModBanner();
                    _modPropertiesDialog?.RefreshData();
                }
            },
            addExistingFolder: () => AddExistingCopy(),
            searchInstall: () => _ = SearchInstallAsync(_updateService.Profile));
        // NO Owner on purpose: an owned window with ShowInTaskbar=True minimizes
        // its owner when closed (the "closing properties minimizes the launcher"
        // bug). Independent top-level window like LobbyWindow; Activate() on open.
        _modPropertiesDialog = dialog;

        // Closed (fires for the ✕ button, Esc, and Alt+F4) is the
        // single rendezvous point for post-dialog refresh. Captures
        // `profile` and `dialog` so they survive the long-lived
        // closure on the dialog reference.
        dialog.Closed += (_, _) =>
        {
            // Race guard: if a new dialog was opened (e.g. user clicked
            // a different mod's gear while this one was still closing),
            // don't clobber that new reference.
            if (ReferenceEquals(_modPropertiesDialog, dialog))
                _modPropertiesDialog = null;

            // Repaint the dashboard chrome in case the language was
            // changed inside Properties — same condition as the old
            // post-ShowDialog block.
            if (string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                RefreshIdlePanel();
                RefreshActiveModBanner();
            }
        };

        dialog.Show();
        dialog.Activate(); // ownerless → ensure it comes to front on first open
    }

    /// <summary>
    /// Opens the v0.9 "Publish my mod" wizard. Step navigation lives in
    /// the dialog; the form fields, JSON generation and PR-open action
    /// land in commit 8. Localised step titles / hints come from
    /// <see cref="ConfigurePublishWizardStrings"/>.
    /// </summary>
    private void ModsBrowserView_PublishRequested(object? sender, EventArgs e)
    {
        var dlg = new PublishModDialog { Owner = this };
        ConfigurePublishWizardStrings(dlg);
        dlg.ShowDialog();
    }

    /// <summary>
    /// Pushes the current launcher locale into the wizard's button labels
    /// and step copy. Kept separate from the dialog constructor so the
    /// dialog itself stays string-source-agnostic (no <c>Strings.Get</c>
    /// dependency leaks into a reusable dialog).
    /// </summary>
    private static void ConfigurePublishWizardStrings(PublishModDialog dlg)
    {
        dlg.HeaderTitleText = Strings.Get("PublishWizardTitle");
        dlg.CancelLabel = Strings.Get("PublishWizardCancel");
        dlg.BackLabel = Strings.Get("PublishWizardBack");
        dlg.NextLabel = Strings.Get("PublishWizardNext");
        dlg.FinishLabel = Strings.Get("PublishWizardFinish");
        dlg.StepIndicatorFormat = Strings.Get("PublishWizardStepFormat");
        dlg.SetStepTitle(1, Strings.Get("PublishWizardStep1Title"));
        dlg.SetStepHint(1, Strings.Get("PublishWizardStep1Hint"));
        dlg.SetStepTitle(2, Strings.Get("PublishWizardStep2Title"));
        dlg.SetStepHint(2, Strings.Get("PublishWizardStep2Hint"));
        dlg.SetStepTitle(3, Strings.Get("PublishWizardStep3Title"));
        dlg.SetStepHint(3, Strings.Get("PublishWizardStep3Hint"));
        dlg.SetStepTitle(4, Strings.Get("PublishWizardStep4Title"));
        dlg.SetStepHint(4, Strings.Get("PublishWizardStep4Hint"));
        dlg.SetStepTitle(5, Strings.Get("PublishWizardStep5Title"));
        dlg.SetStepHint(5, Strings.Get("PublishWizardStep5Hint"));
        dlg.SetStepTitle(6, Strings.Get("PublishWizardStep6Title"));
        dlg.SetStepHint(6, Strings.Get("PublishWizardStep6Hint"));

        // Field labels + hints. Kept as one explicit block so adding a new
        // language is a checklist exercise — every visible string is
        // assigned exactly once.
        dlg.LblIdText = Strings.Get("PublishFieldId");
        dlg.HintIdText = Strings.Get("PublishFieldIdHint");
        dlg.LblDisplayNameText = Strings.Get("PublishFieldDisplayName");
        dlg.HintDisplayNameText = Strings.Get("PublishFieldDisplayNameHint");
        dlg.LblAuthorText = Strings.Get("PublishFieldAuthor");
        dlg.HintAuthorText = Strings.Get("PublishFieldAuthorHint");
        dlg.LblSubtitleText = Strings.Get("PublishFieldSubtitle");
        dlg.HintSubtitleText = Strings.Get("PublishFieldSubtitleHint");
        dlg.LblAccentText = Strings.Get("PublishFieldAccent");
        dlg.HintAccentText = Strings.Get("PublishFieldAccentHint");
        dlg.LblIconText = Strings.Get("PublishFieldIcon");
        dlg.HintIconText = Strings.Get("PublishFieldIconHint");
        dlg.LblBannerText = Strings.Get("PublishFieldBanner");
        dlg.HintBannerText = Strings.Get("PublishFieldBannerHint");
        dlg.LblInstallTypeText = Strings.Get("PublishFieldInstallType");
        dlg.HintInstallTypeText = Strings.Get("PublishFieldInstallTypeHint");
        dlg.LblDefaultFolderText = Strings.Get("PublishFieldDefaultFolder");
        dlg.HintDefaultFolderText = Strings.Get("PublishFieldDefaultFolderHint");
        dlg.LblProbeFileText = Strings.Get("PublishFieldProbeFile");
        dlg.HintProbeFileText = Strings.Get("PublishFieldProbeFileHint");
        dlg.LblExecutableText = Strings.Get("PublishFieldExecutable");
        dlg.HintExecutableText = Strings.Get("PublishFieldExecutableHint");
        dlg.LblArgumentsText = Strings.Get("PublishFieldArguments");
        dlg.HintArgumentsText = Strings.Get("PublishFieldArgumentsHint");
        dlg.LblMechanismText = Strings.Get("PublishFieldMechanism");
        dlg.HintMechanismText = Strings.Get("PublishFieldMechanismHint");
        dlg.LblWolUpdateInfoUrlText = Strings.Get("PublishFieldWolUpdateInfoUrl");
        dlg.HintWolUpdateInfoUrlText = Strings.Get("PublishFieldWolUpdateInfoUrlHint");
        dlg.LblSourceRepoText = Strings.Get("PublishFieldSourceRepo");
        dlg.HintSourceRepoText = Strings.Get("PublishFieldSourceRepoHint");
        dlg.LblApprovedTagText = Strings.Get("PublishFieldApprovedTag");
        dlg.HintApprovedTagText = Strings.Get("PublishFieldApprovedTagHint");
        dlg.LblDescriptionEnText = Strings.Get("PublishFieldDescriptionEn");
        dlg.HintDescriptionText = Strings.Get("PublishFieldDescriptionHint");
        dlg.LblDescriptionEsText = Strings.Get("PublishFieldDescriptionEs");
        dlg.LblWebsiteText = Strings.Get("PublishFieldWebsite");
        dlg.HintWebsiteText = Strings.Get("PublishFieldWebsiteHint");
        dlg.CopyJsonLabel = Strings.Get("PublishCopyJson");
        dlg.OpenPrLabel = Strings.Get("PublishOpenPr");
        dlg.IntroBodyText = Strings.Get("PublishWizardIntro");
        dlg.ImagesUploadNoteText = Strings.Get("PublishImagesUploadNote");
        dlg.NextStepsTitleText = Strings.Get("PublishNextStepsTitle");
        dlg.NextStepsBodyText = Strings.Get("PublishNextStepsBody");
        dlg.ErrorIdInvalid = Strings.Get("PublishErrorId");
        dlg.ErrorDisplayNameRequired = Strings.Get("PublishErrorDisplayName");
        dlg.ErrorAccentInvalid = Strings.Get("PublishErrorAccent");
        dlg.ErrorIconInvalid = Strings.Get("PublishErrorIcon");
        dlg.ErrorBannerInvalid = Strings.Get("PublishErrorBanner");
        dlg.ErrorExecutableInvalid = Strings.Get("PublishErrorExecutable");
        dlg.ErrorWebsiteInvalid = Strings.Get("PublishErrorWebsite");

        dlg.GoTo(1);
    }

    /// <summary>
    /// Re-order the top nav bar's tab buttons left-to-right to match the
    /// user's saved <see cref="LauncherConfig.TopTabOrder"/>. Called once
    /// at startup (with <paramref name="switchToFirst"/>=true so the first
    /// tab in the order is the one that opens) and again after the user
    /// reorders from Launcher Settings (with false, so the bar re-orders
    /// but we DON'T yank them away from whatever tab they're on).
    ///
    /// Settings is NOT part of this set — it lives behind the gear menu,
    /// not in the nav bar — so it never appears in TopTabOrder.
    /// </summary>
    private void ApplyTopTabOrder(bool switchToFirst)
    {
        var order = _config.GetTopTabOrder();

        // id -> the button declared in XAML. The buttons stay alive via
        // their x:Name fields, so Clear()+re-Add just re-parents them in
        // the new order (no rebuild, no lost event wiring).
        var byId = new System.Collections.Generic.Dictionary<string, UIElement>(System.StringComparer.Ordinal)
        {
            ["library"] = TopTabPlay,
            ["workshop"] = TopTabMods,
            ["multiplayer"] = TopTabMultiplayer,
        };

        TopTabBar.Children.Clear();
        foreach (var id in order)
            if (byId.TryGetValue(id, out var btn))
                TopTabBar.Children.Add(btn);

        if (switchToFirst)
        {
            // First tab in the order = the one that opens on launch.
            SwitchTopTab(TabIdToTopTab(order.Length > 0 ? order[0] : "library"));
        }
        else
        {
            // Re-parenting cleared nothing about the active view; just
            // repaint the highlight so it still marks the current tab.
            RefreshTopTabHighlight();
        }
    }

    private static TopTab TabIdToTopTab(string id) => id switch
    {
        "workshop" => TopTab.Mods,
        "multiplayer" => TopTab.Multiplayer,
        _ => TopTab.Play, // "library" and any unexpected fallback
    };

    /// <summary>
    /// Handle a Discord "Join" deep link (<c>wol-launcher://join/&lt;id&gt;</c>):
    /// navigate to Multiplayer → Rooms and auto-join the room. The MP tab owns all
    /// the gates (sign-in, mod installed, password, room-not-found). Called from the
    /// Loaded drain (cold start) and the App.JoinRequested pipe forward (running).
    /// </summary>
    private async Task HandleJoinDeepLink(string lobbyId)
    {
        try
        {
            SwitchTopTab(TopTab.Multiplayer);
            await MultiplayerView.JoinByLobbyIdAsync(lobbyId);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"DeepLink: HandleJoinDeepLink failed: {ex.Message}");
        }
    }

    private void SwitchTopTab(TopTab tab)
    {
        _activeTopTab = tab;
        PlayView.Visibility = tab == TopTab.Play ? Visibility.Visible : Visibility.Collapsed;
        ModsBrowserView.Visibility = tab == TopTab.Mods ? Visibility.Visible : Visibility.Collapsed;
        MultiplayerView.Visibility = tab == TopTab.Multiplayer ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tab == TopTab.Settings ? Visibility.Visible : Visibility.Collapsed;
        // Landing on Multiplayer clears the "new room" dot on its nav tab (the
        // finer Rooms-subtab dot clears when the user actually opens that subtab).
        if (tab == TopTab.Multiplayer) SetMultiplayerTabDot(false);
        RefreshTopTabHighlight();
    }

    /// <summary>
    /// Toggles the small red "new room created" dot overlaid on the MULTIPLAYER
    /// nav tab. Room notifications no longer add a bell item — they surface as a
    /// Windows toast plus this dot (and the Rooms-subtab dot inside the tab).
    /// Set from <see cref="PollNewRoomsAsync"/> when a room appears while the user
    /// is on another top tab; cleared when they switch to Multiplayer.
    /// </summary>
    private void SetMultiplayerTabDot(bool on)
    {
        if (MultiplayerTabDot != null)
            MultiplayerTabDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Marks the active sidebar nav button by toggling its Tag to
    /// <c>"active"</c>. The SidebarNavButton style's ControlTemplate
    /// triggers on Tag and paints the right rail accent + background +
    /// bold foreground; this method just sets the flag.
    /// (Pre-Fase 2: this used to manually paint Foreground + Border
    /// because the old horizontal top-tab style had no Tag trigger.)
    /// </summary>
    private void RefreshTopTabHighlight()
    {
        static void Paint(System.Windows.Controls.Button btn, bool active)
        {
            btn.Tag = active ? "active" : null;
        }

        Paint(TopTabPlay, _activeTopTab == TopTab.Play);
        Paint(TopTabMods, _activeTopTab == TopTab.Mods);
        Paint(TopTabMultiplayer, _activeTopTab == TopTab.Multiplayer);
        // TopTabSettings was removed from the sidebar (moved into
        // the brand menu). The TopTab.Settings enum value and the
        // SettingsView placeholder are still wired internally but
        // there's no sidebar button to paint as active anymore.
    }

    // ------------------------------------------------------------------
    // Dashboard WebView2 (Fase 5). The Dashboard tab now hosts a
    // Vite/Tailwind build of "Imperial Mod Manager" (Assets/Dashboard/).
    // We map that folder to a virtual https:// host so the HTML's
    // relative asset paths (./assets/index-xxx.css, .js) keep working
    // and Vite's hash-based caching survives. Fase 5b will add the
    // bidirectional JS ↔ C# bridge — for now the HTML just runs its
    // mock data.
    // ------------------------------------------------------------------

    // ROLLBACK: InitializeDashboardWebViewAsync removed along with the
    // DashboardWebView control and the Microsoft.Web.WebView2 package.
    // Kept stub-free (no method) since nothing calls it after we
    // dropped the constructor invocation too.

    // ROLLBACK: the entire Dashboard WebView ↔ C# bridge from Fase 5b
    // is gone (the WebView itself, and the JS bridge). What's below is
    // a stub that keeps the rest of this file compiling if anything
    // still references these names — but nothing should.
#if FALSE
    private void OnDashboardWebMessage(object? sender, object e)
    {
        string json;
        try { json = e.WebMessageAsJson; }
        catch { return; }

        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;
                var action = actionProp.GetString() ?? "";

                switch (action)
                {
                    case "ready":
                        // JS finished loading and is ready to receive
                        // the initial mod list + active mod snapshot.
                        PushModListToWebView();
                        PushActiveModToWebView();
                        PushPrimaryActionToWebView();
                        PushProgressIdleToWebView();
                        PushActiveTabToWebView();
                        break;

                    case "nav":
                        var tab = root.TryGetProperty("tab", out var t) ? t.GetString() : null;
                        HandleNavFromWeb(tab);
                        break;

                    case "play":
                        // Forward to the existing primary-action click flow.
                        InvokeButtonClick(ActionPanelControl?.PlayButton);
                        break;

                    case "modSettings":
                        // The hero gear opens the same per-mod More menu.
                        if (ActionPanelControl?.MoreButton?.ContextMenu is { } menu)
                        {
                            menu.PlacementTarget = ActionPanelControl.MoreButton;
                            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                            menu.IsOpen = true;
                        }
                        break;

                    case "selectMod":
                        var modId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (!string.IsNullOrEmpty(modId))
                        {
                            var profile = ModRegistry.Find(modId);
                            if (profile != null) LoadModProfile(profile);
                        }
                        break;

                    default:
                        DiagnosticLog.Write($"Dashboard bridge: unknown action '{action}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"OnDashboardWebMessage failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Sidebar nav from the web → swap the visible XAML overlay. The
    /// WebView itself never hides (otherwise the web sidebar would
    /// disappear with it), so the Dashboard tab simply means "no XAML
    /// overlay shown".
    /// </summary>
    private void HandleNavFromWeb(string? tab)
    {
        switch (tab)
        {
            case "dashboard":
                ModsBrowserView.Visibility = Visibility.Collapsed;
                MultiplayerView.Visibility = Visibility.Collapsed;
                SettingsView.Visibility = Visibility.Collapsed;
                _activeTopTab = TopTab.Play;
                break;
            case "catalog":
                ModsBrowserView.Visibility = Visibility.Visible;
                MultiplayerView.Visibility = Visibility.Collapsed;
                SettingsView.Visibility = Visibility.Collapsed;
                _activeTopTab = TopTab.Mods;
                break;
            case "multiplayer":
                ModsBrowserView.Visibility = Visibility.Collapsed;
                MultiplayerView.Visibility = Visibility.Visible;
                SettingsView.Visibility = Visibility.Collapsed;
                _activeTopTab = TopTab.Multiplayer;
                break;
            case "settings":
                ModsBrowserView.Visibility = Visibility.Collapsed;
                MultiplayerView.Visibility = Visibility.Collapsed;
                SettingsView.Visibility = Visibility.Visible;
                _activeTopTab = TopTab.Settings;
                break;
            case "support":
                OpenExternalUrl(string.IsNullOrEmpty(_config?.OfficialWebsite)
                    ? "https://aoe3wol.com/"
                    : _config.OfficialWebsite);
                break;
            case "wiki":
                OpenExternalUrl("https://aoe3wol.com/");
                break;
        }
        PushActiveTabToWebView();
    }

    /// <summary>
    /// Send a JSON message to the Dashboard JS. No-op if the WebView
    /// isn't ready yet — JS posts a "ready" message on load so we
    /// know when it's safe to push state.
    /// </summary>
    private async void SendToDashboard(object payload)
    {
        try
        {
            if (DashboardWebView?.CoreWebView2 == null) return;
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            await DashboardWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.dispatchEvent(new MessageEvent('message', {{ data: {json} }}));");
            // Why dispatchEvent instead of PostWebMessageAsJsonAsync?
            // PostWebMessage routes to window.chrome.webview's listener
            // which we already use for JS→C#. To go C#→JS we synthesise
            // a native MessageEvent on `window` so the JS listener
            // (window.chrome.webview.addEventListener) receives it the
            // same way it receives postMessage data — symmetric API.
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SendToDashboard failed: {ex.Message}");
        }
    }

    private void PushModListToWebView()
    {
        var lang = (_config?.Language ?? "en").ToLowerInvariant();
        var mods = ModRegistry.All.Select(p => new
        {
            id = p.Id,
            title = p.DisplayName,
            version = "",
            img = "",   // Fase 5c: bridge mod banner image as a data: URI
            desc = ResolveDescription(p, lang),
            updating = false,
            progress = 100,
        }).ToList();
        SendToDashboard(new { type = "modList", mods });
    }

    private void PushActiveModToWebView()
    {
        var p = _updateService?.Profile;
        if (p == null) return;
        var lang = (_config?.Language ?? "en").ToLowerInvariant();
        SendToDashboard(new
        {
            type = "modChanged",
            mod = new
            {
                id = p.Id,
                title = p.DisplayName,
                desc = ResolveDescription(p, lang),
                img = "",   // populated in Fase 5c
            },
        });
    }

    private void PushPrimaryActionToWebView()
    {
        var text = ActionPanelControl?.PlayButtonText?.Text ?? "PLAY";
        var enabled = ActionPanelControl?.PlayButton?.IsEnabled ?? true;
        SendToDashboard(new { type = "primaryAction", text, enabled });
    }

    private void PushProgressIdleToWebView()
    {
        SendToDashboard(new
        {
            type = "progress",
            state = "idle",
            pct = 0,
            speed = (string?)null,
            eta = (string?)null,
            title = (string?)null,
            subtitle = (string?)null,
        });
    }

    private void PushActiveTabToWebView()
    {
        var tab = _activeTopTab switch
        {
            TopTab.Mods => "catalog",
            TopTab.Multiplayer => "multiplayer",
            TopTab.Settings => "settings",
            _ => "dashboard",
        };
        SendToDashboard(new { type = "activeTab", tab });
    }

    private static string ResolveDescription(ModProfile p, string lang)
    {
        if (p.Description != null)
        {
            if (p.Description.TryGetValue(lang, out var d) && !string.IsNullOrWhiteSpace(d)) return d;
            if (p.Description.TryGetValue("en", out var de) && !string.IsNullOrWhiteSpace(de)) return de;
        }
        return p.Subtitle ?? string.Empty;
    }
#endif // ROLLBACK: closes the #if FALSE wrap that hides all Fase 5b bridge code above

    // REDESIGN-2: Sidebar (Support/Wiki) + Dashboard (PLAY/Settings/Change
    // Mod) handlers that drive the cinema view in PlayView. They forward
    // to the legacy ActionPanelControl / open external links / show a mod
    // picker popup. Earlier the block was wrapped in #if FALSE during the
    // rollback because the visible controls had been removed; the
    // redesign 2 XAML re-introduces matching x:Names so these are live
    // again.

    /// <summary>
    /// Hero PLAY button — synthesises a click on the (now-hidden)
    /// ActionPanelControl.PlayButton so the existing "play vs install vs
    /// update vs stop" state machine inside ActionPanel keeps owning
    /// the decision.
    /// </summary>
    private void DashboardPlayButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeButtonClick(ActionPanelControl?.PlayButton);
    }

    /// <summary>
    /// Cinema-dashboard PAUSE button → forwards the click to the
    /// legacy <c>ProgressPanelControl.PauseButton</c>. That button's
    /// existing handler owns the pause/resume toggle for the active
    /// download. The legacy button is hidden inside LegacyPlayContent
    /// but its click logic still runs because handlers are attached
    /// at MainWindow ctor time, not at first display.
    /// </summary>
    private void DashboardPauseButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeButtonClick(ProgressPanelControl?.PauseButton);
        // Repaint the icon immediately — the next 200ms pump tick
        // would also sync it, but doing it here avoids the lag.
        SyncDashboardProgressFromLegacyPanel();
    }

    /// <summary>
    /// Cinema-dashboard CANCEL button → forwards the click to the
    /// legacy <c>ProgressPanelControl.CancelButton</c>. Cancellation
    /// then flows through CancelButton_Click → operation cancellation
    /// token → ProgressState transitions back to Idle → the pump
    /// hides the action buttons on its next tick.
    /// </summary>
    private void DashboardCancelButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeButtonClick(ProgressPanelControl?.CancelButton);
    }

    /// <summary>
    /// Hero gear button → opens the per-mod settings popup. Used to
    /// surface the legacy <c>ActionPanelControl.MoreButton.ContextMenu</c>
    /// directly, but that menu's WPF-default look (rainbow icons,
    /// cramped padding, light-grey hover) clashed with the rest of
    /// the dorado-imperial chrome. The new popup mirrors the MODS
    /// popup styling (BgSidebar surface, BorderSecondary outline,
    /// gold accents, 12% dorado hover) and uses a flat list with
    /// sub-views for Paths / User data / Language so the top level
    /// stays compact. Each leaf forwards its click to the
    /// corresponding MenuItem so the existing handlers — and all
    /// the dialog wiring they own — keep working untouched.
    /// </summary>
    private void DashboardSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle: the gear opens the per-mod Properties dialog, and
        // clicking it AGAIN while that dialog is open closes it (the
        // dialog is non-modal, so the gear stays clickable behind it).
        // Without this the click fell straight through to
        // OpenModPropertiesDialog, which closes the stale dialog and
        // opens a fresh one — so the user saw the menu "just reopen"
        // instead of closing. Close() raises Closed synchronously, whose
        // handler nulls _modPropertiesDialog and refreshes the chrome.
        if (_modPropertiesDialog != null)
        {
            _modPropertiesDialog.Close();
            return;
        }

        // Per user-redesign: gear opens the ModPropertiesDialog
        // directly. The previous SETTINGS popup (flat list of
        // maintenance/config actions) was folded into the dialog's
        // tabs (GENERAL / LOCAL FILES / USER DATA / LANGUAGE), so
        // the gear has a single destination for everything per-mod.
        OpenModPropertiesDialog(_updateService.Profile);
    }

    /// <summary>
    /// Brand-strip menu (the "AOE3 LAUNCHER" wordmark in the top-
    /// left of the sidebar acts as a Steam-style dropdown). Opens
    /// a popup with launcher-wide actions: Launcher settings,
    /// About, Exit. Per-mod settings live in the dashboard gear
    /// button, NOT here — the brand menu is launcher-level only.
    /// </summary>
    private void BrandMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var btn = (System.Windows.Controls.Button)sender;

        // Toggle: if this same click is the one that just dismissed the brand
        // popup (StaysOpen=false auto-closes on the re-click), don't reopen it.
        if (Controls.ChromePopups.ConsumeToggleOff(btn))
            return;

        var popup = BuildBrandPopup(btn);

        // Light up the button for as long as the menu is showing: Tag
        // "open" drives the persistent highlight in the button template,
        // and the chevron flips ▾ → ▴ to read as "expanded". Both revert
        // on Closed (fires for click-away — StaysOpen=false — Esc, or an
        // item picking that sets IsOpen=false). char-from-hex instead of
        // "\uXXXX" literals so the source stays pure ASCII (some
        // round-trips mangle non-ASCII bytes).
        btn.Tag = "open";
        if (BrandChevron != null)
            BrandChevron.Text = ((char)0xE70E).ToString();   // ChevronUp

        popup.Closed += (_, _) =>
        {
            btn.Tag = null;
            if (BrandChevron != null)
                BrandChevron.Text = ((char)0xE70D).ToString();   // ChevronDown
        };

        popup.IsOpen = true;
    }

    private System.Windows.Controls.Primitives.Popup BuildBrandPopup(System.Windows.Controls.Button anchor)
    {
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            // Opens BELOW the wordmark — drops down into the
            // BgBase content area where the popup's BgPanelAlt
            // surface + gold border contrast cleanly. With the
            // sidebar gone (Steam-style horizontal nav), the
            // popup no longer needs the 12px horizontal offset
            // that used to push it out of the sidebar; the
            // wordmark is already at the top-left of the nav row.
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
        };

        // BgPanelAlt (darker than BgSidebar) so the popup pops
        // against the sidebar's lighter slate background. Brighter
        // gold border + heavier drop shadow makes the popup feel
        // like a clearly separated panel hovering above the chrome.
        var border = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("BgPanelAlt"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            MinWidth = 260,
            MaxWidth = 360,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.75,
            },
        };

        var content = new System.Windows.Controls.StackPanel();
        border.Child = content;
        ApplyPopupScale(border);
        popup.Child = border;

        // Header — same dorado caption treatment as the other
        // popups (SETTINGS / MODS).
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.Get("BrandMenuTitle"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            FontSize = (double)FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(8, 4, 8, 8),
        });

        // Launcher settings — opens the existing LauncherSettingsDialog.
        // Reuses the LauncherSettingsButton_Click handler so the
        // dialog wiring (refresh-on-close, etc.) stays in one place.
        content.Children.Add(BuildSettingsRow(
            glyph: "",   // Settings (gear)
            label: Strings.Get("BrandMenuLauncherSettings"),
            subtitle: Strings.Get("BrandMenuLauncherSettingsSubtitle"),
            click: () =>
            {
                popup.IsOpen = false;
                LauncherSettingsButton_Click(this, new RoutedEventArgs());
            }));

        // About — small info MessageBox with version + credits.
        content.Children.Add(BuildSettingsRow(
            glyph: "",   // Info
            label: Strings.Get("BrandMenuAbout"),
            subtitle: Strings.Get("BrandMenuAboutSubtitle"),
            click: () =>
            {
                popup.IsOpen = false;
                ShowAboutDialog();
            }));

        content.Children.Add(BuildSettingsDivider());

        // Exit — closes the launcher entirely.
        content.Children.Add(BuildSettingsRow(
            glyph: "",   // ChromeClose / Sign-out
            label: Strings.Get("BrandMenuExit"),
            subtitle: Strings.Get("BrandMenuExitSubtitle"),
            click: () =>
            {
                popup.IsOpen = false;
                System.Windows.Application.Current.Shutdown();
            }));

        // Single-open invariant + close-on-dialog-open + toggle (see ChromePopups).
        // Owner = the anchor button so a re-click toggles it off.
        Controls.ChromePopups.Track(popup, anchor);
        return popup;
    }

    /// <summary>
    /// Tiny "About" MessageBox — version string + credits. Used by
    /// the brand menu's About entry. Could grow into a real dialog
    /// with a links / system-info panel later; MessageBox is fine
    /// for the first cut.
    /// </summary>
    private void ShowAboutDialog()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
        var body = Strings.Format("AboutDialogBody", version);
        MessageBox.Show(this, body, Strings.Get("AboutDialogTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // The custom title-bar min/max/close handlers + the maximize-glyph sync
    // moved to the shared controls:TitleBar (Controls/TitleBar.xaml.cs); the
    // brand button stays here as the bar's BarContent (BrandMenuButton_Click).



    // The dashboard hero's window-size scaling moved to the shared scaler
    // (Controls/UiScale.cs): see the UiScale.Attach(HeroContentGrid, PlayView,
    // 1500, 760, Kind.Render, (0,1)) call in the constructor. The reference,
    // [0.82, 1.0] band, bottom-left render-pin and the Display<->Ideal text
    // crispness toggle are all preserved there, so the hero looks identical;
    // the rest of the UI now rides the same scaler (Kind.Layout) per view.


    // -- Maximize-bounds fix (WM_GETMINMAXINFO) -----------------------------
    //
    // WPF + WindowStyle=None + WindowChrome has a long-standing bug: when
    // the window maximizes, Windows asks it how big it wants to be via
    // WM_GETMINMAXINFO. The default reply is "the whole monitor" which
    // means the maximized window COVERS the taskbar (and any other
    // appbars docked to screen edges). Native WindowStyle windows get
    // this for free because Windows clips them to the work area; styled
    // chrome windows have to do it themselves.
    //
    // Fix: hook the window's wndproc, intercept WM_GETMINMAXINFO,
    // and write back the MONITORINFO.rcWork rect — which is the
    // monitor's drawable area minus the taskbar and any docked
    // appbars. The launcher then maximizes correctly: full width,
    // full height ABOVE the taskbar.

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int WM_GETMINMAXINFO = 0x0024;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            try
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        // Position the maximized window at the work
                        // area's top-left (relative to the monitor's
                        // own origin) and size it to the work area's
                        // dimensions — i.e. monitor minus taskbar /
                        // docked appbars.
                        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                        mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                        mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
                        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                        handled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"WM_GETMINMAXINFO hook failed: {ex.Message}");
            }
        }
        return IntPtr.Zero;
    }

    // -- Popup row primitive builders ---------------------------------------
    //
    // Shared by the brand popup in the title bar (BuildBrandPopup).
    // Originally built for the per-mod SETTINGS popup that lived behind
    // the gear button — that popup was folded into ModPropertiesDialog,
    // but the row + divider widgets still earn their keep elsewhere.

    /// <summary>Thin horizontal divider between popup sections.</summary>
    private FrameworkElement BuildSettingsDivider() => new System.Windows.Controls.Border
    {
        Height = 1,
        Background = (System.Windows.Media.Brush)FindResource("BorderSecondary"),
        Margin = new Thickness(8, 6, 8, 6),
        Opacity = 0.6,
    };

    /// <summary>
    /// One leaf / sub-view-opener row in a popup menu. Visual language
    /// mirrors the MODS popup: icon column, label, optional chevron,
    /// hover flips to the 12% dorado tint + gold text/icon.
    /// <paramref name="subtitle"/> renders as a small cool-grey
    /// second line under the main label — gives each row a
    /// "what does this do?" hint so the popup doesn't feel like a
    /// bare list of one-word commands.
    /// </summary>
    private FrameworkElement BuildSettingsRow(string glyph, string label,
                                              Action click,
                                              string? subtitle = null,
                                              bool hasChevron = false,
                                              bool destructive = false,
                                              bool enabled = true,
                                              bool activeAccent = false)
    {
        var iconBrush = destructive
            ? (System.Windows.Media.Brush)FindResource("ErrorBrush")
            : (activeAccent
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"));
        var labelBrush = destructive
            ? (System.Windows.Media.Brush)FindResource("ErrorBrush")
            : (activeAccent
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("SecondaryFixed"));

        var row = new System.Windows.Controls.Grid();
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

        var icon = new System.Windows.Controls.TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Width = 22,
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = iconBrush,
        };
        System.Windows.Controls.Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        // Label + optional subtitle stacked vertically. The
        // StackPanel sits in the * column so it gets the
        // auto-width residue after icon + chevron are measured.
        var labelStack = new System.Windows.Controls.StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        labelStack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label ?? "",
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            FontSize = (double)FindResource("FontSizeBody"),
            FontWeight = activeAccent ? FontWeights.Bold : FontWeights.Medium,
            Foreground = labelBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            labelStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = subtitle,
                FontSize = (double)FindResource("FontSizeCaption"),
                FontWeight = FontWeights.Normal,
                // Slightly dimmed cool tone — present but recedes
                // behind the main label.
                Foreground = (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                Opacity = 0.85,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        System.Windows.Controls.Grid.SetColumn(labelStack, 1);
        row.Children.Add(labelStack);

        if (hasChevron)
        {
            var chevron = new System.Windows.Controls.TextBlock
            {
                Text = "",   // ChevronRight
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Margin = new Thickness(8, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
            };
            System.Windows.Controls.Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
        }

        // SidebarSecondaryButton already provides the BgNeutral hover
        // we don't want here, so build the row as a plain Button with
        // a Tag-driven hover handled in code (saves cloning the whole
        // style just to flip the hover brush). Active rows skip the
        // dorado hover so they don't double-emphasise.
        var btn = new System.Windows.Controls.Button
        {
            Content = row,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            IsEnabled = enabled,
            // Custom one-shot template so the row reads as a flat
            // pill with a hover background — WPF's default Button
            // template paints a Windows-grey raised border which
            // would clash with the popup chrome.
            Template = BuildSettingsRowTemplate(destructive),
            Opacity = enabled ? 1.0 : 0.5,
        };
        btn.Click += (_, _) => click();
        return btn;
    }

    /// <summary>
    /// Helper to resolve a SolidColorBrush resource to a hex string
    /// usable inside a XAML template literal. The settings popup
    /// builds its row template from a string (XamlReader.Parse), so
    /// we can't bind `{DynamicResource}` directly — instead we bake
    /// the *current* token value into the string at construction
    /// time. The popup is rebuilt every time it opens, so a theme
    /// swap would still propagate to the next open.
    ///
    /// <paramref name="alpha"/> overrides the brush's own alpha when
    /// you want a different opacity (e.g. derive a 20% danger tint
    /// from a 100% DangerAlert brush). Pass null to use the brush's
    /// own alpha.
    /// </summary>
    private string HexFromBrush(string resourceKey, byte? alpha = null)
    {
        try
        {
            if (FindResource(resourceKey) is System.Windows.Media.SolidColorBrush b)
            {
                var c = b.Color;
                byte a = alpha ?? c.A;
                return $"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HexFromBrush({resourceKey}) failed: {ex.Message}");
        }
        // Defensive fallback — matches the historical hardcoded
        // tint so the popup stays legible if resource lookup fails.
        return "#1FD8B66A";
    }

    /// <summary>
    /// One-shot ControlTemplate for settings-row buttons. Transparent
    /// at rest, dorado 12% tint on hover (or destructive-red tint
    /// for the Uninstall row). 6px corner radius matches the
    /// MODS-popup row treatment exactly.
    /// </summary>
    private System.Windows.Controls.ControlTemplate BuildSettingsRowTemplate(bool destructive)
    {
        // Hover tint pulled from the centralized interaction tokens
        // (TintGoldHover / a low-alpha derivative of DangerAlert) so a
        // future palette swap in Colors.xaml propagates here too. We
        // resolve to a hex string because this template is built from
        // a XAML string literal — `{DynamicResource}` inside a parsed
        // template string is brittle, but baking the *current* colour
        // value at construction is fine since these popups are rebuilt
        // every time they open. The dim red for destructive uses
        // DangerAlert at 20% alpha so the row tints toward "danger"
        // without flooding the popup.
        var hoverHex = destructive
            ? HexFromBrush("DangerAlert", alpha: 0x33)
            : HexFromBrush("TintGoldHover");

        var xaml = @"
<ControlTemplate TargetType='Button'
                 xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Border x:Name='border'
            Background='{TemplateBinding Background}'
            CornerRadius='6'
            Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Stretch'
                          VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='border' Property='Background' Value='" + hoverHex + @"'/>
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>";

        return (System.Windows.Controls.ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    /// <summary>
    /// Forwards a click to a legacy WPF MenuItem. Used by the new
    /// settings popup so each row delegates to the existing handler
    /// (paths picker / user-data dialog / language switch / etc.)
    /// rather than re-implementing the action.
    /// </summary>
    private static void RaiseMenuClick(System.Windows.Controls.MenuItem? item)
    {
        if (item == null) return;
        item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent, item));
    }

    /// <summary>
    /// Pixel-snapped crisp text rendering for popup surfaces. WPF
    /// popups live in their own HwndSource separate from the main
    /// Window, so the TextOptions set on the Window root don't
    /// propagate into them — every popup looks fuzzy out of the box.
    /// Call this on each popup's root <see cref="UIElement"/> (Border,
    /// Window, etc.) right after construction to force Display mode
    /// + ClearType + Fixed hinting → glyphs align to pixel and look
    /// crisp at the small popup font sizes (10–13pt).
    /// </summary>
    /// <summary>
    /// Crispness + window-size scaling for the code-built popups (brand menu,
    /// mod-switch). They live in their own top-level visual tree, out of reach
    /// of the per-view content-root transform, so they read
    /// <see cref="UiScale.Current"/> to match the shell. At 1.0 (the default
    /// window) this is an identity transform + the Display/ClearType/Fixed trio
    /// — identical to the old anti-blur pass; below 1.0 it shrinks the popup and
    /// flips to the Ideal/Grayscale/Animated text mode WPF uses under a transform.
    /// </summary>
    private static void ApplyPopupScale(System.Windows.FrameworkElement element)
    {
        if (element == null) return;
        double s = UiScale.Current;
        element.LayoutTransform = s < 0.999
            ? new System.Windows.Media.ScaleTransform(s, s)
            : System.Windows.Media.Transform.Identity;
        UiScale.SetTextCrispForScale(element, s);
    }

    /// <summary>
    /// Hero CHANGE MOD button → opens a popup listing only the mods
    /// the user has installed (active mod always shown, even when
    /// missing on disk, so the picker never looks empty). Discovering
    /// or installing new mods is a Catalog flow, not a Dashboard one
    /// — Change Mod is purely "switch between things I can actually
    /// play right now".
    ///
    /// Placement is Top: the popup expands UPWARD from the button so
    /// it doesn't shoot off the bottom of the window. Centred
    /// horizontally over the button so it reads as anchored to the
    /// click target instead of floating off to one side.
    /// </summary>
    private void DashboardChangeModButton_Click(object sender, RoutedEventArgs e)
    {
        var btn = (System.Windows.Controls.Button)sender;

        // Toggle — same field-based model as the gear (DashboardSettingsButton_Click):
        // if a MODS popup is already live, this click closes it instead of reopening.
        // The field survives the StaysOpen=false auto-dismiss race because its Closed
        // handler (below) clears it DEFERRED at Background priority — after this Click
        // has already run — so re-clicking the button reliably lands here and returns.
        if (_modSwitchPopup != null)
        {
            _modSwitchPopup.IsOpen = false; // the Closed handler clears the field
            return;
        }

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            // Best-guess initial offset based on MinWidth — recomputed
            // in the Opened handler below once the popup measures its
            // actual width from its contents.
            HorizontalOffset = (btn.ActualWidth - 240) / 2.0,
            // Small breathing-room gap between popup and button.
            VerticalOffset = -6,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
        };

        // Single-open invariant + close-on-dialog-open (see ChromePopups):
        // mutually exclusive with the brand popup and closed automatically when the
        // gear (ModPropertiesDialog) or any other dialog opens. The TOGGLE itself is
        // now owned by the _modSwitchPopup field (above), not ConsumeToggleOff.
        Controls.ChromePopups.Track(popup, btn);

        // Field-based toggle: remember this popup, and clear the field when it
        // closes — but DEFERRED to Background priority so the clear runs AFTER the
        // opener's Click (Input priority always beats Background). That ordering is
        // what defeats the StaysOpen=false auto-dismiss/Click race: on a re-click,
        // mouse-down auto-dismisses the popup and QUEUES this clear, then Click fires
        // with the field still non-null → the gate above closes and returns (no
        // reopen). An outside click just closes it and the field clears momentarily
        // after, so the next MODS click opens fresh.
        _modSwitchPopup = popup;
        popup.Closed += (_, _) =>
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (ReferenceEquals(_modSwitchPopup, popup))
                        _modSwitchPopup = null;
                }));

        // Two-tone "punched out" rim — same recipe as the gear
        // ContextMenu's template in ActionPanel.xaml: outer 1px
        // near-black band (visible against bright backdrops like
        // the title bar / hero image) + inner 2px MenuBorder
        // bright line (visible against the dark popup interior).
        // Together they form a crisp 3px boundary that reads as a
        // discrete card no matter what's behind it. The drop
        // shadow lives on the OUTER band so it skirts the whole
        // composite rim instead of being clipped by the inner one.
        var panel = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("BgSidebar"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("MenuBorder"),
            BorderThickness = new Thickness(2),
            CornerRadius = (CornerRadius)FindResource("RadiusPopupInner"),
            Padding = new Thickness(10),
            // Auto-sized to its content — same guardrails as the
            // SETTINGS popup so the two read as a matched pair.
            MinWidth = 240,
            MaxWidth = 360,
        };
        var rim = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("MenuBorderOuter"),
            CornerRadius = (CornerRadius)FindResource("RadiusPopupOuter"),
            Padding = new Thickness(1),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.6,
            },
        };

        // Auto-width centring — same trick as the Settings popup.
        popup.Opened += (_, _) =>
        {
            if (popup.Child is FrameworkElement fe && fe.ActualWidth > 0)
            {
                // fe (the rim) carries the popup scale as a LayoutTransform, so
                // its rendered width is ActualWidth × the scale — centre on that
                // (× 1.0 = unchanged at the default window).
                popup.HorizontalOffset = (btn.ActualWidth - fe.ActualWidth * UiScale.Current) / 2.0;
            }
        };

        var stack = new System.Windows.Controls.StackPanel();
        panel.Child = stack;
        ApplyPopupScale(rim);
        popup.Child = rim;

        // Header: small dorado caption so the popup feels like a
        // titled menu instead of a bare list.
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.Get("DashboardChangeMod"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            FontSize = (double)FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(8, 4, 8, 8),
        });

        // Filter to mods in the user's personal collection (added via
        // the Workshop) plus all built-ins (always implicit). The
        // active mod is always included as a safety net so the popup
        // is never empty — covers the edge case where the user
        // removed their last added mod while it was active.
        //
        // Ordering: favorites first (in the order they were starred),
        // then the rest alphabetically by display name. Mirrors
        // Steam's library where favorites pin to the top.
        var activeId = _updateService?.Profile?.Id;
        var collection = ModRegistry.All
            .Where(p => _config.IsUserMod(p.Id)
                        || string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => _config.IsFavoriteMod(p.Id))
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (collection.Count == 0)
        {
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Strings.Get("ModSelectorNotInstalled"),
                Foreground = (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                FontSize = (double)FindResource("FontSizeBody"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 8, 8, 12),
            });
        }

        foreach (var p in collection)
        {
            var isActive = string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase);
            var item = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("SidebarSecondaryButton"),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(12, 10, 12, 10),
                // 12% dorado tint on the active row — pulls from the
                // centralized TintGoldHover token so a palette swap in
                // Colors.xaml automatically retints this row instead of
                // leaving an orphan literal.
                Background = isActive
                    ? (System.Windows.Media.Brush)FindResource("TintGoldHover")
                    : System.Windows.Media.Brushes.Transparent,
                Content = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Controls.TextBlock
                        {
                            Text = isActive ? "" : "",   // CheckMark when active, GridView otherwise
                            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12,
                            Width = 18,
                            Margin = new Thickness(0, 0, 10, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = isActive
                                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                : (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                        },
                        // Label + subtitle stacked vertically so each
                        // mod entry shows its display name plus the
                        // short Subtitle from ModProfile ("Launcher",
                        // "Asian Dynasties overhaul", etc.). Matches
                        // the per-row "what is this?" affordance of
                        // the SETTINGS popup so the two read as a
                        // pair stylistically.
                        new System.Windows.Controls.StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = p.DisplayName,
                                    FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
                                    FontSize = (double)FindResource("FontSizeBody"),
                                    FontWeight = isActive ? FontWeights.Bold : FontWeights.Medium,
                                    // Active row reads dorado; inactive
                                    // rows use the cool secondary so
                                    // the gold/cool tonal contrast
                                    // matches the main UI.
                                    Foreground = isActive
                                        ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                        : (System.Windows.Media.Brush)FindResource("SecondaryFixed"),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                },
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = p.Subtitle ?? "",
                                    FontSize = (double)FindResource("FontSizeCaption"),
                                    FontWeight = FontWeights.Normal,
                                    Foreground = (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                                    Opacity = 0.85,
                                    Margin = new Thickness(0, 1, 0, 0),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                    Visibility = string.IsNullOrWhiteSpace(p.Subtitle)
                                        ? Visibility.Collapsed
                                        : Visibility.Visible,
                                },
                            },
                        },
                        // Favorite star — small dorado pin to the
                        // right of the label when the user has
                        // starred this mod via the right-click
                        // context menu. Collapsed (no horizontal
                        // gap consumed) when not favourited.
                        new System.Windows.Controls.TextBlock
                        {
                            Text = "",
                            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12,
                            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                            Margin = new Thickness(8, 0, 2, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Visibility = _config.IsFavoriteMod(p.Id)
                                ? System.Windows.Visibility.Visible
                                : System.Windows.Visibility.Collapsed,
                        },
                    },
                },
            };
            var profile = p;
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                if (!string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase))
                    LoadModProfile(profile);
            };
            // (Steam-style right-click context menu removed — per
            // user request the MODS popup is now a pure "switch
            // active mod" list with nothing else. Per-mod admin
            // actions live in the gear button's Administrar
            // submenu / Properties dialog instead.)
            stack.Children.Add(item);
        }

        // ---- Multi-install: quick-switch between copies of the active mod ----
        AppendInstallCopiesToModPopup(stack, popup);

        popup.IsOpen = true;
    }

    /// <summary>
    /// Shows/updates the dashboard hero's "active copy" chip. Only visible when the
    /// active mod has 2+ registered copies (HasMultipleInstalls) — for a single install
    /// the folder name is redundant with the title, so the chip stays hidden and the hero
    /// looks exactly as before. The label is the ACTIVE copy's real folder leaf (same
    /// CopyDisplayLabel source the switcher uses), so the user always knows which copy PLAY
    /// will launch. Clicking the chip opens the same copy switcher as the MODS button.
    /// </summary>
    private void RefreshActiveCopyChip()
    {
        if (DashboardCopyChip == null) return;
        var st = _config.GetActiveState();
        var path = _updateService?.InstallPath;
        if (st.HasMultipleInstalls && !string.IsNullOrWhiteSpace(path))
        {
            DashboardCopyText.Text = CopyDisplayLabel(null, path);
            DashboardCopyChip.Visibility = Visibility.Visible;
        }
        else
        {
            DashboardCopyChip.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Friendly label for an install copy: its explicit user label, else the
    /// install folder's leaf name.
    /// </summary>
    private static string CopyDisplayLabel(string? label, string? installPath)
    {
        if (!string.IsNullOrWhiteSpace(label)) return label!.Trim();
        var leaf = Path.GetFileName((installPath ?? "").TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(leaf) ? (installPath ?? "") : leaf;
    }

    /// <summary>
    /// Appends a "switch active copy" section to the MODS popup when the active
    /// mod has more than one registered install. One row per copy (active marked
    /// with a check); clicking an inactive copy switches to it via
    /// <see cref="SwitchActiveInstallAsync"/>.
    /// </summary>
    private void AppendInstallCopiesToModPopup(
        System.Windows.Controls.StackPanel stack,
        System.Windows.Controls.Primitives.Popup popup)
    {
        var state = _config.GetActiveState();
        if (!state.HasMultipleInstalls) return;

        // Only show copies whose folder still exists on disk. A registration whose
        // folder was deleted outside the launcher (a "phantom") must not appear — but
        // the entry stays in config (a disconnected drive re-appears when reconnected);
        // the user can forget it permanently from Properties → Local files → "Manage installs".
        var liveOthers = new List<ModInstall>();
        foreach (var o in state.OtherInstalls)
        {
            if (string.IsNullOrWhiteSpace(o.InstallPath)) continue;
            if (ModState.PathEquals(o.InstallPath, state.InstallPath)) continue;
            try { if (!Directory.Exists(o.InstallPath)) continue; } catch { continue; }
            liveOthers.Add(o);
        }
        if (liveOthers.Count == 0) return;   // no real extra copies → collapse the section

        stack.Children.Add(new System.Windows.Controls.Border
        {
            Height = 1,
            Background = (System.Windows.Media.Brush)FindResource("MpDivider"),
            Margin = new Thickness(8, 8, 8, 6),
        });
        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.Get("InstallCopiesHeader"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            FontSize = (double)FindResource("FontSizeCaption"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(8, 2, 8, 6),
        });

        // Names derive from the real FOLDER (pass null → folder leaf), so a stale custom label
        // from the removed rename feature never shows.
        var rows = new List<(string Id, string Label, string Path, bool IsActive)>
        {
            (state.ActiveInstallId,
             CopyDisplayLabel(null, state.InstallPath),
             state.InstallPath, true),
        };
        foreach (var o in liveOthers)
            rows.Add((o.Id, CopyDisplayLabel(null, o.InstallPath), o.InstallPath, false));

        // Make every label unique for display (parent folder, then a stable #N) so the
        // switcher never shows two identical rows — see PathDisplay.DisambiguateLabels.
        var uniqueLabels = PathDisplay.DisambiguateLabels(
            rows.Select(r => (r.Label, r.Path)).ToList());
        for (int i = 0; i < rows.Count; i++)
            rows[i] = (rows[i].Id, uniqueLabels[i], rows[i].Path, rows[i].IsActive);

        foreach (var row in rows)
        {
            bool isAct = row.IsActive;
            var btn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("SidebarSecondaryButton"),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(12, 8, 12, 8),
                Background = isAct
                    ? (System.Windows.Media.Brush)FindResource("TintGoldHover")
                    : System.Windows.Media.Brushes.Transparent,
                Content = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Controls.TextBlock
                        {
                            Text = isAct ? "" : "", // CheckMark / Folder
                            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 12,
                            Width = 18,
                            Margin = new Thickness(0, 0, 10, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = isAct
                                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                : (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                        },
                        new System.Windows.Controls.StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = row.Label,
                                    FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
                                    FontSize = (double)FindResource("FontSizeBody"),
                                    FontWeight = isAct ? FontWeights.Bold : FontWeights.Medium,
                                    Foreground = isAct
                                        ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                        : (System.Windows.Media.Brush)FindResource("SecondaryFixed"),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                },
                                new System.Windows.Controls.TextBlock
                                {
                                    Text = PathDisplay.CompactPathMiddle(row.Path),
                                    FontSize = (double)FindResource("FontSizeCaption"),
                                    Foreground = (System.Windows.Media.Brush)FindResource("OnSecondaryContainer"),
                                    Opacity = 0.85,
                                    Margin = new Thickness(0, 1, 0, 0),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                    MaxWidth = 280,
                                },
                            },
                        },
                    },
                },
            };
            var id = row.Id;
            btn.Click += async (_, _) =>
            {
                popup.IsOpen = false;
                if (!isAct) await SwitchActiveInstallAsync(id);
            };

            // The switcher popup is a pure copy SELECTOR: every row just switches.
            // Forgetting a registered copy (the remove-from-list action) lives only
            // in Properties > Local files > "Manage installs", not here.
            stack.Children.Add(btn);
        }
    }

    /// <summary>
    /// Tiny helper: synthesise a Button.Click without relying on the
    /// button being attached to the visual tree of the current logical
    /// focus. RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))
    /// runs whatever handlers were wired up at construction time (which
    /// is what we want here — every ActionPanel button has its handler
    /// attached during MainWindow's ctor).
    /// </summary>
    private static void InvokeButtonClick(System.Windows.Controls.Button? btn)
    {
        if (btn == null) return;
        btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, btn));
    }

    /// <summary>
    /// Shell out to the OS default browser. Best-effort: a logged
    /// failure beats a crash if the user's system has no default
    /// browser registered (rare on Windows but possible on stripped
    /// down installs).
    /// </summary>
    private static void OpenExternalUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"OpenExternalUrl({url}) failed: {ex.Message}");
        }
    }
    // (end of REDESIGN-2 sidebar + dashboard handlers)

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

        // Be tolerant: try the folder itself + mod-named subfolders, then a
        // bounded deep scan of the chosen tree (so the user can point at any
        // reasonable ancestor — the AoE3 root, a "Games" parent — and the
        // install is still found). Content-based, name-independent; the marker
        // keeps a vanilla AoE3 folder from passing as WoL.
        string? resolved = ResolvePickedModInstall(chosen, profile, out var reason);

        if (resolved == null)
        {
            DiagnosticLog.Write($"Change mod folder ('{profile.Id}'): rejected '{chosen}' (reason={reason}).");
            MessageBox.Show(this,
                InvalidFolderMessage(profile, reason),
                Strings.Get("DlgInvalidFolderTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var st = _config.GetActiveState();
        // Re-pointing the active install at a path that's already registered as an
        // inactive copy would leave a duplicate/phantom in the switcher — drop it first.
        st.OtherInstalls.RemoveAll(i => ModState.PathEquals(i.InstallPath, resolved));
        st.InstallPath = resolved;
        _config.Save();
        // Readback diagnostic: a report showed this write not surviving into the
        // subsequent CheckAsync's ResolveInstallPath (read back empty, no reject
        // log). Capture the exact ids/state so a future bundle pins that paradox.
        DiagnosticLog.Write(
            $"Change mod folder: set active install for '{profile.Id}' -> '{resolved}'. " +
            $"Readback: serviceProfile='{_updateService.Profile.Id}', activeModId='{_config.ActiveModId}', " +
            $"getActiveState='{st.InstallPath}', getState('{_updateService.Profile.Id}')=" +
            $"'{_config.GetState(_updateService.Profile.Id).InstallPath}'.");
        InvalidateActiveModCheckCache();
        // Pass the picked path THROUGH the check so it's adopted directly — the
        // config write above was observed not surviving the read in ResolveInstallPath.
        await CheckAsync(forceInstallPath: resolved);
        // Keep an open Mod Properties window in sync with the new mod path /
        // re-detected version once the async re-check completes.
        _modPropertiesDialog?.RefreshData();
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
        // Content check (probe file + optional marker), name-independent. The
        // marker keeps a manually-picked vanilla AoE3 folder from passing as
        // WoL — its probe file (data\stringtabley.xml) exists in vanilla too.
        if (ModInstallProbe.LooksLikeModInstall(candidate, profile))
            return true;

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

    /// <summary>
    /// Resolve the real mod-install folder from a user-picked folder. Tries the
    /// shallow candidates first (the folder itself, a mod-named subfolder, the
    /// default-install leaf); if none match, does a BOUNDED deep scan of the
    /// chosen subtree (<see cref="ModInstallScanner.FindDeep"/>, skipping system
    /// dirs). Because the user explicitly pointed at this folder, scanning a few
    /// levels down is safe and forgiving — point at any reasonable ancestor (a
    /// parent, a "Games" folder) and the install is still found. Returns the
    /// resolved install path, or null when nothing under the tree looks like a
    /// real install of this mod.
    /// </summary>
    private static string? ResolvePickedModInstall(string chosen, ModProfile profile)
        => ResolvePickedModInstall(chosen, profile, out _);

    /// <summary>
    /// Overload that also reports, via <paramref name="bestReason"/>, the most
    /// informative failure across the tried candidates (MarkerMissing &gt;
    /// ProbeMissing &gt; NotADirectory) so the caller can name the missing signal
    /// in the rejection message. Every step is written to the diagnostic log —
    /// the manual picker was previously silent, so a rejection left no trace in a
    /// shared diagnostic bundle.
    /// </summary>
    private static string? ResolvePickedModInstall(string chosen, ModProfile profile, out ProbeOutcome bestReason)
    {
        chosen = chosen.TrimEnd('\\', '/');
        bestReason = ProbeOutcome.NotADirectory;

        var candidates = new List<string> { chosen };
        if (!string.IsNullOrEmpty(profile.DisplayName))
            candidates.Add(Path.Combine(chosen, profile.DisplayName));
        var defaultLeaf = Path.GetFileName(profile.DefaultInstallFolder?.TrimEnd('\\', '/') ?? "");
        if (!string.IsNullOrEmpty(defaultLeaf)
            && !candidates.Contains(Path.Combine(chosen, defaultLeaf), StringComparer.OrdinalIgnoreCase))
            candidates.Add(Path.Combine(chosen, defaultLeaf));

        DiagnosticLog.Write($"ResolvePickedModInstall ('{profile.Id}'): chosen='{chosen}', {candidates.Count} candidate(s).");
        foreach (var candidate in candidates)
        {
            var outcome = ModInstallProbe.Inspect(candidate, profile);
            DiagnosticLog.Write($"  candidate '{candidate}' -> {outcome}");
            if (outcome == ProbeOutcome.Match)
            {
                bestReason = ProbeOutcome.Match;
                return candidate.TrimEnd('\\', '/');
            }
            // Keep the closest-to-a-real-install reason (higher enum = more install-like).
            if (outcome > bestReason)
                bestReason = outcome;

            // WoL legacy Inno-registry install without the modern probe/marker still counts.
            if (string.Equals(profile.Id, ModRegistry.WolId, StringComparison.OrdinalIgnoreCase)
                && RegistryService.IsValidInstall(candidate))
            {
                DiagnosticLog.Write($"  candidate '{candidate}' -> accepted via WoL Inno-registry fallback.");
                bestReason = ProbeOutcome.Match;
                return candidate.TrimEnd('\\', '/');
            }
        }

        // Deep scan of the chosen tree (bounded depth, system dirs skipped).
        var hit = ModInstallScanner.FindDeep(chosen, profile, maxDepth: 4).FirstOrDefault();
        if (hit != null)
        {
            DiagnosticLog.Write($"  deep scan (depth 4) matched '{hit}'.");
            bestReason = ProbeOutcome.Match;
            return hit.TrimEnd('\\', '/');
        }

        DiagnosticLog.Write($"ResolvePickedModInstall ('{profile.Id}'): no match; best reason = {bestReason}.");
        return null;
    }

    /// <summary>
    /// Message body for a rejected manual folder pick, chosen by the closest
    /// failure reason: a folder that has the probe but lacks the mod's content
    /// marker looks like a base-game / incomplete install, so we say so and name
    /// the marker; anything else lists the content signals we expected to find.
    /// </summary>
    private static string InvalidFolderMessage(ModProfile profile, ProbeOutcome reason)
    {
        if (reason == ProbeOutcome.MarkerMissing && !string.IsNullOrEmpty(profile.InstallMarker))
            return Strings.Format("DlgInvalidFolderMarkerBody", profile.DisplayName, profile.InstallMarker);

        var expected = string.IsNullOrEmpty(profile.InstallProbeFile)
            ? "(unknown probe file)"
            : profile.InstallProbeFile;
        if (!string.IsNullOrEmpty(profile.InstallMarker))
            expected += " + " + profile.InstallMarker;
        return Strings.Format("DlgInvalidFolderBody", profile.DisplayName, expected);
    }

    /// <summary>
    /// Register an EXISTING install folder as an inactive copy of the active mod — adopts a
    /// real install already on disk WITHOUT reinstalling. Reuses the same folder picker +
    /// content probe as "Change mod folder"; validates it's a real install of this mod, then
    /// appends it via <see cref="ModState.RegisterInstall"/> (deduped). Returns true if added.
    /// </summary>
    private bool AddExistingCopy()
    {
        var profile = _updateService.Profile;
        if (profile.IsStockGame) return false;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Format("DlgFolderPickerTitle", profile.DisplayName),
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(_updateService.InstallPath) && Directory.Exists(_updateService.InstallPath))
            dialog.InitialDirectory = _updateService.InstallPath;
        if (dialog.ShowDialog(this) != true) return false;

        var chosen = dialog.FolderName.TrimEnd('\\', '/');
        string? resolved = ResolvePickedModInstall(chosen, profile, out var reason);

        if (resolved == null)
        {
            DiagnosticLog.Write($"Add existing folder ('{profile.Id}'): rejected '{chosen}' (reason={reason}).");
            MessageBox.Show(this,
                InvalidFolderMessage(profile, reason),
                Strings.Get("DlgInvalidFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var st = _config.GetActiveState();

        // No active install yet? "Add existing folder" on a not-installed mod must
        // ADOPT the picked folder as the ACTIVE install — otherwise RegisterInstall
        // files it as an inactive copy and the mod keeps reading "not installed".
        // Route it through the same forced-adoption path as "Change mod folder".
        if (string.IsNullOrEmpty(st.InstallPath))
        {
            st.OtherInstalls.RemoveAll(i => ModState.PathEquals(i.InstallPath, resolved));
            st.InstallPath = resolved;
            _config.Save();
            DiagnosticLog.Write(
                $"Add existing folder ('{profile.Id}'): no active install — adopted '{resolved}' as ACTIVE.");
            InvalidateActiveModCheckCache();
            _ = CheckAsync(forceInstallPath: resolved);
            _modPropertiesDialog?.RefreshData();
            return true;
        }

        // There IS an active install — register the folder as an inactive copy.
        if (!st.RegisterInstall(resolved, Path.GetFileName(resolved)))
        {
            MessageBox.Show(this,
                Strings.Format("DlgInstallCopyExistsBody", resolved), Strings.Get("DlgInstallCopyExistsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _config.Save();
        RefreshActiveModBanner();
        return true;
    }

    /// <summary>
    /// On-demand broad search for an existing install of <paramref name="profile"/>
    /// across likely game locations (Steam libraries, Program Files, drive roots),
    /// for when auto-detection didn't find a WoL install that's actually there.
    /// Runs OFF the UI thread; adopts the first match (near-AoE3 roots are searched
    /// first, so it's the most likely "main" install) and re-checks. Content-gated
    /// (probe + marker via <see cref="LooksLikeModInstall"/>), so it can never
    /// mistake vanilla AoE3 for WoL.
    /// </summary>
    /// <summary>
    /// Show the hero "Search for my install" affordance only when it makes sense:
    /// an isolated-folder mod (WoL) that isn't the stock game. Called from the
    /// not-installed branches of <c>ApplyCheckResult</c>.
    /// </summary>
    private void MaybeShowSearchInstall()
    {
        var p = _updateService.Profile;
        DashboardSearchInstallButton.Visibility =
            (!p.IsStockGame && p.InstallType == ModInstallType.IsolatedFolder)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DashboardSearchInstallButton_Click(object sender, RoutedEventArgs e)
        => _ = SearchInstallAsync(_updateService.Profile);

    /// <summary>
    /// Low-disk-space guard for Repair (which re-downloads the payload but does
    /// NOT clone AoE3, so the requirement is small). Returns true to proceed:
    /// enough space (or unknown → don't cry wolf) proceeds silently; short space
    /// prompts a warn-but-allow confirm. False only when the user declines — the
    /// caller then aborts via OperationCanceledException (handled as a cancel).
    /// </summary>
    private bool ConfirmRepairSpaceOk(string installPath)
    {
        long free = Services.DiskSpaceService.SafeFreeSpace(installPath);
        if (!Services.DiskSpaceService.IsShort(free, Services.DiskSpaceService.RepairAllowanceBytes))
            return true;

        var body = Strings.Format("DiskSpaceConfirmRepairBody",
            Services.DiskSpaceService.FormatBytes(Services.DiskSpaceService.RepairAllowanceBytes),
            Services.DiskSpaceService.FormatBytes(free),
            Path.GetPathRoot(installPath) ?? installPath);
        return MessageBox.Show(this, body,
            Strings.Get("DiskSpaceConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private async Task SearchInstallAsync(ModProfile profile)
    {
        if (_isBusy || profile.IsStockGame) return;

        List<string> hits;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            // User-initiated: keep the THOROUGH scan (default includeDriveRoots:true
            // + full cap) — a broad scan is expected when the user explicitly asks
            // to find their install. The AUTOMATIC startup scan
            // (UpdateService.BroadFallbackScan) is the conservative one that skips
            // whole-drive enumeration.
            hits = await Task.Run(() =>
                ModInstallScanner.FindBroad(profile, maxDepth: 3)
                    .Where(p => LooksLikeModInstall(p, profile))
                    .Select(p => p.TrimEnd('\\', '/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SearchInstall for '{profile.Id}' failed: {ex.Message}");
            hits = new List<string>();
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = null;
        }

        if (hits.Count == 0)
        {
            MessageBox.Show(this,
                Strings.Format("SearchInstallNotFound", profile.DisplayName),
                Strings.Get("SearchInstallButton"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var chosen = hits[0];
        var st = _config.GetActiveState();
        st.OtherInstalls.RemoveAll(i => ModState.PathEquals(i.InstallPath, chosen));
        st.InstallPath = chosen;
        _config.Save();
        InvalidateActiveModCheckCache();
        await CheckAsync();
        _modPropertiesDialog?.RefreshData();

        // If several installs turned up, tell the user they can pick a specific
        // one via "Change mod folder" — we adopted the most likely.
        var body = hits.Count == 1
            ? Strings.Format("SearchInstallFound", chosen)
            : Strings.Format("SearchInstallFoundMultiple", chosen, hits.Count - 1);
        MessageBox.Show(this, body, Strings.Get("SearchInstallButton"),
            MessageBoxButton.OK, MessageBoxImage.Information);
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
        else if (GitHubUpdateAvailable())
        {
            // GitHubReleases update: re-overlay the approved release (download +
            // extract on top, now WITH file deletion — delete.lst + auto net-new).
            // Same engine as Repair, surfaced as a dedicated Update action.
            if (!EnsureGameNotRunning()) return;
            await RepairInstallAsync(asUpdate: true);
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

    /// <summary>
    /// True when a GitHubReleases mod has an update pending: the installed tag
    /// (<see cref="UpdateService.CurrentVersion"/>) differs from the catalog's
    /// approved tag (<see cref="UpdateService.LatestVersion"/>). WolPatcher and
    /// the other mechanisms always return false (they use the pending-downloads
    /// path or have no auto-update).
    /// </summary>
    private bool GitHubUpdateAvailable()
    {
        if (_updateService.Profile.UpdateMechanism != ModUpdateMechanism.GitHubReleases)
            return false;
        var cur = _updateService.CurrentVersion?.Ver;
        var latest = _updateService.LatestVersion?.Ver;
        return !string.IsNullOrEmpty(cur) && !string.IsNullOrEmpty(latest)
            && !string.Equals(cur, latest, StringComparison.OrdinalIgnoreCase);
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_modIsInstalled || string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }

        if (_isBusy) return;

        // The deep (hash) pass can take minutes on a multi-GB install, so Verify
        // is now CANCELLABLE and shows live per-file progress (current file,
        // speed, ETA). The panel turns Completed (green) when clean or Error (red)
        // with the Retry button wired to RepairInstallAsync. Pause makes no sense
        // for a read-only scan, so its button is hidden for this operation.
        SetBusy(true);
        _operatingCts = new CancellationTokenSource();
        ShowDownloadControls(true);
        ProgressPanelControl.PauseButton.Visibility = Visibility.Collapsed;
        StartProgressPanel(
            ProgressOperation.Verify,
            title: Strings.Format("ProgressTitleVerifying", _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubVerifying"),
            bar1Label: "ProgressBarVerify",
            bar2Label: "ProgressBarProcess",
            retry: () => RepairInstallAsync());
        ProgressPanelControl.PatchProgress.IsIndeterminate = true;
        ProgressPanelControl.OverallProgress.IsIndeterminate = true;
        SetStatus(Strings.Get("StatusVerifying"));

        try
        {
            var verifyProfile = _updateService.Profile;
            var speed = new SpeedTracker();
            // Per-file hashing reports real progress; the first tick flips the bars
            // to determinate. Old installs (no FileHashes) emit no ticks and stay
            // indeterminate for the quick structural spot-check.
            var verifyProgress = new Progress<VerifyService.VerifyProgress>(p =>
            {
                if (p.Total <= 0) return;
                ProgressPanelControl.PatchProgress.IsIndeterminate = false;
                ProgressPanelControl.OverallProgress.IsIndeterminate = false;
                double pct = 100.0 * p.Done / p.Total;
                ProgressPanelControl.PatchProgress.Value = pct;
                ProgressPanelControl.OverallProgress.Value = pct;
                if (!string.IsNullOrEmpty(p.CurrentFile))
                    ProgressPanelControl.LblCurrentPatch.Text = p.CurrentFile;
                if (p.BytesTotal > 0)
                {
                    speed.Sample(p.BytesDone);
                    ProgressPanelControl.PatchBytesText.Text =
                        $"{FormatBytes(p.BytesDone)} / {FormatBytes(p.BytesTotal)}";
                    var eta = speed.EstimateTimeRemaining(p.BytesTotal - p.BytesDone);
                    ProgressPanelControl.EtaText.Text = eta.HasValue
                        ? Strings.Format("ProgressEta", FormatDuration(eta.Value)) : "";
                    ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
                        ? Strings.Format("ProgressSpeed", FormatBytes((long)speed.BytesPerSecond)) : "";
                }
                else
                {
                    ProgressPanelControl.PatchBytesText.Text = $"{p.Done} / {p.Total}";
                }
            });
            var token = _operatingCts!.Token;
            var result = await Task.Run(
                () => VerifyInstallation(_updateService.InstallPath, verifyProfile,
                    verifyProgress, hashPass: true, token), token);
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
            problems.AddRange(result.CorruptItems.Select(c => $"[corrupt] {c}"));
            int totalProblems = result.MissingItems.Count + result.CorruptItems.Count;

            SetStatus(Strings.Format("StatusVerifyMissing", totalProblems,
                string.Join(", ", problems.Take(10))));
            DiagnosticLog.Write($"Verification: {totalProblems} problems found:");
            foreach (var p in problems) DiagnosticLog.Write($"  {p}");

            // Surface as an Error in the panel — Retry button calls Repair,
            // which is exactly what the old MessageBox offered.
            ShowProgressError(Strings.Format("DlgVerifyRepairBody", totalProblems));
        }
        catch (OperationCanceledException)
        {
            SetStatus(Strings.Get("StatusCancelledUpdate"));
            ShowProgressCancelled();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Verify failed: {ex}");
            ShowProgressError(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ShowDownloadControls(false);
            // Restore the Pause button for future download operations.
            ProgressPanelControl.PauseButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Best-effort incremental delta update for an opted-in GitHubReleases mod: discover a small
    /// <c>patch-&lt;from&gt;-to-&lt;to&gt;</c> on the approved release, verify it, and apply only the
    /// changed files. Returns true when the delta was applied (files + manifest already at the new
    /// version); false for ANY reason so the caller does the full re-overlay. Runs inside
    /// <see cref="RepairInstallAsync"/> so the shared tail (recheck, version write, translation
    /// reconcile, notifications, CheckAsync) is inherited unchanged.
    /// </summary>
    private async Task<bool> TryApplyGitHubDeltaAsync(
        NativeInstallService nativeInstaller, string installPath,
        IProgress<string>? statusProgress, IProgress<InstallPhase>? phaseProgress)
    {
        try
        {
            var profile = _updateService.Profile;
            var gh = profile.GitHubReleases;
            if (gh == null) return false;

            var installedTag = _config.GetState(profile.Id).LastKnownVersion;
            var manifest = InstallManifest.TryLoad(installPath);
            var covered = profile.Translations?.CoveredFiles;

            phaseProgress?.Report(InstallPhase.Download);
            statusProgress?.Report(Strings.Get("StatusDeltaChecking"));

            // Target of a normal update = the effective tag (approved, or the
            // cached latest for follow-latest mods) — the same value
            // ResolveInstallVersion stamps below, so descriptor ToTag, manifest
            // Version and LastKnownVersion stay coherent.
            var targetTag = EffectiveGitHubTag(profile);
            var prepared = await DeltaPatchService.TryPrepareAsync(
                gh, installedTag, targetTag, installPath, manifest, covered, _operatingCts!.Token);
            if (prepared == null) return false;   // no usable delta → full

            DiagnosticLog.Write(
                $"Delta update: {prepared.Descriptor.FromTag} -> {prepared.Descriptor.ToTag} " +
                $"({prepared.Descriptor.Changed.Count} changed) for {profile.Id}.");

            phaseProgress?.Report(InstallPhase.Extract);
            statusProgress?.Report(Strings.Get("StatusDeltaApplying"));

            // Drive the dashboard bar off the (small) extraction.
            var xp = new Progress<ArchiveExtractProgress>(p =>
            {
                if (p.BytesTotal <= 0) return;
                double frac = Math.Clamp((double)p.BytesRead / p.BytesTotal, 0, 1);
                ProgressPanelControl.PatchProgress.Value = 100.0 * frac;
                ProgressPanelControl.OverallProgress.Value = 100.0 * frac;
                ProgressPanelControl.PatchBytesText.Text = $"{p.BytesRead} / {p.BytesTotal}";
            });

            var version = ResolveInstallVersion(overrideTag: null);   // = effective tag (== targetTag)
            bool ok = await nativeInstaller.ApplyGitHubDeltaAsync(
                profile, version, prepared, installPath, statusProgress, xp, _operatingCts!.Token);

            try { if (System.IO.File.Exists(prepared.LocalZipPath)) System.IO.File.Delete(prepared.LocalZipPath); }
            catch { /* best-effort temp cleanup */ }
            return ok;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Delta update attempt failed → full: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Repairs the installation by re-downloading the WoL payload and
    /// re-copying the mod files over the existing install.
    /// </summary>
    private async Task RepairInstallAsync(bool asUpdate = false, string? targetReleaseTag = null)
    {
        if (_isBusy) return;
        bool updated = false;

        // Resolve payload via the same helper InstallAsync uses so Repair
        // works for every mechanism the launcher can install (WolPatcher,
        // GitHubReleases). DelegatedExternal / Manual mods hit the empty-
        // URL branch and surface "no install URL" — the menu gating in
        // ApplyMenuVisibility hides Repair for them anyway, this is just
        // belt-and-braces. targetReleaseTag (GitHubReleases only) installs a
        // user-chosen version; null keeps the default approved-tag behaviour.
        var payload = await ResolvePayloadUrlsAsync(overrideTag: targetReleaseTag);
        if (payload == null) return;
        var payloadUrls = payload.Urls;
        var payloadSha256 = payload.Sha256;

        if (!EnsureGameNotRunning()) return;

        var installPath = _updateService.InstallPath!;

        SetBusy(true);
        _operatingCts = new CancellationTokenSource();
        ShowDownloadControls(true);

        StartProgressPanel(
            ProgressOperation.Repair,
            title: Strings.Format(
                asUpdate ? "ProgressTitleUpdating" : "ProgressTitleRepairing",
                _updateService.Profile.DisplayName),
            subtitle: Strings.Get("ProgressSubVerifying"),
            bar1Label: "ProgressBarVerify",
            bar2Label: "ProgressBarRepair",
            retry: () => RepairInstallAsync(asUpdate, targetReleaseTag));
        ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressBarDownload");
        ProgressPanelControl.PatchProgress.Value = 0;
        ProgressPanelControl.OverallProgress.Value = 0;
        ProgressPanelControl.PatchBytesText.Text = "";
        ProgressPanelControl.OverallBytesText.Text = "";

        var nativeInstaller = new NativeInstallService();

        try
        {
            var speed = new SpeedTracker();

            // Phase weights so the OverallProgress bar advances through extract +
            // overlay instead of freezing at the end of the download. Mirrors the
            // scheme InstallAsync uses (DL/Extract/Overlay), summing to 100%.
            const double weightDownload = 60;
            const double weightExtract  = 20;
            const double weightOverlay  = 20;

            var dlProgress = new Progress<DownloadProgress>(p =>
            {
                speed.Sample(p.BytesReceived);
                if (p.TotalBytes > 0)
                {
                    var eta = speed.EstimateTimeRemaining(p.TotalBytes - p.BytesReceived);
                    ProgressPanelControl.PatchProgress.Value = p.Percentage;
                    ProgressPanelControl.OverallProgress.Value = (p.Percentage / 100.0) * weightDownload;
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
                    ? Strings.Format("ProgressSpeedDownload", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Format(
                    "StatusDownloadingInstaller", _updateService.Profile.DisplayName);
            });

            var statusProgress = new Progress<string>(s =>
            {
                SetStatus(s);
                ProgressPanelControl.LblCurrentPatch.Text = s;
            });

            // Extract progress — advances the bar (60→80%) while the payload ZIP
            // is decompressed, so Repair no longer looks frozen during extraction.
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
                    ? Strings.Format("ProgressSpeedExtract", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Overlay progress — advances the bar (80→100%) while the mod files
            // are copied on top (full re-overlay) or the damaged set is restored.
            var overlayProgress = new Progress<NativeInstallService.ModOverlayProgress>(p =>
            {
                speed.Sample(p.BytesDone);
                double pct = p.BytesTotal > 0
                    ? (double)p.BytesDone / p.BytesTotal * 100.0
                    : (p.FilesTotal > 0 ? (double)p.FilesDone / p.FilesTotal * 100.0 : 0);
                ProgressPanelControl.PatchProgress.Value = pct;
                ProgressPanelControl.OverallProgress.Value =
                    weightDownload + weightExtract + (pct / 100.0) * weightOverlay;
                ProgressPanelControl.PatchBytesText.Text = $"{p.FilesDone}/{p.FilesTotal} files";
                ProgressPanelControl.OverallBytesText.Text = $"{ProgressPanelControl.OverallProgress.Value:0}%";
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Format("StatusInstallingMod", p.FilesDone, p.FilesTotal);
                ProgressPanelControl.SpeedText.Text = speed.BytesPerSecond > 0
                    ? Strings.Format("ProgressSpeedCopy", FormatBytes((long)speed.BytesPerSecond))
                    : "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Phase changes: reset the speed tracker at each boundary so the
            // extract/overlay speed isn't polluted by the download's byte history.
            var phaseProgress = new Progress<InstallPhase>(_ =>
            {
                speed.Reset();
                ProgressPanelControl.SpeedText.Text = "";
                ProgressPanelControl.EtaText.Text = "";
            });

            // Choose the repair strategy. A PLAIN repair (not an update, not a
            // version switch) VERIFIES FIRST: if the overlay is intact it skips the
            // multi-GB download (but still auto-continues into any pending updates
            // below); if anything is damaged it re-lays the WHOLE mod overlay (NOT a
            // granular per-file copy) so the install is restored to a complete,
            // known-good state. An update / version switch — and an old install
            // with no manifest FileHashes — go straight to the full re-overlay.
            // NOTE: this only covers the mod OVERLAY; base-game engine files
            // (cloned from AoE3) aren't in the verify set and aren't re-laid here.
            bool plainRepair = !asUpdate && targetReleaseTag == null;
            var preManifest = plainRepair ? InstallManifest.TryLoad(installPath) : null;
            // True when verify found no damage: we skip the download/re-overlay AND
            // the structural recheck (the verify we just ran is the proof), but we
            // still fall through to the pending-update continuation below.
            bool intact = false;
            int intactFilesChecked = 0;

            if (plainRepair && VerifyService.HasFileHashes(preManifest))
            {
                // ---- Verify first: is anything damaged? ----
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("ProgressBarVerify");
                var coveredFiles = _updateService.Profile.Translations?.CoveredFiles;
                var verifyProgress = new Progress<VerifyService.VerifyProgress>(t =>
                {
                    if (t.Total > 0)
                    {
                        ProgressPanelControl.PatchProgress.Value = 100.0 * t.Done / t.Total;
                        ProgressPanelControl.OverallProgress.Value = (100.0 * t.Done / t.Total) * 0.2;
                        ProgressPanelControl.PatchBytesText.Text = $"{t.Done} / {t.Total}";
                    }
                });
                SetStatus(Strings.Get("StatusVerifying"));
                var pre = await Task.Run(() => VerifyService.VerifyAgainstManifest(
                    installPath, preManifest!, coveredFiles, verifyProgress, _operatingCts!.Token));
                var damaged = pre.MissingItems.Concat(pre.CorruptItems)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                if (damaged.Count == 0)
                {
                    // Intact — skip the multi-GB download. Don't return: fall through
                    // so the pending-update continuation below still runs.
                    intact = true;
                    intactFilesChecked = pre.TotalFilesChecked;
                    SetStatus(Strings.Format("StatusRepairNothing", pre.TotalFilesChecked));
                }
                else
                {
                    // Damaged — re-lay the WHOLE overlay (not just the damaged set).
                    // This re-downloads the payload, so warn on low disk space first.
                    if (!ConfirmRepairSpaceOk(installPath))
                        throw new OperationCanceledException();
                    SetStatus(Strings.Format("StatusRepairingFiles", damaged.Count));
                    ProgressPanelControl.LblCurrentPatch.Text =
                        Strings.Format("StatusRepairingFiles", damaged.Count);
                    await nativeInstaller.InstallModOnlyAsync(
                        _updateService.Profile,
                        ResolveInstallVersion(overrideTag: targetReleaseTag),
                        payloadUrls,
                        installPath,
                        dlProgress,
                        statusProgress,
                        phaseProgress,
                        extractProgress,
                        overlayProgress,
                        payloadSha256: payloadSha256,
                        ct: _operatingCts!.Token);
                }
            }
            else
            {
                // Incremental delta patch first (only for a normal update to the approved tag, when
                // the mod opted in): downloads/apply just the changed files. Any doubt returns false
                // and we fall through to the full re-overlay below — the delta can never make an
                // update worse than the full path, only faster. See DeltaPatchService.
                bool deltaApplied = false;
                if (asUpdate && targetReleaseTag == null
                    && DeltaPatchService.IsEligible(_updateService.Profile))
                {
                    deltaApplied = await TryApplyGitHubDeltaAsync(
                        nativeInstaller, installPath, statusProgress, phaseProgress);
                }

                if (!deltaApplied)
                {
                    // Full re-overlay re-downloads the payload — warn on low space first.
                    if (!ConfirmRepairSpaceOk(installPath))
                        throw new OperationCanceledException();
                    // Mod-only install on top of existing (overwrites all overlay files).
                    // Repair re-stamps the manifest with the version we just verified.
                    await nativeInstaller.InstallModOnlyAsync(
                        _updateService.Profile,
                        ResolveInstallVersion(overrideTag: targetReleaseTag),
                        payloadUrls,
                        installPath,
                        dlProgress,
                        statusProgress,
                        phaseProgress,
                        extractProgress,
                        overlayProgress,
                        payloadSha256: payloadSha256,
                        ct: _operatingCts!.Token);
                }
            }

            var recheckProfile = _updateService.Profile;
            VerifyResult recheck;
            if (intact)
            {
                // Nothing was re-laid; the verify we just ran is the proof it's good.
                recheck = new VerifyResult(new List<string>(), new List<string>(), 0);
            }
            else
            {
                // Re-verify. Show it's still working (don't flash 100% as "done").
                SetStatus(Strings.Get("StatusVerifying"));
                ProgressPanelControl.LblCurrentPatch.Text = Strings.Get("StatusVerifying");
                // Full-overlay branch: structural recheck only (hashPass:false) —
                // we just laid the bytes whose hashes we wrote, so a full re-hash
                // (minutes on a multi-GB install) proves nothing and looked frozen.
                recheck = await Task.Run(() =>
                    VerifyInstallation(installPath, recheckProfile, hashProgress: null, hashPass: false));
            }

            ProgressPanelControl.PatchProgress.Value = 100;
            ProgressPanelControl.OverallProgress.Value = 100;

            if (recheck.MissingItems.Count == 0 && recheck.CorruptItems.Count == 0)
            {
                // Persist the version we just laid down. For GitHubReleases this
                // is the effective tag (approved, or the resolved latest for
                // follow-latest mods), so the next CheckAsync sees installed ==
                // latest and stops offering the update (hides the Update button).
                var st = _config.GetState(_updateService.Profile.Id);
                var laidDownVersion = ResolveInstallVersion(overrideTag: targetReleaseTag);
                if (!string.IsNullOrEmpty(laidDownVersion))
                    st.LastKnownVersion = laidDownVersion;

                // Version switch (Fase 1): if the user installed a version OTHER
                // than the recommended one, PIN it so the launcher doesn't
                // immediately offer to "update" them back (the same pause
                // mechanism as Fase 0). Installing the recommended version clears
                // any stale pin so updates flow normally again. Baseline =
                // EffectiveGitHubTag, not the raw approved tag: for a
                // follow-latest mod, picking the latest in the picker is
                // "following the recommendation", not a deviation to pin (a pin
                // there would suppress every future follow-latest update);
                // without followLatest, effective == approved — zero change.
                if (targetReleaseTag != null)
                {
                    var baseline = EffectiveGitHubTag(_updateService.Profile);
                    st.PinnedVersion =
                        string.Equals(laidDownVersion, baseline, StringComparison.OrdinalIgnoreCase)
                            ? ""
                            : laidDownVersion;
                }
                _config.Save();
                updated = true;

                // Intact (nothing was re-laid) → "nothing to repair"; otherwise the
                // usual update/repair-success line.
                var okMsg = intact
                    ? Strings.Format("StatusRepairNothing", intactFilesChecked)
                    : Strings.Get(asUpdate ? "StatusUpdateSuccess" : "StatusRepairSuccess");
                SetStatus(okMsg);
                // Repair: the user could already play before; no need to
                // surface PLAY on completion (sidebar already has PLAY).
                ShowProgressCompleted("ProgressTitleCompleted", okMsg);
                // Free the re-downloaded payload from %Temp% (see InstallAsync).
                _ = System.Threading.Tasks.Task.Run(NativeInstallService.TryCleanupTemp);
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

        // After a successful GitHubReleases update, re-check so the StatusCard /
        // Update button reflect that installed == approved now (button hides).
        if (asUpdate && updated)
        {
            // The GH re-overlay path doesn't run the WolPatcher post-update
            // translation reconcile, so do it here: revert an incompatible pack
            // to English and notify. Safe no-op when no pack is active.
            try
            {
                var profile = _updateService.Profile;
                var ts = new TranslationService(
                    _updateService.InstallPath!, profile.Translations?.CoveredFiles);
                // Reconcile translations against the version we actually installed.
                // For a version SWITCH that's the chosen tag (not LatestVersion,
                // which would be wrong when switching to an older release).
                var installedVer = ResolveInstallVersion(overrideTag: targetReleaseTag);
                var notice = ts.ReconcileAfterUpdate(
                    _config, profile.Id,
                    targetReleaseTag != null
                        ? installedVer
                        : (_updateService.LatestVersion?.Ver ?? installedVer));
                ShowTranslationRevertNotice(notice);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"GH post-update translation reconcile failed: {ex.Message}");
            }
            InvalidateActiveModCheckCache();
            await CheckAsync();
        }
        // A plain Repair (asUpdate == false) must also refresh the UI so the
        // CTA reflects the now-recognized install — the asUpdate branch above
        // already did this for updates. With manifest-baseline recognition a
        // successful repair flips CurrentVersion from null to a real value, so
        // the primary action moves off the stale "Install".
        else if (updated)
        {
            InvalidateActiveModCheckCache();
            await CheckAsync();

            // Auto-continue: a WolPatcher repair re-lays the base payload, which
            // can leave the install behind the latest version (the base is a
            // snapshot + incremental patches). Rather than make the user notice
            // "Update available" and click it, chain straight into the update
            // flow — the same entry point the Update button uses for pending
            // patches. No loop risk: applying the patches reaches the latest
            // version and clears _pendingDownloads; GitHubReleases repairs lay
            // the full version so they have none.
            if (_pendingDownloads.Count > 0 && !_isBusy)
            {
                if (!EnsureGameNotRunning()) return;
                SetStatus(Strings.Get("StatusContinuingUpdate"));
                await ApplyUpdateWithElevationCheckAsync();
            }
        }
    }

    /// <summary>
    /// Surfaces a post-update translation revert to the user (instead of the old
    /// silent fall-back to English). No-op when <paramref name="notice"/> is null.
    /// </summary>
    private void ShowTranslationRevertNotice(Models.TranslationRevertNotice? notice)
    {
        if (notice == null) return;
        var forVer = notice.PackForVersions.Count > 0
            ? string.Join(", ", notice.PackForVersions)
            : "?";
        var body = Strings.Format(
            "TranslationRevertedBody", notice.PackName, forVer, notice.NewModVersion ?? "?");
        SetStatus(body);
        MessageBox.Show(this, body, Strings.Get("TranslationRevertedTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Runs the update flow, but first checks whether the launcher has write
    /// permission on the mod's install folder. If it doesn't (typical for
    /// installs under C:\Program Files), prompts the user to consent to a
    /// UAC elevation and relaunches the app with admin privileges.
    /// </summary>
    private async Task ApplyUpdateWithElevationCheckAsync(
        InstallCompletion installContext = InstallCompletion.None)
    {
        if (string.IsNullOrEmpty(_updateService.InstallPath))
        {
            await ApplyAsync(installContext);
            return;
        }

        // If we already have write access, just proceed normally
        if (ElevationService.CanWriteTo(_updateService.InstallPath))
        {
            await ApplyAsync(installContext);
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

    // The launcher update found at startup, stashed so the title-bar pill's
    // click handler can open the download dialog without re-checking. Null when
    // no update is available.
    private LauncherUpdateService.UpdateCheckResult? _pendingLauncherUpdate;

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
        // skippedTag is intentionally empty: the persistent "skip this version"
        // suppression was removed. Users were hitting Cancel on the auto-modal
        // and then never being reminded (SkippedLauncherTag silenced the tag
        // forever), so they stayed on old versions. Now an available update
        // surfaces a persistent, non-invasive PILL in the title bar that
        // reminds on EVERY launch until the user actually updates. The SemVer
        // guard inside EvaluateUpdate still gates "remote strictly newer than
        // installed", so this never offers a same/older version. Passing ""
        // also recovers users who previously dismissed (their stale persisted
        // SkippedLauncherTag is no longer read).
        var result = await LauncherUpdateService.CheckAsync(
            lastInstalledTag: _config.LastInstalledLauncherTag,
            skippedTag: "",
            cachedETag: _config.LauncherUpdateETag);

        if (!result.UpdateAvailable)
        {
            // No update pending (or a rollback below the installed version):
            // cache the ETag so subsequent launches short-circuit on 304 and
            // spare the unauthenticated GitHub rate-limit. Only write when we
            // got one back, to avoid clobbering a good cached value on failure.
            if (!string.IsNullOrEmpty(result.ResponseETag) &&
                !string.Equals(result.ResponseETag, _config.LauncherUpdateETag, StringComparison.Ordinal))
            {
                _config.LauncherUpdateETag = result.ResponseETag;
                _config.Save();
            }
            _pendingLauncherUpdate = null;
            LauncherUpdatePill.Visibility = Visibility.Collapsed;
            StopLauncherUpdatePillPulse();
            return;
        }

        // An update IS pending. Do NOT cache the ETag — a cached ETag makes the
        // NEXT launch receive 304 Not Modified, which CheckAsync reports as
        // NoUpdate, which would HIDE the pill even though the user never updated
        // (the "appeared once then never again" bug). Clearing it forces a full
        // (non-conditional) check each launch WHILE an update is pending, so the
        // pill reliably reappears every time and we always hold the full payload
        // for the download dialog. Once the user updates, UpdateAvailable becomes
        // false on its own and the branch above resumes 304-caching.
        if (!string.IsNullOrEmpty(_config.LauncherUpdateETag))
        {
            _config.LauncherUpdateETag = "";
            _config.Save();
        }

        // Surface the persistent pill instead of popping a modal. The user
        // opens the dialog when ready via LauncherUpdatePill_Click.
        _pendingLauncherUpdate = result;
        LauncherUpdatePill.Content = Strings.Format("LauncherUpdatePill", result.LatestVersion);
        LauncherUpdatePill.ToolTip = Strings.Get("LauncherUpdatePillTooltip");
        LauncherUpdatePill.Visibility = Visibility.Visible;
        PulseLauncherUpdatePill();
        // Also surface it in the bell (deduped per tag) so it's discoverable from the
        // notification history, not just the pill. Click → the self-update dialog.
        _notifications.RaiseLauncherUpdate(
            result.RemoteTag,
            Strings.Get("NotifLauncherUpdateTitle"),
            Strings.Format("NotifLauncherUpdateBody", result.LatestVersion));
        DiagnosticLog.Write($"Launcher update {result.RemoteTag} available — showing persistent pill.");
    }

    /// <summary>
    /// Opens the launcher self-update dialog when the user clicks the title-bar
    /// pill. Accepting downloads + relaunches (the dialog owns that, persisting
    /// LastInstalledLauncherTag); cancelling just closes the dialog and leaves
    /// the pill visible on purpose — no persistent skip, so the reminder returns
    /// next launch until the user updates.
    /// </summary>
    private void LauncherUpdatePill_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingLauncherUpdate == null) return;
        var dialog = new LauncherUpdateDialog(_pendingLauncherUpdate, _config) { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Gentle scale "breath" on the update pill to draw the eye. LOOPS while the
    /// pill is shown: a short double-pulse occupies the first ~700 ms of each
    /// ~3.6 s cycle (long idle gap between breaths), repeated forever. The bounded
    /// 3× version "fired once at startup then settled forever", so a user who
    /// looked at the pill later only ever saw it static — it read as broken. The
    /// long idle gap keeps it a nudge, not a harassment. Stopped on hide via
    /// <see cref="StopLauncherUpdatePillPulse"/>. Mirrors
    /// <see cref="PulseNotificationBell"/> but scales instead of rotating.
    /// </summary>
    private void PulseLauncherUpdatePill()
    {
        var pulse = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
        {
            // One cycle = a short double-pulse, then a long hold at rest.
            Duration = TimeSpan.FromMilliseconds(3600),
            // Breathe forever (gently) so it's visible whenever the user looks; the
            // long idle gap below keeps each cycle mostly at rest. Stopped on hide.
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };
        void Key(double scale, double ms) => pulse.KeyFrames.Add(
            new System.Windows.Media.Animation.EasingDoubleKeyFrame(
                scale, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        Key(1.0, 0); Key(1.08, 160); Key(1.0, 340); Key(1.05, 500); Key(1.0, 680);
        Key(1.0, 3600); // long idle gap before the next breath
        LauncherUpdatePillScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        LauncherUpdatePillScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty, pulse.Clone());
    }

    /// <summary>
    /// Stops the looping pill breath and returns the scale to its 1.0 base. Called
    /// when the pill is hidden so no animation clock lingers on a Collapsed element.
    /// </summary>
    private void StopLauncherUpdatePillPulse()
    {
        LauncherUpdatePillScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        LauncherUpdatePillScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty, null);
    }

    private async Task CheckAsync(string? forceInstallPath = null)
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
        // genuinely instant after the first visit to each mod. SKIPPED when the
        // user just picked a folder (forceInstallPath) — that must re-detect the
        // install from the chosen path, not replay a stale "not installed".
        if (forceInstallPath == null
            && _checkResultCache.TryGetValue(profileAtStart, out var cached))
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
            var result = await _updateService.CheckAsync(statusReporter, _cts.Token, forceInstallPath);

            if (!string.Equals(_updateService.Profile.Id, profileAtStart, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write(
                    $"CheckAsync stale: started for '{profileAtStart}', " +
                    $"active is now '{_updateService.Profile.Id}'. Skipping result writeback.");
                return;
            }

            // Cache so the next visit to this mod is sync — but NEVER cache a degraded
            // offline result (network was unreachable), or the synchronous cache
            // fast-path above would replay it all session and never surface the real
            // updates once we're back online. ApplyCheckResult still runs either way,
            // so PLAY renders from the local install state.
            if (!result.Degraded)
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
    /// <summary>
    /// True when the user has pinned this mod to its currently-installed version
    /// (<see cref="ModState.PinnedVersion"/> == the detected version). While
    /// pinned, the launcher PAUSES update prompts: the PLAY button stays "Play"
    /// instead of flipping to "Update", and the secondary Update button is hidden.
    /// The user opted to keep playing this version; they resume updates from Mod
    /// Properties. Nothing is auto-updated either way — this only hides the prompt.
    /// </summary>
    private bool IsUpdatePausedByPin(UpdateService.CheckResult result)
    {
        var pinned = _config.GetState(_updateService.Profile.Id).PinnedVersion;
        if (string.IsNullOrEmpty(pinned)) return false;
        var cur = result.CurrentVersion?.Ver;
        return !string.IsNullOrEmpty(cur)
            && string.Equals(pinned, cur, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyCheckResult(UpdateService.CheckResult result)
    {
        // When the mod has more than one registered copy, prefix the active
        // install's path with its label so the user always sees WHICH copy is
        // active (e.g. "[Wars of Liberty (2)]  C:\…"). Single-install shows just
        // the path, exactly as before.
        var activeSt = _config.GetActiveState();
        InstallPathText.Text =
            activeSt.HasMultipleInstalls && !string.IsNullOrEmpty(_updateService.InstallPath)
                ? $"[{CopyDisplayLabel(null, _updateService.InstallPath)}]  {PathDisplay.CompactPathMiddle(_updateService.InstallPath, 64)}"
                : (_updateService.InstallPath ?? "(not detected)");

        _modIsInstalled = result.IsValidInstall;

        // "Search for my install" affordance: shown on the hero next to Install
        // ONLY when an isolated-folder mod (WoL) reads as not-installed — so a
        // user who already has it can point the launcher at it instead of
        // re-downloading. Hidden by default here; the not-installed branches
        // below flip it on. Never for the stock game.
        DashboardSearchInstallButton.Visibility = Visibility.Collapsed;

        // Backstop for the "update finished" bell: if the detected installed version
        // advanced since we last recorded it, raise it here in the USER's own session
        // (covers an elevated / other-profile apply that couldn't write this user's
        // bell, and install/repair paths that don't go through ApplyAsync). Idempotent
        // with the direct raise in ApplyAsync. Skipped for offline/degraded results.
        ReconcileUpdateFinishedNotification(result);

        // Once the mod is confirmed installed, make sure its desktop / Start
        // Menu shortcut points at a real .ico. Older installs wrote the cached
        // .png path into the shortcut's IconLocation, which Windows can't
        // render (it falls back to the exe icon = "no mod icon"). This is the
        // shared check-result path (cache replay + network), so it covers
        // startup, mod switch, post-install and post-repair. Once per mod per
        // session, off the UI thread (COM + disk), best-effort.
        if (result.IsValidInstall
            && !string.IsNullOrEmpty(_updateService.InstallPath)
            && _shortcutHealAttempted.Add(_updateService.Profile.Id))
        {
            var healProfile = _updateService.Profile;
            var healInstallPath = _updateService.InstallPath!;
            _ = Task.Run(() =>
                Services.NativeInstallService.TryHealShortcutIcons(healProfile, healInstallPath));
        }

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

                // GitHubReleases: surface a dedicated Update button when the
                // installed tag differs from the catalog's approved tag. The
                // re-overlay (download + extract on top + deletion) runs through
                // UpdateButton_Click → RepairInstallAsync(asUpdate: true).
                var ghCur = result.CurrentVersion?.Ver;
                var ghLatest = result.LatestVersion?.Ver;
                bool ghHasNewer =
                    _updateService.Profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases
                    && !string.IsNullOrEmpty(ghCur) && !string.IsNullOrEmpty(ghLatest)
                    && !string.Equals(ghCur, ghLatest, StringComparison.OrdinalIgnoreCase);
                // The user can pause the prompt by pinning their version (Fase 0).
                bool ghPaused = ghHasNewer && IsUpdatePausedByPin(result);
                bool ghUpdate = ghHasNewer && !ghPaused;

                ActionPanelControl.UpdateButton.Visibility =
                    ghUpdate ? Visibility.Visible : Visibility.Collapsed;

                SetStatus(ghUpdate
                    ? Strings.Format("StatusUpdateAvailableGh", ghLatest!)
                    : ghPaused
                        ? Strings.Format("StatusUpdatePausedPinned", ghCur!)
                        : Strings.Format(
                            _updateService.Profile.IsStockGame
                                ? "StatusStockReady"
                                : "StatusReadyExternalUpdates",
                            _updateService.Profile.DisplayName));
            }
            else if (_updateService.Profile.IsStockGame)
            {
                // Detect-only base game we couldn't locate on disk. There's
                // nothing for the launcher to install — point the user at
                // their store/disc and leave PLAY disabled until it's found.
                SetPrimaryAction(PrimaryAction.Play, enabled: false);
                SetStatus(Strings.Format(
                    "StatusStockNotDetected", _updateService.Profile.DisplayName));
            }
            else if (launcherCanInstall)
            {
                SetPrimaryAction(PrimaryAction.Install);
                SetStatus(Strings.Get("StatusNotInstalled"));
                MaybeShowSearchInstall();
            }
            else
            {
                SetPrimaryAction(PrimaryAction.Install, enabled: false);
                SetStatus(Strings.Format(
                    "StatusModNotInstalledExternal", _updateService.Profile.DisplayName));
            }

            _pendingDownloads = new();
            MaybeResetProgressUI();
            return;
        }

        // From here down: WoL-specific path.

        if (!result.IsValidInstall)
        {
            // No installation detected — primary becomes "Install".
            SetStatus(Strings.Get("StatusNotInstalled"));
            SetPrimaryAction(PrimaryAction.Install);
            MaybeShowSearchInstall();
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
        // While the user is pinned to their installed version (Fase 0), pause the
        // prompt: hide the secondary Update button and (below) keep PLAY as Play.
        bool updatePaused = IsUpdatePausedByPin(result);
        ActionPanelControl.UpdateButton.Visibility =
            (versionKnown && _pendingDownloads.Count > 0 && !updatePaused)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!versionKnown && result.IsValidInstall)
        {
            // A VALID install (marker present) whose version we couldn't identify —
            // e.g. a transiently short/truncated or offline UpdateInfo, or a
            // fall-back to an ancient mirror. NEVER push a destructive from-scratch
            // reinstall: the mod is on disk and playable. Keep PLAY, show a neutral
            // note, and DROP the pending list (those "updates" came from a stale
            // UpdateInfo — acting on them would be a downgrade). Repair/reinstall
            // stays available via the gear menu for anyone who actually wants it.
            DiagnosticLog.Write(
                "Valid install with unrecognized version — keeping Play (not offering a reinstall).");
            _pendingDownloads = new();
            SetStatus(Strings.Get("StatusInstalledVersionUnknown"));
            SetPrimaryAction(PrimaryAction.Play);
            MaybeResetProgressUI();
        }
        else if (!versionKnown)
        {
            long totalBytes = 0;
            foreach (var d in _pendingDownloads) totalBytes += d.Size;
            SetStatus(Strings.Format(
                "StatusUpdatesAvailable",
                _pendingDownloads.Count,
                FormatBytes(totalBytes)));
            SetPrimaryAction(PrimaryAction.Install);
            MaybeResetProgressUI();
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
            MaybeResetProgressUI();
        }
        else if (updatePaused)
        {
            // User pinned this version: patches exist but we don't push them.
            // PLAY stays Play (not Update) so they keep playing their version,
            // and the status explains updates are paused (resume in Mod Properties).
            SetStatus(Strings.Format("StatusUpdatePausedPinned", result.CurrentVersion?.Ver));
            SetPrimaryAction(PrimaryAction.Play);
            MaybeResetProgressUI();
        }
        else
        {
            long totalBytes = 0;
            foreach (var d in _pendingDownloads) totalBytes += d.Size;
            SetStatus(Strings.Format(
                "StatusUpdatesAvailable",
                _pendingDownloads.Count,
                FormatBytes(totalBytes)));
            // Version is known and patches are pending → primary
            // becomes UPDATE. Legacy design pointed primary at Play and
            // surfaced UPDATE as a secondary button (so the user could
            // still launch the old version), but that secondary button
            // lives inside LegacyPlayContent which is Visibility=
            // Collapsed in the cinema dashboard — it was effectively
            // hidden, leaving the user with no way to apply patches.
            // Mirroring Steam's behaviour: when patches are pending,
            // the primary CTA IS update. PlayButton_Click's switch
            // routes PrimaryAction.Update through
            // ApplyUpdateWithElevationCheckAsync, so the wiring is
            // already in place.
            SetPrimaryAction(PrimaryAction.Update);
            MaybeResetProgressUI();
        }

        // Notification bell: raise an "update available" item for the ACTIVE mod
        // when a newer version exists and the user hasn't pinned it. Deduped per
        // (mod, latest-version) inside NotificationCenter, so re-checks are quiet.
        // Skipped while pinned (updatePaused) or when the version is unknown
        // (CurrentVersion null = fresh install, not an update).
        MaybeNotifyUpdateAvailable(
            _updateService.Profile, result.CurrentVersion?.Ver, result.LatestVersion?.Ver,
            _pendingDownloads.Count, versionKnown, updatePaused);
    }

    /// <summary>
    /// Raise an "update available" notification (deduped) when a mod has a newer
    /// version the launcher can surface. Shared by the active-mod check
    /// (<see cref="ApplyCheckResult"/>) and the background sweep of other
    /// installed mods. No-op while pinned or when the installed version is unknown.
    /// </summary>
    private void MaybeNotifyUpdateAvailable(
        ModProfile profile, string? currentVer, string? latestVer,
        int pendingCount, bool versionKnown, bool updatePaused)
    {
        if (!versionKnown || updatePaused) return;
        bool hasNewer = !string.IsNullOrEmpty(latestVer)
            && !string.Equals(currentVer, latestVer, StringComparison.OrdinalIgnoreCase);
        bool canApply = pendingCount > 0;
        if (!hasNewer && !canApply) return;
        var ver = latestVer ?? "";
        _notifications.RaiseUpdateAvailable(
            profile.Id, ver,
            Strings.Get("NotifUpdateAvailableTitle"),
            Strings.Format("NotifUpdateAvailableBody", profile.DisplayName,
                string.IsNullOrEmpty(ver) ? "?" : ver));
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
        // Cancel the running OP (its own token), not a background check's _cts. The
        // pause/cancel strip is only interactive while viewing the operating mod, so
        // _updateService above is that mod's service.
        _operatingCts?.Cancel();
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

        // Color-code the cinema dashboard progress strip per operation
        // (install=blue, update=gold, repair=amber, verify=lilac,
        // uninstall=red) so the user can read the operation type at
        // a glance from the bar gradient + icon colour.
        SetDashboardProgressTone(op);

        // Start the polling pump that mirrors the legacy panel into the
        // visible cinema-dashboard progress strip. The pump auto-stops
        // itself when the panel goes back to Idle.
        StartDashboardProgressPump();
    }

    /// <summary>
    /// Paints the cinema-dashboard progress bar + icon in the colour
    /// associated with a given operation. The bar gradient lives on
    /// ProgressBar.Foreground (the template binds PART_Indicator's
    /// Background to it), so swapping Foreground is the only thing
    /// needed to retint the indicator. Icon foreground + glyph follow
    /// the same operation so the eye reads "this is what's running"
    /// from both signals at once.
    /// </summary>
    private void SetDashboardProgressTone(ProgressOperation op)
    {
        if (DashboardProgressBar == null) return;

        var (gradientKey, toneKey, glyph) = op switch
        {
            ProgressOperation.Install   => ("ProgressGradientInstall",   "ToneInstall",   ""), // Download
            ProgressOperation.Update    => ("ProgressGradientUpdate",    "ToneUpdate",    ""), // Sync
            ProgressOperation.Repair    => ("ProgressGradientRepair",    "ToneRepair",    ""), // Repair
            ProgressOperation.Verify    => ("ProgressGradientVerify",    "ToneVerify",    ""), // CheckMark
            ProgressOperation.Uninstall => ("ProgressGradientUninstall", "ToneUninstall", ""), // Delete
            _                           => ("ProgressGradientUpdate",    "ToneUpdate",    ""),
        };

        try
        {
            DashboardProgressBar.Foreground = (System.Windows.Media.Brush)FindResource(gradientKey);
            var tone = (System.Windows.Media.Brush)FindResource(toneKey);
            if (DashboardProgressIcon != null)
            {
                DashboardProgressIcon.Text = glyph;
                DashboardProgressIcon.Foreground = tone;
            }
            if (DashboardProgressLabel != null) DashboardProgressLabel.Foreground = tone;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SetDashboardProgressTone({op}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 200ms DispatcherTimer that copies legacy ProgressPanelControl state
    /// into the visible cinema dashboard while an operation is running.
    /// Polling beats threading a SyncDashboardProgress() call into every
    /// one of the ~20 progress-update sites scattered across the file
    /// (installs, downloads, extractions, uninstalls, repairs). The pump
    /// is cheap (text + value writes) and stops itself when ProgressState
    /// goes back to Idle, so it has zero cost outside of active ops.
    /// </summary>
    private System.Windows.Threading.DispatcherTimer? _dashboardProgressPump;
    private void StartDashboardProgressPump()
    {
        _dashboardProgressPump?.Stop();
        _dashboardProgressPump = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _dashboardProgressPump.Tick += (_, _) =>
        {
            SyncDashboardProgressFromLegacyPanel();
            if (_progressState == ProgressState.Idle)
            {
                _dashboardProgressPump?.Stop();
                _dashboardProgressPump = null;
            }
        };
        _dashboardProgressPump.Start();
        // Mirror immediately so the user doesn't see a 200ms lag at the
        // very start of an op.
        SyncDashboardProgressFromLegacyPanel();
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

    /// <summary>
    /// Gear-menu "Install another copy…": installs an ADDITIONAL isolated copy of
    /// the active mod into a new folder (a second install the launcher tracks and
    /// can switch between), instead of overwriting the existing one.
    /// </summary>
    private async void MenuInstallAnotherCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (_updateService.Profile.IsStockGame) return;
        await InstallAsync(addNewSlot: true);
    }

    private async void MenuRepairInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        // Stock Age of Empires III is detect-only — there is no launcher-managed
        // payload to repair. Never run a repair against the base game.
        if (_updateService.Profile.IsStockGame) return;
        if (!_modIsInstalled || string.IsNullOrEmpty(_updateService.InstallPath))
        {
            SetStatus(Strings.Get("StatusNotInstalled"));
            return;
        }
        await RepairInstallAsync();
    }

    private void MenuVerifyFiles_Click(object sender, RoutedEventArgs e)
    {
        // No launcher-managed install to verify for the stock base game.
        if (_updateService.Profile.IsStockGame) return;
        VerifyButton_Click(sender, e);
    }

    /// <summary>
    /// Opens the diagnostic log file in the system's default text editor —
    /// useful for support / debugging without us having to surface every
    /// internal state in the UI.
    /// </summary>
    private void MenuViewLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = AppPaths.LogFile;
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

    /// <summary>
    /// Bundles the diagnostic files into a single .zip, ASKING the user where to
    /// save it (a Save dialog pre-filled with a timestamped name + Desktop), then
    /// opens Explorer with it pre-selected — so a user reporting a bug can attach
    /// ONE file instead of hunting for the log under %LocalAppData%. The bundle
    /// excludes the config (Discord token) — see <see cref="DiagnosticLog.ExportBundle"/>.
    /// </summary>
    private void ShareDiagnostics()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop)) desktop = AppPaths.DataDir;

        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Get("ModPropShareDiagnosticsSaveTitle"),
            Filter = "ZIP archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            FileName = $"WoL-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            InitialDirectory = desktop,
        };

        // Owner = the Properties dialog if it's open (so the Save box doesn't sit
        // behind it), else the main window.
        Window owner = _modPropertiesDialog ?? (Window)this;
        if (picker.ShowDialog(owner) != true) return;   // user cancelled — do nothing
        var zipPath = picker.FileName;

        try
        {
            // Also fold in the active mod's game user-data OOS/sync/log artifacts
            // (My Games\<folder>), so an in-game OUT-OF-SYNC report is diagnosable —
            // a sim desync is written by AoE3, not the launcher log. Read-only; null
            // (e.g. the stock game, which has no managed user-data folder) is a no-op.
            var gameUserDataDir = UserDataService.GetUserDataFolder(_updateService.Profile.UserDataFolder);
            DiagnosticLog.ExportBundle(zipPath, gameUserDataDir: gameUserDataDir);

            // Reveal the freshly-created zip selected in Explorer (ready to drag
            // into Discord / attach). /select expects a quoted absolute path.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{zipPath}\"",
                UseShellExecute = true,
            });
            DiagnosticLog.Write($"Diagnostics bundle written to '{zipPath}'.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Share diagnostics failed: {ex.Message}");
            MessageBox.Show(owner,
                Strings.Format("ModPropShareDiagnosticsFailed", ex.Message),
                Strings.Get("ModPropShareDiagnostics"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
    private static VerifyResult VerifyInstallation(
        string installPath, ModProfile profile,
        IProgress<VerifyService.VerifyProgress>? hashProgress = null,
        bool hashPass = true,
        CancellationToken ct = default)
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

        // --- Generic: AoE3 base-game presence ---
        // The native install pipeline (WolPatcher / GitHubReleases) clones AoE3
        // and flattens bin\ into the root, so a CORRECT install ALWAYS has the
        // three version-key data files at data\. If they're missing, the base
        // game wasn't laid down — e.g. a PARTIAL clone the clone-count gate in
        // NativeInstallService.InstallAsync didn't catch because it copied SOME
        // files (the gate only fires on a total 0-file clone). Without this the
        // generic layer below only confirms the MOD payload landed, not the base,
        // so a GitHubReleases mod (Improvement Mod) could verify "OK" yet be
        // unplayable (missing engine DLLs + data — the game exits on launch).
        // Skipped for DelegatedExternal / Manual mechanisms whose on-disk layout
        // the launcher doesn't control. (IsStockGame never reaches here — verify
        // is guarded against the detect-only base game.)
        bool nativeAoe3Install = profile.UpdateMechanism is ModUpdateMechanism.WolPatcher
                                 or ModUpdateMechanism.GitHubReleases;
        if (nativeAoe3Install)
        {
            string[] baseKeyFiles =
            {
                @"data\protoy.xml",
                @"data\techtreey.xml",
                @"data\stringtabley.xml",
            };
            foreach (var rel in baseKeyFiles)
            {
                totalChecked++;
                if (!File.Exists(Path.Combine(installPath, rel)))
                    missing.Add(rel + " (AoE3 base file — the game can't launch without it)");
            }
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

        // --- Per-file integrity pass (the real check) ---
        // When the manifest carries per-file size+SHA-256 fingerprints (installs
        // from this build onward), verify EVERY overlay file against them — the
        // exact damaged/missing set, which Repair then re-copies selectively.
        // Older installs have no FileHashes, so fall back to the legacy random
        // spot-check below rather than reporting everything as unverifiable.
        //
        // hashPass is the cost gate: the full pass re-reads the entire overlay
        // (multi-GB for WoL → minutes), so it runs ONLY for the explicit "Verify
        // files" action. AUTOMATIC post-install / post-repair rechecks pass
        // hashPass:false and use the fast structural + spot-check layer instead —
        // hashing files we JUST laid down (whose hashes we just wrote) proves
        // nothing and was making the operation look frozen at 100%.
        var manifest = InstallManifest.TryLoad(installPath);
        if (hashPass && VerifyService.HasFileHashes(manifest))
        {
            var hashRes = VerifyService.VerifyAgainstManifest(
                installPath, manifest!, profile.Translations?.CoveredFiles, hashProgress, ct);
            // Don't double-count files the structural layer already flagged.
            foreach (var m in hashRes.MissingItems)
                if (!missing.Contains(m)) missing.Add(m);
            foreach (var c in hashRes.CorruptItems)
                if (!corrupt.Contains(c)) corrupt.Add(c);
            totalChecked += hashRes.TotalFilesChecked;

            // Base-engine files (SEPARATE map): a damaged engine file is NOT
            // repairable from the mod payload, so it's reported with a distinct
            // "reinstall the base game" suffix and is deliberately kept OUT of the
            // missing/corrupt overlay sets that drive the Repair re-overlay.
            if (VerifyService.HasEngineHashes(manifest))
            {
                var engineDamaged = VerifyService.VerifyEngineFiles(
                    installPath, manifest!, profile.Translations?.CoveredFiles, ct);
                foreach (var rel in engineDamaged)
                    corrupt.Add(rel + Strings.Get("VerifyEngineSuffix"));
                totalChecked += manifest!.EngineFileHashes.Count;
            }

            // Unexpected/leftover files — diagnostic only (logged, not flagged):
            // a patched install legitimately gains untracked files, so surfacing
            // them as problems would be noise. Capped.
            try
            {
                var extras = VerifyService.FindUnexpectedFiles(installPath, manifest!);
                if (extras.Count > 0)
                {
                    DiagnosticLog.Write($"Verify: {extras.Count} unexpected/untracked file(s) (first 50):");
                    foreach (var x in extras.Take(50)) DiagnosticLog.Write($"  [extra] {x}");
                }
            }
            catch { /* diagnostic only */ }
        }
        else
        {
            // --- Legacy fallback: spot-check zero-byte content files (random sample) ---
            // A content file at 0 bytes is almost always a broken download/extract
            // regardless of which mod produced it.
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
        }

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
    private async Task<PayloadResolution?> ResolvePayloadUrlsAsync(
        UpdateService? targetService = null, string? overrideTag = null)
    {
        var service = targetService ?? _updateService;
        var profile = service.Profile;
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
                // overrideTag (when set) installs a user-chosen version. Null =
                // the default target: the approved tag, or — for follow-latest
                // mods — the newest stable release cached by CheckAsync
                // (EffectiveGitHubTag; external-hosted mods always resolve to
                // approved there, so ResolveAssetAsync's external guard holds).
                var tag = string.IsNullOrWhiteSpace(overrideTag)
                    ? EffectiveGitHubTag(profile)
                    : overrideTag;
                var asset = await new GitHubReleaseDownloader()
                    .ResolveAssetAsync(ghs, tag, default);
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
        var payloadUrls = service.EffectivePayloadZipUrls();
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
    private string ResolveInstallVersion(UpdateService? targetService = null, string? overrideTag = null)
    {
        var service = targetService ?? _updateService;
        var profile = service.Profile;
        return profile.UpdateMechanism switch
        {
            // overrideTag stamps the user-chosen version; else the effective
            // default (approved tag, or the cached latest for follow-latest
            // mods) — this MUST match what ResolvePayloadUrlsAsync downloads.
            ModUpdateMechanism.GitHubReleases =>
                string.IsNullOrWhiteSpace(overrideTag)
                    ? EffectiveGitHubTag(profile)
                    : overrideTag.Trim(),
            _ => service.CurrentVersion?.Ver
                ?? service.LatestVersion?.Ver ?? "",
        };
    }

    /// <summary>
    /// The GitHub tag a default (no explicit user choice) install/update of
    /// this mod targets. Delegates to the pure
    /// <see cref="UpdateService.ResolveEffectiveGitHubTag"/> with the cached
    /// latest tag CheckAsync persisted — fresh by construction: the Update CTA
    /// only appears after a CheckAsync, which is what writes the cache.
    /// IMPORTANT: never thread this value through
    /// <c>targetReleaseTag</c>/<c>overrideTag</c> — those mean "the USER chose
    /// a version" and trigger the version-picker auto-pin, which would pin the
    /// mod and suppress every future follow-latest update.
    /// </summary>
    private string EffectiveGitHubTag(ModProfile profile)
        => UpdateService.ResolveEffectiveGitHubTag(
            profile.GitHubReleases, _config.GetState(profile.Id).LastKnownLatestVersion);

    /// <summary>
    /// Fase 1 — enumerate the active GitHubReleases mod's published versions so
    /// the Mod Properties picker can list them. Empty for non-GitHubReleases mods
    /// or when no source repo is configured. Network errors propagate so the
    /// dialog can show a "couldn't load versions" hint.
    /// </summary>
    private async Task<IReadOnlyList<GitHubReleaseDownloader.ReleaseInfo>> ListGitHubVersionsAsync()
    {
        var ghs = _updateService.Profile.GitHubReleases;
        if (_updateService.Profile.UpdateMechanism != ModUpdateMechanism.GitHubReleases
            || ghs == null || string.IsNullOrWhiteSpace(ghs.SourceRepo))
            return Array.Empty<GitHubReleaseDownloader.ReleaseInfo>();
        return await new GitHubReleaseDownloader().ListReleasesAsync(ghs.SourceRepo);
    }

    /// <summary>
    /// Fase 1 — install a user-chosen GitHubReleases version (re-overlay the
    /// chosen tag's payload via the shared repair/update path). Routes through
    /// <see cref="RepairInstallAsync"/> with the target tag so download +
    /// verify + manifest-stamp + the auto-pin all run exactly like a normal
    /// update, just for a specific version.
    /// </summary>
    private Task InstallGitHubVersionAsync(string tag) =>
        RepairInstallAsync(asUpdate: true, targetReleaseTag: tag);

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
    /// <param name="targetService">
    /// Optional override so the Mod Browser (v0.9) can install a mod that
    /// isn't the currently active one without forcing a mod-switch first.
    /// When null we default to <see cref="_updateService"/>, which keeps
    /// every existing call site behaving exactly as before.
    /// </param>
    /// <summary>
    /// Returns a NEW install folder that collides with neither an
    /// already-registered copy nor an existing on-disk folder, appending
    /// " (2)", " (3)", … to <paramref name="baseFolder"/> as needed. Used to
    /// suggest a destination for an additional copy.
    /// </summary>
    private static string MakeUniqueInstallFolder(string baseFolder, IEnumerable<string> existing)
    {
        var bas = baseFolder.TrimEnd('\\', '/');
        var taken = new HashSet<string>(
            existing.Select(p => p.TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
        string candidate = bas;
        int n = 2;
        while (taken.Contains(candidate) || Directory.Exists(candidate))
            candidate = $"{bas} ({n++})";
        return candidate;
    }

    /// <param name="addNewSlot">
    /// When true, install an ADDITIONAL copy of an already-installed mod into a
    /// new folder instead of overwriting the existing one: suggests a
    /// collision-free folder, always clones (isolated copy, even for
    /// InPlaceOverlay mods), disambiguates shortcuts/registry per-install, and
    /// on completion rotates the previous active install into OtherInstalls.
    /// </param>
    private async Task InstallAsync(UpdateService? targetService = null, bool addNewSlot = false)
    {
        if (_isBusy) return;

        // Check if game is running first
        if (!EnsureGameNotRunning()) return;

        // Resolve once at the top so the rest of the body talks about the
        // install target uniformly via `profile` / `service`.
        var service = targetService ?? _updateService;
        var profile = service.Profile;

        // Tracks whether the install verified clean — gates the post-install
        // auto-continue-into-update ("todo corrido") below so it never fires
        // after a failed / incomplete install.
        bool installSucceeded = false;
        // True when an addNewSlot copy install left the CURRENT active install in place — the
        // new copy is registered inactive, so the tail must NOT auto-continue an update against
        // the still-active OTHER copy. keptCopyVersion is that new copy's snapshot version, for
        // the "Copy installed" bell (no CheckAsync runs on it, so CurrentVersion would be null).
        bool keptCurrentActive = false;
        string keptCopyVersion = "";
        // Install id of the freshly-registered copy, so the tail can (when safe) auto-switch to it
        // and bring it fully current — "install from scratch, then update" for a copy (Option A).
        string newCopyInstallId = "";

        var payload = await ResolvePayloadUrlsAsync(service);
        if (payload == null) return;
        var payloadUrls = payload.Urls;
        var payloadSha256 = payload.Sha256;

        // Detect AoE3
        var aoe3Installs = AoE3Detector.FindAll();

        // Fallback: if the fast name/registry detection found nothing, run a
        // bounded content scan that catches a clean AoE3 base in a NON-STANDARD
        // folder (e.g. …\Microsoft Studios\Age of Empires III) the probes miss.
        // Off the UI thread; the passive variant skips whole-drive enumeration
        // (an antivirus behavioural signal). Non-fatal — worst case is the same
        // "no AoE3 detected" the dialog already handles.
        if (aoe3Installs.Count == 0)
        {
            try
            {
                aoe3Installs = await Task.Run(
                    () => AoE3Detector.FindAllDeep(includeDriveRoots: false));
                if (aoe3Installs.Count > 0)
                    DiagnosticLog.Write(
                        $"Install: deep AoE3 scan found base at '{aoe3Installs[0].ModRoot}'.");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Install: deep AoE3 scan failed (non-fatal): {ex.Message}");
            }
        }

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
        if (addNewSlot)
        {
            // An ADDITIONAL copy is ALWAYS an isolated clone in a NEW folder —
            // even for InPlaceOverlay mods, since two overlays can't share one
            // AoE3 folder. Base the suggestion on the IsolatedFolder convention
            // (a mod-named subfolder of AoE3) and make it collision-free against
            // every already-registered copy and any existing folder on disk.
            string baseFolder = !string.IsNullOrEmpty(aoe3SourcePath)
                ? Path.Combine(aoe3SourcePath, profile.DisplayName)
                : (string.IsNullOrEmpty(profile.DefaultInstallFolder)
                    ? profile.DisplayName
                    : profile.DefaultInstallFolder);
            suggestedFolder = MakeUniqueInstallFolder(
                baseFolder, _config.GetState(profile.Id).AllInstallPaths());
        }
        else if (profile.InstallType == ModInstallType.InPlaceOverlay)
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
            profile.DisplayName)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        var installFolder = dialog.SelectedFolder;
        aoe3SourcePath = dialog.Aoe3SourcePath; // may have been inferred

        // For an additional copy: guard against picking a folder that is ALREADY
        // a registered copy of this mod (would reinstall over it / desync slots).
        // (Adopting an unregistered real install is a Fase 4 follow-up.)
        if (addNewSlot)
        {
            var st = _config.GetState(profile.Id);
            bool already = st.AllInstallPaths().Any(p =>
                string.Equals(p.TrimEnd('\\', '/'), installFolder.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));
            if (already)
            {
                MessageBox.Show(this,
                    Strings.Format("DlgInstallCopyExistsBody", installFolder),
                    Strings.Get("DlgInstallCopyExistsTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        // Per-install discriminator for shortcuts / registry (the folder leaf).
        // null for a normal/first install → canonical names, zero change.
        string? installLabel = addNewSlot
            ? Path.GetFileName(installFolder.TrimEnd('\\', '/'))
            : null;

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
        _operationIsBackgroundable = true;   // isolated below → switch-to-another-mod OK
        // The op targets the NEW folder (a copy install), not the active copy — so a DIFFERENT
        // already-installed copy of the same mod stays playable while this one installs.
        _operatingInstallPath = installFolder;
        _operatingCts = new CancellationTokenSource();
        ShowDownloadControls(true);

        // Open the colored progress panel in the sidebar with the right
        // title for an Install. The progress reporters below fill in the
        // bars / step counter / speed.
        StartProgressPanel(
            ProgressOperation.Install,
            title: Strings.Format("ProgressTitleInstalling", profile.DisplayName),
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

            var installVersion = ResolveInstallVersion(service);

            // Retry loop for corruption failures (InvalidDataException —
            // raised by the ZIP extractor when a local file header is bad,
            // or by the SHA-256 pin check when a part's hash doesn't match
            // the catalog). Both indicate the bytes on disk can't be
            // trusted, so we wipe the temp folder and ask the user whether
            // to redownload. Other exception types (OperationCanceledException,
            // network errors, IO errors during clone) skip this loop and go
            // straight to the outer catch.
            const int MaxInstallAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (aoe3SourcePath != null)
                    {
                        // Canonical sibling-mod exclusion list. When the
                        // user installs Improvement Mod and already has
                        // Wars of Liberty cloned under the AoE3 root
                        // (common Steam layout: ...\Age Of Empires 3\
                        // Wars of Liberty\), the clone phase would
                        // otherwise scoop that whole sibling install
                        // into the new destination. The helper lives on
                        // LauncherConfig so every install / repair flow
                        // shares the exact same rule — see
                        // GetSiblingInstallPaths for the contract. The
                        // clone service ALSO auto-detects mod folders
                        // via "*-manifest.json" probes as defense in
                        // depth, but the explicit list covers the
                        // config-tracked cases reliably.
                        var siblingExcludes = _config.GetSiblingInstallPaths(profile.Id);
                        if (siblingExcludes.Count > 0)
                            DiagnosticLog.Write($"Install: excluding sibling mod paths: {string.Join(" ; ", siblingExcludes)}");

                        // Full install: clone AoE3 + overlay mod
                        await nativeInstaller.InstallAsync(
                            profile,
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
                            extraExcludedSubtrees: siblingExcludes,
                            installLabel: installLabel,
                            ct: _operatingCts!.Token);
                    }
                    else
                    {
                        // Mod-only: just download and copy mod files
                        await nativeInstaller.InstallModOnlyAsync(
                            profile,
                            installVersion,
                            payloadUrls,
                            installFolder,
                            dlProgress,
                            statusProgress,
                            phaseProgress,
                            extractProgress,
                            overlayProgress,
                            payloadSha256: payloadSha256,
                            installLabel: installLabel,
                            ct: _operatingCts!.Token);
                    }
                    break; // success — exit retry loop
                }
                catch (InvalidDataException ex)
                {
                    DiagnosticLog.Write(
                        $"Install attempt {attempt}/{MaxInstallAttempts} failed with corrupted payload: {ex.Message}");

                    bool canRetry = attempt < MaxInstallAttempts;
                    bool userAgreesToRetry = canRetry && MessageBox.Show(
                        this,
                        Strings.Format("DlgInstallRetryCorruptBody", attempt, MaxInstallAttempts),
                        Strings.Get("DlgInstallRetryCorruptTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;

                    if (!userAgreesToRetry)
                    {
                        // Either the user declined or we burned every attempt.
                        // Surface a clear message via the outer error handler.
                        if (!canRetry)
                        {
                            throw new InvalidDataException(
                                Strings.Format("StatusInstallCorruptedGaveUp", MaxInstallAttempts), ex);
                        }
                        throw;
                    }

                    // Wipe the bad bytes so the next attempt downloads fresh.
                    NativeInstallService.CleanupTempPayload();

                    // Repaint the progress block so the user sees we're
                    // starting over (not just stuck on the prior step).
                    SetStatus(Strings.Format("StatusInstallRetrying", attempt + 1, MaxInstallAttempts));
                    ProgressPanelControl.PatchProgress.Value = 0;
                    ProgressPanelControl.OverallProgress.Value = 0;
                    ProgressPanelControl.PatchBytesText.Text = "";
                    ProgressPanelControl.OverallBytesText.Text = "";
                    ProgressPanelControl.SpeedText.Text = "";
                    ProgressPanelControl.EtaText.Text = "";
                    ProgressPanelControl.LblCurrentPatch.Text =
                        Strings.Format("StatusInstallRetrying", attempt + 1, MaxInstallAttempts);
                }
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
            var installState = _config.GetState(profile.Id);
            if (addNewSlot
                && !string.IsNullOrEmpty(installState.InstallPath)
                && !ModState.PathEquals(installState.InstallPath, installFolder))
            {
                // "Install another copy" NEVER auto-switches you to the new copy — it registers
                // it in the copy list at its snapshot version, and you switch to it by hand when
                // you want it. This keeps you on (and possibly playing) your current copy while
                // the new one installs in the background. It updates when you switch to it.
                var leaf = Path.GetFileName(installFolder.TrimEnd('\\', '/'));
                installState.RegisterInstall(installFolder, leaf);
                newCopyInstallId = installState.OtherInstalls
                    .FirstOrDefault(i => ModState.PathEquals(i.InstallPath, installFolder))?.Id ?? "";
                // Do NOT stamp the copy with installVersion — that's the ACTIVE copy's
                // detected version, not this NEW copy's. The payload snapshot can be an
                // older version than the active copy, so claiming the active version
                // would hide a needed update (no Update CTA when you switch to it, since
                // it'd read "already on that version"). Leave it unknown; the first
                // switch re-detects the copy's real version from its own data\ files
                // (the check-cache is invalidated on switch — see SwitchActiveInstallAsync).
                keptCurrentActive = true;
            }
            if (!keptCurrentActive)
            {
                installState.InstallPath = installFolder;
                if (!string.IsNullOrEmpty(installVersion))
                    installState.LastKnownVersion = installVersion;
            }
            _config.Save();

            // Verify installation. VerifyInstallation is now profile-aware —
            // it always checks the probe file + spot-checks zero-byte content
            // files, and additionally applies the WoL-specific markers
            // (art\zulushield\, .bar archives, sound\, AI3\) when the active
            // profile uses the WolPatcher mechanism. Non-WoL mods get the
            // generic layer only, so no false positives.
            // Automatic post-install recheck: structural only (hashPass:false).
            // The full hash pass would re-read the multi-GB overlay we just wrote
            // for no gain; the explicit "Verify files" action is where it belongs.
            var verifyResult = await Task.Run(
                () => VerifyInstallation(installFolder, profile, hashProgress: null, hashPass: false));
            int totalProblems = verifyResult.MissingItems.Count + verifyResult.CorruptItems.Count;
            if (totalProblems == 0)
            {
                installSucceeded = true;
                SetStatus(Strings.Format(
                    "StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
                ShowProgressCompleted("ProgressTitleCompleted",
                    Strings.Format(
                        "StatusInstallSuccessVerified", verifyResult.TotalFilesChecked));
                // Install succeeded — the multi-GB payload in %Temp% is no longer
                // needed (a Repair re-downloads). Free it now instead of waiting
                // for the next launch. Off-thread so the GB delete doesn't block UI.
                _ = System.Threading.Tasks.Task.Run(NativeInstallService.TryCleanupTemp);
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
        catch (Services.InstallBaseGameMissingException ex)
        {
            // The AoE3 base wasn't cloned (0 files) — the mod would ship
            // unplayable (missing engine DLLs + data\*.xml). Distinct from a
            // corrupt payload (no retry); surface a clear, localized cause so
            // the user knows to check their AoE3 install / exclusions rather
            // than blaming the mod download.
            DiagnosticLog.Write($"Install aborted — AoE3 base not cloned: {ex}");
            SetStatus(Strings.Get("StatusInstallBaseMissing"));
            ShowProgressError(Strings.Get("StatusInstallBaseMissing"));
        }
        catch (Services.PayloadFileBlockedException ex)
        {
            // Windows Defender (or another AV) quarantined a mod file mid-install —
            // a known false positive (e.g. AI3\wolai.upl). A retry re-fails (the temp
            // source is already gone), so tell the user exactly what to do instead of
            // surfacing the raw "...contains a virus..." IOException.
            DiagnosticLog.Write($"Install aborted — antivirus blocked payload file: {ex}");
            var msg = Strings.Format("InstallDefenderBlocked", ex.BlockedFile);
            SetStatus(msg);
            ShowProgressError(msg);
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

        if (installSucceeded)
        {
            // The mod is now durably cache-eligible (installed, not just
            // "operating"): reset the per-session fetch guards and re-kick so
            // its images land in mod-assets\ even if an earlier in-flight
            // attempt failed, and so the detail-panel screenshots download on
            // next open. Cheap when the earlier attempt succeeded — warm-cache
            // resolve + conditional GETs. Uses the op's OWN profile (`service`),
            // not the displayed mod (the user may have switched away).
            _assetFetchAttempted.Remove(service.Profile.Id);
            _screenshotFetchAttempted.Remove(service.Profile.Id);
            _ = EnsureModAssetsAsync(service.Profile);
        }

        var installContext = addNewSlot ? InstallCompletion.Copy : InstallCompletion.Fresh;

        // A copy install that KEPT the current active copy (the user was playing another one):
        // the new copy is registered inactive at its snapshot version. Don't CheckAsync /
        // auto-continue against the still-active OTHER copy — just announce the new copy; it
        // updates when the user switches to it. Refresh the banner so the copy list updates.
        if (keptCurrentActive)
        {
            if (installSucceeded)
            {
                RefreshActiveModBanner();

                // Option A ("install from scratch, then update" for a copy): when it's
                // SAFE, auto-switch to the new copy and bring it fully current, so a copy
                // ends up ready like a first install — instead of sitting at its snapshot
                // until the user switches by hand. Reuses the ACTIVE-install update flow
                // (the copy becomes active first), so no non-active/background-update
                // plumbing: SwitchActiveInstallAsync re-checks the copy (detecting its real
                // version + pending), then MaybeAutoContinueUpdateAfterInstall patches it.
                // Gated: only when NO game is running (a switch is blocked mid-game) and the
                // copy's mod is still the displayed/active one (the user didn't switch mods
                // during the background install). If not safe, fall back to the old behavior
                // (announce the copy; it updates when the user switches to it by hand).
                bool broughtCurrent = false;
                if (!_isGameRunning
                    && !string.IsNullOrEmpty(newCopyInstallId)
                    && string.Equals(service.Profile.Id, _updateService.Profile.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write(
                        $"Copy install: auto-switching to new copy '{newCopyInstallId}' to bring it current.");
                    await SwitchActiveInstallAsync(newCopyInstallId);
                    broughtCurrent = await MaybeAutoContinueUpdateAfterInstall(InstallCompletion.Copy);
                }

                // Only bell "Copy installed" here when we did NOT continue into an update;
                // when we did, ApplyAsync raises the final bell (like the fresh-install tail).
                if (!broughtCurrent)
                    RaiseInstalledBell(service, isCopy: true, keptCopyVersion);
            }
            return;
        }

        // If the user switched to another mod while this install ran in the background, the
        // CheckAsync + auto-continue below would evaluate the now-DISPLAYED mod, patching the
        // wrong one and mis-labelling the bell. Refresh the INSTALLED mod's cache instead and
        // announce it; its pending patches surface as an Update CTA when the user returns to
        // it. (Mirrors ApplyAsync's tail guard.)
        if (!string.Equals(service.Profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (installSucceeded)
            {
                _checkResultCache.Remove(service.Profile.Id);
                RaiseInstalledBell(service, installContext == InstallCompletion.Copy,
                    service.CurrentVersion?.Ver ?? "");
            }
            return;
        }

        // Re-check to detect the freshly installed mod
        InvalidateActiveModCheckCache();
        await CheckAsync();

        // "Todo corrido": if the freshly-installed payload landed BEHIND the
        // latest version, continue straight into the update flow instead of
        // leaving the user to click Update separately. No-op in the normal
        // case — the byte-faithful payload is already the latest, so the
        // recheck finds nothing pending and this returns immediately.
        if (installSucceeded)
        {
            bool continuedIntoUpdate = await MaybeAutoContinueUpdateAfterInstall(installContext);
            // If nothing was pending (payload already at the latest version), the
            // auto-continue didn't run — announce the install here with the installed
            // version. When it DID continue, ApplyAsync raises the bell (final version).
            if (!continuedIntoUpdate)
                RaiseInstalledBell(service, installContext == InstallCompletion.Copy,
                    service.CurrentVersion?.Ver ?? "");
        }
    }

    /// <summary>
    /// Whether a completed <see cref="ApplyAsync"/> should announce an INSTALL (and which
    /// kind) instead of an update. Threaded from <see cref="InstallAsync"/> through the
    /// auto-continue chain; the genuine-update callers leave it <see cref="None"/>.
    /// </summary>
    private enum InstallCompletion { None, Fresh, Copy }

    /// <summary>Raise the "Installation complete" / "Copy installed" bell, attributed to the
    /// given mod's service (NOT necessarily the displayed one — a background op may finish
    /// after the user switched away).</summary>
    private void RaiseInstalledBell(UpdateService svc, bool isCopy, string version)
    {
        var v = string.IsNullOrEmpty(version) ? "?" : version;
        _notifications.RaiseInstalled(
            svc.Profile.Id, v,
            Strings.Get(isCopy ? "NotifCopyInstalledTitle" : "NotifInstalledTitle"),
            Strings.Format(isCopy ? "NotifCopyInstalledBody" : "NotifInstalledBody",
                svc.Profile.DisplayName, v));
    }

    /// <summary>
    /// After a fresh install, auto-runs the update flow when the installed
    /// payload is recognized but still behind the latest version. Gated so it
    /// only fires for a WolPatcher install that is valid, version-recognized,
    /// has pending patches, and isn't pinned — otherwise it's a no-op. Keeps
    /// install→update a single continuous flow without a second user click.
    /// </summary>
    private async Task<bool> MaybeAutoContinueUpdateAfterInstall(InstallCompletion installContext)
    {
        if (!_checkResultCache.TryGetValue(_updateService.Profile.Id, out var result))
            return false;
        if (_updateService.Profile.UpdateMechanism != ModUpdateMechanism.WolPatcher)
            return false;
        if (!result.IsValidInstall) return false;
        if (result.CurrentVersion == null) return false;       // unrecognized → don't auto-act
        if (result.PendingDownloads.Count == 0) return false;  // already at latest
        if (IsUpdatePausedByPin(result)) return false;         // user pinned this version

        DiagnosticLog.Write(
            $"Auto-continue: fresh install at {result.CurrentVersion.Ver} has " +
            $"{result.PendingDownloads.Count} pending patch(es) → running update.");
        await ApplyUpdateWithElevationCheckAsync(installContext);
        return true;
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

    private async Task ApplyAsync(InstallCompletion installContext = InstallCompletion.None)
    {
        if (_isBusy) return;
        SetBusy(true);
        _operationIsBackgroundable = true;   // svc/pending captured below → switch-to-another-mod OK
        _operatingCts = new CancellationTokenSource();
        ShowDownloadControls(true);

        // Capture the operating service + downloads so a mid-op mod switch (background op)
        // can repoint _updateService/_pendingDownloads without corrupting THIS update or
        // mis-attributing its completion to the mod the user switched to.
        var svc = _updateService;
        var pending = _pendingDownloads;
        var fromVersion = svc.CurrentVersion?.Ver ?? "?";
        var toVersion = svc.LatestVersion?.Ver ?? "?";
        StartProgressPanel(
            ProgressOperation.Update,
            title: Strings.Format("ProgressTitleUpdating", svc.Profile.DisplayName),
            subtitle: Strings.Format("ProgressUpdating", fromVersion, toVersion),
            retry: () => ApplyAsync());

        bool succeeded = false;
        try
        {
            var statusReporter = new Progress<string>(SetStatus);
            var progressReporter = new Progress<UpdateProgress>(OnProgress);
            var phaseReporter = new Progress<UpdatePhase>(phase =>
            {
                _currentUpdatePhase = phase;
            });
            await svc.ApplyUpdatesAsync(
                pending, progressReporter, statusReporter, phaseReporter, _operatingCts!.Token);
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

            // Notification bell + tray toast. Routed through the NotificationCenter so it
            // lands in the bell history AND (via ToastRequested → ShowToast) raises the
            // tray balloon. When this ApplyAsync is the auto-continue leg of a FRESH
            // install (installContext set), announce it as an install / copy install
            // instead of "Update complete" — a fresh install chains install → update, and
            // the user's mental model is "I installed", not "I updated". A genuine update
            // (installContext None) keeps the "Update complete" bell exactly as before.
            if (installContext != InstallCompletion.None)
                RaiseInstalledBell(svc, installContext == InstallCompletion.Copy, toVersion);
            else
                _notifications.RaiseUpdateFinished(
                    svc.Profile.Id, toVersion,
                    Strings.Get("NotifUpdateFinishedTitle"),
                    Strings.Format("NotifUpdateFinishedBody",
                        svc.Profile.DisplayName,
                        string.IsNullOrEmpty(toVersion) ? "?" : toVersion));

            // If the post-update reconcile reverted an incompatible translation,
            // tell the user instead of silently switching them to English.
            ShowTranslationRevertNotice(svc.LastTranslationRevertNotice);
            svc.LastTranslationRevertNotice = null;
        }

        // Refresh the mod this op belongs to. If the user switched away while it ran
        // (background op), just drop its stale cache — its UI refreshes when they switch
        // back; don't CheckAsync the now-displayed OTHER mod off this update.
        if (succeeded)
        {
            if (string.Equals(_updateService.Profile.Id, svc.Profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateActiveModCheckCache();
                await CheckAsync();
            }
            else
            {
                _checkResultCache.Remove(svc.Profile.Id);
            }
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
        // MaxWidth is set on the TextBlocks THEMSELVES (not just the ToolTip
        // template) because a gear ContextMenu renders in a separate popup that
        // doesn't inherit MainWindow's ToolTip style — so without this the text
        // measures at infinite width and gets clipped to one line. With a bounded
        // MaxWidth + Wrap, WPF wraps the description to multiple lines reliably.
        const double TooltipContentMaxWidth = 320;
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = Brush("White"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            FontWeight = System.Windows.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = TooltipContentMaxWidth,
        };
        var descBlock = new System.Windows.Controls.TextBlock
        {
            Text = description,
            Foreground = Brush("#aaa"),
            FontSize = (double)Application.Current.FindResource("FontSizeBody"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = TooltipContentMaxWidth,
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
            // Guarded: this fires on the UI thread every 2 s for the whole game
            // session. Process.GetProcessesByName can throw transiently
            // (InvalidOperationException/Win32Exception) and OnGameExited touches
            // UI — an unhandled throw here would kill the launcher WHILE the game
            // runs (the game is launched detached, so it survives). Dispose the
            // returned Process handles too, or they leak over a long session.
            try
            {
                var processes = Process.GetProcessesByName(GameProcessName());
                bool running = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                if (!running)
                {
                    OnGameExited();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Game monitor tick failed (ignored): {ex.Message}");
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

        // Mirror the primary action into the visible cinema dashboard
        // PLAY button. The Dashboard button has its own gold-gradient
        // style (SidebarPrimaryButton) so we don't repaint Background;
        // only the label text + IsEnabled need to follow the legacy
        // ActionPanel button's state. Visibility is locked Visible
        // because the cinema layout treats PLAY as the permanent
        // hero CTA — Hidden actions become Disabled instead so the
        // button still anchors the layout.
        if (DashboardPlayButton != null && DashboardPlayButtonText != null)
        {
            DashboardPlayButtonText.Text = ActionPanelControl.PlayButtonText.Text;
            // Dynamic tooltip: explain what the primary action does in its CURRENT
            // state (Play vs Install vs Update vs Stop), so a newcomer knows what
            // the big button will do before clicking it. Localized per action.
            DashboardPlayButton.ToolTip = action switch
            {
                PrimaryAction.Install => TooltipHelper.Wrap(Strings.Get("TipCtaInstall")),
                PrimaryAction.Play => TooltipHelper.Wrap(Strings.Get("TipCtaPlay")),
                PrimaryAction.Update => TooltipHelper.Wrap(Strings.Get("TipCtaUpdate")),
                PrimaryAction.Stop => TooltipHelper.Wrap(Strings.Get("TipCtaStop")),
                _ => null,
            };
            // The ENABLED state is owned by RefreshOperationGate (displayed-vs-operating), so
            // a background op on another mod doesn't wrongly disable this mod's CTA.
            RefreshOperationGate();
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
    // Offline mode (observed connectivity — Services/ConnectivityState)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Fired (possibly off the UI thread) when the app-wide offline state flips.
    /// Marshals to the dispatcher and re-applies the offline UI.
    /// </summary>
    private void OnConnectivityChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnConnectivityChanged));
            return;
        }
        bool offline = ConnectivityState.IsOffline;
        ApplyOfflineModeUi(offline);
        // Bell item on the connectivity flip. RaiseConnectivity dedups consecutive
        // same-state so a flaky network doesn't spam the history. This only fires from
        // an actual OfflineChanged transition (never the initial online state).
        _notifications.RaiseConnectivity(
            offline,
            Strings.Get(offline ? "NotifOfflineTitle" : "NotifOnlineTitle"),
            Strings.Get(offline ? "NotifOfflineBody" : "NotifOnlineBody"));
    }

    /// <summary>
    /// Reflects the observed offline state across the UI: shows the title-bar chip
    /// and greys out the controls that REQUIRE internet (self-update pill, Workshop
    /// catalog refresh, multiplayer). PLAY and local actions (open folder, logs)
    /// stay enabled — installed mods remain playable offline. The update CTA isn't
    /// touched here: offline, CheckAsync degrades to no-pending so it never renders
    /// (see ApplyCheckResult), and SetBusy owns the Update button's enabled state.
    /// </summary>
    private void ApplyOfflineModeUi(bool offline)
    {
        if (OfflineChip != null)
        {
            OfflineChip.Content = Strings.Get("OfflineChip");
            OfflineChip.ToolTip = Strings.Get("OfflineChipTooltip");
            OfflineChip.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        }

        // The self-update pill points at a download that needs the network — hide it
        // while offline. Don't force-show when online; its own check controls that.
        if (offline && LauncherUpdatePill != null)
            LauncherUpdatePill.Visibility = Visibility.Collapsed;

        // Delegate to the views that own their own online-only controls. Strings are
        // passed in (ModsBrowser doesn't import the Localization layer).
        string needNet = Strings.Get("OfflineNeedsInternet");
        ModsBrowserView?.SetOfflineMode(offline, needNet);
        MultiplayerView?.SetOfflineMode(offline, needNet, Strings.Get("MpOfflineNotice"));
    }

    /// <summary>
    /// The offline chip is a retry affordance: an explicit click re-probes the
    /// network (self-update check + active-mod check), which reports success on
    /// reconnect and clears the chip — so the user doesn't have to wait for the
    /// periodic refresh / window-focus re-check.
    /// </summary>
    private async void OfflineChip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Self-update always hits GitHub (mod-mechanism independent) and reports
            // connectivity; the active-mod check refreshes its state too.
            await Task.WhenAll(CheckForLauncherUpdateAsync(), CheckAsync());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Offline chip retry failed: {ex.Message}");
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
        // Remember which mod a REAL op belongs to, so switching to a different mod can leave
        // its buttons live (background op). Cleared when the op ends.
        if (busy && !checkOnly)
        {
            _operatingModId = _updateService.Profile.Id;
            // Default target = the active install (update / repair / verify). InstallAsync
            // overrides it with the NEW folder right after SetBusy(true) for a copy install.
            _operatingInstallPath = _updateService.InstallPath;
        }
        else if (!busy)
        {
            _operatingModId = null;
            _operatingInstallPath = null;
            _operationIsBackgroundable = false;   // each op re-arms this after SetBusy(true)
        }

        // Lock the Mod Properties language tab during REAL ops (install / update /
        // repair) so the user can't swap translation data files mid-write. The
        // read-only check (checkOnly) doesn't mutate the install, so it doesn't lock.
        _modPropertiesDialog?.SetModBusy(busy && !checkOnly);
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

        // Gate the VISIBLE cinema-hero buttons by whether the DISPLAYED mod is the one this
        // op runs on — so an install on mod A leaves mod B's PLAY live when the user switches
        // to it (background op). See RefreshOperationGate.
        RefreshOperationGate();
    }

    /// <summary>
    /// True when the CURRENTLY DISPLAYED mod is the one a real op is running on — i.e. its
    /// action buttons + progress strip should reflect the busy op. False when idle, during a
    /// read-only check, or when the user switched to a DIFFERENT mod while an op runs in the
    /// background.
    /// </summary>
    private bool DisplayedModIsOperating
    {
        get
        {
            if (!_isBusy || _isCheckOnly || _operatingModId == null) return false;
            if (!string.Equals(_operatingModId, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase))
                return false;
            // Same mod. Gate this view only if it IS the copy being operated on. A DIFFERENT
            // already-installed copy of the same mod (its own path) stays playable while a new
            // copy installs. A fresh install (displayed path empty → nothing else to play) or an
            // unknown op target gates as before.
            var displayedPath = _updateService.InstallPath;
            if (string.IsNullOrEmpty(displayedPath) || string.IsNullOrEmpty(_operatingInstallPath))
                return true;
            return ModState.PathEquals(_operatingInstallPath, displayedPath);
        }
    }

    /// <summary>
    /// Sets the three visible hero buttons' enabled state from "is the displayed mod mid-op".
    /// The primary CTA + gear are blocked only for the mod that is actually operating; the
    /// MODS switch is ALWAYS live (switching to another installed mod is the whole point of
    /// background ops). Called from SetBusy, after a mod switch, and from SetPrimaryAction.
    /// </summary>
    private void RefreshOperationGate()
    {
        bool displayedBusy = DisplayedModIsOperating;
        DashboardPlayButton.IsEnabled =
            !displayedBusy && _primaryAction != PrimaryAction.Hidden && PrimaryActionEnabled();
        // Settings (gear) + MODS switch are ALWAYS enabled — Properties (settings, manage
        // installs, view logs) must stay reachable during an op; its destructive actions
        // (Verify/Repair/Uninstall) self-gate on _isBusy and the language tab locks via SetModBusy.
        DashboardSettingsButton.IsEnabled = true;
        DashboardChangeModButton.IsEnabled = true;
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

    /// <summary>
    /// Resets the progress strip to idle — but NOT while a background op is live, so a
    /// mod/copy switch during an install (which routes through ApplyCheckResult for the
    /// switched-to copy) doesn't wipe the running op's bar / speed / eta. The op keeps
    /// painting ProgressPanelControl and the pump mirrors it; after the op ends
    /// (_operatingModId == null) this resets normally.
    /// </summary>
    private void MaybeResetProgressUI()
    {
        if (_operatingModId == null) ResetProgressUI();
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
        // "Install another copy" — available once the mod is installed, for any
        // installable (non-stock) mod. Installs an isolated clone in a new folder.
        ActionPanelControl.MenuInstallAnotherCopy.IsEnabled =
            !_isBusy && _modIsInstalled && launcherCanInstall
            && !_updateService.Profile.IsStockGame;

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
            // Active first → compatible-with-installed-version → newest → name
            // (shared with the Mod Properties tab via OrderForDisplay).
            var ordered = Models.TranslationCompat.OrderForDisplay(
                entries.Values, _cachedTranslationIndex?.Translations, currentMod, activeId);
            foreach (var entry in ordered)
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

        // (The translator-facing "Package my translation" tool moved out of
        // this gear submenu — it now lives in Launcher Settings → Translations,
        // globalised across mods. The packager dialog used to be hard-coded
        // to the launcher's active mod, which forced translators to switch
        // mods before packaging for a different one.)
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

        // Compatibility: a pack the translator didn't declare for this mod version
        // is shown with a warning (amber + ⚠) but NOT disabled — matching the
        // LANGUAGE-tab cards — so the user can apply it under their own
        // responsibility (the apply dialog confirms first). Empty declared list is
        // "unknown", not a warning. The active pack stays green.
        bool incompatible = Models.TranslationCompat.IsVersionBlocked(
            entry.CompatibleWith, currentModVersion);
        if (incompatible) header += "  ⚠";
        // Positive counterpart: the translator declared this installed version → ✓ green.
        bool compatible = !isActive && !incompatible
            && Models.TranslationCompat.IsCompatible(entry.CompatibleWith, currentModVersion);
        if (compatible) header += "  ✓";

        var item = new System.Windows.Controls.MenuItem
        {
            Header = header,
            // Active / compatible = green; incompatible = amber warning; otherwise
            // white (unknown). Always enabled — incompatibility is a warning the
            // user can override, not a block.
            Foreground = Brush(isActive || compatible ? "#9bd99b" : (incompatible ? "#E0B341" : "White")),
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
            var releasesRepo = _updateService.EffectiveTranslationsRepo();
            var folderRepos = _updateService.EffectiveTranslationsFolderRepos();
            if (folderRepos.Count > 0 || !string.IsNullOrWhiteSpace(releasesRepo))
            {
                // Dual mode: folder-published packs (translations/<id>/ on main)
                // from every configured folder repo (default + user extras),
                // merged together and with legacy release-published packs.
                index = await registry.FetchAsync(folderRepos, releasesRepo);
            }

            // With multiple repos, an extra repo can ship packs whose targetMod
            // is a DIFFERENT mod — keep only packs meant for this mod (empty
            // targetMod = legacy/unverified, still allowed) so foreign packs
            // don't pollute this mod's language menu / version picker.
            if (index != null)
                index.Translations = index.Translations
                    .Where(e => Models.TranslationCompat.TargetModMatches(e.TargetMod, _updateService.Profile.Id))
                    .ToList();

            _cachedTranslationIndex = index;

            // Notification bell: surface translations that appeared since the
            // user's baseline for this mod.
            if (index != null)
                NotifyNewTranslations(_updateService.Profile, index);

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
    /// Diff a mod's freshly-fetched translation index against the user's
    /// per-mod baseline and bell each genuinely-new pack. On the FIRST fetch
    /// for a mod (empty baseline) we seed silently — otherwise a user with a
    /// catalog full of existing translations would be flooded on first launch.
    /// After the baseline exists, only packs that appear later bell.
    /// </summary>
    private void NotifyNewTranslations(ModProfile profile, TranslationIndex index)
    {
        var entries = index?.Translations;
        if (entries == null || entries.Count == 0) return;

        var state = _config.GetState(profile.Id);
        // Dedup key is centralized in TranslationCompat.KeyOf: release tag
        // (release packs) → id@contentHash (folder packs) → id@version (legacy).
        // A folder pack with changed bytes yields a new contentHash → re-bells.
        var keys = entries.Select(Models.TranslationCompat.KeyOf).Distinct().ToList();

        if (state.NotifiedTranslationKeys.Count == 0)
        {
            // First time we see this mod's translations — establish the baseline
            // quietly so only future packs bell.
            state.NotifiedTranslationKeys.AddRange(keys);
            PersistConfigInBackground();
            return;
        }

        foreach (var t in entries)
        {
            var key = Models.TranslationCompat.KeyOf(t);
            var label = !string.IsNullOrWhiteSpace(t.Name) ? t.Name : t.Language;
            _notifications.RaiseNewTranslation(
                profile.Id, key, t.Id,
                Strings.Get("NotifNewTranslationTitle"),
                Strings.Format("NotifNewTranslationBody", profile.DisplayName, label));
        }
    }

    /// <summary>
    /// Background sweep over INSTALLED mods other than the active one, checking
    /// each for an available update and a new translation, raising deduped
    /// notification-bell items. The active mod is already covered live by
    /// <see cref="ApplyCheckResult"/> / <see cref="RefreshTranslationIndexAsync"/>,
    /// so it's skipped here. Sequential + best-effort to stay within the GitHub
    /// API budget; gated by the same <c>CheckUpdatesOnStartup</c> opt-out as the
    /// rest of the launcher's outbound checks.
    /// </summary>
    private async Task SweepInstalledModsForNotificationsAsync()
    {
        if (!_config.CheckUpdatesOnStartup) return;

        // Try the central notification feed ONCE. When it answers (200 fresh or
        // 304-from-cache) it carries every mod's latest version + translation keys,
        // so a single cheap REST call replaces the per-mod GitHub checks below for
        // ALL installed mods. Only a genuine failure (network / bad JSON / no cache
        // on a 304) leaves `feed == null` → we fall back to the direct-GitHub path,
        // so the notifier is never a single point of failure.
        NotificationFeed? feed = null;
        var feedUrl = ResolveNotificationFeedUrl();
        if (feedUrl != null)
        {
            try
            {
                var fetch = await new NotificationFeedService()
                    .FetchAsync(feedUrl, _config.NotificationFeedETag);
                if (!string.IsNullOrEmpty(fetch.ETag)
                    && !string.Equals(fetch.ETag, _config.NotificationFeedETag, StringComparison.Ordinal))
                {
                    _config.NotificationFeedETag = fetch.ETag;
                    PersistConfigInBackground();
                }
                feed = fetch.Feed;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Notif sweep feed fetch failed: {ex.Message}");
            }
        }

        var activeId = _updateService.Profile.Id;
        foreach (var profile in ModRegistry.All)
        {
            if (profile.IsStockGame) continue;
            if (string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase)) continue;
            var state = _config.GetState(profile.Id);
            if (string.IsNullOrEmpty(state.InstallPath)) continue;   // not installed

            // --- Fast path: data from the central feed (no GitHub traffic) ---
            if (feed != null)
            {
                try
                {
                    await Dispatcher.InvokeAsync(() => ApplyFeedToMod(profile, state, feed));
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Notif sweep feed-apply failed for '{profile.Id}': {ex.Message}");
                }
                continue;
            }

            // --- Fallback: per-mod GitHub checks (feed unavailable) ---
            // One UI-free service per mod, reused for the version + translations-repo checks.
            var svc = new UpdateService(_config, profile);

            // --- Update check for this mod (off the UI thread) ---
            try
            {
                var result = await svc.CheckAsync();
                bool versionKnown = result.CurrentVersion != null;
                var pinned = state.PinnedVersion;
                bool paused = !string.IsNullOrEmpty(pinned)
                    && string.Equals(pinned, result.CurrentVersion?.Ver, StringComparison.OrdinalIgnoreCase);
                await Dispatcher.InvokeAsync(() => MaybeNotifyUpdateAvailable(
                    profile, result.CurrentVersion?.Ver, result.LatestVersion?.Ver,
                    result.PendingDownloads?.Count ?? 0, versionKnown, paused));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Notif sweep update-check failed for '{profile.Id}': {ex.Message}");
            }

            // --- New-translation check for this mod ---
            try
            {
                var relRepo = svc.EffectiveTranslationsRepo();
                var folderRepos = svc.EffectiveTranslationsFolderRepos();
                if (folderRepos.Count > 0 || !string.IsNullOrWhiteSpace(relRepo))
                {
                    var index = await new TranslationRegistryService().FetchAsync(folderRepos, relRepo);
                    if (index != null)
                    {
                        index.Translations = index.Translations
                            .Where(e => Models.TranslationCompat.TargetModMatches(e.TargetMod, profile.Id))
                            .ToList();
                        await Dispatcher.InvokeAsync(() => NotifyNewTranslations(profile, index));
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Notif sweep translation-check failed for '{profile.Id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolves the notification-feed URL from config. Empty → the launcher's
    /// built-in default; <c>"none"</c> → opt-out (returns null, caller falls back
    /// to the per-mod GitHub checks); any other value → that URL. Mirrors
    /// <see cref="RefreshCatalogAsync"/>'s repo resolution.
    /// </summary>
    private string? ResolveNotificationFeedUrl()
    {
        const string defaultUrl = "https://wol-notify.duckdns.org/manifest";
        var raw = _config.NotificationFeedUrl;
        if (string.IsNullOrEmpty(raw)) return defaultUrl;
        if (string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) return null;
        return raw;
    }

    /// <summary>
    /// Applies one mod's central-feed entry: raises a deduped "update available"
    /// item when the feed's latest version differs from the locally-cached
    /// installed version (<see cref="ModState.LastKnownVersion"/>), and diffs the
    /// feed's translation keys against the per-mod baseline. Reuses the exact same
    /// <see cref="MaybeNotifyUpdateAvailable"/> + dedup paths as the GitHub
    /// fallback — only the data source differs. Must run on the UI thread (touches
    /// the bound notification collection).
    /// </summary>
    private void ApplyFeedToMod(ModProfile profile, ModState state, NotificationFeed feed)
    {
        if (!feed.Mods.TryGetValue(profile.Id, out var entry) || entry == null) return;

        // Update availability. We only know the installed version from the cache;
        // an empty LastKnownVersion (never detected) is "unknown" and must NOT bell
        // — mirrors MaybeNotifyUpdateAvailable's versionKnown gate (a fresh/unknown
        // install is not an "update"). The server can't compute pending-patch
        // counts, so pass 0: the version-difference check carries the signal.
        var installed = state.LastKnownVersion;
        bool versionKnown = !string.IsNullOrEmpty(installed);
        var pinned = state.PinnedVersion;
        bool paused = !string.IsNullOrEmpty(pinned)
            && string.Equals(pinned, installed, StringComparison.OrdinalIgnoreCase);
        MaybeNotifyUpdateAvailable(profile, installed, entry.LatestVersion, 0, versionKnown, paused);

        NotifyNewTranslationKeys(profile, entry.Translations);
    }

    /// <summary>
    /// Feed-sourced sibling of <see cref="NotifyNewTranslations"/>: the central feed
    /// gives only the dedup KEYS (the GitHub release tag, falling back to
    /// <c>id@version</c> — identical to <c>NotifyNewTranslations</c>'s <c>KeyOf</c>),
    /// so dedup stays consistent across the feed and GitHub paths. Seeds the baseline
    /// silently on the first fetch for a mod; afterwards bells each genuinely-new key.
    /// A readable label / navigable id is derived best-effort from the key (the active
    /// mod still gets the richer language-name path via <see cref="RefreshTranslationIndexAsync"/>).
    /// </summary>
    private void NotifyNewTranslationKeys(ModProfile profile, IReadOnlyList<string>? keys)
    {
        if (keys == null || keys.Count == 0) return;
        var distinct = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (distinct.Count == 0) return;

        var state = _config.GetState(profile.Id);
        if (state.NotifiedTranslationKeys.Count == 0)
        {
            // First time we see this mod's translations — establish the baseline
            // quietly so only future packs bell (same rule as NotifyNewTranslations).
            state.NotifiedTranslationKeys.AddRange(distinct);
            PersistConfigInBackground();
            return;
        }

        foreach (var key in distinct)
        {
            var atIdx = key.IndexOf('@');
            var label = atIdx > 0 ? key.Substring(0, atIdx) : key;
            _notifications.RaiseNewTranslation(
                profile.Id, key, label,
                Strings.Get("NotifNewTranslationTitle"),
                Strings.Format("NotifNewTranslationBody", profile.DisplayName, label));
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
    private async Task RefreshCatalogAsync(bool force = false)
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

        await ModRegistry.RefreshFromCatalogAsync(repo, force: force);
        MaybeNotifyNewMods();
        if (force)
            _lastCatalogRefreshUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Raises a bell item for each community mod that has newly appeared in the
    /// catalog since the last refresh. The FIRST fetch silently baselines every
    /// currently-known mod (so the whole existing catalog doesn't flood the bell);
    /// afterwards only genuinely-new ids bell. The detect-only stock game is excluded;
    /// the WoL built-in is caught by the first-run baseline. Never throws.
    /// </summary>
    private void MaybeNotifyNewMods()
    {
        try
        {
            // Community mods only (IsBuiltIn excludes both WoL and the stock aoe3-tad).
            var community = ModRegistry.All.Where(p => !ModRegistry.IsBuiltIn(p.Id)).ToList();
            // Skip on a failed/empty catalog fetch (only built-ins present). Seeding the
            // baseline now would make EVERY community mod bell as "new" once the real
            // catalog loads later — wait until the catalog actually returned mods.
            if (community.Count == 0) return;
            // First real fetch → silently baseline the whole existing catalog; nothing bells.
            if (_notifications.SeedCatalogBaseline(community.Select(p => p.Id))) return;
            foreach (var p in community)
            {
                _notifications.RaiseNewMod(
                    p.Id,
                    Strings.Get("NotifNewModTitle"),
                    Strings.Format("NotifNewModBody", p.DisplayName));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"New-mod notification sweep failed: {ex.Message}");
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

        // Own the popups with whichever window the user is actually looking at:
        // the (non-modal) ModPropertiesDialog when it's open, else MainWindow.
        // Otherwise a modal owned by MainWindow opens BEHIND the Properties dialog
        // and looks like a freeze (everything disabled, nothing visible).
        System.Windows.Window owner = _modPropertiesDialog ?? (System.Windows.Window)this;

        // Guard: never apply a pack made for a DIFFERENT mod (it would overwrite
        // this mod's files with another's). Legacy packs (no targetMod) are allowed.
        if (!Models.TranslationCompat.TargetModMatches(entry.TargetMod, _updateService.Profile.Id))
        {
            MessageBox.Show(owner,
                Strings.Format("TranslationWrongModBody",
                    entry.Name, entry.TargetMod, _updateService.Profile.DisplayName),
                Strings.Get("TranslationWrongModTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var translations = new TranslationService(
            _updateService.InstallPath, _updateService.Profile.Translations?.CoveredFiles);
        var registry = new TranslationRegistryService();

        var dialog = new TranslationApplyDialog(
            entry,
            _updateService.CurrentVersion?.Ver,
            translations,
            registry)
        {
            Owner = owner,
        };

        if (dialog.ShowDialog() == true && dialog.AppliedSuccessfully)
        {
            _config.GetActiveState().ActiveTranslationId = entry.Id;
            // Remember WHICH version was applied (folder packs with a history);
            // empty for single-version packs. Drives the version picker's active mark.
            _config.GetActiveState().ActiveTranslationVersion = entry.Version ?? "";
            _config.Save();
            SetStatus(Strings.Format("StatusLangApplied", entry.Name));
            // Rebuild the cards so the just-applied pack shows as active without
            // needing to close and reopen the Properties dialog.
            _modPropertiesDialog?.RefreshLanguageTab();
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
            activeState.ActiveTranslationVersion = "";
            _config.Save();
            SetStatus(Strings.Get("StatusLangRevertedToEnglish"));
            _modPropertiesDialog?.RefreshLanguageTab();
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
        => RestoreUserDataCore();

    /// <summary>
    /// Restore flow shared by the gear menu and the Properties USER DATA tab.
    /// Returns the localized result line for the tab's inline hint (null =
    /// no backups / user cancelled the picker).
    /// </summary>
    private string? RestoreUserDataCore()
    {
        if (_isBusy) return null;

        var userDataFolderName = _updateService.Profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName)) return null;

        var backups = UserDataService.ListBackups(userDataFolderName);
        if (backups.Count == 0)
        {
            MessageBox.Show(this,
                Strings.Get("DlgRestoreNoBackupsBody"),
                Strings.Get("DlgRestoreNoBackupsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = new UserDataRestoreDialog(backups, userDataFolderName) { Owner = this };
        if (dialog.ShowDialog() != true) return null;
        if (dialog.RestoredBackup == null) return null;

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
        return Strings.Format("ModPropRestoreDone",
            Path.GetFileName(dialog.RestoredBackup.Path));
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
        => CreateUserDataBackupCore();

    /// <summary>
    /// Backup flow shared by the gear menu and the Properties USER DATA tab.
    /// Returns the localized result line for the tab's inline hint (null =
    /// cancelled / nothing to back up / failed — those paths already told
    /// the user via their own message boxes).
    /// </summary>
    private string? CreateUserDataBackupCore()
    {
        if (_isBusy) return null;

        var userDataFolderName = _updateService.Profile.UserDataFolder;
        if (string.IsNullOrEmpty(userDataFolderName)) return null;

        if (!UserDataService.HasExistingUserData(userDataFolderName))
        {
            MessageBox.Show(this,
                Strings.Get("DlgBackupNothingBody"),
                Strings.Get("DlgBackupNothingTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var confirm = MessageBox.Show(this,
            Strings.Get("DlgBackupConfirmBody"),
            Strings.Get("DlgBackupConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return null;

        var path = UserDataService.BackupUserData(userDataFolderName);
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show(this,
                Strings.Get("DlgUserDataAlertBackupFailedBody"),
                Strings.Get("DlgUserDataAlertBackupFailedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        SetStatus(Strings.Format("StatusUserDataBackedUp", path));
        return Strings.Format("ModPropBackupDone", Path.GetFileName(path));
    }

    private async void UninstallMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        // Never uninstall the stock base game — it's the user's own AoE3
        // install, and uninstall is a blanket recursive delete. The gear
        // dialog hides this for stock; this is the defence-in-depth guard.
        if (_updateService.Profile.IsStockGame) return;
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

            // After removing the ACTIVE copy: if the mod has OTHER registered
            // copies, promote the first to active (the uninstall only removed the
            // active copy's folder/overlay; the other copies on disk are intact).
            // Otherwise clear the saved path so re-detection runs from scratch.
            // A full config reset (ResetConfig) skips promotion — the user asked
            // to wipe everything.
            if (dialog.Options.ResetConfig || result.Success)
            {
                var st = _config.GetActiveState();
                if (result.Success && !dialog.Options.ResetConfig && st.OtherInstalls.Count > 0)
                {
                    var next = st.OtherInstalls[0];
                    st.OtherInstalls.RemoveAt(0);
                    st.AdoptInstall(next); // promote a remaining copy to active
                    DiagnosticLog.Write(
                        $"Uninstall: promoted remaining copy '{next.InstallPath}' to active.");
                }
                else
                {
                    st.InstallPath = "";
                }
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
