using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Records exactly which files and folders the launcher created during a
/// native install. The manifest lives at the root of the install folder
/// (<see cref="FileName"/>) and is the source of truth for safe uninstall.
///
/// Without this file, the uninstaller falls back to a much more conservative
/// strategy (probe-file marker check + best-effort registry/shortcut lookup
/// from the active profile).
///
/// Backward compat: older builds wrote <see cref="LegacyFileName"/> for the
/// WoL install. <see cref="TryLoad(string)"/> probes the new filename first,
/// then falls back to the legacy one — so a WoL folder produced by an old
/// launcher is still readable.
/// </summary>
public class InstallManifest
{
    /// <summary>Current manifest filename. Mod-agnostic.</summary>
    public const string FileName = "install-manifest.json";

    /// <summary>Legacy filename (WoL-only builds). Kept for read-side fallback.</summary>
    public const string LegacyFileName = "wol-manifest.json";

    /// <summary>
    /// Stable identifier of the mod this manifest belongs to (e.g. "wol",
    /// "improvement-mod"). Empty for manifests written by older builds.
    /// </summary>
    [JsonPropertyName("modId")]
    public string ModId { get; set; } = "";

    /// <summary>
    /// Add/Remove Programs registry subkey written at install time. The
    /// uninstaller uses this to delete the exact key that was created —
    /// safer than re-deriving from the profile, since the profile's
    /// product GUID may have changed across launcher versions.
    /// </summary>
    [JsonPropertyName("productGuid")]
    public string ProductGuid { get; set; } = "";

    /// <summary>
    /// Human-readable name written into Add/Remove Programs and used as the
    /// shortcut filename base. Carrying it in the manifest lets the
    /// uninstaller match the exact shortcut/registry display without
    /// having to re-resolve the active profile.
    /// </summary>
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "";

    /// <summary>"Publisher" field shown in Add/Remove Programs.</summary>
    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "";

    /// <summary>Mod version installed (free-form string).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// MD5 (lowercase hex) of the key data files the launcher laid down at
    /// install/repair time, keyed by install-relative path with forward
    /// slashes (e.g. "data/protoy.xml"). Recorded so the launcher can
    /// recognize its OWN byte-faithful payload — which does not MD5-match any
    /// UpdateInfo.xml version — as an intact install at <see cref="Version"/>.
    /// Empty for manifests written by builds before baseline recording
    /// existed; the detector then falls back to trusting <see cref="Version"/>.
    /// At install time no translation is applied yet, so these are the
    /// canonical/English hashes — consistent with the detector, which hashes
    /// the <c>translations\_originals\</c> snapshot when a pack is active.
    /// </summary>
    [JsonPropertyName("keyFileHashes")]
    public Dictionary<string, string> KeyFileHashes { get; set; } = new();

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
    /// Relative paths (forward slashes) of every file that came from the
    /// mod's OWN payload overlay — i.e. the files the mod ships on top of
    /// the cloned/overlaid base game. This is a strict subset of the install
    /// (it excludes the cloned AoE3 base files), and it is the universe the
    /// update-time file deletion is allowed to touch. Empty for manifests
    /// written by builds before overlay tracking existed (the update flow
    /// then captures it on the next re-overlay and skips auto-deletion that
    /// run — no baseline, nothing safe to remove yet).
    /// </summary>
    [JsonPropertyName("overlayFiles")]
    public List<string> OverlayFiles { get; set; } = new();

    /// <summary>
    /// The "net-new" subset of <see cref="OverlayFiles"/>: overlay files that
    /// did NOT exist in the base game the mod was laid over (so removing them
    /// can never leave a hole the engine expects). These are the ONLY files
    /// the update flow may auto-delete when a new release stops shipping them.
    /// Overlay files that shadow a base-game file are deliberately excluded —
    /// auto-deleting one would break the game (the original bytes were
    /// overwritten without backup). Those can only be removed via an explicit
    /// <c>delete.lst</c> (the modder's responsibility). The classification is
    /// "sticky" across updates: a file keeps its install-time net-new/shadow
    /// status; only genuinely-new paths are re-classified by existence.
    /// </summary>
    [JsonPropertyName("overlayNetNew")]
    public List<string> OverlayNetNew { get; set; } = new();

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

    /// <summary>
    /// Per-file integrity fingerprints (size + SHA-256) for every overlay
    /// file the launcher laid down, keyed by install-relative path with
    /// forward slashes (same convention as <see cref="OverlayFiles"/>).
    /// Lets the launcher detect the EXACT set of damaged/missing files during
    /// Verify / Repair instead of a blind spot-check, and lets Repair re-copy
    /// only the damaged files. Scoped to the mod overlay (not the cloned AoE3
    /// base, which the mod payload can't repair anyway).
    ///
    /// Captured at copy time, BEFORE any translation is applied, so these are
    /// the canonical/English hashes — consistent with <see cref="KeyFileHashes"/>
    /// and with the multiplayer fingerprint, both of which hash the
    /// <c>translations\_originals\</c> snapshot for covered files.
    ///
    /// Empty for manifests written by builds before per-file hashing existed;
    /// the verifier then degrades to the legacy structural spot-check rather
    /// than treating every file as unverifiable.
    /// </summary>
    [JsonPropertyName("fileHashes")]
    public Dictionary<string, FileFingerprint> FileHashes { get; set; } = new();

    /// <summary>
    /// Integrity fingerprints (size + SHA-256) of a small curated set of AoE3
    /// base ENGINE files (e.g. <c>RockallDLL.dll</c>) plus the version-key data
    /// files, keyed by install-relative path with forward slashes. Separate from
    /// <see cref="FileHashes"/> on purpose: engine files come from the cloned base
    /// game, NOT the mod payload, so a damaged engine file is NOT repairable by
    /// the granular re-copy (which only restores overlay files from the payload).
    /// Verifying them flags "reinstall the base game" rather than routing into a
    /// futile ~4 GB payload re-download. Captured at install/repair and refreshed
    /// after each patch (engine files a patch modifies go stale otherwise).
    /// Empty for manifests written before engine coverage existed.
    /// </summary>
    [JsonPropertyName("engineFileHashes")]
    public Dictionary<string, FileFingerprint> EngineFileHashes { get; set; } = new();

    public static string GetManifestPath(string installPath) =>
        Path.Combine(installPath, FileName);

    public static InstallManifest? TryLoad(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return null;

        // Probe the current filename first, then fall back to the legacy
        // one. Either way deserialise into the same shape — older files
        // simply leave the new fields empty.
        var paths = new[]
        {
            Path.Combine(installPath, FileName),
            Path.Combine(installPath, LegacyFileName),
        };

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InstallManifest>(json);
            }
            catch
            {
                // Skip unreadable / malformed files; try the next candidate.
                continue;
            }
        }
        return null;
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

/// <summary>
/// Integrity fingerprint of a single installed file: its byte length and
/// SHA-256 (lowercase hex). Size is checked first during verification (a
/// truncated file is the most common corruption and is free to detect),
/// then the hash confirms the bytes. A parameterless constructor is kept so
/// System.Text.Json round-trips the value without constructor-matching
/// subtleties.
/// </summary>
public sealed class FileFingerprint
{
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    public FileFingerprint() { }

    public FileFingerprint(long size, string sha256)
    {
        Size = size;
        Sha256 = sha256;
    }
}
