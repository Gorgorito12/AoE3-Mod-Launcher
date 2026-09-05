using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WarsOfLibertyLauncher;

public partial class App : System.Windows.Application
{
    // App-level startup applies HiDPI-friendly rendering settings AND a
    // maximize-respects-taskbar fix to every Window in the launcher.
    //
    // -- HiDPI text crispness ----------------------------------------
    //
    // The problem: on Windows displays scaled above 100% (the default on
    // most modern laptops — 125% or 150%), WPF's defaults produce visibly
    // blurry text and slightly fuzzy borders because:
    //   • UseLayoutRounding defaults to false, so a 50-logical-pixel
    //     element at 125% DPI scale lands on physical pixel 62.5 — and
    //     ClearType antialiasing smudges that fractional position into a
    //     blur instead of rendering crisply at integer pixels.
    //   • TextFormattingMode defaults to Ideal (typographically pretty
    //     at every scale, but soft-looking for small UI text) instead of
    //     Display (snapped to the target pixel grid, optimal for UI).
    //   • TextRenderingMode + TextHintingMode default to Auto, which can
    //     pick non-ClearType paths on some configurations.
    //
    // Three of the launcher's Windows (MainWindow, RadminAssistantWindow,
    // ModPropertiesDialog) already set TextOptions on their XAML root, but
    // none of them set UseLayoutRounding, and the other 14 dialogs set
    // neither. Rather than copy-paste four attribute lines into 17 XAML
    // files — and remember to add them to every future Window — we hook
    // a routed-event class handler for Window.Loaded and apply the
    // settings programmatically. This catches every Window subclass
    // uniformly, current and future, without per-XAML maintenance.
    //
    // -- Rounded corners on Windows 11 -------------------------------
    //
    // The problem: WPF Windows with WindowStyle="None" + custom
    // WindowChrome paint hard 90-degree corners. Modern Windows 11
    // apps (Discord, VS Code, Settings) all have softly rounded
    // corners — a regular OS window gets them automatically, but
    // chrome-less windows opt out and look dated next to them.
    //
    // Fix: call DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE,
    // DWMWCP_ROUND) on the HWND. This is the OS-level API Windows 11
    // exposes for exactly this case — DWM clips the window surface to
    // a rounded rectangle (and rounds the drop shadow to match) at
    // the compositor, no WPF transparency tricks, no extra paint
    // cost, no loss of Aero Snap. The OS desktop shows through the
    // corner cut-outs, exactly like any native Windows 11 window.
    //
    // Graceful degradation: the attribute id (33) is unknown on
    // Windows 10 and earlier, where DwmSetWindowAttribute simply
    // returns an error HRESULT and the call is a silent no-op.
    // We ignore the return value — corners just stay square.
    //
    // -- Maximize respects the taskbar -------------------------------
    //
    // The problem: WPF Windows with WindowStyle="None" + custom
    // WindowChrome (MainWindow + LauncherSettings + ModProperties +
    // LobbyWindow all use this recipe) cover the ENTIRE monitor when
    // maximised, including the Windows taskbar. The user sees the
    // launcher chrome eat the system tray, start button and clock.
    //
    // Cause: when Windows asks "how big should this window get when
    // maximised?" (the WM_GETMINMAXINFO message), it normally clamps
    // to the work area (monitor minus taskbar) for regular windows.
    // For WindowStyle="None" + WindowChrome it defaults to the full
    // monitor rectangle — the "I'm doing my own chrome, I'll take the
    // whole thing" mode — which is wrong for app windows that want
    // to look like regular OS windows.
    //
    // Fix: every Window that's WindowStyle=None gets a WndProc hook
    // installed at Loaded time. The hook listens for WM_GETMINMAXINFO,
    // reads the current monitor's work area, and overrides MaxPosition
    // / MaxSize / MaxTrackSize to that. Windows then maximises to the
    // visible area only and the taskbar stays put.
    //
    // The hook needs the HWND, which doesn't exist until SourceInitialized;
    // we use Loaded (one routed-event tick later) because there's no
    // routed-event class handler for SourceInitialized. The maximise
    // override applies to the FIRST size change after the user clicks
    // maximise, so a one-tick delay is invisible.
    //
    // Why Loaded and not Initialized: WPF has no routed-event class
    // handler for FrameworkElement.Initialized (it's a normal .NET
    // event, not a routed event, so EventManager.RegisterClassHandler
    // can't bind to it). Loaded fires after the first layout pass; the
    // re-layout / re-render triggered by setting these properties at
    // that point completes in microseconds and is invisible to the user.
    // Explicit XAML settings on individual Windows still work — they
    // just set the same value as the class handler, no conflict.
    //
    // (The original theme picker that previously lived here is gone —
    // the launcher is dark-only "dorado imperial" via Styles/Colors.xaml.)
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the per-user data dir + migrate a pre-existing next-to-exe config
        // BEFORE anything reads config or writes the debug log (MainWindow's ctor).
        Services.AppPaths.EnsureReady();

        // Self-heal the My Games redirect: if a previous session left the standard
        // AoE3 save folder junctioned to a redirect-mod's folder (e.g. the launcher
        // was killed while King's Return was up), restore the real vanilla folder.
        // A redirect-mod re-applies its junction when it next launches. Best-effort.
        try { Services.AoE3UserDataRedirect.EnsureDefault(); } catch { /* never block startup */ }

        // Self-heal the setuppath redirect: if a previous session was killed while a
        // stock-exe replacement mod (e.g. Struggle of Indonesia) was up, the real
        // bin\ folder may still be junctioned to the mod's folder — restore it so the
        // base game / other mods load vanilla content. Best-effort.
        try { Services.AoE3SetupPathRedirect.EnsureDefault(); } catch { /* never block startup */ }

        // Global crash net. Before this existed, an unhandled exception killed the
        // process with ZERO in-app trace: no global handler wrote anything, the
        // debug log is truncated each launch, and there was no persistent crash
        // file — so a user-reported crash left nothing to diagnose. Now every
        // unhandled exception is persisted to a crash-<ts>.log that survives the
        // next launch (see DiagnosticLog.WriteCrash), and UI-thread throws are
        // swallowed so the launcher stays usable instead of dying.
        DispatcherUnhandledException += (_, args) =>
        {
            Services.DiagnosticLog.WriteCrash("DispatcherUnhandledException", args.Exception);
            // Log-and-survive: most UI-thread exceptions leave the app usable, and
            // staying up beats a hard crash for a consumer launcher.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Services.DiagnosticLog.WriteCrash(
                "AppDomain.UnhandledException" + (args.IsTerminating ? " (terminating)" : ""),
                args.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.DiagnosticLog.WriteCrash("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));

        // (The wol-launcher:// scheme registration lives in MainWindow's ctor,
        // gated on the EnableJoinLinks config flag — App.OnStartup has no config
        // yet. The single-instance mutex/pipe below do NOT depend on it.)

        // Single-instance guard + deep-link handoff. A deep link fired while the
        // launcher is already open must route into the RUNNING instance (join that
        // room), not spawn a second window. Extract any join id from our args, then
        // claim the app-wide mutex.
        var joinId = Services.DeepLinkService.FindJoinLobbyId(Environment.GetCommandLineArgs());
        bool fromInstall = Array.Exists(e.Args, a =>
            string.Equals(a, Services.SelfInstallService.FromInstallArg, StringComparison.OrdinalIgnoreCase));
        // Detected once here so BOTH the primary path (StartMinimized) and the
        // second-instance branch below can read it: a manual double-click (no
        // --minimized) that lands on an already-running tray instance should
        // SHOW the window, but a duplicate auto-start (--minimized) should not.
        bool minimized = Array.Exists(e.Args, a =>
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool primary;
        try { _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out primary); }
        catch (Exception ex)
        {
            // Fail OPEN: if the mutex can't be created (rare policy/ACL issue), run
            // as a normal instance rather than refusing to start.
            Services.DiagnosticLog.Write($"SingleInstance: mutex failed, running normally: {ex.Message}");
            _instanceMutex = null;
            primary = true;
        }

        // Self-install relaunch: the portable parent starts us with --from-install
        // and is exiting to release the mutex. Wait briefly for that handoff instead
        // of treating this as a duplicate launch and quitting (which would abort the
        // install relaunch). When the parent exits it abandons the mutex →
        // AbandonedMutexException, which we treat as a clean acquisition.
        if (!primary && fromInstall && _instanceMutex != null)
        {
            try { primary = _instanceMutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { primary = true; }
            catch (Exception ex) { Services.DiagnosticLog.Write($"SingleInstance: from-install wait failed: {ex.Message}"); }
        }

        if (!primary)
        {
            // Another instance owns the app — hand off our intent and exit WITHOUT
            // creating a second MainWindow. A deep link routes into the running
            // instance (join that room); a plain manual relaunch (double-click the
            // .exe while it sits in the tray) asks the running instance to SHOW its
            // window — the Steam/Discord behaviour the user expects. A duplicate
            // auto-start (--minimized) does neither: it wanted the tray.
            if (joinId != null) ForwardJoinToRunningInstance(joinId);
            else if (!minimized) ForwardShowToRunningInstance();
            else Services.DiagnosticLog.Write("SingleInstance: duplicate --minimized launch; staying in tray.");
            Shutdown();
            return;
        }

        // Primary instance: stash a cold-start deep link for MainWindow to pick up
        // once its UI/session are ready, and start listening for links forwarded by
        // later launches.
        PendingJoinLobbyId = joinId;
        StartDeepLinkPipeServer();

        // The auto-start-to-tray flag (detected above). The Run-key registration
        // (see StartupRegistrationService) appends --minimized so a Windows-login
        // launch opens straight to the tray; a manual double-click carries no arg
        // and shows the window normally. MainWindow's Loaded handler reads this and
        // hides to the tray before it paints.
        StartMinimized = minimized;

        // Developer aid: show sample notification cards once the window is up. It exists
        // because a real room invite needs a second player on a second machine, which
        // makes "how does it look" nearly untestable — and because reaching the same
        // preview through Settings takes clicks, which a screenshot script cannot make.
        PreviewToasts = Array.Exists(e.Args, a =>
            string.Equals(a, "--preview-toasts", StringComparison.OrdinalIgnoreCase));

        // Same reasoning one line up, for a window that is three clicks deep. The brand
        // menu is a StaysOpen=false Popup, which closes the instant an automation client
        // invokes the button instead of really clicking it, so a screenshot script cannot
        // reach Settings at all without this.
        OpenSettings = Array.Exists(e.Args, a =>
            string.Equals(a, "--open-settings", StringComparison.OrdinalIgnoreCase));

        // The per-mod window, same reason.
        OpenModSettings = Array.Exists(e.Args, a =>
            string.Equals(a, "--open-mod-settings", StringComparison.OrdinalIgnoreCase));

        // A populated tournament bracket, for the same reason as --preview-toasts three
        // lines up and more so: a bracket with sixteen entrants and three played rounds
        // needs sixteen people and fifteen games before anybody can see one.
        DemoTournaments = Array.Exists(e.Args, a =>
            a.StartsWith("--demo-tournaments", StringComparison.OrdinalIgnoreCase));

        // Which of the samples to open on. There are six, one per state worth looking at,
        // and reaching the other five used to mean clicking a row - which is exactly what
        // this argument exists to avoid: a screenshot that needs a click is a screenshot
        // that cannot be scripted, and that was the whole reason for the flag.
        foreach (var a in e.Args)
        {
            if (!a.StartsWith("--demo-tournaments=", StringComparison.OrdinalIgnoreCase)) continue;
            DemoTournamentScenario = a["--demo-tournaments=".Length..];
        }

        // The filled Statistics page, for a sharper version of the same problem: the
        // civilization table needs hundreds of rated matches CARRYING a civilization, and this
        // community has none - the launcher only started recording them. No amount of playing
        // would show that page today.
        foreach (var a in e.Args)
        {
            if (!a.StartsWith("--demo-stats", StringComparison.OrdinalIgnoreCase)) continue;
            DemoStats = true;
            int eq = a.IndexOf('=');
            if (eq > 0) DemoStatsScenario = a[(eq + 1)..];
        }

        // Text size, applied BEFORE the first window is built so nothing paints at one
        // size and then jumps. It multiplies the font-size tokens and nothing else — see
        // Services/TextScale.cs for why that is not UiScale, and for why the XAML had to
        // move to DynamicResource before it could reach anything.
        ApplyTextScale();

        // StartupUri was removed from App.xaml so this guard can suppress a second
        // window; create + show the main window ourselves for the primary instance.
        var main = new WarsOfLibertyLauncher.MainWindow();
        MainWindow = main;
        // Even when starting minimized we call Show() so the window's visual tree
        // (and the Hardcodet TaskbarIcon it hosts) initialises and Loaded fires;
        // MainWindow then hides itself to the tray from Loaded. WindowState is set
        // to Minimized first to avoid a visible flash of the full window.
        if (StartMinimized) main.WindowState = System.Windows.WindowState.Minimized;
        main.Show();
    }

    /// <summary>
    /// The launcher-wide text size, resolved and applied. Best-effort throughout: a font
    /// size is not worth failing a launch over.
    ///
    /// <para>The setting is read straight out of the config JSON rather than through
    /// <c>LauncherConfig.Load()</c>, deliberately. Load runs four migrations and can write
    /// the file, and MainWindow's constructor is about to call it properly a moment later —
    /// doing all of that twice, once for one string, is how a startup path acquires a
    /// second opinion about the config.</para>
    /// </summary>
    private static void ApplyTextScale()
    {
        try
        {
            var setting = ReadTextScaleSetting();
            var screen = Services.TextScale.DescribePrimaryScreen();
            TextScaleDiagonalInches = Services.TextScale.DetectPrimaryDiagonalInches();
            TextScaleFactor = Services.TextScale.Resolve(
                setting, TextScaleDiagonalInches, screen.Width, screen.Height, screen.DpiScale);

            Services.TextScale.Apply(TextScaleFactor);
            _textScaleSetting = setting;
        }
        catch (Exception ex)
        {
            Services.DiagnosticLog.Write($"App.ApplyTextScale: {ex.Message}");
        }
    }

    private static string ReadTextScaleSetting()
    {
        try
        {
            var path = Services.AppPaths.ConfigFile;
            // BOTH keys, resolved by LauncherConfig's own rule rather than by a simpler copy of
            // it. This method reads the raw JSON because it runs before MainWindow and must not
            // fire four migrations for one string, and that shortcut has already cost two bugs:
            // first a fallback literal that drifted from the property's default, then a stored
            // value being obeyed even though nobody had chosen it — which is what pinned every
            // machine that ran one intermediate build to the size that build defaulted to.
            if (!System.IO.File.Exists(path)) return Models.LauncherConfig.DefaultTextScale;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));

            var stored = doc.RootElement.TryGetProperty("textScale", out var v)
                         && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? v.GetString()
                : null;
            var chosen = doc.RootElement.TryGetProperty("textScaleExplicitlyChosen", out var c)
                         && c.ValueKind == System.Text.Json.JsonValueKind.True;

            return Models.LauncherConfig.ResolveTextScale(stored, chosen);
        }
        catch
        {
            return Models.LauncherConfig.DefaultTextScale;
        }
    }

    private static string _textScaleSetting = Models.LauncherConfig.DefaultTextScale;

    /// <summary>
    /// One line describing how the text size was arrived at, for the diagnostic log.
    ///
    /// <para>MainWindow writes it, not this class: the resolution happens in
    /// <see cref="OnStartup"/>, which runs BEFORE MainWindow's constructor rotates the log,
    /// so a line written here goes into the PREVIOUS session's file and is missing from the
    /// one a bundle is about.</para>
    /// </summary>
    public static string DescribeTextScale()
    {
        var screen = Services.TextScale.DescribePrimaryScreen();
        return $"Text scale: setting='{_textScaleSetting}' "
             + $"screen={screen.Width}x{screen.Height} dpi={screen.DpiScale:0.00} diagonal="
             + (TextScaleDiagonalInches is double inches ? $"{inches:0.0}\"" : "unknown")
             + $" -> {TextScaleFactor:0.00}";
    }

    /// <summary>The factor currently applied to every font-size token.</summary>
    public static double TextScaleFactor { get; private set; } = 1.0;

    /// <summary>
    /// The primary monitor's physical diagonal, or null when the panel didn't say.
    /// Kept so Settings can show what "Automatic" worked out from without probing again.
    /// </summary>
    public static double? TextScaleDiagonalInches { get; private set; }

    /// <summary>
    /// Re-resolve and re-apply the text size after Settings changed it. Every font size in
    /// the app is a <c>DynamicResource</c>, so this re-lays out the live windows — the
    /// setting needs no restart.
    /// </summary>
    public static void RefreshTextScale(string? setting)
    {
        var screen = Services.TextScale.DescribePrimaryScreen();
        TextScaleFactor = Services.TextScale.Resolve(
            setting, TextScaleDiagonalInches, screen.Width, screen.Height, screen.DpiScale);
        _textScaleSetting = setting ?? Services.TextScale.Auto;
        Services.TextScale.Apply(TextScaleFactor);
    }

    /// <summary>True when this launch was an auto-start (Windows login) that
    /// should open straight to the system tray — set from the <c>--minimized</c>
    /// argument the Run-key registration appends. MainWindow drains it on Loaded.
    /// </summary>
    public static bool StartMinimized { get; private set; }

    /// <summary>True when <c>--preview-toasts</c> was passed: MainWindow fires sample
    /// notification cards from Loaded so their appearance can be inspected (and
    /// screenshotted) without a real invitation arriving. Inert otherwise.</summary>
    public static bool PreviewToasts { get; private set; }

    /// <summary>True when <c>--open-settings</c> was passed: MainWindow opens the
    /// launcher settings window from Loaded, so the screen can be captured by a script.
    /// Inert otherwise.</summary>
    public static bool OpenSettings { get; private set; }

    /// <summary>True when <c>--open-mod-settings</c> was passed. Same screenshot-script
    /// reason as <see cref="OpenSettings"/>.</summary>
    public static bool OpenModSettings { get; private set; }

    /// <summary>True when <c>--demo-tournaments</c> was passed: MainWindow opens the
    /// Multiplayer tab on Tournaments and fills it with fabricated brackets, so their
    /// appearance can be judged without a backend, an account or a played match.
    ///
    /// <para>Like every other argument here it is defeated by the single-instance mutex
    /// when a launcher is already running — which is precisely why the same preview also
    /// has a button in Settings → Developer. Inert otherwise.</para></summary>
    public static bool DemoTournaments { get; private set; }

    /// <summary>Which sample <c>--demo-tournaments=&lt;name&gt;</c> asked for, or null for the
    /// first one. Matched loosely against the scenario names, because this is a developer
    /// argument and refusing a near miss would only cost a second attempt.</summary>
    public static string? DemoTournamentScenario { get; private set; }

    /// <summary>True when <c>--demo-stats</c> was passed: the Statistics subtab opens filled
    /// with fabricated community figures. Same single-instance caveat as every other argument
    /// here, and the same button in Settings for when a launcher is already running.</summary>
    public static bool DemoStats { get; private set; }

    /// <summary><c>full</c> for the table with a sample behind it, <c>empty</c> for the state
    /// this community is actually in. Null means the filled one, because the empty one is what
    /// the real server already shows.</summary>
    public static string? DemoStatsScenario { get; private set; }

    // ---- Single-instance + deep-link IPC -------------------------------------

    // Local\ = per-session single-instance (a deep link fired from a browser runs
    // in the SAME session, so it reaches this instance). Global\ would wrongly
    // block a second Windows user on a shared PC.
    private const string MutexName = @"Local\WarsOfLibertyLauncher.SingleInstance.v1";
    private const string PipeName = "WarsOfLibertyLauncher.DeepLink.v1";
    // Pipe sentinel: "bring the running window to the front" (a plain relaunch,
    // no deep link). The underscores make it an invalid lobby id, so it can never
    // collide with a real join payload in DeepLinkPipeLoop.
    private const string ShowCommand = "__show__";
    private static Mutex? _instanceMutex;

    /// <summary>Cold-start join deep link (set before MainWindow exists); MainWindow
    /// drains it on load. Null when the launch carried no deep link.</summary>
    public static string? PendingJoinLobbyId { get; private set; }

    /// <summary>Raised (on the UI thread) when a join deep link arrives from a later
    /// launch forwarded over the IPC pipe. MainWindow subscribes.</summary>
    public static event Action<string>? JoinRequested;

    /// <summary>Marks the cold-start deep link consumed so a mod-switch re-check
    /// doesn't reprocess it.</summary>
    public static void ClearPendingJoin() => PendingJoinLobbyId = null;

    private void StartDeepLinkPipeServer()
    {
        var t = new Thread(DeepLinkPipeLoop) { IsBackground = true, Name = "DeepLinkPipe" };
        t.Start();
    }

    private void DeepLinkPipeLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);
                server.WaitForConnection();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = reader.ReadLine()?.Trim();
                if (line == ShowCommand)
                    DispatchShow();
                else if (Services.DeepLinkService.IsValidLobbyId(line))
                    DispatchJoin(line!);
                else
                    Services.DiagnosticLog.Write($"DeepLink pipe: ignored invalid payload.");
            }
            catch (Exception ex)
            {
                Services.DiagnosticLog.Write($"DeepLink pipe error: {ex.Message}");
                try { Thread.Sleep(500); } catch { /* backoff */ }
            }
        }
    }

    private void DispatchJoin(string lobbyId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // BringToForeground() SHOWS a window that was hidden to the tray
                // (a bare Activate() can't un-hide a Hide()'d window) as well as
                // un-minimising + fronting it, so a join link works whether the
                // launcher is in the tray, minimised, or just buried behind others.
                (MainWindow as WarsOfLibertyLauncher.MainWindow)?.BringToForeground();
                JoinRequested?.Invoke(lobbyId);
            }
            catch (Exception ex)
            {
                Services.DiagnosticLog.Write($"DeepLink dispatch failed: {ex.Message}");
            }
        });
    }

    /// <summary>Restore + foreground the running window — a plain relaunch of the
    /// .exe while the launcher sits in the tray.</summary>
    private void DispatchShow()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                (MainWindow as WarsOfLibertyLauncher.MainWindow)?.BringToForeground();
                Services.DiagnosticLog.Write("SingleInstance: relaunch — brought running window to front.");
            }
            catch (Exception ex)
            {
                Services.DiagnosticLog.Write($"SingleInstance: show dispatch failed: {ex.Message}");
            }
        });
    }

    private static void ForwardJoinToRunningInstance(string lobbyId)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(lobbyId);
            Services.DiagnosticLog.Write($"DeepLink: forwarded join '{lobbyId}' to running instance.");
        }
        catch (Exception ex)
        {
            Services.DiagnosticLog.Write($"DeepLink: forward to running instance failed: {ex.Message}");
        }
    }

    /// <summary>Ask the already-running instance to bring its window to the front
    /// (the user relaunched the .exe while it sat in the tray).</summary>
    private static void ForwardShowToRunningInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(ShowCommand);
            Services.DiagnosticLog.Write("SingleInstance: forwarded show request to running instance.");
        }
        catch (Exception ex)
        {
            Services.DiagnosticLog.Write($"SingleInstance: forward show failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply the HiDPI-crisp rendering settings + the maximize-respects-
    /// taskbar hook to a Window the first time it loads. Idempotent —
    /// re-running on the same Window just re-sets the same values and
    /// the second AddHook is shadowed by HwndSource's internal dedup.
    /// </summary>
    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w) return;

        // -- HiDPI crispness (always on) --
        //
        // UseLayoutRounding is the single biggest fix for "blurry WPF
        // text at non-100% DPI". It rounds every layout coordinate to
        // a whole device pixel, eliminating the sub-pixel positioning
        // that triggers ClearType's smudge-mode rendering.
        w.UseLayoutRounding = true;

        // TextOptions are inherited attached properties — set on the
        // Window root, they cascade to every TextBlock, TextBox, Label,
        // Button content, etc., in the visual tree.
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(w, TextHintingMode.Fixed);

        // -- Maximize-respects-taskbar (only Windows with no OS chrome) --
        //
        // Regular Windows (WindowStyle=SingleBorderWindow or Three­Dim…)
        // already maximise to the work area by default — Windows handles
        // that internally for them. Only chrome-less Windows fall through
        // to "use the full monitor" behaviour, so we restrict the hook
        // to those. Saves a no-op WndProc on every dialog that opts to
        // keep the native title bar.
        if (ShouldApplyLauncherChrome(w.WindowStyle, w.AllowsTransparency))
        {
            ApplyWindowChrome(w);
            InstallMaximizeFix(w);
            InstallRoundedCorners(w);
        }

        // -- Close lingering transient menus when a dialog opens --
        //
        // The launcher's hand-built popups (brand dropdown, dashboard MODS
        // switcher) are AllowsTransparency + StaysOpen=false, whose WPF
        // auto-dismiss is unreliable when a non-modal Window steals activation
        // — so they linger behind a freshly-opened dialog (the "open MODS,
        // click the gear, the menu stays open" bug). Centralise the fix here
        // rather than in every opener: when ANY secondary window appears or is
        // re-activated, close the tracked popup. Loaded fires once per fresh
        // dialog instance (e.g. ModPropertiesDialog is rebuilt each open) and
        // covers the common case; the Activated subscription additionally
        // covers single-instance dialogs reused via Activate() (LauncherSettings
        // / Lobby), where Loaded won't fire again. MainWindow is EXCLUDED so
        // its own activation never closes a popup that legitimately lives on it.
        // Fully-qualified type name: inside App (derives from Application),
        // bare `MainWindow` binds to the inherited Application.MainWindow
        // PROPERTY, not our window type — so qualify to disambiguate.
        if (w is not WarsOfLibertyLauncher.MainWindow)
        {
            Controls.ChromePopups.CloseOpen();
            w.Activated += (_, _) => Controls.ChromePopups.CloseOpen();
        }
    }

    // ==================================================================
    // Global custom-chrome WindowChrome
    // ==================================================================

    /// <summary>
    /// Apply the launcher's standard <see cref="WindowChrome"/> to a
    /// WindowStyle=None window so each window no longer repeats the block.
    /// CaptionHeight matches the window's own TitleBar height so the native
    /// caption region (drag / double-click-maximize / restore-on-drag) always
    /// covers the whole bar: the slim TitleBarHeight token for every secondary
    /// window, and the classic TitleBarHeightMain token for the main launcher
    /// header (which keeps the taller bar). ResizeBorderThickness is 6 for
    /// resizable windows (edge-drag) and 0 otherwise. Windows that already
    /// declared a WindowChrome in XAML are left untouched, so this is a safe
    /// additive default. No-op if the token is missing.
    /// </summary>
    /// <summary>
    /// Whether this window wants the launcher's own chrome.
    ///
    /// <para>No native title bar was the whole test, and it caught one window it should not
    /// have: <c>DesktopToastWindow</c>. That one is <c>WindowStyle.None</c> like the dialogs,
    /// but it is also the only <c>Window</c> in the repository with
    /// <c>AllowsTransparency = true</c> - every dialog sets it to False by hand - and it is
    /// transparent on purpose, because the toast cards carry their own rounded corners and
    /// their own shadow over whatever is behind them.</para>
    ///
    /// <para>Chrome on a layered window is not free decoration: <see cref="ApplyWindowChrome"/>
    /// gives it a 44 px caption region and <see cref="InstallRoundedCorners"/> asks DWM to round
    /// the WINDOW rectangle. Over a transparent toast host that is a bordered rectangle drawn
    /// around and beside the cards, which is how it was reported. The maximize fix is meaningless
    /// there too - nothing maximises a toast.</para>
    ///
    /// <para>So the rule is "no title bar of its own AND it does not draw its own shape". A
    /// window that asked for transparency has said its shape is its business.</para>
    /// </summary>
    internal static bool ShouldApplyLauncherChrome(WindowStyle style, bool allowsTransparency)
        => style == WindowStyle.None && !allowsTransparency;

    private static void ApplyWindowChrome(Window w)
    {
        if (WindowChrome.GetWindowChrome(w) != null)
            return;

        // Qualify the type as in OnAnyWindowLoaded: bare MainWindow binds to the
        // inherited Application.MainWindow property, not our window type.
        string heightKey = w is WarsOfLibertyLauncher.MainWindow
            ? "TitleBarHeightMain"
            : "TitleBarHeight";

        double caption = 44;
        if (Current?.TryFindResource(heightKey) is double h)
            caption = h;

        bool resizable = w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = caption,
            ResizeBorderThickness = new Thickness(resizable ? 6 : 0),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });
    }

    // ==================================================================
    // Rounded corners on Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE)
    // ==================================================================

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private enum DWM_WINDOW_CORNER_PREFERENCE : uint
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3,
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Ask DWM to clip this Window to a rounded rectangle on
    /// Windows 11+. No-op on older Windows (the attribute id is
    /// unrecognised there and the call returns an error HRESULT we
    /// deliberately ignore).
    /// </summary>
    private static void InstallRoundedCorners(Window w)
    {
        var helper = new WindowInteropHelper(w);
        if (helper.Handle == IntPtr.Zero) return;
        int pref = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(helper.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    // ==================================================================
    // Maximize-respects-taskbar: WM_GETMINMAXINFO interop
    // ==================================================================

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// Wire a WndProc hook on the Window's HWND that responds to
    /// WM_GETMINMAXINFO with the current monitor's WORK area (excluding
    /// the taskbar) instead of letting Windows default to the full
    /// monitor rectangle. The hook lives for the Window's lifetime;
    /// HwndSource cleans it up when the HWND is destroyed.
    /// </summary>
    private static void InstallMaximizeFix(Window w)
    {
        var helper = new WindowInteropHelper(w);
        var source = HwndSource.FromHwnd(helper.Handle);
        if (source == null) return;
        source.AddHook(MaximizeFixWndProc);
    }

    private static IntPtr MaximizeFixWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        // Snap the MINMAXINFO struct out of the lParam buffer, edit it,
        // write it back. Marshal.StructureToPtr's third param (fDeleteOld)
        // is false because the unmanaged buffer wasn't allocated by us
        // — Windows owns it and just wants us to mutate in place.
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                var mon = info.rcMonitor;
                // MaxPosition is monitor-relative (origin = top-left of
                // the monitor that owns the window), so subtract the
                // monitor's origin from the work area's origin. On a
                // single-monitor / left-anchored taskbar setup this is
                // (0, 0); with a side-mounted taskbar or a non-primary
                // monitor it shifts.
                mmi.ptMaxPosition.X = Math.Abs(work.Left - mon.Left);
                mmi.ptMaxPosition.Y = Math.Abs(work.Top - mon.Top);
                mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }
}
