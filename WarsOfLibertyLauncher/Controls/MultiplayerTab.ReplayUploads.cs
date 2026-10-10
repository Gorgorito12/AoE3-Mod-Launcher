using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Uploading the recording of a competitive match we reported, through
/// <see cref="ReplayUploadQueue"/> — so a server that could not take it, or a connection that
/// dropped, costs a delay rather than the recording.
///
/// <para><b>When the queue is tried.</b> Right after a recording is queued; once per session, as
/// soon as the account is signed in (which is what covers "the server was fixed while the
/// launcher was closed"); when the connection comes back; and on a one-minute timer that runs
/// only while something is waiting. All four go through <see cref="KickReplayUploads"/>, and the
/// queue lets one pass run at a time, so they cannot overlap.</para>
///
/// <para>It runs whatever tab is showing and while the window is in the tray: an upload is
/// background work the player never has to watch, and the queue's own back-off is what keeps it
/// cheap.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>Match ids whose recording was queued this session. The queue de-duplicates too;
    /// this spares re-reading a 2 MB file when two paths find the same recording.</summary>
    private readonly HashSet<string> _replayUploadStarted = new(StringComparer.Ordinal);

    /// <summary>Ticks while something is waiting for the signed-in account.</summary>
    private DispatcherTimer? _replayUploadTimer;

    /// <summary>The signed-in account the queue was last looked at for with the back-off ignored.
    /// A new session — or another account — looks again once.</summary>
    private string? _replayUploadsLookedFor;

    /// <summary>
    /// Queues the recording of a competitive match we REPORTED and starts uploading it.
    ///
    /// <para><see cref="ReplayUploadService.Decide"/> is the gate: a casual room, a player who
    /// switched sharing off, no file, a file never proved to be this match's, no match id. The
    /// server checks again that we are the reporter. Never delays the report or the card: the
    /// copy is made off the UI thread.</para>
    /// </summary>
    private void MaybeUploadReplayInBackground(
        MatchContext ctx, string? matchId, MatchReplayInfo? replay, string? reportedSha256, string why)
    {
        var decision = ReplayUploadService.Decide(
            ctx.IsCompetitive, _config?.ReplayUploadPolicy,
            replay?.File.Exists == true, replay?.Verified == true, matchId);
        if (decision != ReplayUploadService.ReplayUploadDecision.Upload)
        {
            DiagnosticLog.Write($"MultiplayerTab: recording not uploaded ({why}) - {decision}.");
            return;
        }

        var userId = _session?.CurrentUser?.Id;
        if (string.IsNullOrEmpty(userId))
        {
            DiagnosticLog.Write($"MultiplayerTab: recording not uploaded ({why}) - nobody is signed in.");
            return;
        }
        lock (_replayUploadStarted)
        {
            if (!_replayUploadStarted.Add(matchId!)) return;
        }

        var file = replay!.File;
        DiagnosticLog.Write($"MultiplayerTab: queuing the recording of match {matchId} ({why}).");
        _ = Task.Run(() =>
        {
            try { ReplayUploadQueue.Default.Enqueue(matchId!, userId, file, reportedSha256); }
            catch (Exception ex) { DiagnosticLog.Write($"MultiplayerTab: could not queue the recording - {ex.Message}"); }
            _ = Dispatcher.InvokeAsync(() => KickReplayUploads("a recording was queued", lookAgain: false));
        });
    }

    /// <summary>
    /// Called from every session pass. The first pass that finds an account signed in looks at
    /// the queue with the back-off ignored, once; signing out stops the timer.
    /// </summary>
    private void SyncReplayUploads()
    {
        var me = _session?.Status == MultiplayerSession.SessionStatus.SignedIn
            ? _session.CurrentUser?.Id
            : null;
        if (string.IsNullOrEmpty(me))
        {
            _replayUploadsLookedFor = null;
            StopReplayUploadTimer();
            return;
        }
        if (string.Equals(_replayUploadsLookedFor, me, StringComparison.Ordinal)) return;
        _replayUploadsLookedFor = me;
        KickReplayUploads("signed in", lookAgain: true);
    }

    /// <summary>
    /// Tries what is waiting, in the background. Cheap when nothing is due: the queue reads its
    /// index from memory and sends nothing.
    /// </summary>
    /// <param name="lookAgain">Try every waiting recording of this account now, whatever the
    /// back-off said — a new session or a connection that came back is new information.</param>
    private void KickReplayUploads(string why, bool lookAgain)
    {
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn)
        {
            StopReplayUploadTimer();
            return;
        }
        var api = _session.Api;
        var me = _session.CurrentUser?.Id;
        if (api == null || string.IsNullOrEmpty(me)) return;
        // Offline, every attempt would fail and spend a back-off step for nothing. The way back
        // online kicks the queue itself (SetOfflineMode).
        if (_offlineMode) return;

        var policy = _config?.ReplayUploadPolicy;
        var queue = ReplayUploadQueue.Default;
        if (ReplayUploadService.IsSharingEnabled(policy) && !queue.HasPendingFor(me))
        {
            StopReplayUploadTimer();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var pass = await queue.DrainAsync(
                    (entry, bytes, ct) => ReplayUploadService.UploadBytesAsync(
                        api, entry.MatchId, bytes, entry.Sha256, entry.SourceName, ct),
                    me, policy, lookAgain).ConfigureAwait(false);
                if (pass.Attempted > 0 || pass.Dropped > 0)
                    DiagnosticLog.Write(
                        $"MultiplayerTab: recording uploads ({why}) - tried {pass.Attempted}, "
                        + $"uploaded {pass.Uploaded}, forgotten {pass.Dropped}, waiting {pass.Waiting}.");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"MultiplayerTab: recording uploads ({why}) failed - {ex.Message}");
            }
            finally
            {
                _ = Dispatcher.InvokeAsync(UpdateReplayUploadTimer);
            }
        });
    }

    /// <summary>Runs the timer while something is waiting for the signed-in account, and only then.</summary>
    private void UpdateReplayUploadTimer()
    {
        var me = _session?.Status == MultiplayerSession.SessionStatus.SignedIn
            ? _session.CurrentUser?.Id
            : null;
        if (string.IsNullOrEmpty(me) || !ReplayUploadQueue.Default.HasPendingFor(me))
        {
            StopReplayUploadTimer();
            return;
        }
        if (_replayUploadTimer == null)
        {
            _replayUploadTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _replayUploadTimer.Tick += (_, _) => KickReplayUploads("timer", lookAgain: false);
        }
        if (!_replayUploadTimer.IsEnabled) _replayUploadTimer.Start();
    }

    private void StopReplayUploadTimer() => _replayUploadTimer?.Stop();
}
