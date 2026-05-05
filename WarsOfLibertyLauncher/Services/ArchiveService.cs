using System;
using System.Collections.Generic;
using System.IO;
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
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            Directory.CreateDirectory(backupFolder);

            var backedUp = new List<string>();

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

                    // Backup existing file before overwriting
                    if (File.Exists(destFile))
                    {
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
                }
            }
            catch (Exception)
            {
                // Restore everything we backed up so far
                progress?.Report(WarsOfLibertyLauncher.Localization.Strings.Get("StatusExtractFailedRestoring"));
                RestoreBackup(backupFolder, destinationFolder, backedUp);
                throw;
            }
        }, ct);
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
                var fullPath = Path.Combine(installPath, relativePath);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch
            {
                // Best-effort delete
            }
        }
    }
}
