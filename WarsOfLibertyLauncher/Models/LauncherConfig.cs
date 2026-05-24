using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// Multiplayer-specific persistent state. Lives nested under
/// <see cref="LauncherConfig.Multiplayer"/> so the JSON layout stays
/// flat at the top level even as the multiplayer feature grows.
/// </summary>
public class MultiplayerConfig
{
    /// <summary>
    /// Base URL of the lobby backend. The default points at the
    /// maintainer's self-hosted Node.js + Fastify deployment on an
    /// Oracle Cloud VM, fronted by DuckDNS + Let's Encrypt. Every
    /// fresh install hits this URL until the user explicitly
    /// overrides it in Settings. Power users can point at their
    /// own deployment by editing this field. Configs written by
    /// older launchers (which defaulted to the now-retired
    /// Cloudflare Worker URL) are auto-healed by
    /// <see cref="MigrateLobbyBaseUrl"/> on next load.
    /// </summary>
    [JsonPropertyName("lobbyBaseUrl")]
    public string LobbyBaseUrl { get; set; } = "https://wol-lobby.duckdns.org";

    /// <summary>
    /// Session JWT issued by the backend after a successful Discord
    /// sign-in. Empty when the user is not signed in (the Multiplayer
    /// tab will prompt them on first visit). Treat this like a
    /// password — it's a bearer credential.
    /// </summary>
    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = "";

    /// <summary>
    /// Unix seconds when the <see cref="SessionToken"/> stops being
    /// accepted by the backend. The launcher refreshes silently when the
    /// remaining lifetime drops below 24 h.
    /// </summary>
    [JsonPropertyName("sessionExpiresAt")]
    public long SessionExpiresAt { get; set; }

    /// <summary>
    /// Cached profile of the signed-in user — saves a /me round trip on
    /// every launcher start. Refreshed whenever the user signs in or
    /// when /me is called for any other reason.
    /// </summary>
    [JsonPropertyName("cachedUser")]
    public Multiplayer.LobbyUserSummary? CachedUser { get; set; }

    /// <summary>
    /// True once the user has clicked the "✕" on the Radmin VPN
    /// banner in the Multiplayer tab. Persists so the banner doesn't
    /// reappear every launch after the user has read it.
    /// </summary>
    [JsonPropertyName("radminBannerDismissed")]
    public bool RadminBannerDismissed { get; set; } = false;
}

/// <summary>
/// Per-mod state that has to survive launcher restarts AND has to be kept
/// separate per profile. Stored under <see cref="LauncherConfig.Mods"/>
/// keyed by mod id so switching between mods doesn't cross-contaminate
/// (e.g. so detecting Improvement Mod's install path doesn't overwrite
/// the Wars of Liberty install path the user already had cached).
/// </summary>
public class ModState
{
    /// <summary>
    /// Where this mod is installed on disk. Empty when the launcher hasn't
    /// found it yet — the next call to the install detector will populate it.
    /// </summary>
    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";

    /// <summary>
    /// ID of the community translation pack currently applied for this mod
    /// (e.g. "es", "fr"). Empty means the canonical English data is active.
    /// </summary>
    [JsonPropertyName("activeTranslationId")]
    public string ActiveTranslationId { get; set; } = "";

    /// <summary>
    /// Last mod version we detected, stored so the UI can show "Installed"
    /// with the right version number immediately after the user switches to
    /// this mod, without waiting for the async CheckAsync MD5-and-XML pass
    /// to complete. CheckAsync overwrites it with the freshly-computed value
    /// when it finishes. Empty means we have never detected a version for
    /// this profile (e.g. brand-new install, or mod whose UpdateMechanism
    /// isn't WolPatcher and so doesn't compute versions at all).
    /// </summary>
    [JsonPropertyName("lastKnownVersion")]
    public string LastKnownVersion { get; set; } = "";

    /// <summary>
    /// Last "latest version" we got from the mod's update server, cached so
    /// the "Latest version" row in the status card has a value to show
    /// immediately after a mod switch instead of waiting for the async
    /// CheckAsync HTTP fetch to complete. Empty until the first successful
    /// CheckAsync (or for non-WolPatcher mods that don't fetch a manifest).
    /// </summary>
    [JsonPropertyName("lastKnownLatestVersion")]
    public string LastKnownLatestVersion { get; set; } = "";
}

/// <summary>
/// Local launcher config. Most defaults match the official servers; the install
/// path is normally auto-detected from the Windows registry on first run.
/// </summary>
public class LauncherConfig
{
    /// <summary>
    /// ID of the mod profile the launcher last had selected (e.g. "wol",
    /// "improvement-mod"). Empty on a fresh config — the launcher resolves
    /// it to the registry's default profile at startup. Set whenever the
    /// user picks a different mod in the header dropdown.
    /// </summary>
    [JsonPropertyName("activeModId")]
    public string ActiveModId { get; set; } = "";

    /// <summary>
    /// Resolves <see cref="ActiveModId"/> to its full profile, falling
    /// back to <see cref="ModRegistry.Default"/> when the id is empty or
    /// unknown (e.g. user hand-edited the config with a typo).
    /// </summary>
    public ModProfile GetActiveProfile() =>
        ModRegistry.Find(ActiveModId) ?? ModRegistry.Default;

    /// <summary>
    /// Per-mod state (install path, active translation, etc.) keyed by
    /// <see cref="ModProfile.Id"/>. Replaces the old shared root-level
    /// fields like <c>modInstallPath</c> and <c>activeTranslationId</c> so
    /// switching mods doesn't overwrite data belonging to another mod.
    /// Created lazily by <see cref="GetState(string)"/>.
    /// </summary>
    [JsonPropertyName("mods")]
    public Dictionary<string, ModState> Mods { get; set; } = new();

    /// <summary>
    /// Returns the persistent state record for a given mod id, creating an
    /// empty one if it doesn't exist yet. The returned reference is the
    /// live one stored in <see cref="Mods"/> — modifying its fields and
    /// then calling <see cref="Save"/> persists the change.
    /// </summary>
    public ModState GetState(string modId)
    {
        if (string.IsNullOrEmpty(modId)) modId = ModRegistry.Default.Id;
        if (!Mods.TryGetValue(modId, out var state))
        {
            state = new ModState();
            Mods[modId] = state;
        }
        return state;
    }

    /// <summary>Convenience overload: state of the currently active profile.</summary>
    public ModState GetActiveState() => GetState(GetActiveProfile().Id);

    /// <summary>
    /// Returns every non-empty install path currently registered for a
    /// mod profile OTHER than <paramref name="excludeModId"/>. Used by
    /// the install pipeline as the canonical "sibling-mod exclusion
    /// list" so a fresh install of mod B never scoops up the on-disk
    /// folder of mod A that happens to live inside the same AoE3 root.
    ///
    /// Centralised here (instead of inlined at each call site) so that
    /// every install / repair / update entry point uses the same rule —
    /// future code paths just call this method and get the same
    /// exclusion behaviour the WoL → Improvement Mod install fix
    /// introduced.
    /// </summary>
    public IReadOnlyList<string> GetSiblingInstallPaths(string excludeModId)
    {
        var paths = new List<string>();
        foreach (var p in ModRegistry.All)
        {
            if (string.Equals(p.Id, excludeModId, StringComparison.OrdinalIgnoreCase))
                continue;
            var path = GetState(p.Id).InstallPath;
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }
        return paths;
    }

    /// <summary>Primary URL of UpdateInfo.xml. Default: official aoe3wol.com server.</summary>
    [JsonPropertyName("updateInfoUrl")]
    public string UpdateInfoUrl { get; set; } = "http://aoe3wol.com/updates/UpdateInfo.xml";

    /// <summary>Fallback URL used if the primary fails. Default: SourceForge mirror.</summary>
    [JsonPropertyName("updateInfoUrlAlt")]
    public string UpdateInfoUrlAlt { get; set; } =
        "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml";

    /// <summary>
    /// LEGACY — kept for backward compatibility with configs written before
    /// the per-mod <see cref="Mods"/> dictionary existed. New code should
    /// read/write via <see cref="GetState(string)"/>. On <see cref="Load"/>,
    /// when a non-empty value here AND no <c>mods["wol"]</c> entry exists,
    /// the value is migrated under the WoL profile.
    /// </summary>
    [JsonPropertyName("modInstallPath")]
    public string ModInstallPath { get; set; } = "";

    /// <summary>
    /// Path to age3y.exe (Age of Empires III: The Asian Dynasties).
    /// If empty, the launcher tries to find it automatically by walking up
    /// from the WoL install folder. Wars of Liberty does NOT have its own
    /// .exe — it patches AoE3's data files and the game is launched via
    /// age3y.exe in the AoE3 folder.
    /// </summary>
    [JsonPropertyName("gameExecutable")]
    public string GameExecutable { get; set; } = "";

    /// <summary>Optional command-line arguments for the game.</summary>
    [JsonPropertyName("gameArguments")]
    public string GameArguments { get; set; } = "";

    // ------------------------------------------------------------------------
    // Launcher-wide preferences (not per-mod). Surfaced in the
    // "Launcher Settings" dialog. Default values match the previous
    // hard-coded behaviour, so upgrading from an older launcher config
    // doesn't change what the user sees out of the box.
    // ------------------------------------------------------------------------

    /// <summary>
    /// When true, the launcher registers itself in
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> so Windows
    /// starts it automatically at login. Off by default — opt-in.
    /// <see cref="Services.StartupRegistrationService"/> applies / clears
    /// the registry key whenever this flag is saved.
    /// </summary>
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// When true, the launcher's main window closes itself once the game
    /// process has started, freeing resources while the user plays. The
    /// previously default behaviour (window stays open) is preserved by
    /// the false default — turning this on is opt-in.
    /// </summary>
    [JsonPropertyName("closeLauncherOnGameStart")]
    public bool CloseLauncherOnGameStart { get; set; } = false;

    /// <summary>
    /// When true, closing the main window minimises the launcher to the
    /// system tray instead of exiting the process. Useful for users who
    /// keep the launcher running in the background (e.g. waiting on a
    /// long download). Right-click the tray icon → Exit to actually
    /// terminate. False by default — the conventional "X = quit"
    /// behaviour is what most users expect.
    /// </summary>
    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>
    /// When true, the launcher shows a system-tray balloon notification
    /// after long-running operations finish (mod update applied, launcher
    /// self-update available). The toast only fires when the main window
    /// is hidden or minimised — there's no point notifying the user about
    /// something they're already watching on screen.
    ///
    /// Default true: matches the principle of "let the user step away and
    /// come back when something's done". Turning it off is opt-out for
    /// users who want a silent launcher.
    /// </summary>
    [JsonPropertyName("showToastNotifications")]
    public bool ShowToastNotifications { get; set; } = true;

    /// <summary>
    /// When true (default), the launcher runs the standard "check for
    /// updates" routine on startup — launcher self-update + mod patches +
    /// translations index + mods catalog. Turning it off lets users with
    /// flaky connections, metered data, or strict privacy preferences
    /// avoid any outbound HTTP at launch (the launcher still works fully
    /// from cached state).
    /// </summary>
    [JsonPropertyName("checkUpdatesOnStartup")]
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>If true, opens the postUpdatePage URLs in the browser after each update.</summary>
    [JsonPropertyName("openPostUpdatePages")]
    public bool OpenPostUpdatePages { get; set; } = true;

    /// <summary>UI language: "en" (default) or "es".</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// UI theme: "dark" (default), "light", or "system" (follow Windows).
    /// Applied at startup and switched live via ThemeService on settings change.
    /// </summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    /// <summary>
    /// URL of the catalog news.json feed. Default points at the official
    /// catalog repo. Empty disables the news fetch entirely (the Noticias
    /// tab then shows just the placeholder).
    /// </summary>
    [JsonPropertyName("newsUrl")]
    public string NewsUrl { get; set; } =
        "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/news.json";

    /// <summary>
    /// Persisted window geometry. Width/Height are the user's preferred
    /// normal-state size; Left/Top default to NaN meaning "let WPF
    /// CenterScreen pick a position on first run". Maximized is restored
    /// as a separate flag so we don't store maximized dimensions.
    /// </summary>
    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1100;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 700;

    // Nullable so "never saved a position" serialises as JSON null rather
    // than NaN (System.Text.Json refuses NaN by default and would throw
    // from Save()).
    [JsonPropertyName("windowLeft")]
    public double? WindowLeft { get; set; }

    [JsonPropertyName("windowTop")]
    public double? WindowTop { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; } = false;

    /// <summary>
    /// Tab the right content panel was showing when the launcher last closed.
    /// One of "Noticias" (default), "Changelog", "Ayuda".
    /// </summary>
    [JsonPropertyName("lastActiveTab")]
    public string LastActiveTab { get; set; } = "Noticias";

    /// <summary>
    /// URLs of the Wars of Liberty payload ZIP parts. The ZIP is split into
    /// multiple files (.zip.001, .zip.002, ...) to work around GitHub's file
    /// size limits. The launcher downloads all parts, concatenates them into
    /// a single ZIP, then extracts the raw mod files.
    /// </summary>
    [JsonPropertyName("payloadZipUrls")]
    public string[] PayloadZipUrls { get; set; } = new[]
    {
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003",
    };

    /// <summary>
    /// Legacy single-URL field. Kept for backward compat; if PayloadZipUrls is
    /// empty, the launcher falls back to this URL.
    /// </summary>
    [JsonPropertyName("installerZipUrl")]
    public string InstallerZipUrl { get; set; } = "";

    /// <summary>
    /// Default install folder shown in the install dialog. The user can
    /// override it before installing.
    /// </summary>
    [JsonPropertyName("defaultInstallFolder")]
    public string DefaultInstallFolder { get; set; } =
        @"C:\Program Files (x86)\Wars of Liberty";

    /// <summary>
    /// Official Wars of Liberty website. Used as a fallback link if the
    /// installer ZIP URL is empty or fails.
    /// </summary>
    [JsonPropertyName("officialWebsite")]
    public string OfficialWebsite { get; set; } = "http://aoe3wol.com/";

    /// <summary>
    /// GitHub release tag of the launcher binary the user is currently running
    /// (e.g. "v0.6.0"). Set automatically after a successful self-update.
    /// Empty on a fresh install — the launcher will prompt once and save it.
    ///
    /// This is the source of truth for self-update detection: we compare it
    /// against the latest release tag on GitHub, NOT the AssemblyVersion of
    /// the running binary. That way the update mechanism doesn't depend on
    /// remembering to bump csproj before publishing.
    /// </summary>
    [JsonPropertyName("lastInstalledLauncherTag")]
    public string LastInstalledLauncherTag { get; set; } = "";

    /// <summary>
    /// GitHub release tag the user dismissed via "Later". The launcher won't
    /// prompt again for this exact tag — only when a different tag appears.
    /// </summary>
    [JsonPropertyName("skippedLauncherTag")]
    public string SkippedLauncherTag { get; set; } = "";

    /// <summary>
    /// GitHub repository where community translations live (format
    /// "owner/repo"). The launcher discovers translations by listing
    /// the releases of this repo and reading the <c>translation.json</c>
    /// asset inside each one.
    /// </summary>
    [JsonPropertyName("translationsRepo")]
    public string TranslationsRepo { get; set; } = "papillo12/translations";

    /// <summary>
    /// GitHub repository (format "owner/repo") that hosts the mods catalog
    /// — one folder per community-submitted mod, each with a
    /// <c>mod.json</c> manifest.
    ///
    /// Three values are meaningful:
    /// <list type="bullet">
    ///   <item><c>""</c> (empty, default) — use the launcher's built-in
    ///     default catalog at <c>Gorgorito12/aoe3-mods-catalog</c>. This
    ///     is what most users want.</item>
    ///   <item><c>"none"</c> — opt-out: skip the catalog fetch entirely.
    ///     The launcher still works, just shows only its built-in mods
    ///     (WoL + Improvement Mod). For users who don't want their
    ///     launcher reaching out to GitHub, or for kiosk deployments.</item>
    ///   <item><c>"owner/repo"</c> — fetch from a specific repo. Useful
    ///     for forks, mirrors, or private test catalogs.</item>
    /// </list>
    ///
    /// Whichever path is taken, built-in mods always win on id collisions:
    /// a community PR cannot shadow the official "wol" entry to redirect
    /// downloads.
    /// </summary>
    [JsonPropertyName("modsCatalogRepo")]
    public string ModsCatalogRepo { get; set; } = "";

    /// <summary>
    /// LEGACY — see <see cref="ModInstallPath"/>. Migrated to
    /// <see cref="ModState.ActiveTranslationId"/> for the WoL profile on
    /// first load.
    /// </summary>
    [JsonPropertyName("activeTranslationId")]
    public string ActiveTranslationId { get; set; } = "";

    // ------------------------------------------------------------------------
    // Multiplayer (v1.0). Empty / unset values mean "user hasn't opted in";
    // the Multiplayer tab handles bootstrap (sign-in, ZeroTier install) on
    // first open, so a fresh launcher with no MP config still works fully
    // for single-player updates.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Multiplayer state — backend URL and the session token issued by
    /// the lobby backend after a Discord sign-in. Lives in its own
    /// nested object so the JSON layout stays tidy and so adding new
    /// multiplayer fields later doesn't keep ballooning the root
    /// schema. Initialised lazily; <see cref="Multiplayer"/> is never
    /// null after <see cref="Load"/> returns.
    /// </summary>
    [JsonPropertyName("multiplayer")]
    public MultiplayerConfig Multiplayer { get; set; } = new();

    private const string ConfigFileName = "launcher-config.json";

    public static LauncherConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            var defaults = new LauncherConfig();
            defaults.Save();
            return defaults;
        }
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
        // The JSON may have been written by an older launcher (no
        // "multiplayer" key) or by a user who edited it and set the
        // section to null. Either way, callers rely on Multiplayer
        // being non-null, so normalise here.
        cfg.Multiplayer ??= new MultiplayerConfig();
        cfg.MigrateLegacyState();
        cfg.MigrateLobbyBaseUrl();
        return cfg;
    }

    /// <summary>
    /// Heal stale <c>multiplayer.lobbyBaseUrl</c> values that point
    /// at addresses which no longer (or never) resolved. Known bad
    /// values shipped in earlier builds:
    ///
    ///   * <c>https://wol-launcher-lobby.jeisonso1997.workers.dev</c>
    ///     — the previous production URL, served by a Cloudflare
    ///     Worker that has been retired in favour of the self-hosted
    ///     Node backend at wol-lobby.duckdns.org.
    ///   * <c>https://wol-launcher-lobby.workers.dev</c> — looked
    ///     like a public Cloudflare URL but doesn't include the
    ///     account subdomain, so DNS fails with "Host desconocido".
    ///   * <c>http://127.0.0.1:8787</c> — the local wrangler dev
    ///     server. Useful only on the developer's PC.
    ///   * <c>https://*.trycloudflare.com</c> — quick tunnels
    ///     baked into a release; tunnels die when the dev closes
    ///     the terminal.
    ///
    /// When we spot any of these, rewrite to the current production
    /// backend URL and save. Idempotent — once migrated, subsequent
    /// loads see a healthy URL and do nothing.
    /// </summary>
    private void MigrateLobbyBaseUrl()
    {
        var url = Multiplayer.LobbyBaseUrl ?? "";
        bool isBroken = url == "https://wol-launcher-lobby.jeisonso1997.workers.dev"
            || url == "http://wol-launcher-lobby.jeisonso1997.workers.dev"
            || url == "https://wol-launcher-lobby.workers.dev"
            || url == "http://wol-launcher-lobby.workers.dev"
            || url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || url.Contains(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
        if (!isBroken) return;

        var oldUrl = url;
        Multiplayer.LobbyBaseUrl = new MultiplayerConfig().LobbyBaseUrl;
        // Old sessionToken was signed by a different backend / JWT
        // key, so clear it too — otherwise the next /me call fails
        // with `invalid_token` and the user can't sign in until they
        // manually edit the config. Forcing a fresh Discord sign-in
        // is the right reset.
        Multiplayer.SessionToken = "";
        Multiplayer.SessionExpiresAt = 0;
        Multiplayer.CachedUser = null;

        try { Save(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Config lobbyBaseUrl migration save failed: {ex.Message}");
        }
        DiagnosticLog.Write(
            $"Migrated multiplayer.lobbyBaseUrl: '{oldUrl}' -> '{Multiplayer.LobbyBaseUrl}'. " +
            $"Session cleared; user needs to sign in again with Discord.");
    }

    /// <summary>
    /// One-time migration of the pre-multi-mod root-level state fields
    /// (<see cref="ModInstallPath"/>, <see cref="ActiveTranslationId"/>)
    /// into the per-mod <see cref="Mods"/> dictionary. Only runs when the
    /// dictionary doesn't already have an entry for the WoL profile, so
    /// it's idempotent — re-loading a migrated config is a no-op.
    /// </summary>
    private void MigrateLegacyState()
    {
        var wolId = ModRegistry.WolId;
        bool needsMigration =
            (!string.IsNullOrEmpty(ModInstallPath) || !string.IsNullOrEmpty(ActiveTranslationId))
            && !Mods.ContainsKey(wolId);

        if (!needsMigration) return;

        Mods[wolId] = new ModState
        {
            InstallPath = ModInstallPath,
            ActiveTranslationId = ActiveTranslationId,
        };
        try { Save(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Config migration save failed: {ex.Message}");
        }
        DiagnosticLog.Write(
            $"Migrated legacy mod state into Mods[\"{wolId}\"]: " +
            $"installPath='{ModInstallPath}', activeTranslationId='{ActiveTranslationId}'.");
    }

    public void Save()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }
}
