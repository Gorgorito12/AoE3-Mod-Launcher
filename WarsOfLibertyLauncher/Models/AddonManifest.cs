using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One <c>addons/&lt;id&gt;/addon.json</c> from the catalog repo — a small
/// optional overlay (transparent UI, gun-smoke effects, …) a player can toggle
/// per install.
///
/// Addons are catalog-hosted with a pinned SHA-256 rather than downloaded from
/// the community page they originate on: those pages (AoE3 Heaven's
/// <c>showfile.php?fileid=</c>) are HTML landing pages, not stable direct
/// downloads, so there is nothing to verify and nothing that reliably resolves.
/// <see cref="SourceUrl"/> keeps the link to the original page so the author
/// stays credited and the player can read the notes.
///
/// Deliberately NOT part of <see cref="ModCatalogManifest"/>: these overlay the
/// stock AoE3 files every mod clones, so they apply to any mod rather than
/// belonging to one, and duplicating them per mod.json would mean editing three
/// manifests to fix one URL.
/// </summary>
public class AddonManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Per-language blurb, same shape as a mod's <c>description</c>.</summary>
    [JsonPropertyName("description")]
    public Dictionary<string, string>? Description { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    /// <summary>Re-hosted archive the launcher downloads. HTTPS only.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// SHA-256 of <see cref="Url"/>'s bytes. Required: an addon writes into the
    /// player's game folder, so "whatever that URL serves today" is not good
    /// enough. Verified before a single file is extracted.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>Original community page, for credit and release notes.</summary>
    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = "";
}

/// <summary>
/// A catalog addon resolved for display: the manifest plus whatever the launcher
/// knows locally about it.
/// </summary>
public sealed class AddonInfo
{
    public AddonManifest Manifest { get; init; } = new();

    /// <summary>True when this addon's files are currently applied to the install.</summary>
    public bool Enabled { get; init; }

    public string Id => Manifest.Id;
    public string Name => string.IsNullOrWhiteSpace(Manifest.Name) ? Manifest.Id : Manifest.Name;

    public string DescriptionFor(string language)
    {
        if (Manifest.Description == null || Manifest.Description.Count == 0) return "";
        if (Manifest.Description.TryGetValue(language, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;
        if (Manifest.Description.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
            return en;
        foreach (var v in Manifest.Description.Values)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return "";
    }
}
