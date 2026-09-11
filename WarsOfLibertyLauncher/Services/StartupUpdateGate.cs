using System;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Installs a newer launcher and restarts into it, unattended, BEFORE the main window opens.
///
/// <para><b>Why it exists.</b> Every release is already mandatory for multiplayer
/// (<see cref="LauncherUpdateGate"/>), but the only way to install one was the gold pill — so
/// anybody who never clicked it stayed shut out of multiplayer indefinitely, which is not a
/// choice they made. Startup is also the one moment when nothing can be interrupted: no game
/// is running, no install is in flight, and there is no window whose state a restart throws
/// away.</para>
///
/// <para><b>Every failure path ends in the normal launcher.</b> Offline, slow, rate-limited, a
/// bad hash, a locked file, anything unforeseen — the gate gives up and <c>App</c> opens the
/// launcher exactly as it does today, with the pill offering the update by hand. An update
/// must never be the reason somebody cannot open the launcher.</para>
///
/// <para>The decisions live in <see cref="AutoUpdatePolicy"/>, which is pure and pinned by
/// tests; this class is only the orchestration around them.</para>
/// </summary>
public static class StartupUpdateGate
{
    /// <summary>
    /// How long the version check gets before we stop waiting and open the launcher. The
    /// service's own <c>HttpClient</c> allows 15 s, which is fine for a check nobody is
    /// waiting on and far too long to hold a launch behind a blank screen.
    /// </summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Ceiling on an unattended download. Only the headless path needs one: everywhere else
    /// the window has a Skip button, and there is no window at all here, so a stalled-but-alive
    /// connection would otherwise hold a login session for ever.
    /// </summary>
    private static readonly TimeSpan HeadlessDownloadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>What this launch is, as far as the gate is concerned.</summary>
    /// <param name="Headless">Started into the tray (<c>--minimized</c>): do it all with no
    /// window. The promise of "run in the background" is that a login launch is invisible, and
    /// a progress window at logon would break it.</param>
    /// <param name="ExplicitTask">This launch was started to do one specific job
    /// (<c>--update-now</c>, a self-install or self-update handoff) — leave it alone.</param>
    /// <param name="Bypassed"><c>App.NoUpdateGate</c>: a debug build, a debugger, or the
    /// maintainer's switch.</param>
    /// <param name="JoinLobbyId">A validated deep-link lobby id this launch carried, so a
    /// restart does not swallow it.</param>
    public readonly record struct Context(
        bool Headless,
        bool ExplicitTask,
        bool Bypassed,
        string? JoinLobbyId);

    /// <summary>
    /// What happened. <c>Relaunched</c> true means the replacement process is already starting
    /// and the caller must shut this one down; anything else means "carry on and open the
    /// launcher". <c>Check</c> is the version check's answer when one was made, handed to
    /// MainWindow so it does not spend a second GitHub request on the same question.
    /// </summary>
    public readonly record struct Outcome(
        bool Relaunched,
        LauncherUpdateService.UpdateCheckResult? Check,
        AutoUpdateDecision Decision);

    public static async Task<Outcome> RunAsync(Context ctx)
    {
        var state = StartupUpdateState.Read();

        var pre = AutoUpdatePolicy.DecideBeforeCheck(
            state.CheckUpdatesOnStartup, ctx.ExplicitTask, ctx.Bypassed, Environment.ProcessPath);
        if (pre != AutoUpdateDecision.Apply)
        {
            DiagnosticLog.Write($"Startup auto-update: not checking - {pre}.");
            return new Outcome(false, null, pre);
        }

        // The same self-heal MainWindow applies before ITS check: a saved tag that contradicts
        // the running binary is stale (somebody swapped the .exe by hand), and believing it
        // would make the check answer a different question than the one we are asking. Applied
        // in memory only - MainWindow owns the correction and the write.
        var savedTag = state.LastInstalledLauncherTag;
        var informational = LauncherUpdateService.CurrentInformationalTag;
        if (LauncherUpdateService.SavedTagContradictsBinary(savedTag, informational))
        {
            DiagnosticLog.Write(
                $"Startup auto-update: saved tag '{savedTag}' contradicts the binary " +
                $"'{informational}'; using the binary.");
            savedTag = informational;
        }

        LauncherUpdateService.UpdateCheckResult check;
        using (var cts = new CancellationTokenSource(CheckTimeout))
        {
            // The token goes INTO CheckAsync rather than racing it with WhenAny: the service
            // reads it to decide whether a failure means "offline", and a token-less timeout
            // would flag a perfectly online user as offline for the rest of the session.
            check = await LauncherUpdateService.CheckAsync(
                lastInstalledTag: savedTag,
                skippedTag: "",
                cachedETag: state.LauncherUpdateETag,
                ct: cts.Token);
        }

        var decision = AutoUpdatePolicy.Decide(
            updateAvailable: check.UpdateAvailable,
            downloadUrl: check.DownloadUrl,
            remoteTag: check.RemoteTag,
            checkUpdatesOnStartup: state.CheckUpdatesOnStartup,
            explicitTask: ctx.ExplicitTask,
            bypassed: ctx.Bypassed,
            processPath: Environment.ProcessPath,
            attemptTag: state.AttemptTag,
            attemptCount: state.AttemptCount,
            enoughDisk: HasRoomFor(check.DownloadSize));

        if (decision != AutoUpdateDecision.Apply)
        {
            // The result still goes back: MainWindow lights the pill from it, so a refusal
            // here is not the same as never having looked.
            DiagnosticLog.Write($"Startup auto-update: not applying {Describe(check)} - {decision}.");
            return new Outcome(false, check, decision);
        }

        DiagnosticLog.Write($"Startup auto-update: applying {Describe(check)} unattended.");
        return await ApplyAsync(ctx, check, state);
    }

    private static async Task<Outcome> ApplyAsync(
        Context ctx,
        LauncherUpdateService.UpdateCheckResult check,
        StartupUpdateState.Snapshot state)
    {
        // BEFORE the download, not after the restart. An attempt that dies anywhere in between
        // - a failed swap, a machine switched off, a binary that comes back still reporting the
        // old version - has to count, or the loop guard guards nothing and a release that
        // cannot recognise itself re-downloads ~165 MB on every single launch, for ever.
        StartupUpdateState.RecordAttempt(
            check.RemoteTag,
            AutoUpdatePolicy.NextAttemptCount(check.RemoteTag, state.AttemptTag, state.AttemptCount));

        // The language, resolved the way MainWindow will resolve it in a moment. Strings default
        // to English until MainWindow's constructor runs, and on this path our window can be the
        // only thing the user sees all launch.
        Strings.SetLanguage(StartupUpdateState.ResolveLanguage(state));

        using var cts = ctx.Headless
            ? new CancellationTokenSource(HeadlessDownloadTimeout)
            : new CancellationTokenSource();

        StartupUpdateWindow? window = null;
        if (!ctx.Headless)
        {
            window = new StartupUpdateWindow(check.LatestVersion);
            window.SkipRequested += () =>
            {
                DiagnosticLog.Write("Startup auto-update: skipped by the user.");
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            };
            window.Show();
        }

        try
        {
            var progress = window == null
                ? null
                : new Progress<DownloadProgress>(window.Report);

            // Task.Run, and it is not ceremony: DownloadUpdateAsync ends by SHA-256ing ~170 MB
            // through HashService.ComputeSha256Async, which awaits without ConfigureAwait(false)
            // - so on the UI thread every chunk's transform is posted back to it and the window
            // freezes solid. Measured at 4.6 seconds on this machine, all of it with "Verifying"
            // on screen and nothing repainting. The Progress was built on the UI thread, so its
            // callbacks still marshal home.
            await Task.Run(() => LauncherUpdateService.DownloadUpdateAsync(
                check.DownloadUrl!, check.ExpectedSha256, progress, cts.Token));

            // Only now, with verified bytes on disk: the new binary has to recognise itself or
            // it comes back offering the update it just installed.
            StartupUpdateState.CommitInstalled(check.RemoteTag);

            window?.ShowRestarting();

            // Off the UI thread: the swap retries around an antivirus holding the new file, and
            // the window should keep painting while it does.
            var args = BuildRelaunchArguments(ctx);
            await Task.Run(() => LauncherUpdateService.RelaunchUpdated(args));

            DiagnosticLog.Write($"Startup auto-update: restarting into {check.RemoteTag}.");
            DiagnosticLog.Flush();
            return new Outcome(true, check, AutoUpdateDecision.Apply);
        }
        catch (OperationCanceledException)
        {
            // Skip, or the headless ceiling ran out. Neither is a failure and neither burns the
            // tag: the .part file survives, so the next launch resumes where this one stopped.
            DiagnosticLog.Write("Startup auto-update: cancelled; opening the launcher.");
            return new Outcome(false, check, AutoUpdateDecision.Apply);
        }
        catch (UpdateVerificationException ex)
        {
            // The bytes are not what the release promised, and re-downloading them would reach
            // the same conclusion - so this tag stops being tried unattended. The pill still
            // offers it, which is where a human should decide.
            DiagnosticLog.Write(
                $"Startup auto-update: verification failed ({ex.Message}); burning the tag.");
            StartupUpdateState.RecordAttempt(check.RemoteTag, AutoUpdatePolicy.BurnedAttemptCount);
            return new Outcome(false, check, AutoUpdateDecision.Apply);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Startup auto-update failed: {ex.Message}");
            return new Outcome(false, check, AutoUpdateDecision.Apply);
        }
        finally
        {
            window?.CloseByOwner();
        }
    }

    /// <summary>
    /// What the replacement process is started with. Two things must survive a restart the user
    /// did not ask for: a launch headed for the tray must still go there, and a deep link
    /// somebody clicked must still open its room.
    ///
    /// <para><see cref="LauncherUpdateService.FromUpdateArg"/> is the load-bearing one — it is
    /// what makes the child WAIT for this process to release the single-instance mutex instead
    /// of quitting as a duplicate.</para>
    /// </summary>
    private static string BuildRelaunchArguments(Context ctx)
    {
        var args = LauncherUpdateService.FromUpdateArg;
        if (ctx.Headless) args += " --minimized";
        if (DeepLinkService.IsValidLobbyId(ctx.JoinLobbyId))
            args += " \"" + DeepLinkService.BuildJoinUri(ctx.JoinLobbyId!) + "\"";
        return args;
    }

    /// <summary>
    /// Room for the download beside the running executable — twice its size, because the old
    /// and new binaries sit side by side until the swap. The manual dialog warns and lets the
    /// user continue anyway; unattended there is nobody to ask, so a shortfall is a refusal and
    /// the pill inherits the decision.
    /// </summary>
    private static bool HasRoomFor(long downloadSize)
    {
        // Unmeasurable means don't warn - DiskSpaceService's own rule, applied here so an
        // unknown size cannot silently disable the update.
        if (downloadSize <= 0) return true;

        var shortfall = DiskSpaceService.Check(
            LauncherUpdateService.GetPendingUpdatePath(), downloadSize * 2,
            tempPath: null, tempRequired: 0);
        if (shortfall == null) return true;

        DiagnosticLog.Write(
            $"Startup auto-update: not enough room on {shortfall.Drive} " +
            $"({shortfall.FreeBytes} bytes free, {shortfall.RequiredBytes} needed).");
        return false;
    }

    private static string Describe(LauncherUpdateService.UpdateCheckResult check)
        => $"{check.CurrentVersion} -> {check.LatestVersion}";
}
