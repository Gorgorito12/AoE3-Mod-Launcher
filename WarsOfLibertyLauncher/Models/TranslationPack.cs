using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Content fingerprint written by the packager, derived from the files'
    /// <see cref="TranslationFile.TranslatedHash"/> via
    /// <see cref="TranslationCompat.ComputeContentHash"/>. Drives the folder-pack
    /// dedup key (<c>id@contentHash</c>) so an IMPROVED pack (different bytes)
    /// re-notifies without bumping <see cref="Version"/> or relying on a release
    /// tag. Optional: consumers recompute it from <see cref="Files"/> when absent.
    /// </summary>
    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }

    /// <summary>
    /// ISO-8601 UTC timestamp the packager stamped when this version was built.
    /// Used to order a translation's version history (newest first) reliably
    /// without parsing arbitrary <see cref="Version"/> strings. Optional: when
    /// absent, consumers fall back to the version-subfolder name.
    /// </summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>
    /// Filename of the pack's <c>.zip</c> in the SAME folder as this manifest
    /// (folder-published packs). Lets the launcher build the raw-CDN download URL.
    /// Optional: defaults to <c>{id}.zip</c> when absent.
    /// </summary>
    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

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
/// One historical version of a folder-published translation pack (one
/// <c>translations/&lt;id&gt;/&lt;version&gt;/</c> subfolder). The launcher lists
/// these in a per-translation version picker so the user can apply an older
/// version (e.g. one that matches their installed mod version).
/// </summary>
public class TranslationVersion
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public List<string> CompatibleWith { get; set; } = new();
    /// <summary>ISO-8601 UTC build timestamp (for newest-first ordering); may be empty.</summary>
    public string Date { get; set; } = "";
    public long Size { get; set; }
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

    /// <summary>
    /// GitHub release tag this entry came from (set by the registry, NOT from the
    /// manifest). Used as the notification dedup key so a NEW release alerts even
    /// when the maintainer didn't bump the manifest's internal <see cref="Version"/>.
    /// Empty for folder-published packs (those key off <see cref="ContentHash"/>).
    /// </summary>
    [JsonIgnore]
    public string ReleaseTag { get; set; } = "";

    /// <summary>
    /// Content fingerprint for folder-published packs (the manifest's
    /// <c>contentHash</c>, or recomputed from the files). Drives the
    /// <c>id@contentHash</c> dedup key when there's no release tag. Empty for
    /// release-published packs.
    /// </summary>
    [JsonIgnore]
    public string ContentHash { get; set; } = "";

    /// <summary>True when this entry came from a repo <c>translations/</c> folder
    /// (vs a GitHub release). Folder packs win over release packs on id collision.</summary>
    [JsonIgnore]
    public bool FromFolder { get; set; }

    /// <summary>
    /// Version history for a folder-published pack, NEWEST FIRST. The first
    /// element mirrors this entry's top-level fields (which always describe the
    /// newest version, so the menu / dedup / notification stay unchanged). Has
    /// 0 or 1 elements for single-version packs (release packs, or a flat folder
    /// pack); 2+ when the repo ships <c>translations/&lt;id&gt;/&lt;version&gt;/</c>
    /// subfolders. The Properties → Language tab shows a version picker when this
    /// has 2+.
    /// </summary>
    [JsonIgnore]
    public List<TranslationVersion> Versions { get; set; } = new();
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

    /// <summary>Max versions shown in a translation's history picker (newest kept).</summary>
    public const int MaxTranslationVersions = 10;

    /// <summary>
    /// Orders a translation's versions NEWEST FIRST — by <c>date</c> (ISO-8601, so
    /// ordinal-descending string compare is chronological) when present, falling
    /// back to the version string. Caps the result to
    /// <see cref="MaxTranslationVersions"/> so a long history doesn't make an
    /// unwieldy combo (older ones stay in git, just not listed).
    /// </summary>
    public static List<TranslationVersion> OrderVersions(IEnumerable<TranslationVersion>? versions)
    {
        if (versions == null) return new();
        return versions
            .Where(v => v != null)
            .OrderByDescending(v => v.Date ?? "", StringComparer.Ordinal)
            .ThenByDescending(v => v.Version ?? "", StringComparer.OrdinalIgnoreCase)
            .Take(MaxTranslationVersions)
            .ToList();
    }

    /// <summary>
    /// Deterministic content fingerprint of a pack, derived ONLY from its files'
    /// translated-hashes (sorted by path) so the launcher, the packager and the
    /// notifier all compute the SAME value from the same <c>translation.json</c>.
    /// Recipe (must stay identical across all three): sort files by path (ordinal),
    /// join "<c>path\ntranslatedHash</c>" with "\n", SHA-256 the UTF-8 bytes, take
    /// the first 16 lowercase-hex chars. A pack with changed bytes yields a new
    /// hash → a new <c>id@contentHash</c> key → a fresh "new translation" bell.
    /// </summary>
    public static string ComputeContentHash(IEnumerable<TranslationFile>? files)
    {
        var ordered = (files ?? Enumerable.Empty<TranslationFile>())
            .Where(f => f != null)
            .OrderBy(f => f.Path ?? "", StringComparer.Ordinal);
        var payload = string.Join("\n",
            ordered.Select(f => (f.Path ?? "") + "\n" + (f.TranslatedHash ?? "")));
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
    }

    /// <summary>
    /// The dedup / notification key for a translation entry. Cascade:
    /// release tag (release-published packs) → <c>id@contentHash</c>
    /// (folder-published packs) → <c>id@version</c> (legacy fallback). MUST match
    /// the key the notifier emits for the same pack.
    /// </summary>
    public static string KeyOf(TranslationIndexEntry e)
    {
        if (e == null) return "";
        if (!string.IsNullOrWhiteSpace(e.ReleaseTag)) return e.ReleaseTag;
        if (!string.IsNullOrWhiteSpace(e.ContentHash)) return $"{e.Id}@{e.ContentHash}";
        return $"{e.Id}@{e.Version}";
    }

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

    /// <summary>
    /// Display order for the language list (Mod Properties tab + gear menu):
    /// the ACTIVE pack first, then packs COMPATIBLE with the installed mod version
    /// (so the one the user can actually use surfaces to the top), then NEWEST
    /// first, then by name. "Newest" is the position of the pack's id in
    /// <paramref name="registryOrder"/> — the index built from GitHub releases,
    /// which is newest-first — so a lower rank means a more recent release; packs
    /// not in the registry (local-only/sideloaded) sort last. Pure + WPF-free so
    /// both surfaces share one ordering and it can be unit-tested.
    /// </summary>
    public static List<TranslationIndexEntry> OrderForDisplay(
        IEnumerable<TranslationIndexEntry> entries,
        IReadOnlyList<TranslationIndexEntry>? registryOrder,
        string? modVersion,
        string? activeId)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (registryOrder != null)
            for (int i = 0; i < registryOrder.Count; i++)
            {
                var id = registryOrder[i].Id;
                if (!string.IsNullOrEmpty(id) && !rank.ContainsKey(id))
                    rank[id] = i;
            }

        int RankOf(string id) => rank.TryGetValue(id ?? "", out var r) ? r : int.MaxValue;
        bool IsActive(string id) => !string.IsNullOrEmpty(activeId)
            && string.Equals(id, activeId, StringComparison.OrdinalIgnoreCase);

        return entries
            .OrderBy(e => IsActive(e.Id) ? 0 : 1)                                  // active pack first
            .ThenBy(e => IsVersionBlocked(e.CompatibleWith, modVersion) ? 1 : 0)   // compatible/unknown before incompatible
            .ThenBy(e => RankOf(e.Id))                                             // newest release first
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)          // stable, readable tiebreak
            .ToList();
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
