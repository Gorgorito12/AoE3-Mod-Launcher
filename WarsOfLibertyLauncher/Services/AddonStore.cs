using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Keeps the launcher's own copy of every addon archive, under
/// <c>%LocalAppData%\AoE3ModLauncher\addons\</c>.
///
/// <b>Why copy instead of remembering where the user's file was.</b>
/// <see cref="AddonService.ReapplyAllAsync"/> needs the archive again after
/// every update and repair — those re-lay the overlay and wipe the addon, so the
/// only way to put it back is to extract it a second time. The file the user
/// picked lives in Downloads: they delete it, move it, or reinstall Windows, and
/// then the first update silently loses their addons with nothing in the UI
/// explaining why. A launcher-owned copy makes re-applying independent of what
/// the user does with their own files.
///
/// It also unifies the two sources: a catalog addon and an imported one both end
/// up here, so <c>ReapplyAllAsync</c> never has to know which is which.
/// </summary>
public static class AddonStore
{
    public static string RootDir => Path.Combine(AppPaths.DataDir, "addons");

    /// <summary>Prefix marking an addon the user imported rather than one from the catalog.</summary>
    public const string LocalIdPrefix = "local-";

    /// <summary>
    /// Content-derived id, so importing the same archive twice is recognised as
    /// the same addon instead of stacking duplicates the user then has to tell
    /// apart. Twelve hex chars is plenty to separate a handful of addons and
    /// keeps the on-disk name readable.
    /// </summary>
    public static string LocalIdFor(string sha256) =>
        LocalIdPrefix + (sha256 ?? "").Trim().ToLowerInvariant().PadRight(12, '0')[..12];

    public static string PathFor(string addonId) =>
        Path.Combine(RootDir, Sanitize(addonId) + ".zip");

    public static bool Has(string addonId) => File.Exists(PathFor(addonId));

    /// <summary>
    /// Resolves an archive for <see cref="AddonService.ReapplyAllAsync"/>.
    /// Returns null when the copy is missing, which that method treats as
    /// "skip this addon" rather than failing the update around it.
    /// </summary>
    public static Task<string?> ResolveAsync(string addonId, CancellationToken ct = default)
    {
        _ = ct;
        var path = PathFor(addonId);
        return Task.FromResult(File.Exists(path) ? path : null);
    }

    /// <summary>
    /// Copies <paramref name="sourceZip"/> into the store and returns its id.
    /// Re-importing an identical archive overwrites its own copy, which is a
    /// no-op in practice.
    /// </summary>
    public static async Task<string> ImportAsync(string sourceZip, CancellationToken ct = default)
    {
        var sha = await HashService.ComputeSha256Async(sourceZip, ct);
        var id = LocalIdFor(sha);

        Directory.CreateDirectory(RootDir);
        var dest = PathFor(id);
        await Task.Run(() => File.Copy(sourceZip, dest, overwrite: true), ct);

        DiagnosticLog.Write($"Addon store: imported '{Path.GetFileName(sourceZip)}' as {id}.");
        return id;
    }

    /// <summary>
    /// Adopts an already-downloaded archive (the catalog path) under the addon's
    /// catalog id, so both sources resolve identically later.
    /// </summary>
    public static async Task AdoptAsync(string addonId, string sourceZip, CancellationToken ct = default)
    {
        Directory.CreateDirectory(RootDir);
        var dest = PathFor(addonId);
        await Task.Run(() => File.Copy(sourceZip, dest, overwrite: true), ct);
    }

    /// <summary>
    /// Drops the stored copy. Called when the user removes an imported addon —
    /// not when they merely disable it, since disabling is reversible and
    /// re-enabling needs the archive back.
    /// </summary>
    public static void Remove(string addonId)
    {
        try
        {
            var path = PathFor(addonId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Addon store: could not remove {addonId}: {ex.Message}");
        }
    }

    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((id ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
