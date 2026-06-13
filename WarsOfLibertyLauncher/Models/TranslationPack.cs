using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One file inside a translation pack — one of the localized XML files
/// (typically stringtabley.xml or unithelpstringsy.xml) plus the hashes
/// the launcher uses to validate compatibility and integrity.
/// </summary>
public class TranslationFile
{
    /// <summary>Path inside the install relative to the WoL root, e.g. "data/stringtabley.xml".</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// MD5 of the canonical English file the translator used as a base.
    /// If the user's snapshot of the original matches this hash, the
    /// translation is bit-exact compatible. If not, we fall back to the
    /// declared <see cref="TranslationManifest.CompatibleWith"/> list.
    /// </summary>
    [JsonPropertyName("originalHash")]
    public string OriginalHash { get; set; } = "";

    /// <summary>MD5 of the translated file shipped inside the pack.</summary>
    [JsonPropertyName("translatedHash")]
    public string TranslatedHash { get; set; } = "";

    /// <summary>Size of the translated file in bytes (informational).</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// The manifest shipped INSIDE every translation pack as
/// <c>translation.json</c>. Generated either by hand or by the launcher's
/// "Empaquetar traducción" tool.
/// </summary>
public class TranslationManifest
{
    /// <summary>Short identifier, used as the folder name. e.g. "es", "fr", "pt-br".</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Human-readable name shown in the menu.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>BCP-47-style locale tag (purely informational).</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    /// <summary>Translator credit shown in the menu.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    /// <summary>Pack version (e.g. "1.0", "1.1") — bumped when the pack ships changes.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Mod versions the translator declared compatible. Used as a fallback
    /// when the file-level <see cref="TranslationFile.OriginalHash"/> doesn't
    /// match exactly (e.g. small whitespace difference) but the translator
    /// believes the strings still apply.
    /// </summary>
    [JsonPropertyName("compatibleWith")]
    public List<string> CompatibleWith { get; set; } = new();

    /// <summary>The translated files this pack ships. Usually 1-3 entries.</summary>
    [JsonPropertyName("files")]
    public List<TranslationFile> Files { get; set; } = new();

    /// <summary>Optional free-form description shown in the confirmation dialog.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The mod id this pack targets (e.g. "wol", "improvement-mod"). Guards
    /// against applying a pack to the wrong mod, which would corrupt its files.
    /// Empty for packs made before this field existed — treated as "unverified"
    /// (allowed, not rejected) for backward compatibility.
    /// </summary>
    [JsonPropertyName("targetMod")]
    public string TargetMod { get; set; } = "";

    public const string ManifestFileName = "translation.json";
}

/// <summary>
/// One translation entry produced by scanning a GitHub repo's releases.
/// Carries enough info to render the menu without downloading the actual
/// zip; the user only triggers the download when they pick a translation
/// to apply.
/// </summary>
public class TranslationIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("compatibleWith")]
    public List<string> CompatibleWith { get; set; } = new();

    /// <summary>URL to download the .zip of the translation pack.</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    /// <summary>Total .zip size in bytes (informational, for the dialog).</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Optional SHA256 of the .zip — verified after download.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Mod id this pack targets. See <see cref="TranslationManifest.TargetMod"/>.</summary>
    [JsonPropertyName("targetMod")]
    public string TargetMod { get; set; } = "";
}

/// <summary>
/// Pure, UI-free compatibility helpers for translation packs. The AUTHORITATIVE
/// check is the per-file MD5 hash (see
/// <see cref="WarsOfLibertyLauncher.Services.TranslationService.CheckCompatibilityAsync"/>);
/// these cover the secondary version-string layer used to pre-filter packs in
/// menus (the hash isn't available for remote-only entries) and the target-mod
/// guard. Kept here so they can be unit-tested without WPF.
/// </summary>
public static class TranslationCompat
{
    /// <summary>
    /// Best-effort version-list compatibility: true only when the mod's current
    /// version is one the translator explicitly declared. Deliberately exact
    /// membership — NO ranges: a pack tested for 1.2.0 makes no promise about
    /// 1.3.0, whose strings may have changed. Empty list or unknown version →
    /// false (indeterminate; the caller decides, and the hash check / apply
    /// dialog remains the final authority).
    /// </summary>
    public static bool IsCompatible(IReadOnlyCollection<string>? compatibleWith, string? modVersion)
    {
        if (compatibleWith == null || compatibleWith.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(modVersion)) return false;
        var target = modVersion.Trim();
        foreach (var v in compatibleWith)
            if (string.Equals(v?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// True when the card/menu should mark a pack as INCOMPATIBLE on version
    /// grounds alone: the translator declared specific versions and the current
    /// one isn't among them. An empty declared list is "unknown", NOT blocked —
    /// the hash check at apply time decides. (For installed packs the caller
    /// should prefer the hash-first <c>CheckCompatibilityAsync</c> instead.)
    /// </summary>
    public static bool IsVersionBlocked(IReadOnlyCollection<string>? compatibleWith, string? modVersion)
        => compatibleWith != null && compatibleWith.Count > 0
           && !IsCompatible(compatibleWith, modVersion);

    /// <summary>
    /// Target-mod guard: allowed when the pack names no target mod
    /// (legacy/unverified) or it matches the mod being applied to. Rejected only
    /// when it explicitly names a DIFFERENT mod.
    /// </summary>
    public static bool TargetModMatches(string? packTargetMod, string? modId)
    {
        if (string.IsNullOrWhiteSpace(packTargetMod)) return true;  // unverified → allow
        if (string.IsNullOrWhiteSpace(modId)) return true;
        return string.Equals(packTargetMod.Trim(), modId.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Returned by the post-update reconciliation when an active translation was
/// reverted to English because it's no longer compatible with the new mod
/// version. The UI surfaces it so the user isn't silently switched to English.
/// </summary>
public record TranslationRevertNotice(
    string PackId,
    string PackName,
    IReadOnlyList<string> PackForVersions,
    string? NewModVersion);

/// <summary>
/// In-memory list of translations the launcher has discovered, populated
/// by <see cref="WarsOfLibertyLauncher.Services.TranslationRegistryService"/>
/// from the configured GitHub repo's releases.
/// </summary>
public class TranslationIndex
{
    public List<TranslationIndexEntry> Translations { get; set; } = new();
}
