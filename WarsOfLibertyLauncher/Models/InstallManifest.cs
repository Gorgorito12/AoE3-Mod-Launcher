using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Records exactly which files and folders the launcher created during a
/// native install. The manifest lives at the root of the install folder
/// (wol-manifest.json) and is the source of truth for safe uninstall.
///
/// Without this file, the uninstaller falls back to a much more conservative
/// strategy (delete the whole install folder only if it's clearly a WoL
/// subfolder — never if it looks like an AoE3 root).
/// </summary>
public class InstallManifest
{
    public const string FileName = "wol-manifest.json";

    /// <summary>Mod version installed (e.g. "1.0.15d").</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>Absolute install path (where the manifest lives).</summary>
    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";

    /// <summary>UTC timestamp of the install.</summary>
    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Source AoE3 folder cloned from (informational, not used by uninstall).</summary>
    [JsonPropertyName("aoe3SourcePath")]
    public string? Aoe3SourcePath { get; set; }

    /// <summary>True if the install cloned AoE3 into the destination
    /// (full install). False if it was a mod-only install on top of an
    /// existing AoE3.</summary>
    [JsonPropertyName("clonedAoe3")]
    public bool ClonedAoe3 { get; set; }

    /// <summary>
    /// Relative paths (forward slashes) of every file the launcher created.
    /// Stored relative to <see cref="InstallPath"/>.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    /// <summary>
    /// Relative paths of every directory the launcher created. Stored in
    /// the order they were created (so we can delete them in reverse and
    /// remove leaves first).
    /// </summary>
    [JsonPropertyName("directories")]
    public List<string> Directories { get; set; } = new();

    /// <summary>
    /// Absolute paths of shortcuts the installer created. Includes the
    /// .lnk on the desktop and inside the Start Menu folder.
    /// </summary>
    [JsonPropertyName("shortcuts")]
    public List<string> Shortcuts { get; set; } = new();

    /// <summary>
    /// Start Menu folder created by the installer (so we can remove it
    /// once empty).
    /// </summary>
    [JsonPropertyName("startMenuFolder")]
    public string? StartMenuFolder { get; set; }

    public static string GetManifestPath(string installPath) =>
        Path.Combine(installPath, FileName);

    public static InstallManifest? TryLoad(string installPath)
    {
        try
        {
            var path = GetManifestPath(installPath);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstallManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(InstallPath))
            throw new InvalidOperationException("InstallPath must be set before saving.");
        Directory.CreateDirectory(InstallPath);
        var path = GetManifestPath(InstallPath);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }
}
