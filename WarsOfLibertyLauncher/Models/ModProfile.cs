using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// How a mod is laid out on disk and how the launcher updates it. Two shapes
/// are supported today; more can be added when a new mod profile needs them.
/// </summary>
public enum ModInstallType
{
    /// <summary>
    /// The mod lives in its own folder, separate from the AoE3 install. The
    /// launcher applies patches inside that folder and runs a copy of the
    /// game executable (or a symlink to it). Wars of Liberty works this way.
    /// </summary>
    IsolatedFolder,

    /// <summary>
    /// The mod's files are extracted directly into the AoE3 install folder
    /// (or its <c>bin\</c> subfolder) and ship their own .exe alongside
    /// AoE3's. Improvement Mod works this way.
    /// </summary>
    InPlaceOverlay,
}

/// <summary>
/// How the launcher gets new versions of the mod's files.
/// </summary>
public enum ModUpdateMechanism
{
    /// <summary>
    /// Pull-based updater driven by an <c>UpdateInfo.xml</c> feed and
    /// incremental <c>.tar.xz</c> patches. The Wars of Liberty pipeline.
    /// </summary>
    WolPatcher,

    /// <summary>
    /// The launcher only knows how to play the mod; updates are handled by
    /// the mod's own external tool (e.g. Improvement Mod's <c>age3m.exe</c>
    /// patcher). The launcher exposes a "play" button and reads the
    /// installed version if it can, but doesn't push updates itself.
    /// </summary>
    DelegatedExternal,

    /// <summary>
    /// No automated updates — the user installs the mod manually. Used as a
    /// fallback while a proper mechanism is being added.
    /// </summary>
    Manual,

    /// <summary>
    /// Pin-to-tag updater. The mod lives in its own GitHub repository
    /// and publishes versions as GitHub Releases (each tagged
    /// <c>v1.0</c>, <c>v1.1</c>, …). The catalog manifest references a
    /// specific approved tag; the launcher downloads the release asset
    /// from that tag and applies it. Updates flow via Tier 2 micro-PRs
    /// to the catalog that bump the tag. See <see cref="GitHubReleasesSettings"/>.
    /// </summary>
    GitHubReleases,
}

/// <summary>
/// Settings for the WoL-style update pipeline (UpdateInfo.xml + tar.xz
/// patches). Only meaningful when <see cref="ModProfile.UpdateMechanism"/>
/// is <see cref="ModUpdateMechanism.WolPatcher"/>.
/// </summary>
public class WolPatcherSettings
{
    /// <summary>Primary URL of the UpdateInfo.xml feed.</summary>
    public string UpdateInfoUrl { get; set; } = "";

    /// <summary>Mirror used when the primary URL fails.</summary>
    public string UpdateInfoUrlAlt { get; set; } = "";

    /// <summary>Public website / fallback download page if the feed is unreachable.</summary>
    public string OfficialWebsite { get; set; } = "";

    /// <summary>
    /// Multipart payload zip URLs (split with .zip.001 / .002 / ... to
    /// dodge GitHub's per-file size cap). The launcher concatenates them
    /// before extracting.
    /// </summary>
    public string[] PayloadZipUrls { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Settings for the community-translation overlay system. Only meaningful
/// when the mod's data layout matches WoL's (the launcher hashes a
/// snapshot of the canonical English files and applies translation packs
/// over <c>data\</c>).
/// </summary>
public class TranslationsSettings
{
    /// <summary>
    /// GitHub repository (format <c>owner/repo</c>) whose releases each
    /// host a <c>translation.json</c> + <c>.zip</c> pair.
    /// </summary>
    public string Repo { get; set; } = "";

    /// <summary>
    /// Files (relative to the install root) that translation packs are
    /// allowed to replace. Used to build the originals snapshot and to
    /// validate incoming packs.
    /// </summary>
    public List<string> CoveredFiles { get; set; } = new();
}

/// <summary>
/// Everything that distinguishes one mod from another in the launcher.
/// All hard-coded "Wars of Liberty"-specific values live in a profile
/// instead of in the launcher's code so adding a new mod is just a new
/// profile entry.
///
/// Profiles are populated from two sources:
///   * <c>ModRegistry._builtIn</c> — the WoL + Improvement Mod entries
///     compiled into the launcher itself (offline fallback).
///   * Community catalog at <c>Gorgorito12/aoe3-mods-catalog</c> —
///     fetched on startup by <see cref="ModRegistry"/> and merged with
///     the built-ins. Each remote <c>mod.json</c> is projected into one
///     of these objects.
/// Built-in entries always win on id collisions: a community PR can't
/// shadow the official "wol" entry to redirect downloads.
/// </summary>
public class ModProfile
{
    /// <summary>Stable identifier used in config files and on disk. Lowercase, no spaces.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable name shown in the header and the mod selector.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Short tagline shown under the title (e.g. "Launcher" or
    /// "AoE3:TAD overhaul"). Optional.
    /// </summary>
    public string Subtitle { get; set; } = "";

    /// <summary>Hex accent color used for primary buttons and highlights.</summary>
    public string AccentColor { get; set; } = "#c8102e";

    /// <summary>
    /// Author / team that maintains the mod. Empty for built-in profiles
    /// (the launcher's own UI doesn't surface an author for them) and
    /// populated from the catalog manifest for community ones. Used in
    /// the mod-selector tile under the title.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// Mod's homepage / official site. Opened in the user's browser
    /// from the Settings menu. May be HTTP for legacy mod sites that
    /// don't have HTTPS yet (the catalog schema permits it for this
    /// field; payload URLs are HTTPS-only).
    /// </summary>
    public string OfficialWebsite { get; set; } = "";

    /// <summary>
    /// Per-language descriptions keyed by ISO 639-1 ("en", "es", …).
    /// Resolved against the user's UI language with a fallback to "en".
    /// Null for built-in profiles, which fall back to a hard-coded
    /// description string elsewhere if needed.
    /// </summary>
    public Dictionary<string, string>? Description { get; set; }

    /// <summary>
    /// Built-in pack URI for the mod's icon (e.g.
    /// <c>pack://application:,,,/WoL.ico</c>). Used by the legacy mod
    /// selector tiles. Community mods leave this null and use
    /// <see cref="IconUrl"/> + cached local file instead — see
    /// <c>ModAssetCacheService</c>.
    /// </summary>
    public string? BannerImage { get; set; }

    /// <summary>
    /// Remote URL of the mod's 256×256 icon (PNG). Set only for
    /// community mods discovered via the catalog. Resolved to a local
    /// cached file on demand by the UI layer.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Remote URL of the mod's 1200×300 banner image (PNG/JPG). Optional
    /// even for community mods — when null, the UI synthesises a
    /// gradient from <see cref="AccentColor"/>.
    /// </summary>
    public string? BannerUrl { get; set; }

    /// <summary>
    /// Local file path of the cached icon, populated by the UI once
    /// <see cref="IconUrl"/> has been downloaded into the mod-asset
    /// cache (<c>%LocalAppData%\AoE3ModLauncher\mod-assets\</c>). Null
    /// while the download is in flight or if the mod doesn't ship an
    /// icon — the UI falls back to a coloured monogram (<see cref="AccentColor"/>
    /// + first letter of <see cref="DisplayName"/>) in either case.
    /// Mutable on purpose: it gets set after the lazy fetch completes.
    /// </summary>
    public string? LocalIconPath { get; set; }

    /// <summary>
    /// Local file path of the cached banner. Same lifecycle as
    /// <see cref="LocalIconPath"/>: null until <see cref="BannerUrl"/>
    /// is downloaded. When null, the active-mod header uses a synthetic
    /// gradient driven by <see cref="AccentColor"/>.
    /// </summary>
    public string? LocalBannerPath { get; set; }

    /// <summary>How the mod's files relate to the AoE3 install folder.</summary>
    public ModInstallType InstallType { get; set; } = ModInstallType.IsolatedFolder;

    /// <summary>
    /// Default folder offered to the user in the install dialog. For
    /// isolated mods this is a separate folder; for in-place mods this is
    /// usually the AoE3 folder itself.
    /// </summary>
    public string DefaultInstallFolder { get; set; } = "";

    /// <summary>
    /// Filename of a probe file inside the install folder that, when
    /// present, confirms the mod is installed. For WoL this is one of the
    /// patched data files; for IM it's <c>age3m.exe</c>. The launcher uses
    /// this to detect "is the mod installed at this path".
    /// </summary>
    public string InstallProbeFile { get; set; } = "";

    /// <summary>
    /// Filename of the .exe to launch when the user hits PLAY. Resolved
    /// relative to the install folder for in-place mods, or to the mod's
    /// own folder for isolated mods.
    /// </summary>
    public string GameExecutable { get; set; } = "";

    /// <summary>Optional command-line arguments passed to the executable.</summary>
    public string GameArguments { get; set; } = "";

    /// <summary>How the launcher pulls new versions of this mod.</summary>
    public ModUpdateMechanism UpdateMechanism { get; set; } = ModUpdateMechanism.Manual;

    /// <summary>
    /// Registry subkey used for the Add/Remove Programs entry. When empty,
    /// <see cref="EffectiveProductGuid"/> derives a stable key from
    /// <see cref="Id"/> (e.g. <c>"improvement-mod_launcher"</c>). Built-in
    /// WoL keeps its Inno Setup GUID
    /// (<c>"{EB448764-CABB-4766-8055-495AEA292020}_is1"</c>) for backwards
    /// compatibility with existing installs. Community mods can leave this
    /// blank or set a stable string in their <c>mod.json</c>.
    /// </summary>
    public string ProductGuid { get; set; } = "";

    /// <summary>
    /// Registry subkey to use for Add/Remove Programs entries. Honours an
    /// explicit <see cref="ProductGuid"/> when set; otherwise falls back to
    /// <c>"&lt;id&gt;_launcher"</c>. Either way it's a stable string for a
    /// given mod id, so future uninstalls find the same key the install
    /// wrote.
    /// </summary>
    public string EffectiveProductGuid =>
        string.IsNullOrEmpty(ProductGuid) ? $"{Id}_launcher" : ProductGuid;

    /// <summary>Settings for the WoL-style updater. Used only when <see cref="UpdateMechanism"/> = <see cref="ModUpdateMechanism.WolPatcher"/>.</summary>
    public WolPatcherSettings? Wol { get; set; }

    /// <summary>
    /// Settings for the "Pin to GitHub Release" update flow. Used only
    /// when <see cref="UpdateMechanism"/> =
    /// <see cref="ModUpdateMechanism.GitHubReleases"/>. Carries the
    /// modder's source-repo identifier and the approved release tag the
    /// launcher should download from.
    /// </summary>
    public GitHubReleasesSettings? GitHubReleases { get; set; }

    /// <summary>
    /// Translations overlay configuration. <c>null</c> means the mod
    /// doesn't participate in the community-translation system.
    /// </summary>
    public TranslationsSettings? Translations { get; set; }
}
