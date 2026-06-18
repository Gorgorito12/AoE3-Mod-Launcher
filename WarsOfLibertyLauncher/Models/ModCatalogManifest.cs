using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Raw shape of a <c>mod.json</c> file as it lives in the
/// <c>aoe3-mods-catalog</c> repository — one file per mod folder. The
/// catalog repo's CI validates each manifest against
/// <c>schema/mod.schema.json</c> before allowing the PR to merge, so by
/// the time the launcher fetches one, it is well-formed at the schema
/// level. The launcher still validates business rules
/// (<see cref="ModCatalogManifestValidator"/>) before projecting it to a
/// <see cref="ModProfile"/> — schema correctness is a necessary but not
/// sufficient guarantee.
///
/// This model intentionally mirrors the JSON 1:1 (snake_case → camelCase
/// via <c>[JsonPropertyName]</c>). It is NOT the runtime model the
/// launcher uses: that's <see cref="ModProfile"/>, which gets built from
/// these DTOs in <c>ModCatalogService.ProjectToProfile</c>. Keeping the
/// raw and runtime models separate lets the schema evolve without
/// breaking the rest of the launcher.
/// </summary>
public class ModCatalogManifest
{
    /// <summary>Stable mod id. Must match the folder name under <c>/mods/</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = "";

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("officialWebsite")]
    public string OfficialWebsite { get; set; } = "";

    /// <summary>
    /// Filename of the icon, sitting next to <c>mod.json</c> in the same
    /// folder. The launcher resolves this to a raw GitHub URL and downloads
    /// it through <c>ModAssetCacheService</c> on first use.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>Filename of the banner image. Same resolution scheme as <see cref="Icon"/>.</summary>
    [JsonPropertyName("banner")]
    public string? Banner { get; set; }

    /// <summary>
    /// Filename of the dashboard hero image (the large background painted
    /// behind the title + PLAY button). Same resolution scheme as
    /// <see cref="Icon"/> / <see cref="Banner"/> — bare filename, the
    /// launcher splices it onto the raw GitHub URL. Specs: 1920x1080
    /// PNG/JPG, ≤ 8 MB (16:9, up to 4K). Important subject should sit in the
    /// right half of the image because the left half is covered by the title
    /// and the PLAY button.
    /// </summary>
    [JsonPropertyName("heroImage")]
    public string? HeroImage { get; set; }

    /// <summary>
    /// Optional ROTATING dashboard heroes (bare filenames, same scheme as
    /// <see cref="HeroImage"/>). When 2+ are listed the dashboard cycles them
    /// with a crossfade; takes precedence over <see cref="HeroImage"/>. Each
    /// follows the single-hero spec (16:9, ≤ 8 MB). Capped on download.
    /// </summary>
    [JsonPropertyName("heroImages")]
    public List<string>? HeroImages { get; set; }

    /// <summary>
    /// Optional gallery of screenshots/GIFs shown in the Workshop detail panel.
    /// Bare filenames in the same folder as <c>mod.json</c> (same resolution
    /// scheme as <see cref="Icon"/>). Animated GIFs are allowed here only. The
    /// launcher caps the count and each file's size on download.
    /// </summary>
    [JsonPropertyName("screenshots")]
    public List<string>? Screenshots { get; set; }

    /// <summary>
    /// Per-language descriptions (keyed by ISO 639-1: "en", "es", …).
    /// The launcher picks the user's UI language with a fallback to "en".
    /// </summary>
    [JsonPropertyName("description")]
    public Dictionary<string, string>? Description { get; set; }

    /// <summary>
    /// owner/repo of the mod's own GitHub repository. Used by the "pin to
    /// release tag" flow — the launcher loads runtime files from this repo
    /// at the tag named in <see cref="ApprovedReleaseTag"/>.
    /// </summary>
    [JsonPropertyName("sourceRepo")]
    public string? SourceRepo { get; set; }

    /// <summary>
    /// Tag in <see cref="SourceRepo"/> whose contents are the canonical
    /// version of this mod for launcher consumption. Bumping this is a
    /// micro-PR to the catalog (Tier 2) — auto-merge if validation passes.
    /// </summary>
    [JsonPropertyName("approvedReleaseTag")]
    public string? ApprovedReleaseTag { get; set; }

    /// <summary>
    /// Optional Add/Remove Programs registry subkey. When set, the launcher
    /// uses this string as the Inno-style key under
    /// <c>HKLM\SOFTWARE\…\Uninstall\&lt;here&gt;</c>. Lets modders keep a
    /// stable key across releases — useful for installers that already had
    /// one. When omitted, the launcher derives a key from <see cref="Id"/>
    /// (<c>"&lt;id&gt;_launcher"</c>), which is deterministic and safe.
    /// </summary>
    [JsonPropertyName("installProductGuid")]
    public string? InstallProductGuid { get; set; }

    /// <summary>
    /// Optional folder name (relative to
    /// <c>%USERPROFILE%\Documents\My Games\</c>) where this mod keeps its
    /// user-side data — saves, custom metropolises, replays, etc. When
    /// present, the launcher offers a backup-before-install prompt and
    /// surfaces "Open / Create backup / Restore backup" entries in the
    /// gear menu, all pointing at that folder. When omitted the launcher
    /// silently skips the user-data feature for this mod.
    /// </summary>
    [JsonPropertyName("userDataFolder")]
    public string? UserDataFolder { get; set; }

    [JsonPropertyName("install")]
    public ModCatalogInstall Install { get; set; } = new();

    [JsonPropertyName("update")]
    public ModCatalogUpdate Update { get; set; } = new();

    [JsonPropertyName("translations")]
    public ModCatalogTranslations? Translations { get; set; }
}

public class ModCatalogInstall
{
    /// <summary>
    /// "IsolatedFolder" or "InPlaceOverlay". Parsed into
    /// <see cref="ModInstallType"/> by the projection step.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("defaultFolder")]
    public string DefaultFolder { get; set; } = "";

    [JsonPropertyName("probeFile")]
    public string ProbeFile { get; set; } = "";

    /// <summary>
    /// Optional content marker (file or directory, relative to the install
    /// folder) unique to the mod and absent from the base game. Projected into
    /// <see cref="ModProfile.InstallMarker"/>; lets the launcher detect the mod
    /// by content in a folder with any name. Only needed when
    /// <see cref="ProbeFile"/> is shared with the base game.
    /// </summary>
    [JsonPropertyName("marker")]
    public string Marker { get; set; } = "";

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    /// <summary>
    /// Initial-install payload URLs, agnostic of the update mechanism. Used
    /// for first-time installs where the mod ships its files as one or
    /// more archives. Multi-part archives (.zip.001 / .zip.002 / ...)
    /// list every part in order; the launcher concatenates them before
    /// extracting. <c>null</c> or empty means the mod can't be installed
    /// automatically (e.g. ModDB-hosted mods that need a browser to
    /// download); the launcher's install button should fall back to
    /// opening <see cref="ModCatalogManifest.OfficialWebsite"/> instead.
    /// </summary>
    [JsonPropertyName("payloadUrls")]
    public string[]? PayloadUrls { get; set; }

    /// <summary>
    /// Parallel array to <see cref="PayloadUrls"/> with the SHA-256 of
    /// each file. The launcher verifies after download and aborts if any
    /// hash mismatches — protects against silent payload tampering at the
    /// hosting source (account compromise, repo takeover, etc.).
    /// Optional but strongly encouraged for any mod the launcher
    /// auto-installs.
    /// </summary>
    [JsonPropertyName("payloadSha256")]
    public string[]? PayloadSha256 { get; set; }
}

public class ModCatalogUpdate
{
    /// <summary>
    /// "WolPatcher", "DelegatedExternal", or "GitHubReleases". Parsed into
    /// <see cref="ModUpdateMechanism"/>.
    /// </summary>
    [JsonPropertyName("mechanism")]
    public string Mechanism { get; set; } = "";

    [JsonPropertyName("wol")]
    public ModCatalogWolSettings? Wol { get; set; }

    /// <summary>
    /// Optional settings for the <c>GitHubReleases</c> mechanism. Only
    /// surfaces fields that aren't already top-level on the manifest
    /// (<c>sourceRepo</c> and <c>approvedReleaseTag</c> stay at the root
    /// because they identify the mod's authoritative repo regardless of
    /// mechanism). Used to declare external hosting — see
    /// <see cref="ModCatalogGitHubSettings"/>.
    /// </summary>
    [JsonPropertyName("github")]
    public ModCatalogGitHubSettings? Github { get; set; }
}

/// <summary>
/// Optional GitHubReleases-specific settings inside a catalog manifest.
/// Lets a modder host the actual payload outside of GitHub Releases while
/// keeping the release tag as the version marker. See
/// <see cref="GitHubReleasesSettings.ExternalAssetUrlTemplate"/> for the
/// full rationale.
/// </summary>
public class ModCatalogGitHubSettings
{
    /// <summary>
    /// URL template with a <c>{tag}</c> placeholder for the payload host.
    /// Empty means "use the GitHub Release asset" (default behaviour).
    /// </summary>
    [JsonPropertyName("externalAssetUrlTemplate")]
    public string? ExternalAssetUrlTemplate { get; set; }

    /// <summary>
    /// Expected SHA-256 (lowercase hex) of the file at the templated URL.
    /// Required when <see cref="ExternalAssetUrlTemplate"/> is set;
    /// rejected at download time if missing.
    /// </summary>
    [JsonPropertyName("externalAssetSha256")]
    public string? ExternalAssetSha256 { get; set; }
}

public class ModCatalogWolSettings
{
    [JsonPropertyName("updateInfoUrl")]
    public string UpdateInfoUrl { get; set; } = "";

    [JsonPropertyName("updateInfoUrlAlt")]
    public string UpdateInfoUrlAlt { get; set; } = "";

    [JsonPropertyName("payloadZipUrls")]
    public string[]? PayloadZipUrls { get; set; }

    /// <summary>
    /// Optional but strongly recommended. Parallel array to
    /// <see cref="PayloadZipUrls"/> — the SHA-256 of each .zip. Lets the
    /// launcher refuse downloads whose contents drift from what the
    /// catalog promised, even if the modder's own GitHub release is
    /// compromised post-approval.
    /// </summary>
    [JsonPropertyName("payloadSha256")]
    public string[]? PayloadSha256 { get; set; }
}

public class ModCatalogTranslations
{
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("coveredFiles")]
    public List<string>? CoveredFiles { get; set; }
}
