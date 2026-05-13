using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Locates a newly-written AoE3 replay and uploads it to the lobby
/// Worker. Called by <see cref="MultiplayerSession"/> after a match
/// completes (or by the host after <c>POST /matches</c> returns a
/// match id).
///
/// AoE3 (2007) writes replays into
/// <c>%USERPROFILE%\Documents\My Games\&lt;mod&gt;\Savegame\</c> with
/// the extension <c>.age3yrec</c> — same path as the game's regular
/// save dir. The convention is to take the newest file written after
/// the match started; we keep the trigger explicit (not a continuous
/// watcher) so the user can opt out of uploading a given replay by
/// simply deleting it before clicking "upload" in the post-game UI.
/// </summary>
public static class ReplayUploadService
{
    /// <summary>
    /// Find the most recently written replay inside the mod's user-data
    /// folder, filtered to only files created after <paramref name="afterUtc"/>.
    /// Returns null when no such file exists, e.g. the user aborted out
    /// before the engine flushed the recording.
    /// </summary>
    public static FileInfo? FindLatestReplay(string userDataDir, DateTime afterUtc)
    {
        try
        {
            if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
                return null;

            // AoE3 stores replays under "Savegame" by convention. Some
            // mods (e.g. WoL) keep the same layout. If the folder is
            // missing, fall back to a recursive search — slower but
            // robust to mod-specific paths.
            var saveDir = Path.Combine(userDataDir, "Savegame");
            var searchRoot = Directory.Exists(saveDir) ? saveDir : userDataDir;

            var candidates = new DirectoryInfo(searchRoot)
                .EnumerateFiles("*.age3yrec", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc >= afterUtc)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(1)
                .ToList();

            return candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadService.FindLatestReplay: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Upload a replay file to the Worker for the given match id.
    /// Enforces the size cap up front so a 500 MB recording from a
    /// 4-hour FFA isn't streamed across the network just to be
    /// rejected at the end. Returns the server-side object key on
    /// success, or null on error (already logged).
    /// </summary>
    public static async Task<string?> UploadAsync(
        LobbyApiClient api,
        string matchId,
        FileInfo replayFile,
        CancellationToken ct = default)
    {
        if (api == null) throw new ArgumentNullException(nameof(api));
        if (replayFile == null || !replayFile.Exists)
        {
            DiagnosticLog.Write("ReplayUploadService.UploadAsync: file missing");
            return null;
        }

        try
        {
            var handle = await api.RequestReplayUploadAsync(matchId, ct);
            if (replayFile.Length > handle.MaxBytes)
            {
                DiagnosticLog.Write(
                    $"ReplayUploadService: replay {replayFile.Length} > cap {handle.MaxBytes}, skipping");
                return null;
            }

            await using var stream = replayFile.OpenRead();
            await api.UploadReplayAsync(handle.UploadUrl, stream, replayFile.Length, ct);
            return handle.UploadUrl;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ReplayUploadService.UploadAsync: {ex.Message}");
            return null;
        }
    }
}
