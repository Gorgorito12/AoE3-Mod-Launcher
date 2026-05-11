using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// On-disk cache of the mods catalog. Written by
/// <c>ModCatalogService.SaveCache</c> after a successful online fetch and
/// read back synchronously on the next launcher start so we can render the
/// community mods without waiting for the GitHub round-trip.
///
/// File lives at
/// <c>%LocalAppData%\AoE3ModLauncher\catalog-cache.json</c> — per-user,
/// outside Program Files, so no UAC is needed to write.
///
/// Cache lifecycle:
///   * Fresh   (age &lt; TTL) → use as-is, skip the network.
///   * Stale   (age ≥ TTL) → still use it for the immediate render so the
///                            UI is instant, but kick a background refresh
///                            to update the file for the next session.
///   * Missing                → fall back to a full online fetch.
///   * Wrong repo             → ignored (the launcher's catalog-repo
///                            config changed; cache must be rebuilt).
///
/// The cache is intentionally lossy: it stores the projected
/// <see cref="ModCatalogEntry"/> objects (manifest + resolved asset URLs),
/// not the raw GitHub API response. If we ever change the manifest schema,
/// in-flight caches just look unparseable and the launcher falls back to a
/// fresh fetch automatically.
/// </summary>
public class ModCatalogCache
{
    /// <summary>UTC timestamp of the fetch that produced this cache.</summary>
    [JsonPropertyName("fetchedAt")]
    public DateTime FetchedAt { get; set; }

    /// <summary>
    /// owner/repo the cache was built from. If the user changes
    /// <c>modsCatalogRepo</c> in launcher-config.json (e.g. switches to a
    /// fork), the cache is discarded because this field won't match the
    /// new repo.
    /// </summary>
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    /// <summary>
    /// The catalog entries as returned by the last fetch. Same shape as
    /// <see cref="Services.ModCatalogService.FetchAsync"/>'s return value.
    /// </summary>
    [JsonPropertyName("manifests")]
    public List<Services.ModCatalogEntry> Manifests { get; set; } = new();
}
