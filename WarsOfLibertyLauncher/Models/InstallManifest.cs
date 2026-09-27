using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    /// game, NOT the mod payload, so no payload re-lay can repair them — Repair puts a
    /// damaged one back from the player's own AoE3 only when a copy matches this
    /// fingerprint byte for byte (<c>Repair.EngineRestore</c>). Measured at the first
    /// install and then MERGED, never recomputed (<see cref="Services.EngineBaseline"/>):
    /// only entries an operation actually wrote are re-measured, or a file already
    /// corrupt at the next repair would become the recorded truth.
    /// Empty for manifests written before engine coverage existed.
    /// </summary>
    [JsonPropertyName("engineFileHashes")]
    public Dictionary<string, FileFingerprint> EngineFileHashes { get; set; } = new();

    /// <summary>
    /// Install-relative paths each enabled community addon owns, keyed by addon
    /// id. Written when an addon is applied and cleared when it is disabled.
    ///
    /// This is what makes an addon reversible: disabling one has to know exactly
    /// which files to restore from <c>addons\_originals\&lt;id&gt;\</c> and which
    /// files it ADDED (those have no original to restore and must simply be
    /// deleted). Deriving the list from the addon's zip at disable time would be
    /// wrong — the zip may be gone, or a later version of it may ship a different
    /// file set than the one actually on disk.
    ///
    /// Empty for manifests written before addons existed, which reads correctly
    /// as "no addon owns anything here".
    /// </summary>
    [JsonPropertyName("addonFiles")]
    public Dictionary<string, List<string>> AddonFiles { get; set; } = new();

    /// <summary>
    /// The private AoE3 registry key this install created, relative to the hive — e.g.
    /// <c>Software\Microsoft\Microsoft Games\Struggle of Indonesia\1.0</c>. Written only for
    /// mods using <see cref="ModProfile.PrivateSetupPath"/>.
    ///
    /// Recording it is what lets uninstall take it away again: the key is derived from the
    /// mod's display name at install time, so re-deriving it later would break the moment a
    /// mod is renamed in the catalogue. Empty for every other install, which reads correctly
    /// as "this one created no registry key of its own".
    /// </summary>
    [JsonPropertyName("privateSetupPathKey")]
    public string PrivateSetupPathKey { get; set; } = "";

    /// <summary>
    /// Absolute <c>Documents\My Games\&lt;mod&gt;</c> folder this install seeded a user-data payload
    /// into, or empty — which is every other install, and reads correctly as "this one created
    /// nothing outside <see cref="InstallPath"/>".
    ///
    /// <para>Along with <see cref="Shortcuts"/> and <see cref="StartMenuFolder"/> this is one of
    /// the only fields naming an ABSOLUTE path, and the only one pointing at a folder full of the
    /// player's own saves. Every other collection here is install-relative by contract and every
    /// consumer clamps to the install root; this one deliberately cannot, so its own consumers
    /// clamp to <b>this</b> root instead.</para>
    /// </summary>
    [JsonPropertyName("userDataRoot")]
    public string UserDataRoot { get; set; } = "";

    /// <summary>
    /// The files the launcher actually CREATED under <see cref="UserDataRoot"/>, keyed by
    /// root-relative forward-slash path, with the fingerprint it wrote.
    ///
    /// <para>Two properties make this safe to delete from. The seed is copy-if-absent, so a file
    /// the player already had is never in here at all. And the fingerprint means a file the
    /// player has since PLAYED with — their profile, a home-city deck the game rewrote — no
    /// longer matches and stops being ours. Same idea as <see cref="OverlayNetNew"/>: only ever
    /// remove what we added, and only while it is still what we added.</para>
    /// </summary>
    [JsonPropertyName("userDataFiles")]
    public Dictionary<string, FileFingerprint> UserDataFiles { get; set; } = new();

    /// <summary>
    /// Root-relative directories the launcher created there, parents first. Removed in reverse
    /// and only while empty, so a folder the player has put anything into is kept.
    /// </summary>
    [JsonPropertyName("userDataDirs")]
    public List<string> UserDataDirs { get; set; } = new();

    /// <summary>
    /// True when the launcher created <see cref="UserDataRoot"/> itself. A folder that was
    /// already there belongs to the player and is never removed, however empty it ends up.
    /// </summary>
    [JsonPropertyName("userDataRootCreated")]
    public bool UserDataRootCreated { get; set; }

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
                var json = ReadWithRetry(path);
                var manifest = JsonSerializer.Deserialize<InstallManifest>(json);
                if (manifest == null) continue;
                manifest.Normalize(installPath);
                return manifest;
            }
            catch
            {
                // Skip unreadable / malformed files; try the next candidate.
                continue;
            }
        }
        return null;
    }

    /// <summary>
    /// Suffix of the scratch file <see cref="Save"/> writes before swapping it in. Anything that
    /// enumerates an install folder has to recognise a leftover one as bookkeeping.
    /// </summary>
    internal const string TempSuffix = ".tmp";

    /// <summary>True for a scratch file a <see cref="Save"/> left behind (crash between write and swap).</summary>
    internal static bool IsSaveScratch(string fileNameOrRelPath)
    {
        var name = Path.GetFileName(fileNameOrRelPath.Replace('/', Path.DirectorySeparatorChar));
        return name.StartsWith(FileName + ".", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the manifest ATOMICALLY: to a scratch file beside it, flushed to disk, then swapped
    /// over the old one. It used to be <c>File.WriteAllText</c> in place, so a crash or power cut
    /// mid-write left a truncated manifest — which reads back as NO manifest, and a folder with no
    /// manifest loses per-file verify, gets a full re-download on every repair and the
    /// conservative uninstall. Never creates the install folder: a manifest for a folder that no
    /// longer exists is a ghost, not a record.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(InstallPath))
            throw new InvalidOperationException("InstallPath must be set before saving.");
        if (!Directory.Exists(InstallPath))
            throw new DirectoryNotFoundException($"Install folder not found: {InstallPath}");

        var path = GetManifestPath(InstallPath);
        var tmp = $"{path}.{Guid.NewGuid():N}{TempSuffix}";
        var options = new JsonSerializerOptions { WriteIndented = true };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, options);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var attrs = File.GetAttributes(path);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    }
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 5)
                {
                    // A reader (verify, the snapshot, an antivirus scan) holding the file for a
                    // moment is the usual cause; it clears within a few hundred ms.
                    Thread.Sleep(150 * attempt);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 3) { Thread.Sleep(100 * attempt); }
        }
    }

    /// <summary>
    /// Makes a loaded manifest safe to use: every map compares paths case-insensitively (Windows
    /// paths are; a case-sensitive map let the same file sit under two keys, one stale for ever),
    /// a <c>null</c> written into a collection reads as empty, and <see cref="InstallPath"/> is the
    /// folder the file was actually READ from — so a later <see cref="Save"/> can never write to a
    /// stale path recorded before the install was moved.
    /// </summary>
    private void Normalize(string loadedFrom)
    {
        InstallPath = loadedFrom;
        KeyFileHashes = CaseInsensitive(KeyFileHashes);
        FileHashes = CaseInsensitive(FileHashes);
        EngineFileHashes = CaseInsensitive(EngineFileHashes);
        UserDataFiles = CaseInsensitive(UserDataFiles);
        AddonFiles = CaseInsensitive(AddonFiles);
        Files ??= new();
        Directories ??= new();
        OverlayFiles ??= new();
        OverlayNetNew ??= new();
        Shortcuts ??= new();
        UserDataDirs ??= new();
    }

    /// <summary>
    /// Rebuilds a map with an ordinal-ignore-case comparer. <c>new Dictionary(existing, comparer)</c>
    /// would THROW on case-duplicate keys, so this merges in order and the LAST entry wins — the
    /// later write is the more recent re-capture.
    /// </summary>
    internal static Dictionary<string, T> CaseInsensitive<T>(Dictionary<string, T>? source)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (source == null) return result;
        foreach (var (key, value) in source)
            if (key != null && value != null) result[key] = value;
        return result;
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
