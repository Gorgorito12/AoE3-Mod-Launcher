using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// An advisory record that an operation rewriting an install has STARTED and not yet finished.
///
/// <para><b>Why.</b> A repair or update overwrites files in place and records the result in the
/// manifest last, so a crash, a power cut or a killed launcher in between leaves a mix of old and
/// new files under the OLD manifest — which PLAY launches happily, and which can desync a match.
/// Nothing on disk says it happened. This entry is written before the first file and removed
/// once the manifest is saved, so the next launch can say "the last repair was interrupted" and
/// offer to run it again.</para>
///
/// <para><b>Where.</b> Under <see cref="AppPaths.DataDir"/>, never inside the install:
/// <see cref="ModInstallProbe.InstallInProgressMarker"/> lives there and BLOCKS adoption of the
/// folder, which would strand a working install. This one only informs.</para>
/// </summary>
internal static class OperationJournal
{
    internal sealed record Entry(string Operation, string ModId, string InstallPath, DateTime StartedUtc);

    private static string Folder => Path.Combine(AppPaths.DataDir, "journal");

    /// <summary>Stable key for an install folder: case, separators and a trailing slash don't matter.</summary>
    internal static string KeyFor(string installPath)
    {
        string normalized;
        try { normalized = Path.GetFullPath(installPath); }
        catch { normalized = installPath ?? ""; }
        normalized = normalized.TrimEnd('\\', '/').ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }

    private static string PathFor(string installPath) => Path.Combine(Folder, KeyFor(installPath) + ".json");

    /// <summary>Records that <paramref name="operation"/> is about to write into the install. Never throws.</summary>
    internal static void Begin(string operation, string modId, string installPath)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(new Entry(operation, modId, installPath, DateTime.UtcNow));
            File.WriteAllText(PathFor(installPath), json);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Operation journal: could not record start for '{installPath}': {ex.Message}");
        }
    }

    /// <summary>The operation finished (its manifest is saved) or the install is gone. Never throws.</summary>
    internal static void Clear(string installPath)
    {
        try
        {
            var p = PathFor(installPath);
            if (File.Exists(p)) File.Delete(p);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Operation journal: could not clear '{installPath}': {ex.Message}");
        }
    }

    /// <summary>An unfinished operation on this install, or null. A corrupt entry counts as none.</summary>
    internal static Entry? TryGetOpen(string installPath)
    {
        try
        {
            var p = PathFor(installPath);
            if (!File.Exists(p)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(p));
            if (entry == null || !ModState.PathEquals(entry.InstallPath, installPath)) return null;
            return entry;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Operation journal: unreadable entry for '{installPath}' ignored: {ex.Message}");
            return null;
        }
    }
}
