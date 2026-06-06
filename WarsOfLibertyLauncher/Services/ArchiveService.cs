using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Extracts .tar.xz update archives into the mod folder.
///
/// Replicates the safe-update flow of the original Java updater:
///   1. For each entry, if a file already exists at the destination, copy it
///      to a backup folder before overwriting.
///   2. If anything fails partway through, restore all files from the backup
///      so the install is left in a consistent state.
/// </summary>
/// <summary>
/// Live extract progress: bytes-read from the compressed source plus
/// the count of entries written so far. <c>BytesTotal</c> is the size
/// of the .tar.xz on disk; the bytes-read figure is the position of
/// the underlying stream after each entry. Not perfectly linear in
/// uncompressed terms but more than good enough to drive a progress bar.
/// </summary>
public record ArchiveExtractProgress(
    long BytesRead,
    long BytesTotal,
    int EntriesDone,
    string CurrentFile);

public class ArchiveService
{
    /// <summary>
    /// Extract a .tar.xz archive into <paramref name="destinationFolder"/>, with
    /// safety backups in <paramref name="backupFolder"/>.
    /// </summary>
    public Task ExtractTarXzWithBackupAsync(
        string archivePath,
        string destinationFolder,
        string backupFolder,
        IProgress<string>? progress = null,
        IProgress<ArchiveExtractProgress>? extractProgress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            Directory.CreateDirectory(backupFolder);

            // Two parallel lists drive the rollback:
            //   * backedUp  — files that ALREADY existed and were copied to
            //                 the backup folder before being overwritten.
            //   * created   — files that DID NOT exist before this patch (so
            //                 there's nothing to restore from backup; rolling
            //                 back means deleting them outright).
            // Without tracking `created`, a cancelled patch left brand-new
            // files dangling in the install — the data files ended up as a
            // mix of pre-patch + half-patched, causing "Version not
            // recognised" on the next run. Tracking both lists lets the
            // catch block return the install to its real pre-patch state.
            var backedUp = new List<string>();
            var created = new List<string>();
            long totalBytes = new FileInfo(archivePath).Length;
            int entriesDone = 0;

            try
            {
                using var stream = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(stream);

                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    if (reader.Entry.IsDirectory) continue;

                    var entryPath = reader.Entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    var destFile = Path.Combine(destinationFolder, entryPath);
                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    bool destExisted = File.Exists(destFile);
                    if (destExisted)
                    {
                        // Backup existing file before overwriting.
                        var backupFile = Path.Combine(backupFolder, entryPath);
                        var backupDir = Path.GetDirectoryName(backupFile);
                        if (!string.IsNullOrEmpty(backupDir))
                            Directory.CreateDirectory(backupDir);

                        File.Copy(destFile, backupFile, overwrite: true);
                        backedUp.Add(entryPath);
                    }

                    progress?.Report(WarsOfLibertyLauncher.Localization.Strings.Format("StatusExtracting", entryPath));

                    reader.WriteEntryToFile(destFile, new ExtractionOptions
                    {
                        Overwrite = true,
                        PreserveFileTime = false
                    });

                    // Track newly-introduced files so the rollback can
                    // delete them. Do this AFTER the write so we only mark
                    // files we actually managed to create (a write that
                    // failed mid-flight would leave the destFile partial
                    // and File.Exists may still be true; either way the
                    // rollback removes it).
                    if (!destExisted)
                        created.Add(entryPath);

                    entriesDone++;
                    // Reading the stream's Position is cheap and updates as
                    // SharpCompress consumes the .tar.xz. Report it after
                    // each entry so the UI bar advances throughout the
                    // extract phase instead of staying frozen.
                    extractProgress?.Report(new ArchiveExtractProgress(
                        BytesRead: stream.Position,
                        BytesTotal: totalBytes,
                        EntriesDone: entriesDone,
                        CurrentFile: entryPath));
                }
            }
            catch (Exception)
            {
                // Cancellation lands here too (OperationCanceledException
                // descends from Exception), so cancels also trigger a clean
                // rollback. Without this, the install was left half-patched
                // — exactly the state that produced "Version not recognised"
                // on relaunch.
                progress?.Report(WarsOfLibertyLauncher.Localization.Strings.Get("StatusExtractFailedRestoring"));
                RestoreBackup(backupFolder, destinationFolder, backedUp);
                DeleteCreated(destinationFolder, created);
                throw;
            }
        }, ct);
    }

    /// <summary>
    /// Removes files that this patch CREATED (didn't exist in the install
    /// before extraction started). Pairs with <see cref="RestoreBackup"/>:
    /// together they undo every effect of a partial extraction so the
    /// install returns to its pre-patch state.
    /// </summary>
    private static void DeleteCreated(
        string destinationFolder,
        IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            try
            {
                var path = Path.Combine(destinationFolder, relativePath);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup; one failed delete shouldn't stop the rest.
            }
        }
    }

    /// <summary>
    /// Restore files from the backup folder back into the install.
    /// Used both on failure and after successful update (to clean up the backup).
    /// </summary>
    public static void RestoreBackup(
        string backupFolder,
        string destinationFolder,
        IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            try
            {
                var src = Path.Combine(backupFolder, relativePath);
                var dst = Path.Combine(destinationFolder, relativePath);
                if (File.Exists(src))
                    File.Copy(src, dst, overwrite: true);
            }
            catch
            {
                // Best-effort restore; one failed file shouldn't stop the rest.
            }
        }
    }

    /// <summary>
    /// Same idea as <see cref="ExtractTarXzWithBackupAsync"/> but for the
    /// regular <c>.zip</c> archives that the GitHub-Releases update flow
    /// downloads. Backs up any file it's about to overwrite, tracks
    /// newly-created files so a cancel/failure mid-extract leaves the
    /// install in its pre-patch state.
    ///
    /// Uses <see cref="System.IO.Compression.ZipArchive"/> (.NET built-in)
    /// rather than SharpCompress; the .zip format is well-supported and
    /// dragging SharpCompress in just for this would be overkill.
    /// </summary>
    public Task ExtractZipWithBackupAsync(
        string archivePath,
        string destinationFolder,
        string backupFolder,
        IProgress<string>? progress = null,
        IProgress<ArchiveExtractProgress>? extractProgress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            Directory.CreateDirectory(backupFolder);

            // Parallel lists drive the rollback — same scheme as the
            // tar.xz path. See the lengthier explanation there.
            var backedUp = new List<string>();
            var created = new List<string>();
            long totalBytes = new FileInfo(archivePath).Length;
            int entriesDone = 0;

            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
                long bytesEstimate = 0;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    // Directory entry (zip stores them with trailing slash
                    // and empty Name). Skip — they're created implicitly
                    // when we write a file inside them.
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var entryPath = entry.FullName;

                    // Zip-slip defence: reject entries that try to escape
                    // the destination folder. ZipArchive happily yields
                    // an entry with FullName "../../etc/passwd" if the
                    // archive was crafted that way; we don't.
                    var destFile = Path.GetFullPath(Path.Combine(destinationFolder, entryPath));
                    var destRoot = Path.GetFullPath(destinationFolder)
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!destFile.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        DiagnosticLog.Write(
                            $"Zip-slip: rejecting entry '{entryPath}' that would escape '{destinationFolder}'.");
                        continue;
                    }

                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    bool destExisted = File.Exists(destFile);
                    if (destExisted)
                    {
                        // Backup the pre-existing file before overwriting.
                        var backupFile = Path.Combine(backupFolder, entryPath);
                        var backupDir = Path.GetDirectoryName(backupFile);
                        if (!string.IsNullOrEmpty(backupDir))
                            Directory.CreateDirectory(backupDir);
                        File.Copy(destFile, backupFile, overwrite: true);
                        backedUp.Add(entryPath);
                    }

                    progress?.Report(WarsOfLibertyLauncher.Localization.Strings.Format(
                        "StatusExtracting", entryPath));

                    entry.ExtractToFile(destFile, overwrite: true);

                    if (!destExisted)
                        created.Add(entryPath);

                    entriesDone++;
                    // ZipArchive doesn't expose a running compressed-byte
                    // position cleanly, so we synthesise progress from
                    // uncompressed sizes — close enough for a UI bar that
                    // just wants something monotonic.
                    bytesEstimate += entry.CompressedLength;
                    extractProgress?.Report(new ArchiveExtractProgress(
                        BytesRead: Math.Min(bytesEstimate, totalBytes),
                        BytesTotal: totalBytes,
                        EntriesDone: entriesDone,
                        CurrentFile: entryPath));
                }
            }
            catch (Exception)
            {
                progress?.Report(WarsOfLibertyLauncher.Localization.Strings.Get("StatusExtractFailedRestoring"));
                RestoreBackup(backupFolder, destinationFolder, backedUp);
                DeleteCreated(destinationFolder, created);
                throw;
            }
        }, ct);
    }

    /// <summary>Apply a delete-list (a text file with one relative path per line).</summary>
    public static void ApplyDeleteList(string installPath, string deleteListContent)
    {
        if (string.IsNullOrWhiteSpace(deleteListContent)) return;

        var lines = deleteListContent.Split(new[] { "\r\n", "\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in lines)
        {
            var relativePath = raw.Trim().TrimStart('\\', '/');
            if (string.IsNullOrEmpty(relativePath)) continue;

            try
            {
                // Path-traversal defence: the delete-list comes from a remote
                // UpdateInfo and could contain "..\" segments; clamp each target
                // to installPath so a crafted list can't delete files elsewhere.
                var fullPath = Path.GetFullPath(Path.Combine(installPath, relativePath));
                var installRoot = Path.GetFullPath(installPath)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Write(
                        $"Delete-list: rejecting '{relativePath}' that would escape '{installPath}'.");
                    continue;
                }
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch
            {
                // Best-effort delete
            }
        }
    }

    /// <summary>
    /// Resolves a download's <c>deleteList</c> reference to its text content.
    /// The official WoL format is an install-RELATIVE path to a file the patch
    /// shipped (e.g. <c>etc\1013c_delete.lst</c>), read locally. Callers handle
    /// the <c>http(s)://</c> URL fallback. Returns "" when the path is empty or
    /// the local file is absent (a missing list is a no-op, like the original
    /// updater).
    /// </summary>
    public static string ReadLocalDeleteList(string installPath, string deleteListRef)
    {
        if (string.IsNullOrWhiteSpace(deleteListRef)) return "";
        var local = Path.Combine(installPath, deleteListRef.TrimStart('\\', '/'));
        return File.Exists(local) ? File.ReadAllText(local) : "";
    }
}
