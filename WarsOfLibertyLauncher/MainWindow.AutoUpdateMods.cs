using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// "Update mods automatically" (2026-11-18): when an installed mod has an update on offer, the
/// launcher applies it by itself once nothing stands in the way (<see cref="ModAutoUpdatePolicy"/>).
/// The mod on screen first; then, one at a time, the mods the notification sweep found behind,
/// each through its OWN <see cref="UpdateService"/> passed as the target of the same
/// <c>ApplyAsync</c> / <c>RepairInstallAsync</c> the Update button runs. Throughout,
/// <see cref="Unattended"/> answers every question "no" and turns every notice into a toast, so
/// nothing opens over the player.
/// </summary>
public partial class MainWindow
{
    /// <summary>True while an automatic update runs: prompts answer "no" by themselves.</summary>
    private bool _automaticOperation;

    /// <summary>The service of the update or repair running now, when it is not the mod on screen
    /// (an automatic update of a mod that is not open). Null = <see cref="_updateService"/>.</summary>
    private UpdateService? _operatingService;

    /// <summary>What the shared progress, pause and bell code reads: the operation's own mod.</summary>
    private UpdateService OperatingService => _operatingService ?? _updateService;

    private DispatcherTimer? _autoUpdateTimer;
    private readonly Dictionary<string, int> _autoUpdateAttempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Asks again after <paramref name="delay"/>: a check found an update (seconds), or a
    /// game or a room just ended (90 s, so the player is not fighting a download at once).</summary>
    private void ScheduleAutoUpdate(TimeSpan delay)
    {
        if (!_config.AutoUpdateMods) return;
        _autoUpdateTimer ??= new DispatcherTimer();
        _autoUpdateTimer.Stop();
        _autoUpdateTimer.Interval = delay;
        _autoUpdateTimer.Tick -= AutoUpdateTick;
        _autoUpdateTimer.Tick += AutoUpdateTick;
        _autoUpdateTimer.Start();
    }

    private async void AutoUpdateTick(object? sender, EventArgs e)
    {
        _autoUpdateTimer?.Stop();
        try
        {
            if (!await MaybeAutoUpdateActiveModAsync()) await MaybeAutoUpdateOtherModAsync();
        }
        catch (Exception ex) { DiagnosticLog.Write($"Automatic mod update failed: {ex.Message}"); }
    }

    /// <summary>Installed mods other than the one on screen that the sweep found behind.</summary>
    private readonly List<string> _autoUpdateBehind = new();

    private void NoteBehindForAutoUpdate(ModProfile profile)
    {
        if (!_config.AutoUpdateMods || profile.IsStockGame) return;
        if (string.Equals(profile.Id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase)) return;
        if (!_autoUpdateBehind.Contains(profile.Id, StringComparer.OrdinalIgnoreCase)) _autoUpdateBehind.Add(profile.Id);
    }

    /// <summary>
    /// One mod that is not open, per tick: its own check (the sweep only said it is behind), the
    /// same refusals as the mod on screen, then the update with that service as its target.
    /// </summary>
    private async Task MaybeAutoUpdateOtherModAsync()
    {
        if (!_config.AutoUpdateMods || _autoUpdateBehind.Count == 0) return;
        var id = _autoUpdateBehind[0];
        var profile = ModRegistry.Find(id);
        var state = _config.Mods.TryGetValue(id, out var st) ? st : null;
        if (profile == null || state == null || string.IsNullOrEmpty(state.InstallPath)
            || string.Equals(id, _updateService.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _autoUpdateBehind.RemoveAt(0);
            if (_autoUpdateBehind.Count > 0) ScheduleAutoUpdate(TimeSpan.FromSeconds(60));
            return;
        }

        var svc = new UpdateService(_config, profile);
        UpdateService.CheckResult result;
        try { result = await svc.CheckAsync(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Automatic update of {id}: its check failed: {ex.Message}");
            ScheduleAutoUpdate(TimeSpan.FromMinutes(5));
            return;
        }

        var latest = result.LatestVersion?.Ver ?? "";
        var key = $"{id}@{latest}";
        var facts = FactsForOtherMod(profile, state, svc, result, key);
        var verdict = ModAutoUpdatePolicy.Decide(facts);
        if (verdict != ModAutoUpdatePolicy.Verdict.Go)
        {
            if (verdict is ModAutoUpdatePolicy.Verdict.GameRunning or ModAutoUpdatePolicy.Verdict.InMultiplayer
                or ModAutoUpdatePolicy.Verdict.Busy or ModAutoUpdatePolicy.Verdict.Offline)
            {
                DiagnosticLog.Write($"Automatic update of {id}: not now - {verdict}.");
                ScheduleAutoUpdate(TimeSpan.FromMinutes(5));
                return;
            }
            // Nothing to do, or nothing it may do without asking: the bell already said so.
            if (verdict is not ModAutoUpdatePolicy.Verdict.NothingPending)
                DiagnosticLog.Write($"Automatic update of {id}: left to the player - {verdict}.");
            _autoUpdateBehind.RemoveAt(0);
            if (_autoUpdateBehind.Count > 0) ScheduleAutoUpdate(TimeSpan.FromSeconds(60));
            return;
        }

        _autoUpdateAttempts[key] = facts.Attempts + 1;
        DiagnosticLog.Write($"Automatic update of {id} (not open): starting (attempt {facts.Attempts + 1}).");
        _automaticOperation = true;
        using (Unattended.Begin(this))
        {
            try
            {
                if (profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases)
                    await RepairInstallAsync(asUpdate: true, target: svc);
                else
                    await ApplyAsync(target: svc, targetPending: result.PendingDownloads?.ToList() ?? new());
            }
            finally
            {
                _automaticOperation = false;
            }
        }

        // Asked again: what is on disk now, not what the operation hoped for.
        try
        {
            var after = await new UpdateService(_config, profile).CheckAsync();
            if (!OtherModUpdatePending(profile, state, after))
            {
                _autoUpdateBehind.RemoveAt(0);
                _autoUpdateAttempts.Remove(key);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Automatic update of {id}: the check after it failed: {ex.Message}");
        }
        if (_autoUpdateBehind.Count > 0) ScheduleAutoUpdate(TimeSpan.FromSeconds(60));
    }

    /// <summary>Whether a mod that is not open has something to update to (pins respected).</summary>
    internal static bool OtherModUpdatePending(ModProfile profile, ModState state, UpdateService.CheckResult r)
    {
        var current = r.CurrentVersion?.Ver;
        if (!string.IsNullOrEmpty(state.PinnedVersion)
            && string.Equals(state.PinnedVersion, current, StringComparison.OrdinalIgnoreCase)) return false;
        if (profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases)
        {
            var latest = r.LatestVersion?.Ver;
            return !string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(latest)
                && !string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);
        }
        return profile.UpdateMechanism == ModUpdateMechanism.WolPatcher && (r.PendingDownloads?.Count ?? 0) > 0;
    }

    private ModAutoUpdatePolicy.Facts FactsForOtherMod(ModProfile profile, ModState state, UpdateService svc,
        UpdateService.CheckResult result, string key)
    {
        var installPath = svc.InstallPath ?? "";
        bool needsAdmin = string.IsNullOrEmpty(installPath)
            || !ElevationService.CanWriteTo(installPath)
            || PlanPrivateKeyWrite(profile, installPath) != null;
        bool shortOnDisk = false;
        var pending = result.PendingDownloads;
        if (profile.UpdateMechanism != ModUpdateMechanism.GitHubReleases && pending is { Count: > 0 } && installPath.Length > 0)
        {
            long bytes = pending.Sum(d => d.Size);
            shortOnDisk = DiskSpaceService.Check(installPath, bytes, AppPaths.InstallTempRoot, bytes * 2) != null;
        }
        _autoUpdateAttempts.TryGetValue(key, out var attempts);
        return new ModAutoUpdatePolicy.Facts(
            Enabled: _config.AutoUpdateMods,
            ChecksAllowed: _config.CheckUpdatesOnStartup,
            Offline: ConnectivityState.IsOffline || result.Degraded,
            UpdatePending: OtherModUpdatePending(profile, state, result),
            VersionKnown: !string.IsNullOrEmpty(result.CurrentVersion?.Ver),
            Attempts: attempts,
            Busy: _isBusy,
            GameRunning: _isGameRunning || MultiplayerView.IsGameRunning
                         || AnyGameProcessRunning(profile) || AnyGameProcessRunning(_updateService.Profile),
            InMultiplayer: MultiplayerView.BusyForAutoUpdate,
            NeedsAdmin: needsAdmin,
            ShortOnDisk: shortOnDisk);
    }

    private string AutoUpdateKey()
        => $"{_updateService.Profile.Id}@{_lastCheckResult?.LatestVersion?.Ver ?? _config.GetState(_updateService.Profile.Id).LastKnownLatestVersion}";

    private ModAutoUpdatePolicy.Facts AutoUpdateFacts()
    {
        var profile = _updateService.Profile;
        var installPath = _updateService.InstallPath ?? "";
        bool gh = profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases;
        bool versionKnown = gh
            ? !string.IsNullOrWhiteSpace(_config.GetState(profile.Id).LastKnownVersion)
            : _lastCheckResult?.CurrentVersion != null;
        bool needsAdmin = string.IsNullOrEmpty(installPath)
            || !ElevationService.CanWriteTo(installPath)
            || PlanPrivateKeyWrite(profile, installPath) != null;
        bool shortOnDisk = false;
        if (!gh && _pendingDownloads.Count > 0 && !string.IsNullOrEmpty(installPath))
        {
            long bytes = _pendingDownloads.Sum(d => d.Size);
            shortOnDisk = DiskSpaceService.Check(installPath, bytes, AppPaths.InstallTempRoot, bytes * 2) != null;
        }
        _autoUpdateAttempts.TryGetValue(AutoUpdateKey(), out var attempts);
        return new ModAutoUpdatePolicy.Facts(
            Enabled: _config.AutoUpdateMods,
            ChecksAllowed: _config.CheckUpdatesOnStartup,
            Offline: ConnectivityState.IsOffline,
            UpdatePending: _primaryAction == PrimaryAction.Update && !profile.IsStockGame,
            VersionKnown: versionKnown,
            Attempts: attempts,
            Busy: _isBusy,
            GameRunning: _isGameRunning || MultiplayerView.IsGameRunning || AnyGameProcessRunning(profile),
            InMultiplayer: MultiplayerView.BusyForAutoUpdate,
            NeedsAdmin: needsAdmin,
            ShortOnDisk: shortOnDisk);
    }

    /// <summary>
    /// A notice an unattended operation would have opened in a window: a toast, silenced by "Do not
    /// disturb" like any news, with one button when the window has more to show (the antivirus one).
    /// </summary>
    private void ShowUnattendedNotice(string title, string body, bool warning, Action? reopen)
    {
        var actions = reopen == null
            ? Array.Empty<Controls.AppToast.ToastAction>()
            : new[] { new Controls.AppToast.ToastAction(Localization.Strings.Get("AvBlockedToastShow"),
                Controls.AppToast.ToastButton.Action, reopen) };
        ShowAppToast(new Controls.AppToast.ToastOptions(
            warning ? Controls.AppToast.ToastTone.Warn : Controls.AppToast.ToastTone.Info,
            warning ? Controls.AppToast.ToastIcon.Warn : Controls.AppToast.ToastIcon.Info,
            title, body, actions, AutoDismissMs: 20000,
            ModId: _operatingModId ?? _updateService.Profile.Id));
    }

    /// <summary>A game started outside the launcher counts too (by executable name).</summary>
    private static bool AnyGameProcessRunning(ModProfile profile)
    {
        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(profile.GameExecutable ?? "");
            if (string.IsNullOrEmpty(name)) return false;
            var procs = Process.GetProcessesByName(name);
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>The mod on screen; true when it started (or must wait for) an update, so the
    /// mods that are not open wait their turn.</summary>
    private async Task<bool> MaybeAutoUpdateActiveModAsync()
    {
        var facts = AutoUpdateFacts();
        var verdict = ModAutoUpdatePolicy.Decide(facts);
        if (verdict != ModAutoUpdatePolicy.Verdict.Go)
        {
            if (verdict is not (ModAutoUpdatePolicy.Verdict.NotEnabled or ModAutoUpdatePolicy.Verdict.NothingPending))
                DiagnosticLog.Write($"Automatic update of {_updateService.Profile.Id}: not now - {verdict}.");
            // Waiting for a game, a room or another operation: ask again later.
            if (verdict is ModAutoUpdatePolicy.Verdict.GameRunning or ModAutoUpdatePolicy.Verdict.InMultiplayer
                or ModAutoUpdatePolicy.Verdict.Busy)
            {
                ScheduleAutoUpdate(TimeSpan.FromMinutes(5));
                return true;
            }
            return false;
        }

        var key = AutoUpdateKey();
        _autoUpdateAttempts[key] = facts.Attempts + 1;
        DiagnosticLog.Write($"Automatic update of {_updateService.Profile.Id}: starting (attempt {facts.Attempts + 1}).");
        _automaticOperation = true;
        // Every question on the way answers "no" and every notice becomes a toast: nobody asked for this.
        using var unattended = Unattended.Begin(this);
        try
        {
            if (_updateService.Profile.UpdateMechanism == ModUpdateMechanism.GitHubReleases)
                await RepairInstallAsync(asUpdate: true);
            else
                await ApplyUpdateWithElevationCheckAsync();
        }
        finally
        {
            _automaticOperation = false;
        }
        if (_primaryAction != PrimaryAction.Update) _autoUpdateAttempts.Remove(key);
        if (_autoUpdateBehind.Count > 0) ScheduleAutoUpdate(TimeSpan.FromSeconds(60));
        return true;
    }
}
