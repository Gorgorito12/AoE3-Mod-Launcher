using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Records which files each enabled addon owns, in a small sidecar next to the
/// backups it is always used with: <c>&lt;install&gt;\addons\_owned.json</c>.
///
/// <b>Why not the install manifest, where this used to live.</b> Addons apply to
/// any Age of Empires III install, including the player's own unmodded copy — and
/// that copy has no <c>install-manifest.json</c>, because the launcher never
/// installed it. Requiring one made every addon fail there.
///
/// Writing a manifest into the real game folder to fix that would be worse than
/// the bug: <see cref="AoE3Detector.IsCleanAoE3Folder"/> treats the presence of
/// <c>install-manifest.json</c> as "this is a mod install, not a clone source", so
/// the launcher would quietly stop offering the player's own AoE3 as the base for
/// installing new mods. A separate record avoids the question entirely.
///
/// The manifest is still used where it exists — it is what keeps "Verify files"
/// honest about a modded install — but it is no longer required.
/// </summary>
public static class AddonOwnership
{
    public const string FileName = "_owned.json";

    public static string PathFor(string installPath) =>
        Path.Combine(installPath, AddonService.BackupFolderName, FileName);

    /// <summary>
    /// Reads the record, absorbing anything an older build left in the install
    /// manifest.
    ///
    /// The migration is not optional: addons enabled before this change recorded
    /// their files only in <c>InstallManifest.AddonFiles</c>, and without picking
    /// those up they would become impossible to disable — the launcher would show
    /// them as on with no idea which files to restore.
    /// </summary>
    public static Dictionary<string, List<string>> Load(string installPath)
    {
        var path = PathFor(installPath);

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (loaded != null) return Normalize(loaded);
            }
        }
        catch (Exception ex)
        {
            // A corrupt record must not make an install unmanageable; fall through
            // to the manifest, which is the same data one build older.
            DiagnosticLog.Write($"Addon ownership: could not read {path}: {ex.Message}");
        }

        var legacy = InstallManifest.TryLoad(installPath)?.AddonFiles;
        if (legacy is { Count: > 0 })
        {
            DiagnosticLog.Write(
                $"Addon ownership: migrating {legacy.Count} addon(s) from the install manifest.");
            var migrated = Normalize(legacy);
            Save(installPath, migrated);
            return migrated;
        }

        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string installPath, Dictionary<string, List<string>> owned)
    {
        var path = PathFor(installPath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path,
                JsonSerializer.Serialize(owned, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Addon ownership: could not write {path}: {ex.Message}");
        }
    }

    private static Dictionary<string, List<string>> Normalize(Dictionary<string, List<string>> source)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, files) in source)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            result[id] = files ?? new List<string>();
        }
        return result;
    }
}
