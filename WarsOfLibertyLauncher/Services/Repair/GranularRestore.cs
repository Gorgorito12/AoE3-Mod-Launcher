using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>
/// Restores damaged mod files ONE BY ONE from the payload, fetching only their bytes over HTTP
/// ranges — the acting half of <see cref="GranularPlanner"/>. Gated: it acts only for a plan every
/// file of which is coverable (<see cref="MayAct"/>), and today only in developer mode, while the
/// shadow numbers from ordinary repairs are gathered.
///
/// <para><b>Nothing is written until everything is proven.</b> Phase one fetches, inflates and checks
/// every file's size AND SHA-256 against the manifest into a staging file beside it; one failure
/// deletes all staging and returns null, and the caller does the ordinary full re-lay. Only then does
/// phase two back each original up and swap the staged file in, rolling the swapped ones back if a
/// later swap fails. The trust anchor is the manifest's fingerprint, never the remote CRC.</para>
/// </summary>
internal static class GranularRestore
{
    internal const long MaxFileBytes = 256L * 1024 * 1024;
    internal const long MaxTotalBytes = 1024L * 1024 * 1024;
    internal const int MaxFiles = 500;
    private const int KeepBackupSets = 5;

    /// <summary>
    /// Whether a plan may act: every damaged file coverable, no structural finding (a missing folder
    /// or key file is the full re-lay's job), bounded in count and bytes, and clearly cheaper than
    /// the full download — a quarter or less, when its size is known.
    /// </summary>
    internal static bool MayAct(GranularPlan plan, long fullBytes, int structuralFindings)
    {
        if (structuralFindings > 0 || !plan.AllCoverable) return false;
        if (plan.Items.Count > MaxFiles || plan.EstimatedBytes > MaxTotalBytes) return false;
        if (plan.Items.Any(i => i.Entry == null || i.Entry.CompressedSize > MaxFileBytes || i.Entry.Size > MaxFileBytes))
            return false;
        return fullBytes <= 0 || plan.EstimatedBytes * 4 <= fullBytes;
    }

    /// <summary>The restored install-relative paths, or null when nothing was changed.</summary>
    internal static async Task<IReadOnlyList<string>?> RestoreAsync(
        RemotePayloadIndex.RangeReader read,
        string installPath,
        GranularPlan plan,
        IReadOnlyDictionary<string, FileFingerprint> manifestHashes,
        string backupRoot,
        Action? beforeFirstWrite,
        CancellationToken ct)
    {
        var hashes = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in manifestHashes) hashes[kv.Key.Replace('\\', '/')] = kv.Value;

        var root = Path.GetFullPath(installPath).TrimEnd('\\', '/');
        var staged = new List<(string Rel, string Dest, string Staging)>();
        try
        {
            // ---- phase 1: fetch, inflate, prove, stage ----
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Verdict != GranularVerdict.Coverable || item.Entry == null
                    || !hashes.TryGetValue(item.Path, out var expected))
                    return Fail(staged, $"'{item.Path}' is not coverable");

                var dest = Path.GetFullPath(Path.Combine(root, item.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return Fail(staged, $"'{item.Path}' resolves outside the install");

                var bytes = await ReadEntryAsync(read, item.Entry, ct);
                if (bytes == null) return Fail(staged, $"'{item.Path}' could not be read from the payload");
                if (bytes.LongLength != expected.Size
                    || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    return Fail(staged, $"'{item.Path}' in the payload is not the recorded file");

                var staging = dest + EngineRestore.StagingSuffix;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await File.WriteAllBytesAsync(staging, bytes, ct);
                staged.Add((item.Path, dest, staging));
            }
        }
        catch (OperationCanceledException)
        {
            Cleanup(staged);
            throw;
        }
        catch (Exception ex)
        {
            return Fail(staged, ex.Message);
        }

        // ---- phase 2: back up and swap ----
        beforeFirstWrite?.Invoke();
        var swapped = new List<(string Dest, string? Backup)>();
        try
        {
            foreach (var (rel, dest, staging) in staged)
            {
                string? backup = null;
                if (File.Exists(dest))
                {
                    backup = Path.Combine(backupRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(dest, backup, overwrite: true);
                }
                try
                {
                    File.Move(staging, dest);
                }
                catch
                {
                    if (backup != null) File.Move(backup, dest, overwrite: true);
                    throw;
                }
                swapped.Add((dest, backup));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Granular restore: swap failed, rolling back {swapped.Count} file(s): {ex.Message}");
            foreach (var (dest, backup) in swapped.AsEnumerable().Reverse())
            {
                try
                {
                    if (backup != null) File.Move(backup, dest, overwrite: true);
                    else File.Delete(dest);
                }
                catch (Exception rollbackEx)
                {
                    DiagnosticLog.Write($"Granular restore: could not roll back '{dest}': {rollbackEx.Message}");
                }
            }
            Cleanup(staged);
            return null;
        }

        PruneBackups(Path.GetDirectoryName(backupRoot));
        return staged.Select(s => s.Rel).ToList();
    }

    /// <summary>One entry's bytes: its local header says where the data starts; stored or deflated.</summary>
    internal static async Task<byte[]?> ReadEntryAsync(
        RemotePayloadIndex.RangeReader read, PayloadEntry entry, CancellationToken ct)
    {
        if (entry.CompressedSize > MaxFileBytes || entry.Size > MaxFileBytes) return null;
        var header = await read(entry.LocalHeaderOffset, 30, ct);
        if (header == null || header.Length != 30
            || header[0] != 0x50 || header[1] != 0x4B || header[2] != 0x03 || header[3] != 0x04)
            return null;
        int nameLen = BitConverter.ToUInt16(header, 26);
        int extraLen = BitConverter.ToUInt16(header, 28);
        var data = await read(entry.LocalHeaderOffset + 30 + nameLen + extraLen, (int)entry.CompressedSize, ct);
        if (data == null) return null;

        if (entry.Method == 0) return data;
        if (entry.Method != 8) return null;
        using var input = new MemoryStream(data);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(entry.Size > 0 ? (int)entry.Size : 0);
        await inflate.CopyToAsync(output, ct);
        return output.ToArray();
    }

    private static IReadOnlyList<string>? Fail(List<(string Rel, string Dest, string Staging)> staged, string why)
    {
        DiagnosticLog.Write($"Granular restore: not acting — {why}. The full repair runs instead.");
        Cleanup(staged);
        return null;
    }

    private static void Cleanup(List<(string Rel, string Dest, string Staging)> staged)
    {
        foreach (var s in staged)
            try { if (File.Exists(s.Staging)) File.Delete(s.Staging); } catch { }
    }

    private static void PruneBackups(string? installBackupFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(installBackupFolder) || !Directory.Exists(installBackupFolder)) return;
            foreach (var old in new DirectoryInfo(installBackupFolder).GetDirectories()
                         .OrderByDescending(d => d.Name, StringComparer.Ordinal).Skip(KeepBackupSets))
                old.Delete(recursive: true);
        }
        catch { }
    }
}
