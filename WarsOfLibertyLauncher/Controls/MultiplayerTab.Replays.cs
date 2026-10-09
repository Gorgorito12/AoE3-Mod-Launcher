using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Downloading a competitive recording — from the Ranking rows (handoff 63) and from a Profile
/// history card — straight into the mod's <c>Savegame</c> folder, with no "Save as" dialog.
///
/// <para><b>The bytes come from the storage bucket, never through the lobby server</b>: the
/// server answers a 10-minute signed link (<c>GET /matches/:id/replay-url</c>) and the launcher
/// refuses anything but an absolute https URL, the same rule the upload follows.</para>
///
/// <para><b>One download per match, whatever is showing it.</b> A row can be rebuilt while its
/// recording downloads (the ranking re-renders on every payload), and the same match can sit in
/// «Latest matches» and in the Matches view at once — so the state lives HERE, keyed by match id,
/// and every live button for that match is repainted from it. A button built mid-download picks
/// the state and the progress up on construction.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>What a match's recording is doing right now, for the buttons being drawn.</summary>
    private readonly Dictionary<string, (ReplayButtonState State, double? Progress)> _replayStates = new(StringComparer.Ordinal);

    /// <summary>Every live button per match id; dead ones are pruned when the match next changes.</summary>
    private readonly Dictionary<string, List<WeakReference<ReplayDownloadButton>>> _replayButtons = new(StringComparer.Ordinal);

    /// <summary>Where this session saved each recording, beside the index on disk.</summary>
    private readonly Dictionary<string, string> _replaySavedPaths = new(StringComparer.Ordinal);

    private readonly HashSet<string> _replayInFlight = new(StringComparer.Ordinal);

    /// <summary>What a download needs to know about its match.</summary>
    internal sealed record ReplayTarget(string MatchId, string ModId, string MatchLine, long? SizeBytes);

    /// <summary>
    /// The right column's recording cell for one match row, or null when the match has none
    /// (casual, no result, a competitive match nobody recorded): the space stays empty.
    /// </summary>
    internal FrameworkElement? BuildReplayCell(CommunityMatch m)
    {
        var availability = ReplayBrowse.Decide(m, DateTime.UtcNow);
        if (availability == ReplayAvailability.None && !_replayStates.ContainsKey(m.Id)) return null;

        var target = TargetOf(m);
        var button = new ReplayDownloadButton(m.Id, Strings.Get("MpReplayExpired"), state => ReplayTip(state, target));
        var (state, progress) = InitialReplayState(m.Id, availability);
        button.SetState(state, progress);
        RegisterReplayButton(button);
        button.Click += async (_, _) => await OnReplayButtonClickAsync(button, target);
        return button;
    }

    private static ReplayTarget TargetOf(CommunityMatch m)
        => new(m.Id, m.ModId,
            ReplayBrowse.MatchLine(MatchParticipantsView.Build(m.Participants, null), m.MapName, Strings.Get("MpActivityVersus")),
            m.ReplaySizeBytes);

    private (ReplayButtonState, double?) InitialReplayState(string matchId, ReplayAvailability availability)
    {
        if (_replayStates.TryGetValue(matchId, out var live)) return live;
        if (availability == ReplayAvailability.Expired) return (ReplayButtonState.Expired, null);
        return (IsReplaySaved(matchId) ? ReplayButtonState.Done : ReplayButtonState.Idle, null);
    }

    /// <summary>The recording of this match is on disk, saved by this session or an earlier one.</summary>
    private bool IsReplaySaved(string matchId) => TryGetSavedReplay(matchId, out _);

    private bool TryGetSavedReplay(string matchId, out string path)
    {
        if (_replaySavedPaths.TryGetValue(matchId, out var p) && File.Exists(p))
        {
            path = p;
            return true;
        }
        return ReplayDownloadIndex.Default.TryGetDownloaded(matchId, out path);
    }

    private void RegisterReplayButton(ReplayDownloadButton button)
    {
        if (!_replayButtons.TryGetValue(button.MatchId, out var list))
            _replayButtons[button.MatchId] = list = new List<WeakReference<ReplayDownloadButton>>();
        list.RemoveAll(w => !w.TryGetTarget(out _));
        list.Add(new WeakReference<ReplayDownloadButton>(button));
    }

    /// <summary>Record a match's state and repaint every button showing it.</summary>
    private void SetReplayState(string matchId, ReplayButtonState state, double? progress = null)
    {
        // Idle and Done are what a fresh button works out on its own (the index says Done);
        // only the states a rebuilt row cannot know are kept.
        if (state is ReplayButtonState.Idle) _replayStates.Remove(matchId);
        else _replayStates[matchId] = (state, progress);

        if (!_replayButtons.TryGetValue(matchId, out var list)) return;
        list.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in list)
            if (weak.TryGetTarget(out var button)) button.SetState(state, progress);
    }

    private async Task OnReplayButtonClickAsync(ReplayDownloadButton button, ReplayTarget target)
    {
        switch (button.State)
        {
            case ReplayButtonState.Downloading:
            case ReplayButtonState.Expired:
                return;
            case ReplayButtonState.Done:
                // The second click shows the file, with no notice (handoff 63). A file that is
                // gone since — deleted, renamed — makes the button a download again.
                if (TryGetSavedReplay(target.MatchId, out var path))
                {
                    FileReveal.Reveal(path);
                    return;
                }
                _replaySavedPaths.Remove(target.MatchId);
                SetReplayState(target.MatchId, ReplayButtonState.Idle);
                break;
        }
        await DownloadReplayAsync(target, Window.GetWindow(button));
    }

    /// <summary>"Download recording" on a Profile history card: the same download, a text button.</summary>
    private async Task DownloadHistoryReplayAsync(MatchHistoryRow row, Button button)
    {
        if (string.IsNullOrEmpty(row.Id)) return;
        if (TryGetSavedReplay(row.Id, out var saved))
        {
            FileReveal.Reveal(saved);
            return;
        }

        var target = new ReplayTarget(row.Id, row.ModId,
            ReplayBrowse.MatchLine(MatchParticipantsView.Build(row.Participants, null), row.MapName, Strings.Get("MpActivityVersus")),
            null);
        button.IsEnabled = false;
        button.Content = Strings.Get("MpHistoryDownloading");
        try
        {
            await DownloadReplayAsync(target, Window.GetWindow(button));
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = Strings.Get(IsReplaySaved(row.Id) ? "MpHistoryShowReplay" : "MpHistoryDownloadReplay");
        }
    }

    /// <summary>
    /// Ask for the link, check it, save into the mod's <c>Savegame</c> without asking where
    /// (a name already taken gets " (2)"; the very same file already there is not downloaded
    /// again), check the disk, download with progress, remember it, and say so. Never throws.
    /// </summary>
    private async Task DownloadReplayAsync(ReplayTarget target, Window? owner)
    {
        var id = target.MatchId;
        if (string.IsNullOrEmpty(id) || !_replayInFlight.Add(id)) return;

        string? path = null;
        try
        {
            var api = _session?.Api;
            if (api == null || _session?.Status != MultiplayerSession.SessionStatus.SignedIn)
            {
                ShowReplayToast(AppToast.ToastTone.Error, "MpReplayDownloadFailedTitle", Strings.Get("MpReplaySignInFirst"), target);
                return;
            }

            SetReplayState(id, ReplayButtonState.Downloading);

            ReplayDownloadLink link;
            try
            {
                link = await api.GetReplayDownloadAsync(id);
            }
            catch (LobbyApiException ex)
            {
                DiagnosticLog.Write($"MultiplayerTab: no download link for match {id} - HTTP {ex.Status} {ex.Code}: {ex.Message}");
                if (ex.Code == "no_replay")
                {
                    // The year is over (the bucket deleted it) between the list and the click.
                    SetReplayState(id, ReplayButtonState.Expired);
                    ShowReplayToast(AppToast.ToastTone.Error, "MpReplayGoneTitle", Strings.Get("MpReplayGoneBody"), target);
                }
                else if (ex.Status is 404 or 503)
                {
                    SetReplayState(id, ReplayButtonState.Error);
                    ShowReplayToast(AppToast.ToastTone.Error, "MpReplayDownloadFailedTitle",
                        Strings.Get("MpReplayDownloadUnavailable"), target);
                }
                else
                {
                    ReplayFailed(target, owner);
                }
                return;
            }

            if (!ReplayUploadService.IsAcceptableStorageUrl(link.Url))
            {
                DiagnosticLog.Write($"MultiplayerTab: the server's download link for match {id} is not a storage URL - refused.");
                SetReplayState(id, ReplayButtonState.Error);
                ShowReplayToast(AppToast.ToastTone.Error, "MpReplayDownloadFailedTitle",
                    Strings.Get("MpReplayDownloadUnavailable"), target);
                return;
            }

            var folder = ReplaySaveFolderFor(target.ModId);
            var name = ReplayUploadService.SafeReplayFileName(link.FileName, id);

            // The same recording already there under its own name — downloaded before this index
            // existed, or copied by hand — is adopted rather than copied a second time.
            var existing = Path.Combine(folder, name);
            if (link.SizeBytes > 0 && File.Exists(existing) && new FileInfo(existing).Length == link.SizeBytes)
            {
                ReplaySaved(target, existing);
                return;
            }

            path = ReplayBrowse.UniqueReplayPath(folder, name, File.Exists);
            var shortfall = DiskSpaceService.Check(folder, link.SizeBytes, tempPath: null, tempRequired: 0);
            if (!DiskSpacePrompt.ConfirmOrCancel(owner, shortfall, "DiskSpaceConfirmDownloadBody"))
            {
                SetReplayState(id, ReplayButtonState.Idle);
                path = null;
                return;
            }

            var expected = link.SizeBytes;
            var progress = new Progress<DownloadProgress>(p =>
            {
                var total = p.TotalBytes > 0 ? p.TotalBytes : expected;
                SetReplayState(id, ReplayButtonState.Downloading,
                    total > 0 ? (double)p.BytesReceived / total : null);
            });
            await new DownloadService().DownloadFileAsync(link.Url, path, progress);
            DiagnosticLog.Write($"MultiplayerTab: saved the recording of match {id} to '{path}'.");
            ReplaySaved(target, path);
            path = null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab: recording download for match {id} failed - {ex.Message}");
            ReplayFailed(target, owner);
        }
        finally
        {
            // DownloadService keeps a ".part" to resume from; half a recording in the player's
            // Savegame folder is only clutter.
            if (path != null)
            {
                try { File.Delete(path + ".part"); } catch (Exception) { /* best-effort */ }
            }
            _replayInFlight.Remove(id);
        }
    }

    private void ReplaySaved(ReplayTarget target, string path)
    {
        _replaySavedPaths[target.MatchId] = path;
        ReplayDownloadIndex.Default.Record(target.MatchId, path);
        SetReplayState(target.MatchId, ReplayButtonState.Done);

        // Which notice: the mod is here (green), installed nowhere here, or not in the launcher
        // at all — then the file went to Documents, since nothing says where its folder is.
        var profile = ModRegistry.Find(target.ModId);
        var modName = profile?.DisplayName ?? target.ModId;
        var (tone, body) = profile == null
            ? (AppToast.ToastTone.Info, Strings.Format("MpReplaySavedUnknownMod", modName))
            : !IsModInstalledLocally(target.ModId)
                ? (AppToast.ToastTone.Info, Strings.Format("MpReplaySavedNotInstalled", modName))
                : (AppToast.ToastTone.Ok, Strings.Format("MpReplaySavedBody", modName));
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "",
            Strings.Get("MpReplaySavedTitle"),
            body,
            new[] { new AppToast.ToastAction(Strings.Get("MpReplayShowInFolder"), true, () => FileReveal.Reveal(path)) },
            AutoDismissMs: 8000,
            Tone: tone,
            Subtitle: target.MatchLine));
    }

    /// <summary>The connection failed: the button offers a retry, and so does a notice that stays.</summary>
    private void ReplayFailed(ReplayTarget target, Window? owner)
    {
        SetReplayState(target.MatchId, ReplayButtonState.Error);
        _showAppToast?.Invoke(new AppToast.ToastOptions(
            "",
            Strings.Get("MpReplayDownloadFailedTitle"),
            Strings.Get("MpReplayDownloadFailed"),
            new[] { new AppToast.ToastAction(Strings.Get("MpReplayRetry"), true, () => _ = DownloadReplayAsync(target, owner)) },
            AutoDismissMs: 0,
            Tone: AppToast.ToastTone.Error,
            Subtitle: target.MatchLine));
    }

    private void ShowReplayToast(AppToast.ToastTone tone, string titleKey, string body, ReplayTarget target)
        => _showAppToast?.Invoke(new AppToast.ToastOptions(
            "",
            Strings.Get(titleKey),
            body,
            Array.Empty<AppToast.ToastAction>(),
            AutoDismissMs: 8000,
            Tone: tone,
            Subtitle: target.MatchLine));

    /// <summary>The button's tooltip for a state (handoff 63's table); the mod by its own name.</summary>
    private (string Title, string? Body) ReplayTip(ReplayButtonState state, ReplayTarget target)
    {
        var modName = ModRegistry.Find(target.ModId)?.DisplayName ?? target.ModId;
        return state switch
        {
            ReplayButtonState.Downloading => (Strings.Get("MpReplayTipDownloading"), null),
            ReplayButtonState.Done => (Strings.Get("MpReplayTipDoneTitle"), Strings.Get("MpReplayTipDoneBody")),
            ReplayButtonState.Error => (Strings.Get("MpReplayTipErrorTitle"), Strings.Get("MpReplayTipErrorBody")),
            _ => (target.SizeBytes is long bytes && bytes > 0
                    ? Strings.Format("MpReplayTipTitle", ReplayBrowse.SizeText(bytes, Strings.Culture))
                    : Strings.Get("MpReplayTipTitleNoSize"),
                  Strings.Format("MpReplayTipBody", modName)),
        };
    }

    /// <summary>
    /// Where a downloaded recording is saved: the mod's own <c>My Games\&lt;mod&gt;\Savegame</c>,
    /// the folder AoE3's "Load recorded game" lists, CREATED when missing — AoE3 lists only that
    /// folder, so saving beside it would put the file where the game cannot find it. Through
    /// <see cref="UserDataService.ResolveMatchFolderName"/>, the same door the recording search
    /// uses, so the base game resolves to its own folder too. A mod with no known folder (or no
    /// profile at all) falls back to Documents.
    /// </summary>
    private string ReplaySaveFolderFor(string? modId)
    {
        try
        {
            var profile = ModRegistry.Find(modId);
            if (profile != null && _config != null)
            {
                var folderName = UserDataService.ResolveMatchFolderName(profile, _config);
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    var folder = UserDataService.GetUserDataFolder(folderName);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var savegame = Path.Combine(folder, "Savegame");
                        Directory.CreateDirectory(savegame);
                        return savegame;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"MultiplayerTab: could not resolve the Savegame folder for '{modId}' - {ex.Message}");
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
