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
///   1. <see cref="_builtIn"/> — embedded at compile time. WoL + Improvement
///      Mod. Always available, no network needed. The launcher boots from
///      this list so the UI is never empty even on a fresh install with no
///      internet.
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
/// Phase 1B note: <see cref="RefreshFromCatalogAsync"/> exists but is not
/// yet called from anywhere in the launcher. Phase 1C wires it into
/// MainWindow's startup. Until then, the launcher's behaviour is
/// identical to before — only the built-in list is observable.
/// </summary>
public static class ModRegistry
{
    /// <summary>Wars of Liberty — full updater pipeline + community translations.</summary>
    public const string WolId = "wol";

    /// <summary>Improvement Mod — uses its own external patcher; the launcher only plays.</summary>
    public const string ImprovementModId = "improvement-mod";

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

    // -- Catalog refresh -------------------------------------------------------

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
        string? repo, CancellationToken ct = default)
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

        var cache = service.LoadFromCache(repo);
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
                DiagnosticLog.Write(
                    $"ModRegistry: catalog entry '{entry.Manifest.Id}' shadows a built-in — ignoring (built-in wins).");
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

        lock (_runtimeLock)
        {
            _runtime = merged;
        }

        DiagnosticLog.Write(
            $"ModRegistry: refresh complete — {_builtIn.Count} built-in + " +
            $"{merged.Count - _builtIn.Count} community = {merged.Count} total.");
        return merged;
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
    private static ModProfile ProjectToProfile(ModCatalogEntry entry)
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
            Description = m.Description,
            // Built-in pack URI stays null for community mods — they use
            // IconUrl/BannerUrl resolved against the catalog repo.
            BannerImage = null,
            IconUrl = entry.IconUrl,
            BannerUrl = entry.BannerUrl,
            InstallType = installType,
            DefaultInstallFolder = m.Install.DefaultFolder ?? "",
            InstallProbeFile = m.Install.ProbeFile ?? "",
            GameExecutable = m.Install.Executable ?? "",
            GameArguments = m.Install.Arguments ?? "",
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
            // hitting an HTTP 404 against an empty URL.
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
                };
            }
        }

        if (m.Translations != null && !string.IsNullOrEmpty(m.Translations.Repo))
        {
            profile.Translations = new TranslationsSettings
            {
                Repo = m.Translations.Repo,
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
            // Reuse the launcher's app icon (WoL.ico, registered as a
            // pack-resource in the .csproj) so the WoL tile shows the real
            // logo instead of the "W" placeholder.
            BannerImage = "pack://application:,,,/WoL.ico",
            InstallType = ModInstallType.IsolatedFolder,
            DefaultInstallFolder = @"C:\Program Files (x86)\Wars of Liberty",
            InstallProbeFile = @"data\stringtabley.xml",
            GameExecutable = "age3y.exe",
            GameArguments = "",
            UpdateMechanism = ModUpdateMechanism.WolPatcher,
            Wol = new WolPatcherSettings
            {
                UpdateInfoUrl = "http://aoe3wol.com/updates/UpdateInfo.xml",
                UpdateInfoUrlAlt =
                    "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
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
                Repo = "papillo12/translations",
                CoveredFiles = new List<string>
                {
                    @"data\stringtabley.xml",
                    @"data\unithelpstringsy.xml",
                },
            },
        },
        new ModProfile
        {
            Id = ImprovementModId,
            DisplayName = "Improvement Mod",
            Subtitle = "AoE3:TAD overhaul",
            // Bluish accent so it's visually distinct from WoL's red without
            // clashing with the launcher's dark theme.
            AccentColor = "#3a8cd9",
            BannerImage = null,
            // IM extracts its files INTO the AoE3 folder (or its bin\
            // subfolder for Steam) and ships its own age3m.exe alongside
            // the original AoE3 binaries.
            InstallType = ModInstallType.InPlaceOverlay,
            // No default — IM lives wherever AoE3 lives. The launcher
            // resolves AoE3's path separately and uses that.
            DefaultInstallFolder = "",
            InstallProbeFile = "age3m.exe",
            GameExecutable = "age3m.exe",
            GameArguments = "",
            // Updates are out of scope for the launcher — IM has its own
            // patcher (which IS age3m.exe, run with a flag). For now we
            // only expose PLAY; later we can shell out to the patcher.
            UpdateMechanism = ModUpdateMechanism.DelegatedExternal,
            Wol = null,
            // The IM team doesn't currently maintain a community translation
            // pipeline compatible with our overlay system. Leaving null hides
            // the language menu for this mod.
            Translations = null,
        },
    };
}
