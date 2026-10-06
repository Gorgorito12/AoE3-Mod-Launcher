using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Simple file logger for diagnostics. Writes to launcher-debug.log in the
/// per-user data dir (%LocalAppData%\AoE3ModLauncher\ via AppPaths.LogFile).
/// On launcher startup the log is reset, so each session is self-contained.
///
/// Write() is non-blocking: callers from the UI thread (mod switch, CheckAsync,
/// progress reporters, …) just enqueue the message and return immediately.
/// A background drainer task serialises writes to disk, preserving order while
/// keeping the synchronous portion of hot UI paths free of disk I/O.
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogPath = AppPaths.LogFile;

    /// <summary>Previous session's rotated log — see <see cref="Reset"/>.</summary>
    private static readonly string PrevLogPath =
        Path.Combine(AppPaths.DataDir, "launcher-debug.prev.log");

    /// <summary>
    /// How many previous sessions <see cref="Reset"/> keeps.
    ///
    /// <para><b>It was one, and one is not enough for a bug report.</b> A player sent a bundle
    /// for two matches that had gone wrong four days earlier; the launcher had been opened many
    /// times since, so both logs in the bundle described that morning and the evening in question
    /// had been overwritten on the second launch after it. Nobody reports a multiplayer problem
    /// within one restart — they finish the session, sleep, and write it up when they next have
    /// the launcher open.</para>
    ///
    /// <para>Five, the same depth the crash logs already keep, for the same reason. Measured
    /// against that player's own files a session's log is ~58 KB, so the whole ring costs a
    /// few hundred kilobytes on disk and in the bundle.</para>
    /// </summary>
    internal const int KeepPreviousLogs = 5;

    /// <summary>
    /// Generation <paramref name="n"/> of the rotated log, 1 being the most recent.
    ///
    /// <para>Generation 1 keeps the historical <c>launcher-debug.prev.log</c> name: CLAUDE.md
    /// names it, <c>DiagnosticLogTests</c> asserts on it, and a player who has been asked for it
    /// by name should still find it. The rest are numbered behind it.</para>
    /// </summary>
    internal static string RotatedLogPath(int n)
        => n <= 1 ? PrevLogPath : Path.Combine(AppPaths.DataDir, $"launcher-debug.prev{n}.log");

    private static readonly object FileLock = new();
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static readonly Task DrainerTask = Task.Run(DrainerLoop);

    public static void Reset()
    {
        try
        {
            lock (FileLock)
            {
                // Preserve the previous session's log as launcher-debug.prev.log
                // BEFORE truncating, so a crash that killed the last run (even one
                // that didn't trip the in-app crash net, e.g. a native/hard kill)
                // still leaves its full log behind for the next diagnostic bundle.
                // KeepPreviousLogs generations, shifted back one each launch. ExportBundle
                // picks them all up automatically — they end in .log.
                try
                {
                    if (File.Exists(LogPath))
                    {
                        // Shift the ring from the back, so nothing is overwritten before it has
                        // been moved. The oldest generation is the one that falls off the end.
                        for (var n = KeepPreviousLogs; n > 1; n--)
                        {
                            var older = RotatedLogPath(n);
                            var newer = RotatedLogPath(n - 1);
                            if (!File.Exists(newer)) continue;
                            try
                            {
                                if (File.Exists(older)) File.Delete(older);
                                File.Move(newer, older);
                            }
                            catch { /* best-effort, per generation */ }
                        }

                        if (File.Exists(PrevLogPath)) File.Delete(PrevLogPath);
                        File.Move(LogPath, PrevLogPath);
                    }
                }
                catch { /* if rotation fails we still truncate below */ }

                // The version belongs in the header, not only in the crash log. A shared
                // diagnostics bundle carried no build number at all, so answering "which
                // launcher is this?" meant inferring it from uptime arithmetic and from
                // which log lines were ABSENT — on a report where the answer decided
                // whether the user was even able to see a release.
                // The EXECUTABLE belongs here for exactly the same reason the version does, and
                // it was missing for exactly as long. A user was stranded for six sessions on
                // `Startup auto-update: not checking - NotOurExecutable.` — a verdict with its
                // evidence stripped out — because the launcher named the rule it had applied but
                // never the path it had applied it to. Both halves matter: the NAME decides
                // whether the self-update may run at all, and the SIZE is what distinguishes our
                // own bundle from a .NET host. This lands above every other line and in all five
                // rotated generations, so it answers "which binary is this?" at a glance.
                File.WriteAllText(LogPath,
                    $"=== Wars of Liberty Launcher debug log ===\n" +
                    $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Version: {VersionLine()}\n" +
                    $"Executable: {ExecutableLine()}\n\n");
            }
        }
        catch
        {
            // If we can't even create the log, oh well — don't crash the app.
        }
    }

    /// <summary>
    /// Persists an unhandled exception to a timestamped <c>crash-&lt;…&gt;.log</c> in
    /// the data dir. Unlike <see cref="LogPath"/> (which <see cref="Reset"/> rotates
    /// each launch), a crash log SURVIVES the next launch so the reporter's
    /// diagnostic bundle actually contains the crash. Also mirrors a one-line marker
    /// into the debug log and flushes. Called from the global exception hooks in
    /// <c>App</c>. Best-effort — must never throw (it runs while the app may be dying).
    /// </summary>
    public static void WriteCrash(string source, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var body =
                "=== Wars of Liberty Launcher CRASH ===\n" +
                $"Time:    {now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Source:  {source}\n" +
                $"Version: {VersionLine()}\n" +
                $"OS:      {Environment.OSVersion} / .NET {Environment.Version}\n\n" +
                (ex?.ToString() ?? "(no exception object)") + "\n";

            var path = Path.Combine(AppPaths.DataDir, $"crash-{now:yyyyMMdd-HHmmss}.log");
            lock (FileLock)
            {
                try { File.WriteAllText(path, body); } catch { /* best-effort */ }
            }

            Write($"UNHANDLED EXCEPTION ({source}): " +
                  $"{ex?.GetType().Name}: {ex?.Message} -> {Path.GetFileName(path)}");
            Flush();
            PruneOldCrashLogs();
        }
        catch { /* never let crash logging itself crash */ }
    }

    /// <summary>
    /// The running executable and its size, e.g.
    /// <c>C:\Users\x\Desktop\WarsOfLibertyLauncher_new.exe (178.2 MB)</c>. The size is not
    /// decoration — it is what tells our ~178 MB self-contained bundle apart from a ~150 KB
    /// .NET host or a ~290 KB apphost stub, which is the same question
    /// <see cref="AutoUpdatePolicy.IsOurExecutable"/> and
    /// <see cref="LauncherUpdateGate.IsDeveloperBuild"/> both decide on.
    /// </summary>
    private static string ExecutableLine()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return "(unknown)";

            var length = LauncherUpdateService.RunningImageLength(path);
            return length == null
                ? path
                : $"{path} ({length.Value / 1024.0 / 1024.0:F1} MB)";
        }
        catch { return "(unknown)"; }
    }

    private static string VersionLine()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(info)
                ? asm.GetName().Version?.ToString() ?? "?"
                : info;
        }
        catch { return "?"; }
    }

    /// <summary>Keep only the newest <paramref name="keep"/> crash logs.</summary>
    private static void PruneOldCrashLogs(int keep = 5)
    {
        try
        {
            var files = Directory.GetFiles(AppPaths.DataDir, "crash-*.log");
            if (files.Length <= keep) return;
            Array.Sort(files,
                (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            for (int i = keep; i < files.Length; i++)
            {
                try { File.Delete(files[i]); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    public static void Write(string message)
    {
        // Format the line on the caller's thread so the timestamp matches
        // the call site, not whenever the drainer happens to flush.
        Queue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        try { Signal.Release(); } catch { /* disposed during shutdown */ }
    }

    /// <summary>
    /// When this process really started, so a milestone can be expressed as "+N ms since launch"
    /// rather than as which second a line happened to land in.
    ///
    /// <para>Taken from the OS rather than from first use of this class, so it includes runtime
    /// start-up and JIT — the part of "the launcher takes ages to open" that happens before any of
    /// our code runs. Falls back to now if the process cannot be queried, which only costs the
    /// milestones their absolute baseline; the gaps between them stay correct.</para>
    /// </summary>
    private static readonly DateTime ProcessStart = ResolveProcessStart();

    private static DateTime ResolveProcessStart()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime; }
        catch { return DateTime.Now; }
    }

    /// <summary>
    /// A named point in the startup sequence, with the elapsed time since the process started.
    ///
    /// <para><b>This exists because whole-second timestamps could not answer "where do the five
    /// seconds go".</b> Twice a startup cost was attributed to the wrong step from second-grained
    /// lines alone. A milestone is one cheap string; keep them few and on the path that decides
    /// when the launcher is usable, not scattered through it.</para>
    /// </summary>
    public static void Milestone(string name)
    {
        var ms = (long)(DateTime.Now - ProcessStart).TotalMilliseconds;
        Write($"TIMING  {name} — +{ms} ms since launch");
    }

    /// <summary>
    /// Watches the UI thread and logs every stretch it spent unable to run.
    ///
    /// <para><b>Why a timer IS the measurement.</b> A <see cref="DispatcherTimer"/> tick can only
    /// run when the dispatcher is free, so the gap between two ticks that should have been 100 ms
    /// apart is exactly how long the thread was held. Nothing else in the launcher records this,
    /// which is why "everything is drawn but nothing responds" could only ever be guessed at —
    /// three rounds of work went into making DATA arrive sooner before it was established that the
    /// symptom was a blocked dispatcher, not a slow fetch.</para>
    ///
    /// <para>Silent on a healthy session: only stalls past the threshold are written, so a normal
    /// launch prints nothing at all. Cheap enough to leave on permanently — one timer, no
    /// allocation per tick.</para>
    /// </summary>
    public static void StartUiStallWatch(
        System.Windows.Threading.Dispatcher dispatcher,
        int thresholdMs = 250)
    {
        if (dispatcher == null) return;

        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };

        var last = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            var gap = now - last;
            last = now;
            // The interval itself is not a stall; only what exceeds it is.
            if (gap >= thresholdMs)
                Write($"UI STALL  {gap} ms — the dispatcher could not run for that long");
        };
        timer.Start();

        // ...and WHICH dispatcher operation held it. The stall figure above says a stretch was
        // lost but not to what, which left several rounds of narrowing to guesswork. Every unit
        // of work the dispatcher runs passes through these hooks, so timing them and naming the
        // slow ones turns "4 s went somewhere" into a method name.
        try
        {
            var started = new System.Runtime.CompilerServices.ConditionalWeakTable<
                System.Windows.Threading.DispatcherOperation, System.Runtime.CompilerServices.StrongBox<long>>();

            dispatcher.Hooks.OperationStarted += (_, a) =>
                started.AddOrUpdate(a.Operation, new System.Runtime.CompilerServices.StrongBox<long>(Environment.TickCount64));

            dispatcher.Hooks.OperationCompleted += (_, a) =>
            {
                if (!started.TryGetValue(a.Operation, out var box)) return;
                started.Remove(a.Operation);
                var ms = Environment.TickCount64 - box.Value;
                // Every redraw is counted, however fast, for the layout-storm watch: a frame that
                // costs 3 ms on a fast PC is the same frame that costs 300 on a slow one.
                if (IsRenderOperation(a.Operation))
                {
                    s_rendersThisSecond++;
                    if (IsAnimatedRender(a.Operation)) s_animatedRendersThisSecond++;
                    s_renderMsThisSecond += ms;
                }
                if (ms < thresholdMs) return;
                Write($"UI OP  {ms} ms — {DescribeOperation(a.Operation)}");
            };
        }
        catch (Exception ex)
        {
            // Never worth failing a launch over a diagnostic.
            Write($"UI stall watch: operation hooks unavailable — {ex.Message}");
        }
    }

    // ── Layout-storm watch ──────────────────────────────────────────────────
    // Everything below runs on the UI thread (dispatcher hooks, a class handler, a
    // DispatcherTimer), so the plain counters need no locking.

    private static readonly System.Reflection.FieldInfo? s_operationMethod =
        typeof(System.Windows.Threading.DispatcherOperation).GetField(
            "_method", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static int s_layoutPassesThisSecond;
    private static int s_rendersThisSecond;
    private static int s_animatedRendersThisSecond;
    private static long s_renderMsThisSecond;
    private static readonly System.Collections.Generic.Dictionary<System.Windows.FrameworkElement, int> s_sizeChangesThisSecond =
        new(ReferenceEqualityComparer.Instance);
    private static readonly LayoutStormDetector s_storm = new();
    private static readonly EffectsGovernor s_governor = new();
    private static bool s_stormWatchStarted;

    /// <summary>
    /// What to do when <see cref="EffectsGovernor"/> finds this PC draws too slowly: the app sets
    /// it to turn every moving light off. Called at most once, on the UI thread.
    /// </summary>
    internal static Action? ReduceEffects { get; set; }

    private static void TripEffects(string? line)
    {
        if (line == null) return;
        Write(line);
        try { ReduceEffects?.Invoke(); }
        catch (Exception ex) { Write($"Reducing effects failed: {ex.Message}"); }
    }

    /// <summary>
    /// What the storm line says about the launcher's state (visible or in the tray, which tab).
    /// Set by MainWindow; read only when a line is written.
    /// </summary>
    internal static Func<string>? LayoutStormContext { get; set; }

    /// <summary>
    /// Whether anybody can see the launcher right now (foreground, on screen, not minimized).
    /// Set by MainWindow; unset counts as watched, the stricter of the two thresholds.
    /// </summary>
    internal static Func<bool>? LayoutStormWatched { get; set; }

    /// <summary>
    /// Whether a dispatcher operation is one of WPF's redraws. Read through the same private
    /// field <see cref="DescribeOperation"/> uses; a runtime without it simply counts nothing.
    /// </summary>
    private static bool IsRenderOperation(System.Windows.Threading.DispatcherOperation op)
    {
        try
        {
            if (s_operationMethod?.GetValue(op) is not Delegate d) return false;
            return d.Method.DeclaringType?.Name == "MediaContext"
                   && d.Method.Name is "RenderMessageHandler" or "AnimatedRenderMessageHandler";
        }
        catch { return false; }
    }

    private static bool IsAnimatedRender(System.Windows.Threading.DispatcherOperation op)
    {
        try { return s_operationMethod?.GetValue(op) is Delegate d && d.Method.Name == "AnimatedRenderMessageHandler"; }
        catch { return false; }
    }

    /// <summary>
    /// Starts the layout-storm watch (<see cref="LayoutStormDetector"/>): counts layout passes,
    /// redraws and which elements keep changing size, and writes a <c>LAYOUT STORM</c> line when
    /// they keep coming with nobody touching anything. Also records whether WPF has hardware
    /// rendering, because on a machine without it every redraw is paid for by the CPU.
    ///
    /// <para>Call after <see cref="StartUiStallWatch"/>, whose dispatcher hooks count the
    /// redraws, and hand it the main window through <see cref="WatchLayoutOf"/>.</para>
    /// </summary>
    public static void StartLayoutStormWatch(System.Windows.Threading.Dispatcher dispatcher)
    {
        if (dispatcher == null || s_stormWatchStarted) return;
        s_stormWatchStarted = true;

        try
        {
            var tier = System.Windows.Media.RenderCapability.Tier >> 16;
            var meaning = tier switch
            {
                0 => "software only - every redraw is paid for by the CPU",
                1 => "partial hardware acceleration",
                _ => "full hardware acceleration",
            };
            Write($"Rendering: WPF tier {tier} ({meaning}).");
            TripEffects(s_governor.ObserveRenderTier(tier));
        }
        catch (Exception ex) { Write($"Rendering: tier unavailable — {ex.Message}"); }

        try
        {
            System.Windows.EventManager.RegisterClassHandler(
                typeof(System.Windows.FrameworkElement),
                System.Windows.FrameworkElement.SizeChangedEvent,
                new System.Windows.SizeChangedEventHandler((sender, _) =>
                {
                    if (sender is not System.Windows.FrameworkElement fe) return;
                    s_sizeChangesThisSecond[fe] = s_sizeChangesThisSecond.TryGetValue(fe, out var n) ? n + 1 : 1;
                }),
                handledEventsToo: true);
        }
        catch (Exception ex) { Write($"Layout storm watch: size hook unavailable — {ex.Message}"); }

        var timer = new System.Windows.Threading.DispatcherTimer(
            // Normal, not Background: a storm is a dispatcher kept busy at Render priority, and a
            // Background tick would starve for exactly as long as there is something to report.
            System.Windows.Threading.DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += (_, _) =>
        {
            try { TickLayoutStorm(); }
            catch (Exception ex) { Write($"Layout storm watch failed: {ex.Message}"); timer.Stop(); }
        };
        timer.Start();
    }

    /// <summary>Counts every layout pass of the dispatcher <paramref name="element"/> belongs to.</summary>
    public static void WatchLayoutOf(System.Windows.UIElement element)
    {
        if (element == null) return;
        // LayoutUpdated is raised after EVERY layout pass of the dispatcher, whichever element
        // was laid out, so one subscription covers the main window and the room window alike.
        element.LayoutUpdated += (_, _) => s_layoutPassesThisSecond++;
    }

    private static void TickLayoutStorm()
    {
        var passes = s_layoutPassesThisSecond;
        var renders = s_rendersThisSecond;
        var renderMs = s_renderMsThisSecond;
        var animated = s_animatedRendersThisSecond;
        s_layoutPassesThisSecond = 0;
        s_rendersThisSecond = 0;
        s_renderMsThisSecond = 0;
        s_animatedRendersThisSecond = 0;

        var watched = true;
        try { watched = LayoutStormWatched?.Invoke() ?? true; }
        catch { /* assume watched: the stricter threshold */ }
        var empty = new System.Collections.Generic.Dictionary<string, int>(0);
        var hot = LayoutStormDetector.IsHot(
            new LayoutStormDetector.Second(passes, renders, renderMs, empty, animated, watched));
        var sizes = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        string? busiest = null;
        var busiestChanges = 0;
        if (hot)
        {
            // Naming an element walks its ancestors, so it is done only for a hot second and only
            // for the busiest few — an idle launcher pays nothing here.
            foreach (var kv in s_sizeChangesThisSecond.OrderByDescending(kv => kv.Value).Take(24))
            {
                var key = ElementKey(kv.Key);
                sizes[key] = sizes.TryGetValue(key, out var n) ? n + kv.Value : kv.Value;
                // The busiest single INSTANCE, before the grouping by name adds its siblings in:
                // one element resizing hundreds of times a second is a layout that never settles.
                if (busiest == null) { busiest = key; busiestChanges = kv.Value; }
            }
        }
        s_sizeChangesThisSecond.Clear();

        string context = "";
        if (hot)
        {
            try { context = LayoutStormContext?.Invoke() ?? ""; }
            catch { /* the context is decoration */ }
            try
            {
                var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.FrameworkElement;
                context += $", focus {(focused == null ? "none" : ElementKey(focused))}";
            }
            catch { /* decoration */ }
            var clocks = DescribeActiveClocks(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            if (clocks.Length > 0) context += " — " + clocks;
        }

        var second = new LayoutStormDetector.Second(passes, renders, renderMs, sizes, animated, watched,
            busiest, busiestChanges);
        var gauges = PerfCounters.GaugesSnapshot();
        var line = s_storm.Observe(DateTime.UtcNow, second, PerfCounters.CountersSnapshot(), gauges, context);
        if (line != null) Write(line);

        if (!s_governor.Tripped)
            TripEffects(s_governor.Observe(second,
                gauges.TryGetValue("badges animating", out var badges) ? badges : 0));
    }

    /// <summary>
    /// The animation clocks running right now, grouped by what they are — e.g.
    /// <c>DoubleAnimation 0.5s Forever ×1</c>. A redraw driven by an animation names no element,
    /// so this is the only way a storm line can say which animation is keeping the UI awake.
    ///
    /// <para>Reads WPF's internal clock tree by reflection (MediaContext → TimeManager → its root
    /// clock group). A runtime that renames those members returns an empty string: a diagnostic
    /// may come back empty-handed, it may not throw.</para>
    /// </summary>
    internal static string DescribeActiveClocks(System.Windows.Threading.Dispatcher dispatcher)
    {
        try
        {
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            var mcType = typeof(System.Windows.Media.Visual).Assembly.GetType("System.Windows.Media.MediaContext");
            var from = mcType?.GetMethod("From", any, new[] { typeof(System.Windows.Threading.Dispatcher) });
            var mc = from?.Invoke(null, new object[] { dispatcher });
            var tm = mc == null ? null : mcType!.GetProperty("TimeManager", any)?.GetValue(mc);
            if (tm == null) return "";
            System.Windows.Media.Animation.ClockGroup? root = null;
            foreach (var f in tm.GetType().GetFields(any))
                if (f.GetValue(tm) is System.Windows.Media.Animation.ClockGroup g) { root = g; break; }
            if (root == null) return "";
            // The root keeps its children WEAKLY, in a list of its own; its public Children
            // collection is always empty. Measured: one running animation, Children = 0,
            // _rootChildren = 1.
            var roots = typeof(System.Windows.Media.Animation.ClockGroup)
                .GetField("_rootChildren", any)?.GetValue(root) as System.Collections.IEnumerable;

            var groups = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            void Walk(System.Windows.Media.Animation.Clock clock, int depth)
            {
                if (depth > 12) return;
                if (clock is System.Windows.Media.Animation.ClockGroup cg && cg.Children != null)
                {
                    foreach (var child in cg.Children) Walk(child, depth + 1);
                    return;
                }
                if (clock.CurrentState != System.Windows.Media.Animation.ClockState.Active) return;
                var t = clock.Timeline;
                var dur = t.Duration.HasTimeSpan ? $"{t.Duration.TimeSpan.TotalSeconds:0.##}s" : t.Duration.ToString();
                var repeat = t.RepeatBehavior == System.Windows.Media.Animation.RepeatBehavior.Forever
                    ? "Forever" : t.RepeatBehavior.ToString();
                var key = $"{t.GetType().Name} {dur} {repeat}";
                groups[key] = groups.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            if (roots != null)
                foreach (var item in roots)
                    if (item is WeakReference w && w.Target is System.Windows.Media.Animation.Clock c) Walk(c, 0);
            return groups.Count == 0
                ? "no active animation clocks"
                : "active clocks: " + string.Join(", ", groups.OrderByDescending(g => g.Value).Take(10)
                    .Select(g => $"{g.Key} ×{g.Value}"));
        }
        catch (Exception ex) { return $"clocks unavailable ({ex.GetType().Name})"; }
    }

    /// <summary>
    /// "Type#Name@NearestNamedAncestor" — enough to find an element in the XAML without dumping
    /// the tree. An unnamed element is placed by the first named one above it.
    /// </summary>
    internal static string ElementKey(System.Windows.FrameworkElement fe)
    {
        var sb = new System.Text.StringBuilder(fe.GetType().Name);
        if (!string.IsNullOrEmpty(fe.Name)) sb.Append('#').Append(fe.Name);
        System.Windows.DependencyObject? p = fe;
        for (var depth = 0; depth < 40; depth++)
        {
            p = p is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(p) ?? System.Windows.LogicalTreeHelper.GetParent(p)
                : System.Windows.LogicalTreeHelper.GetParent(p);
            if (p == null) break;
            if (p is System.Windows.FrameworkElement f && !string.IsNullOrEmpty(f.Name))
            {
                sb.Append('@').Append(f.Name);
                break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The method behind a dispatcher operation, for the stall log.
    ///
    /// <para>WPF exposes no public way to ask an operation what it will run, so this reaches for
    /// the private delegate field by reflection and degrades to the priority alone when the
    /// runtime does not have it. A diagnostic may guess; it may not throw.</para>
    /// </summary>
    private static string DescribeOperation(System.Windows.Threading.DispatcherOperation op)
    {
        try
        {
            var field = typeof(System.Windows.Threading.DispatcherOperation).GetField(
                "_method",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(op) is Delegate d)
            {
                var owner = d.Method.DeclaringType?.FullName ?? "?";
                return $"{owner}.{d.Method.Name}  (priority {op.Priority})";
            }
        }
        catch { /* fall through to the priority */ }
        return $"(method unavailable, priority {op.Priority})";
    }

    /// <summary>
    /// Runs <paramref name="action"/> and logs it only if it was slow.
    ///
    /// <para>The companion to <see cref="StartUiStallWatch"/>: that one says the dispatcher was
    /// held for N ms, this one says by what. Wrap the UI-thread steps that currently log nothing —
    /// a three-second stall with no log line between its ends is unattributable, which is exactly
    /// the state that made this take several rounds to pin down.</para>
    ///
    /// <para>Silent below the threshold, so wrapping a step that is usually fast costs one
    /// subtraction and adds no noise.</para>
    /// </summary>
    public static void Time(string what, Action action, int thresholdMs = 150)
    {
        var started = Environment.TickCount64;
        try { action(); }
        finally
        {
            var ms = Environment.TickCount64 - started;
            if (ms >= thresholdMs) Write($"SLOW  {what} — {ms} ms on the UI thread");
        }
    }

    public static void WriteSection(string title)
    {
        Write("");
        Write("--- " + title + " ---");
    }

    /// <summary>Save raw text content (e.g. the UpdateInfo.xml) for inspection.</summary>
    public static void SaveSnapshot(string filename, string content)
    {
        try
        {
            var path = AppPaths.SnapshotFile(filename);
            File.WriteAllText(path, content);
            Write($"Snapshot guardado: {filename}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Synchronously flushes any queued messages. Call from shutdown hooks
    /// (AppDomain.ProcessExit, unhandled exception handlers, …) so the last
    /// few log lines aren't lost when the process dies before the drainer
    /// gets to them.
    /// </summary>
    public static void Flush()
    {
        WriteBatch();
    }

    /// <summary>
    /// Bundles the diagnostic files into a single <c>.zip</c> at
    /// <paramref name="destinationZipPath"/> so a user can attach ONE file when
    /// reporting a bug. Includes the top-level <c>*.log</c> (launcher-debug.log,
    /// multiplayer-events.log) and <c>*snapshot*</c> files from
    /// <paramref name="sourceDir"/> (defaults to <see cref="AppPaths.DataDir"/>).
    ///
    /// When <paramref name="gameUserDataDir"/> is given (the mod's
    /// <c>My Games\&lt;folder&gt;</c> resolved by
    /// <see cref="UserDataService.GetUserDataFolder(string)"/>), the bundle ALSO
    /// carries — under a <c>game-userdata/</c> subfolder — the small OOS / sync /
    /// text-log artifacts AoE3 writes there (matched by
    /// <see cref="ShouldIncludeGameFile"/>, size- and count-capped so recorded
    /// games / savegames are never swept), plus a <c>game-userdata-listing.txt</c>
    /// snapshot of that folder. This is what makes an in-game OUT-OF-SYNC report
    /// diagnosable: a sim desync is written by the GAME, not the launcher log, so
    /// without these files the bundle can't show the cause. It is READ-ONLY — files
    /// are copied to staging, the game folder is never modified.
    ///
    /// DELIBERATELY EXCLUDES <c>launcher-config.json</c>: it holds the cached
    /// Discord session token, which must not leave the user's machine in a shared
    /// bundle. In its place the bundle carries <c>settings-summary.txt</c> — an
    /// ALLOWLISTED extract of the few settings a support report turns on (see
    /// <see cref="SettingsSummaryKeys"/>), so the token cannot be in it by
    /// construction. Subfolders (e.g. <c>mod-assets\</c>) are not included.
    ///
    /// Files are copied to a temp staging folder first (not zipped from their live
    /// path) so an in-flight log write can't race the archive. Returns the zip
    /// path; throws on failure so the caller can surface it.
    /// </summary>
    public static string ExportBundle(
        string destinationZipPath, string? sourceDir = null, string? gameUserDataDir = null)
    {
        Flush();
        var src = sourceDir ?? AppPaths.DataDir;
        var staging = Path.Combine(Path.GetTempPath(), "wol-diag-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            if (Directory.Exists(src))
            {
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    bool include =
                        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("snapshot", StringComparison.OrdinalIgnoreCase);
                    // Never ship the config — it carries the Discord session token.
                    if (name.Equals(AppPaths.ConfigFileName, StringComparison.OrdinalIgnoreCase))
                        include = false;
                    if (!include) continue;

                    try { File.Copy(file, Path.Combine(staging, name), overwrite: true); }
                    catch { /* skip a file we couldn't read; bundle the rest */ }
                }
            }

            // A redacted handful of settings — NOT the config itself (see above).
            StageSettingsSummary(src, staging);

            // Game user-data OOS/sync artifacts (best-effort, read-only).
            if (!string.IsNullOrEmpty(gameUserDataDir))
                StageGameUserData(gameUserDataDir!, staging);

            var destDir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);

            ZipFile.CreateFromDirectory(staging, destinationZipPath, CompressionLevel.Optimal,
                includeBaseDirectory: false);
            return destinationZipPath;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// The only settings that may leave the machine in a shared bundle, by exact JSON
    /// name. An ALLOWLIST, never a denylist: the config is excluded wholesale because it
    /// carries the cached Discord session token, and a denylist would leak the next
    /// secret somebody adds. Anything not named here is not copied, whatever it is.
    ///
    /// <para>These are the fields a support report actually turns on. The one that
    /// prompted the list is <c>checkUpdatesOnStartup</c>: a user reported that a release
    /// was not being offered, and with neither that flag nor the saved tag in the bundle
    /// the answer had to be inferred from log lines that were <i>absent</i>.</para>
    /// </summary>
    private static readonly string[] SettingsSummaryKeys =
    {
        "checkUpdatesOnStartup",
        "autoUpdateMods",
        "lastInstalledLauncherTag",
        // Legacy: builds after v1.0.15f never read it and blank it on every load. Only older
        // copies sharing this config still write it.
        "launcherUpdateETag",
        // The pair that replaced it: which release a 304 would vouch for.
        "launcherReleaseETag",
        "launcherReleaseTag",
        "activeModId",
        "modsCatalogRepo",
        "language",
        // Nothing in the launcher ever assigns this, so a non-empty value means the
        // config was hand-edited — and it is passed to the game on every launch, which
        // makes it one of the few things that can differ between "started from the
        // launcher" and "double-clicked". Added after a report where that had to be
        // ruled out and the bundle could not answer it.
        "gameArguments",
    };

    /// <summary>
    /// Writes <c>settings-summary.txt</c> into the staging folder: the
    /// <see cref="SettingsSummaryKeys"/> values, plus each known mod's id and recorded
    /// install path. Reads the config as raw JSON rather than through
    /// <c>LauncherConfig</c> so this stays a leaf with no dependency on the model — and
    /// so a field can only appear here by being named above.
    ///
    /// <para>Install paths are included deliberately: the launcher's own log already
    /// prints them on every probe, and "which folder does it think the mod is in" is
    /// half of every install report. Best-effort — a diagnostics extra must never block
    /// the export it feeds.</para>
    /// </summary>
    private static void StageSettingsSummary(string sourceDir, string stagingDir)
    {
        try
        {
            var configPath = Path.Combine(sourceDir, AppPaths.ConfigFileName);
            if (!File.Exists(configPath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Redacted extract of launcher-config.json.");
            sb.AppendLine("The config itself is never bundled - it holds a Discord session token.");
            sb.AppendLine();

            foreach (var key in SettingsSummaryKeys)
            {
                var value = root.TryGetProperty(key, out var el)
                    ? (el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString())
                    : "(absent)";
                sb.AppendLine($"{key} = {value}");
            }

            if (root.TryGetProperty("mods", out var mods)
                && mods.ValueKind == JsonValueKind.Object)
            {
                sb.AppendLine();
                sb.AppendLine("mods (id -> installPath):");
                foreach (var mod in mods.EnumerateObject())
                {
                    var path = mod.Value.ValueKind == JsonValueKind.Object
                               && mod.Value.TryGetProperty("installPath", out var ip)
                        ? ip.GetString() ?? ""
                        : "";
                    sb.AppendLine($"  {mod.Name} = {(path.Length == 0 ? "(none)" : path)}");
                }
            }

            File.WriteAllText(Path.Combine(stagingDir, "settings-summary.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            Write($"ExportBundle: could not stage the settings summary: {ex.Message}");
        }
    }

    /// <summary>Cap on a single game-folder file we'll copy (bytes). Above this we
    /// skip it — a recorded game (<c>.age3Yrec</c>) / savegame is far larger and
    /// isn't a small OOS/sync/log dump.</summary>
    internal const long GameFileMaxBytes = 2L * 1024 * 1024;

    /// <summary>Safety cap on how many game-folder files we copy into the bundle.</summary>
    internal const int GameFileMaxCount = 40;

    /// <summary>
    /// Pure include/exclude rule for a top-level file in the game's user-data
    /// folder (kept static + parameterised so it's unit-testable). We take only the
    /// small OOS / sync / plain-text-log artifacts — matched by name pattern
    /// (<c>*oos*</c>, <c>*sync*</c>, <c>*.txt</c>, <c>*.log</c>, case-insensitive) —
    /// and reject anything over <see cref="GameFileMaxBytes"/>, so recorded games
    /// (<c>.age3Yrec</c>), savegames (<c>.age3Ysav</c>), configs and other binaries
    /// never enter the shared bundle.
    /// </summary>
    internal static bool ShouldIncludeGameFile(string name, long sizeBytes)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (sizeBytes < 0 || sizeBytes > GameFileMaxBytes) return false;

        bool nameMatch =
            name.Contains("oos", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
        return nameMatch;
    }

    /// <summary>
    /// Copy the game user-data folder's OOS/sync/log artifacts into a
    /// <c>game-userdata/</c> subfolder of the staging area, and always write a
    /// <c>game-userdata-listing.txt</c> snapshot of the folder's top level — even
    /// when nothing matched — so a reporter (and we) can see exactly what AoE3 left
    /// there and learn the real dump names. Whole-operation best-effort: a failure
    /// here must never abort the bundle.
    /// </summary>
    private static void StageGameUserData(string gameUserDataDir, string staging)
    {
        try
        {
            if (!Directory.Exists(gameUserDataDir)) return;

            var outDir = Path.Combine(staging, "game-userdata");
            Directory.CreateDirectory(outDir);

            var listing = new System.Text.StringBuilder();
            listing.Append("Game user-data folder: ").AppendLine(gameUserDataDir);
            listing.AppendLine("Top-level entries (name | bytes | last write UTC):");
            listing.AppendLine();

            int copied = 0;
            foreach (var file in Directory.EnumerateFiles(gameUserDataDir, "*", SearchOption.TopDirectoryOnly))
            {
                long size;
                DateTime mtimeUtc;
                var name = Path.GetFileName(file);
                try
                {
                    var info = new FileInfo(file);
                    size = info.Length;
                    mtimeUtc = info.LastWriteTimeUtc;
                }
                catch { continue; }

                listing.Append(name).Append(" | ").Append(size).Append(" | ")
                       .AppendLine(mtimeUtc.ToString("u"));

                if (copied < GameFileMaxCount && ShouldIncludeGameFile(name, size))
                {
                    try { File.Copy(file, Path.Combine(outDir, name), overwrite: true); copied++; }
                    catch { /* skip a file we couldn't read; bundle the rest */ }
                }
            }

            // Directories too. The listing used to enumerate FILES only, so a bundle from a
            // player reporting an ELO problem showed two .txt files and said nothing about
            // whether Savegame\ even existed — the one folder the whole question turns on.
            listing.AppendLine();
            listing.AppendLine("Top-level folders:");
            foreach (var dir in Directory.EnumerateDirectories(gameUserDataDir))
                listing.Append("  ").AppendLine(Path.GetFileName(dir));

            listing.AppendLine().Append("Files copied into bundle: ").Append(copied)
                   .Append(" (cap ").Append(GameFileMaxCount).AppendLine(").");

            try { File.WriteAllText(Path.Combine(outDir, "game-userdata-listing.txt"), listing.ToString()); }
            catch { /* listing is a nice-to-have */ }

            StageReplayIndex(gameUserDataDir, outDir);
        }
        catch (Exception ex)
        {
            Write($"ExportBundle: could not stage game user-data '{gameUserDataDir}': {ex.Message}");
        }
    }

    /// <summary>Recordings described in the bundle's replay index.</summary>
    private const int ReplayIndexMaxFiles = 10;

    /// <summary>
    /// Skip anything this big. A recording of a long game is a few MB; well past that and we
    /// are looking at something else, and inflating it would cost seconds for nothing.
    /// </summary>
    /// <summary>
    /// How much of a recording's tail to print when its outcome could not be settled.
    ///
    /// <para>Wider than the block itself (32 bytes), so a trailer that is merely in the wrong
    /// place is visible rather than cut off — which is exactly the shape a team game might turn
    /// out to have. Sixteen bytes, the previous width, could not have shown one.</para>
    /// </summary>
    private const int TailDumpBytes = 48;

    private const long ReplayIndexMaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Describe the newest recordings in <c>Savegame\</c> — WITHOUT copying them.
    ///
    /// <para><b>Why this exists.</b> Every question worth asking when a match does not score is
    /// answered by these few lines: whether a recording was written at all, when relative to the
    /// match, which map and players it names, and whether the game finished writing its outcome.
    /// Working that out for one reported incident meant asking the player for his files over
    /// Discord and parsing them by hand; the same answer now travels in the bundle he already
    /// sends. The files themselves are megabytes each and are never copied — only read.</para>
    ///
    /// <para>Also the measurement that settles WHEN AoE3 writes the file: the timestamps here,
    /// against the game-exit line in the launcher log, are what decide whether the post-report
    /// retry ladder is buying anything.</para>
    ///
    /// <para>Best-effort throughout: a single unreadable recording is described as such and the
    /// rest are still listed.</para>
    /// </summary>
    private static void StageReplayIndex(string gameUserDataDir, string outDir)
    {
        try
        {
            var saveDir = Path.Combine(gameUserDataDir, "Savegame");
            var root = Directory.Exists(saveDir) ? saveDir : gameUserDataDir;

            var files = new DirectoryInfo(root)
                .EnumerateFiles("*.age3yrec", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(ReplayIndexMaxFiles)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.Append("Recordings under: ").AppendLine(root);
            sb.Append("Newest ").Append(ReplayIndexMaxFiles).AppendLine(" shown; files are never copied.");
            sb.AppendLine();

            if (files.Count == 0) sb.AppendLine("(none)");

            foreach (var f in files)
            {
                sb.Append(f.Name).Append(" | ").Append(f.Length).Append(" bytes | ")
                  .AppendLine(f.LastWriteTimeUtc.ToString("u"));

                if (f.Length > ReplayIndexMaxBytes) { sb.AppendLine("    (too large to parse)"); continue; }

                try
                {
                    var data = Multiplayer.ReplayParserService.TryReadContainer(File.ReadAllBytes(f.FullName));
                    if (data == null) { sb.AppendLine("    (container unreadable)"); continue; }

                    var header = Multiplayer.ReplayParserService.ParseHeader(data);
                    if (header == null) { sb.AppendLine("    (header unreadable)"); continue; }

                    sb.Append("    map=").Append(header.MapName)
                      .Append(" seed=").Append(header.RandomSeed)
                      .Append(" hostTime=").Append(header.HostTime).AppendLine();

                    // team=<lobby>/<game> is what makes a 2v2 legible in a bundle at all. The first
                    // value is gameplayer{N}teamid, the lobby's dropdown — -1 whenever nobody touched
                    // it, which is how the first competitive 2v2s lost their teams. The second is the
                    // side the game assigned, from the map-setup string; -1 there means the string
                    // could not be read with certainty.
                    sb.Append("    players: ");
                    sb.AppendLine(string.Join(", ", header.Players.Select(
                        pl => $"[{pl.Slot}] {(string.IsNullOrWhiteSpace(pl.Name) ? "(unnamed)" : pl.Name)}"
                              + $" team={pl.TeamId}/{pl.GameTeam}"
                              + (pl.IsHuman ? "" : " (not human)"))));

                    var outcome = Multiplayer.ReplayParserService.ReadOutcome(data, header);

                    // Every resign command, from the whole stream. In a team game this is what
                    // decides the match — the side every member of which resigned or was removed —
                    // and the earlier resignations sit far before the end, so the outcome line
                    // below shows only the last one. "agrees" is the self-check that the last
                    // record IS the outcome block; a bundle where it reads false is a file the
                    // launcher would not decide from.
                    var resignations = Multiplayer.ReplayParserService.ReadResignations(data, header);
                    sb.Append("    resignations: ")
                      .Append(Multiplayer.ReplayParserService.DescribeResignations(resignations, header.Players))
                      .Append(" agrees=")
                      .Append(Multiplayer.ReplayParserService.ResignationsAgreeWithOutcome(resignations, outcome))
                      .AppendLine();
                    // The trailer is the whole reason a readable recording can still fail to
                    // decide a match, so it is spelled out rather than summarised.
                    sb.Append("    outcome: ").Append(outcome.Confidence)
                      .Append(" loser=").Append(outcome.LoserSlot)
                      .Append(" signature=").Append(outcome.SignaturePresent)
                      // Printed raw, under a name that claims nothing. It was called
                      // "recorder=" and is not one; a bundle that asserts it again would send
                      // the next reader down the same path.
                      .Append(" trailerB=").Append(outcome.TrailerSecondSlot).AppendLine();
                    // The tail, whenever the outcome was not settled — not only when the
                    // signature is absent. A trailer sitting just past the slack window, or a
                    // partially-written one, both read as "no result" and both leave no evidence
                    // at all under the narrower rule. It is 48 bytes rather than 16 because a
                    // healthy block is 32 on its own, so 16 could never show one that was merely
                    // misplaced.
                    //
                    // THIS IS THE OPEN QUESTION'S INSTRUMENT: nobody knows whether a TEAM game
                    // writes an outcome block, because the one 2v2 anyone has been able to
                    // inspect has none — and so does a quarter of the 1v1s, which is why that
                    // proves nothing. The first team game played with the launcher answers it
                    // here. (Its twin lives on the live path in MultiplayerTab; change both or
                    // they disagree.)
                    if (outcome.Confidence != Multiplayer.ReplayParserService.ReplayOutcomeConfidence.Confident
                        && data.Length >= TailDumpBytes)
                        sb.Append("    unsettled outcome; last ").Append(TailDumpBytes)
                          .Append(" bytes = ")
                          .AppendLine(BitConverter.ToString(data, data.Length - TailDumpBytes));
                }
                catch (Exception ex)
                {
                    sb.Append("    (could not be read: ").Append(ex.Message).AppendLine(")");
                }
            }

            File.WriteAllText(Path.Combine(outDir, "replay-index.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            Write($"ExportBundle: could not index recordings: {ex.Message}");
        }
    }

    private static async Task DrainerLoop()
    {
        // Wait for at least one enqueued message, then drain everything
        // currently pending in one batched append. Coalescing keeps the
        // file handle churn low when many writes arrive in quick
        // succession (mod switches, CheckAsync progress streams, etc.).
        while (true)
        {
            try { await Signal.WaitAsync().ConfigureAwait(false); }
            catch { return; }

            WriteBatch();
        }
    }

    private static void WriteBatch()
    {
        if (Queue.IsEmpty) return;

        var sb = new System.Text.StringBuilder();
        while (Queue.TryDequeue(out var line))
            sb.Append(line);

        if (sb.Length == 0) return;

        lock (FileLock)
        {
            try { File.AppendAllText(LogPath, sb.ToString()); }
            catch { /* best-effort */ }
        }
    }
}
