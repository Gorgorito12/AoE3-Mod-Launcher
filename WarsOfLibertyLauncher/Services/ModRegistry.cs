using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Source of truth for "which mods does this launcher know about". Two
/// layers:
///
///   1. <see cref="_builtIn"/> — embedded at compile time. Wars of Liberty
///      plus the stock Age of Empires III (detect-only) base-game entry.
///      Always available, no network needed. The launcher boots from
///      this list so the UI is never empty even on a fresh install with no
///      internet. WoL stays hardcoded because it's the launcher's reason
///      for existing and we don't want a catalog outage to leave a fresh
///      install with zero mods to show.
///
///   2. <see cref="_runtime"/> — populated lazily by
///      <see cref="RefreshFromCatalogAsync(string, CancellationToken)"/>
///      from the community catalog repo
///      (<c>Gorgorito12/aoe3-mods-catalog</c>). When set, replaces the
///      built-in list as the source of <see cref="All"/>. The built-in
///      list is folded into it on merge: built-in always wins on id
///      collisions, so a community PR cannot shadow the official "wol"
///      entry to redirect downloads (defence-in-depth on top of the
///      catalog's own PR review).
///
/// Improvement Mod (and any other community mod) lives only in the
/// catalog — no built-in shadow entry. A cold start without network shows
/// just WoL until the catalog fetch completes; the 24h cache means after
/// the first successful fetch the community mods are available offline.
/// </summary>
public static class ModRegistry
{
    /// <summary>Wars of Liberty — full updater pipeline + community translations.</summary>
    public const string WolId = "wol";

    /// <summary>
    /// Stock Age of Empires III: The Asian Dynasties — the unmodded base
    /// game. Detect-only: the launcher locates an existing install and runs
    /// it (single-player + Radmin multiplayer), but never installs, updates,
    /// or uninstalls it. See <see cref="ModProfile.IsStockGame"/>.
    /// </summary>
    public const string StockAoe3Id = "aoe3-tad";

    /// <summary>
    /// The hard-coded set. Never mutated after construction — treat as
    /// immutable. <see cref="RefreshFromCatalogAsync"/> never edits this;
    /// it builds a fresh merged list and assigns it to
    /// <see cref="_runtime"/>.
    /// </summary>
    private static readonly List<ModProfile> _builtIn = BuildBuiltInProfiles();

    /// <summary>
    /// Set once <see cref="RefreshFromCatalogAsync"/> has successfully
    /// fetched and merged the catalog. Reads/writes are guarded by
    /// <see cref="_runtimeLock"/> so a fetch in progress doesn't tear
    /// concurrent reads from the UI thread.
    /// </summary>
    private static volatile List<ModProfile>? _runtime;
    private static readonly object _runtimeLock = new();

    /// <summary>
    /// All known mods. Returns the merged runtime list once the catalog
    /// has been fetched, or the built-in list otherwise. Safe to call
    /// from any thread.
    /// </summary>
    public static IReadOnlyList<ModProfile> All
    {
        get
        {
            // Single volatile read avoids the lock on the hot path. The
            // assignment in RefreshFromCatalogAsync writes the new list
            // before flipping the reference, so any reader that observes
            // a non-null _runtime sees a fully-populated list.
            return _runtime ?? _builtIn;
        }
    }

    /// <summary>
    /// Returns the profile with the given id, or null when nothing matches.
    /// IDs are case-insensitive — config files written by hand often use
    /// different capitalization.
    /// </summary>
    public static ModProfile? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return All.FirstOrDefault(
            p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the profile to use when nothing is specified. Today: WoL,
    /// because the launcher started its life there and the existing
    /// installed user base expects it. When the launcher gets a proper
    /// "first-run picker" UI this will go away.
    /// </summary>
    public static ModProfile Default => Find(WolId) ?? All[0];

    /// <summary>
    /// True when the given id matches one of the hard-coded built-in
    /// profiles (WoL and the stock Age of Empires III entry). Built-ins are always treated as
    /// part of the user's mod collection — they can't be removed via
    /// the Workshop because the launcher would have nothing to show
    /// otherwise on a fresh install.
    /// </summary>
    public static bool IsBuiltIn(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return _builtIn.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    // -- Catalog refresh -------------------------------------------------------

    /// <summary>The catalog the launcher ships with when the config doesn't name one.</summary>
    public const string DefaultCatalogRepo = "Gorgorito12/aoe3-mods-catalog";

    /// <summary>
    /// Turns the config's <c>modsCatalogRepo</c> into the repo to actually query:
    /// empty → the shipped default, <c>"none"</c> → null (opt-out), anything else
    /// → itself. Shared by <see cref="PrimeFromCache"/> and the async refresh so the
    /// two can never disagree about WHICH catalog they're talking about — a
    /// divergence there would make the startup prime read a different cache file
    /// than the refresh writes, and the saved mod would silently fail to resolve.
    /// </summary>
    public static string? ResolveCatalogRepo(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultCatalogRepo;
        if (string.Equals(configured, "none", StringComparison.OrdinalIgnoreCase)) return null;
        return configured;
    }

    /// <summary>
    /// Publishes the merged list from the ON-DISK cache only — no network, no
    /// background refresh — so <see cref="All"/> already knows the community mods
    /// before the first caller needs to resolve one. Returns true when a cache was
    /// applied.
    ///
    /// WHY this exists: the saved active mod is resolved through <see cref="Find"/>
    /// in MainWindow's constructor, but the catalog refresh only runs later (the
    /// Loaded handler's Task.WhenAll). Until then <see cref="All"/> is built-ins
    /// only, so a COMMUNITY mod id never resolved and <see cref="Default"/> (WoL)
    /// was used instead — silently, and never reconciled once the catalog landed.
    /// The launcher therefore could not open on a community mod at all.
    ///
    /// Deliberately IGNORES the cache TTL: this pass only resolves mod IDENTITY, and
    /// the normal refresh right afterwards re-merges and handles staleness (including
    /// kicking the background fetch). Safe by construction with respect to
    /// <c>ClearVanishedAssets</c>: that only runs when a PREVIOUS merge existed, and
    /// this is always the first one. Never throws — a corrupt cache must not stop the
    /// launcher from starting.
    /// </summary>
    public static bool PrimeFromCache(string? repo)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repo)) return false;

            var cache = new ModCatalogService().LoadFromCache(repo);
            if (cache == null || cache.Manifests.Count == 0)
            {
                DiagnosticLog.Write(
                    "ModRegistry: no catalog cache to prime from — community mods " +
                    "resolve only after the startup refresh.");
                return false;
            }

            ApplyMerged(cache.Manifests, default);
            DiagnosticLog.Write(
                $"ModRegistry: primed from cache ({cache.Manifests.Count} entries) so the " +
                "saved active mod can resolve before the catalog refresh.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModRegistry: prime from cache failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fetches the community mods catalog and merges it with the built-in
    /// list. Returns the merged list (also accessible via <see cref="All"/>
    /// from any thread once this completes). On any failure (no network,
    /// rate limit, malformed manifest) returns the built-in list and the
    /// runtime cache stays empty — callers can treat null/exception cases
    /// the same as "use built-in".
    ///
    /// Idempotent: repeated calls do not duplicate entries. Each call
    /// overwrites the runtime list with a freshly merged one — perfect
    /// for a periodic refresh.
    /// </summary>
    /// <param name="repo">
    /// owner/repo of the catalog (e.g. <c>Gorgorito12/aoe3-mods-catalog</c>).
    /// Empty / null disables the fetch and is treated as "stay on
    /// built-in only" — useful for kiosk deployments or for users who
    /// don't want their launcher reaching out to GitHub.
    /// </param>
    public static async Task<IReadOnlyList<ModProfile>> RefreshFromCatalogAsync(
        string? repo, CancellationToken ct = default, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            DiagnosticLog.Write("ModRegistry: catalog disabled (empty repo) — using built-in only.");
            return _builtIn;
        }

        var service = new ModCatalogService();

        // ---- Cache-first path -----------------------------------------------
        //
        // Fast path: if a cache file exists and is still within TTL, render
        // from it and skip the network entirely. This is the common case for
        // anyone who launches the app more than once in a 24h window.
        //
        // Cold path: no cache at all → fall through to the online fetch.
        //
        // Stale path: cache exists but TTL expired → render from the cached
        // copy immediately (so the UI is instant) AND kick a background
        // refresh that updates the on-disk cache for the next session. The
        // user doesn't have to wait for the refresh; if it lands while
        // they're still in the app the in-memory runtime list is updated
        // too, so a subsequent RefreshModCards call picks up any new entries.
        //
        // force=true skips BOTH cache branches and re-fetches online now — used
        // by the manual "Actualizar" button and the periodic/focus refresh so a
        // catalog edit shows up without waiting for the 24h TTL. The online
        // fetch is one GitHub API call (rate-limited 60/h per IP), so callers
        // throttle how often they force.

        var cache = force ? null : service.LoadFromCache(repo);
        if (cache != null && service.IsFresh(cache))
        {
            DiagnosticLog.Write(
                $"ModRegistry: using fresh cache ({cache.Manifests.Count} entries, " +
                $"fetched {cache.FetchedAt:o}).");
            return ApplyMerged(cache.Manifests, ct);
        }

        if (cache != null)
        {
            DiagnosticLog.Write(
                $"ModRegistry: cache is stale (fetched {cache.FetchedAt:o}, TTL " +
                $"{ModCatalogService.CacheTtl}) — using it for now, refreshing in background.");
            var staleMerged = ApplyMerged(cache.Manifests, ct);
            _ = Task.Run(() => BackgroundRefreshAsync(repo!));
            return staleMerged;
        }

        // ---- Cold online fetch ----------------------------------------------

        List<ModCatalogEntry>? remote;
        try
        {
            remote = await service.FetchAsync(repo!, ct);
        }
        catch (Exception ex)
        {
            // FetchAsync already swallows HttpRequestException internally and
            // logs; this catches anything more unusual (cancellation,
            // unexpected JSON shapes). Keep going with built-ins.
            DiagnosticLog.Write($"ModRegistry: catalog fetch threw: {ex.Message}");
            return _builtIn;
        }

        if (remote == null)
        {
            // Service signalled "couldn't reach the catalog" cleanly. The
            // user may be offline or rate-limited; fall back silently.
            return _builtIn;
        }

        // FetchAsync already persisted the new cache; nothing for us to do
        // on disk. Just merge in memory and return.
        return ApplyMerged(remote, ct);
    }

    /// <summary>
    /// Builds the built-in + community merge from a raw entries list,
    /// publishes it into <see cref="_runtime"/>, and returns it. Shared
    /// between the cache-hit, stale-cache, and cold-fetch paths so the
    /// merge rules (built-in id collisions win, bad projections skipped)
    /// stay consistent.
    /// </summary>
    private static IReadOnlyList<ModProfile> ApplyMerged(
        List<ModCatalogEntry> entries, CancellationToken ct)
    {
        var builtInIds = new HashSet<string>(
            _builtIn.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        var merged = new List<ModProfile>(_builtIn);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry?.Manifest == null || string.IsNullOrEmpty(entry.Manifest.Id))
                continue;

            if (builtInIds.Contains(entry.Manifest.Id))
            {
                // The built-in still wins on everything that matters — the entry
                // is never projected, so it cannot redirect downloads or paths.
                // `links` is the one whitelisted exception: cosmetic, sanitised,
                // and already gated by the catalog CI's per-mod ownership check.
                ApplyBuiltInCosmeticOverlay(entry.Manifest);
                DiagnosticLog.Write(
                    $"ModRegistry: catalog entry '{entry.Manifest.Id}' shadows a built-in — " +
                    "ignoring everything but links (built-in wins).");
                continue;
            }

            try
            {
                merged.Add(ProjectToProfile(entry));
            }
            catch (Exception ex)
            {
                // One bad manifest projection shouldn't drop every other
                // catalog entry. The classify_pr.py CI keeps obviously-
                // broken manifests out of main, so this is a backstop for
                // malformed-but-schema-valid edge cases.
                DiagnosticLog.Write(
                    $"ModRegistry: skipping catalog entry '{entry.Manifest.Id}': {ex.Message}");
            }
        }

        List<ModProfile>? previous;
        lock (_runtimeLock)
        {
            previous = _runtime;
            _runtime = merged;
        }

        // Reclaim disk for community mods that dropped out of the catalog since
        // the previous merge. Built-ins and the stock game never drop, so their
        // cached assets are never touched.
        if (previous != null)
            ClearVanishedAssets(previous, merged);

        DiagnosticLog.Write(
            $"ModRegistry: refresh complete — {_builtIn.Count} built-in + " +
            $"{merged.Count - _builtIn.Count} community = {merged.Count} total.");
        return merged;
    }

    /// <summary>
    /// Lets a catalog entry that shadows a built-in contribute its
    /// <c>links</c> — and nothing else — to that built-in profile.
    ///
    /// Built-ins are hard-coded and never pass through
    /// <see cref="ProjectToProfile"/>, so without this the community-links row
    /// could only be given to WoL by editing this file and shipping a release —
    /// a Discord invite change would need a new binary. Widening the shadow rule
    /// by exactly one COSMETIC field keeps the property the rule exists for: the
    /// entry is still never projected, so it cannot touch install paths, payload
    /// urls or the update mechanism. The field is safe to accept because it is
    /// already defended twice over — the catalog CI's per-mod ownership gate
    /// (only a mod's declared <c>maintainers</c> can auto-merge it) and
    /// <see cref="ModLink.Sanitize"/> on this side, which the launcher applies
    /// regardless of what CI did.
    /// </summary>
    private static void ApplyBuiltInCosmeticOverlay(ModCatalogManifest manifest)
        => ApplyCosmeticOverlay(_builtIn, manifest);

    /// <summary>
    /// The pure half of <see cref="ApplyBuiltInCosmeticOverlay"/>, split out so
    /// it can be tested without touching the static built-in list.
    /// </summary>
    /// <remarks>
    /// The assignment is UNCONDITIONAL on purpose — including when the manifest
    /// ships no links at all. <c>_builtIn</c> is a <c>static readonly</c> list
    /// built once, and <see cref="ApplyMerged"/> copies the LIST but not the
    /// profiles, so this mutates the singleton and the value survives every
    /// later merge. Always assigning makes the overlay idempotent and
    /// self-correcting: dropping a link from the manifest drops it from the UI
    /// on the next refresh. Guarding this with <c>if (manifest.Links != null)</c>
    /// would leave phantom links alive until the process restarts.
    /// </remarks>
    internal static void ApplyCosmeticOverlay(
        IEnumerable<ModProfile> targets, ModCatalogManifest? manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) return;

        foreach (var profile in targets)
        {
            if (!string.Equals(profile.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            profile.Links = ModLink.Sanitize(manifest.Links);
            return;
        }
    }

    /// <summary>
    /// Deletes the on-disk asset cache (icon/banner/hero/screenshots) for any
    /// community mod present in <paramref name="previous"/> but absent from
    /// <paramref name="current"/> — i.e. removed from the catalog. Best-effort;
    /// a leftover asset on disk is harmless, so all failures are swallowed.
    /// Built-ins and the stock game are excluded (they never leave the list).
    /// </summary>
    private static void ClearVanishedAssets(
        IReadOnlyList<ModProfile> previous, IReadOnlyList<ModProfile> current)
    {
        try
        {
            var live = new HashSet<string>(
                current.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var vanished = previous
                .Where(p => !string.IsNullOrEmpty(p.Id)
                            && !p.IsStockGame
                            && !IsBuiltIn(p.Id)
                            && !live.Contains(p.Id))
                .Select(p => p.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (vanished.Count == 0) return;

            var cache = new ModAssetCacheService();
            foreach (var id in vanished)
            {
                cache.Clear(id);
                DiagnosticLog.Write($"ModRegistry: cleared cached assets for vanished mod '{id}'.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModRegistry: clear vanished assets failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fire-and-forget refresh path used when a stale cache is rendered.
    /// Fetches the catalog online (which also rewrites the cache file via
    /// <see cref="ModCatalogService.FetchAsync"/>), then re-publishes the
    /// merged runtime list. Failures here are silent — the user already
    /// has the stale cache rendered, and the next refresh attempt will try
    /// again. Doesn't take a CancellationToken because nobody upstream
    /// awaits this task.
    /// </summary>
    private static async Task BackgroundRefreshAsync(string repo)
    {
        try
        {
            var service = new ModCatalogService();
            var entries = await service.FetchAsync(repo, default);
            if (entries == null)
            {
                DiagnosticLog.Write("ModRegistry: background refresh got no entries — keeping stale cache.");
                return;
            }
            ApplyMerged(entries, default);
            DiagnosticLog.Write("ModRegistry: background refresh complete.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ModRegistry: background refresh failed: {ex.Message}");
        }
    }

    // -- Projection ------------------------------------------------------------

    /// <summary>
    /// Translates a fetched catalog manifest into a runtime
    /// <see cref="ModProfile"/>. Handles the string→enum mapping
    /// (<c>"IsolatedFolder"</c> → <see cref="ModInstallType.IsolatedFolder"/>,
    /// etc.) and lifts nested settings (<c>update.wol</c>,
    /// <c>translations</c>) into their typed counterparts.
    ///
    /// Throws on values the schema would have rejected (unknown enum
    /// strings) — those should never reach here in practice because the
    /// catalog's CI runs <c>ajv validate</c> before merging, but being
    /// defensive lets a malformed manifest skip cleanly via the catch in
    /// the caller rather than producing a silently-wrong profile.
    /// </summary>
    internal static ModProfile ProjectToProfile(ModCatalogEntry entry)
    {
        var m = entry.Manifest;

        var installType = ParseInstallType(m.Install.Type);
        var updateMechanism = ParseUpdateMechanism(m.Update.Mechanism);

        var profile = new ModProfile
        {
            Id = m.Id,
            DisplayName = string.IsNullOrEmpty(m.DisplayName) ? m.Id : m.DisplayName,
            Subtitle = m.Subtitle ?? "",
            AccentColor = string.IsNullOrEmpty(m.AccentColor) ? "#3a8cd9" : m.AccentColor,
            Author = m.Author ?? "",
            OfficialWebsite = m.OfficialWebsite ?? "",
            // Sanitised HERE, not at render time, so every consumer can treat
            // profile.Links as already-safe. See ModLink.Sanitize for why this
            // repeats the catalog CI's rules.
            Links = ModLink.Sanitize(m.Links),
            Description = m.Description,
            ProductGuid = m.InstallProductGuid ?? "",
            UserDataFolder = m.UserDataFolder ?? "",
            // Built-in pack URI stays null for community mods — they use
            // IconUrl/BannerUrl/HeroImageUrl resolved against the catalog repo.
            BannerImage = null,
            IconUrl = entry.IconUrl,
            BannerUrl = entry.BannerUrl,
            HeroImageUrl = entry.HeroImageUrl,
            HeroImageUrls = entry.HeroImageUrls ?? new(),
            ScreenshotUrls = entry.ScreenshotUrls ?? new(),
            InstallType = installType,
            DefaultInstallFolder = m.Install.DefaultFolder ?? "",
            InstallProbeFile = m.Install.ProbeFile ?? "",
            InstallMarker = m.Install.Marker ?? "",
            GameExecutable = m.Install.Executable ?? "",
            GameArguments = m.Install.Arguments ?? "",
            UserDataRedirect = m.Install.UserDataRedirect,
            UpdateMechanism = updateMechanism,
        };

        if (updateMechanism == ModUpdateMechanism.WolPatcher && m.Update.Wol != null)
        {
            profile.Wol = new WolPatcherSettings
            {
                UpdateInfoUrl = m.Update.Wol.UpdateInfoUrl ?? "",
                UpdateInfoUrlAlt = m.Update.Wol.UpdateInfoUrlAlt ?? "",
                OfficialWebsite = m.OfficialWebsite ?? "",
                PayloadZipUrls = m.Update.Wol.PayloadZipUrls ?? Array.Empty<string>(),
            };
        }

        if (updateMechanism == ModUpdateMechanism.GitHubReleases)
        {
            // The schema places sourceRepo + approvedReleaseTag at the top
            // of the manifest (not inside update.*), because they identify
            // the mod's authoritative GitHub repo and could in principle
            // be used by other update mechanisms in the future. The
            // launcher only acts on them when mechanism == GitHubReleases,
            // though — for other mechanisms they're informational.
            //
            // A manifest declaring GitHubReleases as its mechanism but
            // missing one of these fields would have been rejected by the
            // catalog's CI; still, we treat them defensively here and
            // leave GitHubReleases settings null if either is empty so
            // the launcher's install pipeline can skip cleanly instead of
            // hitting an HTTP 404 against an empty URL. approvedReleaseTag
            // stays required even for followLatest mods: it's the only tag
            // installable with no network/cached state, and the fallback
            // when the /releases/latest resolution fails.
            if (!string.IsNullOrEmpty(m.SourceRepo)
                && !string.IsNullOrEmpty(m.ApprovedReleaseTag))
            {
                profile.GitHubReleases = new GitHubReleasesSettings
                {
                    SourceRepo = m.SourceRepo!,
                    ApprovedReleaseTag = m.ApprovedReleaseTag!,
                    // AssetNamePattern is a schema extension we may add
                    // later (top-level "assetNamePattern" field). For now
                    // the downloader's "first .zip wins" default covers
                    // every mod we've seen.
                    AssetNamePattern = "",
                    // External-hosting fields ride inside update.github.
                    // The downloader treats an empty template as "use the
                    // regular GitHub asset"; only modders who actively
                    // host elsewhere set these.
                    ExternalAssetUrlTemplate = m.Update.Github?.ExternalAssetUrlTemplate ?? "",
                    ExternalAssetSha256 = (m.Update.Github?.ExternalAssetSha256 ?? "").ToLowerInvariant(),
                    // Opt-in incremental delta patches (only the changed files) on normal updates.
                    DeltaPatches = m.Update.Github?.DeltaPatches ?? false,
                    // Opt-in "follow the newest stable release" (see the
                    // FollowLatest doc-comment for the seed/fallback rules).
                    FollowLatest = m.Update.Github?.FollowLatest ?? false,
                };
            }
        }

        if (m.Translations != null && !string.IsNullOrEmpty(m.Translations.Repo))
        {
            profile.Translations = new TranslationsSettings
            {
                Repo = m.Translations.Repo,
                FolderRepo = m.Translations.FolderRepo ?? "",
                CoveredFiles = m.Translations.CoveredFiles ?? new List<string>(),
            };
        }

        return profile;
    }

    private static ModInstallType ParseInstallType(string? raw) => raw switch
    {
        "IsolatedFolder" => ModInstallType.IsolatedFolder,
        "InPlaceOverlay" => ModInstallType.InPlaceOverlay,
        _ => throw new ArgumentException($"Unknown install.type: '{raw}'"),
    };

    private static ModUpdateMechanism ParseUpdateMechanism(string? raw) => raw switch
    {
        "WolPatcher" => ModUpdateMechanism.WolPatcher,
        "DelegatedExternal" => ModUpdateMechanism.DelegatedExternal,
        "GitHubReleases" => ModUpdateMechanism.GitHubReleases,
        "Manual" => ModUpdateMechanism.Manual,
        // Anything else is either a typo on the modder's side or a future
        // field we don't yet support — fall back to Manual rather than
        // throwing, so the launcher at least lists the mod even if it
        // doesn't yet know how to update it.
        _ => ModUpdateMechanism.Manual,
    };

    // -- Built-in profiles -----------------------------------------------------

    private static List<ModProfile> BuildBuiltInProfiles() => new()
    {
        new ModProfile
        {
            Id = WolId,
            DisplayName = "Wars of Liberty",
            Subtitle = "Launcher",
            AccentColor = "#c8102e",
            // Preserve the original Inno Setup product GUID so the
            // launcher continues to find Add/Remove Programs entries written
            // by older builds and the (legacy) Inno installer.
            ProductGuid = "{EB448764-CABB-4766-8055-495AEA292020}_is1",
            Author = "Wars of Liberty Team",
            // Templated into error / status messages that tell the user
            // where to re-download the mod from when an update fails.
            OfficialWebsite = "http://aoe3wol.com/",
            // Links are deliberately NOT set here: they come from the catalog's
            // mods/wol/mod.json via ApplyBuiltInCosmeticOverlay, so changing a
            // Discord invite is a manifest edit, not a new release.
            // Shown on the dashboard hero + Workshop detail, and mirrored in
            // the catalog's mods/wol/mod.json. Without it the dashboard would
            // fall back to the bare "Launcher" subtitle as its description.
            Description = new Dictionary<string, string>
            {
                ["en"] = "A free, community-made total conversion for Age of Empires III: The Asian Dynasties, set in the turbulent 19th century — wars of independence and colonial struggles across the Americas and beyond. Adds a huge roster of new civilizations (the United States, Mexico, Argentina, Brazil and many more), each with their own units, home cities, maps and mechanics.",
                ["es"] = "Conversión total gratuita hecha por la comunidad para Age of Empires III: The Asian Dynasties, ambientada en el convulso siglo XIX — guerras de independencia y luchas coloniales en América y más allá. Agrega un enorme elenco de civilizaciones nuevas (Estados Unidos, México, Argentina, Brasil y muchas más), cada una con sus propias unidades, metrópolis, mapas y mecánicas.",
            },
            // WoL keeps its own save / metropolis folder under Documents.
            // Enabling this turns on the pre-install user-data backup alert
            // and the gear menu's "User data" submenu for this profile.
            UserDataFolder = "Wars of Liberty",
            // WoL.ico ships as a pack-resource specifically for this
            // tile so the WoL profile shows the real Wars of Liberty
            // logo instead of the "W" placeholder. Used to double as
            // the launcher's app icon too, but the launcher rebranded
            // to AppIcon.ico — WoL.ico stayed because it IS the WoL
            // mod's identity.
            BannerImage = "pack://application:,,,/WoL.ico",
            // Catalog-hosted icon override. The WoL Team can swap the icon by
            // committing mods/wol/icon.png to the catalog — no recompile.
            // Resolution is LocalIconPath (this, once fetched/cached) →
            // BannerImage (the embedded WoL.ico above), so if the catalog file
            // 404s or there's no internet, the packed icon still shows.
            // Best of both: editable from the catalog, offline-safe fallback.
            IconUrl = "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/wol/icon.png",
            // Dashboard hero image. Loaded lazily by EnsureModAssetsAsync
            // and painted behind the title + PLAY button. Points at the
            // catalog repo's raw URL so the WoL Team can update the hero
            // by committing a new hero.jpg to /mods/wol/ in the catalog —
            // no launcher recompile needed. Until that file lands the
            // download silently 404s, EnsureModAssetsAsync logs it, and
            // the dashboard falls through to the neutral dark gradient.
            HeroImageUrl = "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/wol/hero.jpg",
            InstallType = ModInstallType.IsolatedFolder,
            // Empty on purpose: lets the install dialog fall through to the
            // "sibling of detected AoE3" default (parent of AoE3 + this mod's
            // DisplayName). For Steam users this resolves to e.g.
            // `…\steamapps\common\Wars of Liberty`, sitting alongside AoE3
            // rather than inside Program Files. The user can override in the
            // dialog if they want a custom location.
            DefaultInstallFolder = "",
            InstallProbeFile = @"data\stringtabley.xml",
            // Content marker unique to WoL (absent from vanilla AoE3): lets the
            // launcher recognise a WoL install in a folder with ANY name, and
            // tells a real WoL folder apart from the base game (whose data\
            // files satisfy the probe too). Same marker the original Java
            // updater and RegistryService.IsValidInstall check.
            InstallMarker = @"art\zulushield",
            GameExecutable = "age3y.exe",
            GameArguments = "",
            UpdateMechanism = ModUpdateMechanism.WolPatcher,
            Wol = new WolPatcherSettings
            {
                // HTTPS is the PRIMARY: aoe3wol's HTTP endpoint returns a
                // truncated ~7 KB body (fails XML parse) — consistently as of this
                // writing — while HTTPS serves the correct, complete file (verified,
                // 47 versions). HTTP stays as the alt in case it recovers. (The old
                // alt was a SourceForge mirror frozen at 1.0.9h; falling back to that
                // ancient file made a valid 1.2.0e install read as unrecognized → a
                // spurious reinstall prompt — that's the bug this pair fixes.)
                UpdateInfoUrl = "https://aoe3wol.com/updates/UpdateInfo.xml",
                UpdateInfoUrlAlt = "http://aoe3wol.com/updates/UpdateInfo.xml",
                OfficialWebsite = "http://aoe3wol.com/",
                PayloadZipUrls = new[]
                {
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
                    "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003",
                },
            },
            Translations = new TranslationsSettings
            {
                // Legacy release-published packs (kept for dual mode).
                Repo = "papillo12/translations",
                // New folder-published packs (translations/<id>/ on main) in the
                // dedicated translations repo. Read alongside the legacy releases.
                FolderRepo = "Gorgorito12/translations",
                CoveredFiles = new List<string>
                {
                    @"data\stringtabley.xml",
                    @"data\unithelpstringsy.xml",
                },
            },
        },

        // Stock Age of Empires III: The Asian Dynasties. Detect-only — the
        // launcher never installs/updates/uninstalls the base game (legal:
        // it's the user's own copy). It's modelled as an InPlaceOverlay with
        // a Manual update mechanism purely so the existing detection +
        // launch + "Ready to play" UI paths light up for free:
        //   * InPlaceOverlay → ResolveInstallPath's disk scan probes the
        //     detected AoE3 install's own folder (ModRoot) for the probe
        //     file, which is exactly where the base game lives.
        //   * Manual         → ApplyCheckResult takes the non-WolPatcher
        //     branch and shows PLAY (no Install/Update buttons) when the
        //     install is valid.
        // IsStockGame=true is what actually guards the destructive paths
        // (Install/Update/Repair/Uninstall) — see ModProfile.IsStockGame.
        new ModProfile
        {
            Id = StockAoe3Id,
            DisplayName = "Age of Empires III: The Asian Dynasties",
            Subtitle = "Original base game",
            // Gold/bronze to set it apart from WoL's red and read as
            // "the classic game".
            AccentColor = "#caa14a",
            Author = "Ensemble Studios, Big Huge Games",
            OfficialWebsite = "https://www.ageofempires.com/games/aoeiii/",
            IsStockGame = true,
            // Real description, mirrored in the catalog repo at
            // mods/aoe3-tad/mod.json. Rendered under the dashboard hero title
            // (DashboardDescText). EN/ES like every other profile.
            Description = new Dictionary<string, string>
            {
                ["en"] = "The second expansion to Age of Empires III, by Ensemble Studios and Big Huge Games. The Asian Dynasties adds three Asian civilizations — Japanese, Chinese and Indians — that advance through the ages by raising Wonders, plus new campaigns, maps, the Consulate, and naval and trade additions. This is the unmodded base game: the launcher only detects and runs your own legally-owned copy, it never installs it.",
                ["es"] = "La segunda expansión de Age of Empires III, de Ensemble Studios y Big Huge Games. The Asian Dynasties suma tres civilizaciones asiáticas —japoneses, chinos e indios— que avanzan de edad construyendo Maravillas, además de nuevas campañas, mapas, el Consulado y agregados navales y de comercio. Es el juego base sin mods: el launcher solo detecta y ejecuta tu propia copia legal, nunca lo instala.",
            },
            // Branding assets are hosted in the catalog repo, same pattern as
            // WoL: drop a 1920x1080 hero.jpg / 256x256 icon.png into
            // mods/aoe3-tad/ there and they appear automatically. Until then
            // the dashboard uses the accent gradient + a monogram (the 404s
            // are swallowed by EnsureModAssetsAsync).
            HeroImageUrl = "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/aoe3-tad/hero.jpg",
            IconUrl = "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/aoe3-tad/icon.png",
            InstallType = ModInstallType.InPlaceOverlay,
            // Empty: there is no install dialog for the stock game.
            DefaultInstallFolder = "",
            // A TAD data file that sits at the install ROOT in every store
            // layout (Steam/GOG/retail all keep data\ at the root, with the
            // exe under bin\ on Steam). It's also one of the three files the
            // multiplayer fingerprint hashes, so "detected on disk" implies
            // "fingerprintable for multiplayer".
            InstallProbeFile = @"data\protoy.xml",
            GameExecutable = "age3y.exe",
            GameArguments = "",
            UpdateMechanism = ModUpdateMechanism.Manual,
            // No UserDataFolder on purpose: the launcher doesn't manage the
            // base game's saves/replays (no backup/restore prompts for it).
        },
    };
}
