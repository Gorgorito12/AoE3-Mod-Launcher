using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

    // (The previous RadminBannerDismissed flag was removed when the
    //  banner became reactive — colour + content change with state and
    //  a dismiss button no longer made sense. Old JSON configs with
    //  "radminBannerDismissed":true deserialise harmlessly: the
    //  unknown key is dropped on the next save.)
}

/// <summary>
/// One INACTIVE installation of a mod (the ACTIVE one lives in the flat fields
/// of <see cref="ModState"/>). A mod may have several copies in different
/// folders; the active copy plus this list make up the full set. Carries the
/// per-INSTALL state — per-MOD state (latest-version cache, notification dedup)
/// stays on <see cref="ModState"/>.
/// </summary>
public class ModInstall
{
    /// <summary>Stable id — the switch key and the per-install productGuid seed.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-facing label ("Principal", "Prueba"…). Empty => derive from the folder name.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";

    [JsonPropertyName("lastKnownVersion")]
    public string LastKnownVersion { get; set; } = "";

    [JsonPropertyName("pinnedVersion")]
    public string PinnedVersion { get; set; } = "";

    [JsonPropertyName("activeTranslationId")]
    public string ActiveTranslationId { get; set; } = "";

    [JsonPropertyName("activeTranslationVersion")]
    public string ActiveTranslationVersion { get; set; } = "";
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
    /// Which VERSION of the active translation (<see cref="ActiveTranslationId"/>)
    /// is applied, for folder packs that keep a version history. Empty for
    /// single-version packs or English. Lets the Language tab's version picker
    /// pre-select and mark the applied version.
    /// </summary>
    [JsonPropertyName("activeTranslationVersion")]
    public string ActiveTranslationVersion { get; set; } = "";

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

    /// <summary>
    /// ETag of the last 200 response from <c>/releases/latest</c> for this mod
    /// (follow-latest GitHubReleases mods only). Sent as <c>If-None-Match</c> so
    /// an unchanged latest release is a free 304 (conditional requests don't
    /// count against GitHub's unauthenticated 60/h rate limit). Kept on a
    /// transient failure; an indivisible pair with
    /// <see cref="LastKnownLatestVersion"/> — only sent when the cached tag is
    /// non-empty, because a 304 carries no body and would leave us tagless.
    /// </summary>
    [JsonPropertyName("latestReleaseETag")]
    public string LatestReleaseETag { get; set; } = "";

    /// <summary>
    /// Version the user explicitly chose to STAY ON for this mod. Empty (the
    /// default) means "follow the latest" — the normal behaviour. When it equals
    /// the installed version, the launcher PAUSES update prompts for this mod: the
    /// PLAY button stays "Play" instead of flipping to "Update" and the secondary
    /// Update button is hidden, so the user can keep playing this version without
    /// being pushed to upgrade. It only suppresses the PROMPT — nothing is ever
    /// auto-updated. The pin goes stale (and stops suppressing) once the installed
    /// version no longer matches it, e.g. after a manual update; the user clears it
    /// from Mod Properties to resume updates.
    /// </summary>
    [JsonPropertyName("pinnedVersion")]
    public string PinnedVersion { get; set; } = "";

    /// <summary>
    /// Latest "available version" for which the notification bell has ALREADY
    /// raised an "update available" item. Dedup key for the notification center:
    /// we only bell a given (mod, latest-version) pair once, even after the
    /// visible notification list rolls past its 50-item cap. Empty until the
    /// first "update available" notification for this mod.
    /// </summary>
    [JsonPropertyName("notifiedUpdateVersion")]
    public string NotifiedUpdateVersion { get; set; } = "";

    /// <summary>
    /// Installed version for which the bell has ALREADY raised (or baselined) an
    /// "update finished" item. Startup reconciliation compares the freshly-detected
    /// installed version against this: empty → seed a silent baseline (no bell);
    /// a newer value → the mod was updated (possibly by an elevated/other-profile
    /// process that couldn't write THIS user's bell), so raise "update finished"
    /// here in the user's own session. Idempotent with the direct raise in
    /// <c>ApplyAsync</c> (that dedups on the visible list).
    /// </summary>
    [JsonPropertyName("notifiedInstalledVersion")]
    public string NotifiedInstalledVersion { get; set; } = "";

    /// <summary>
    /// Translation entries (keyed <c>id@version</c>) for which the notification
    /// bell has already raised a "new translation" item. Dedup set so a freshly
    /// published translation only bells once per mod, surviving the 50-item cap
    /// of the visible notification list.
    /// </summary>
    [JsonPropertyName("notifiedTranslationKeys")]
    public List<string> NotifiedTranslationKeys { get; set; } = new();

    // ---- Multi-install support ----
    // The flat fields above ARE the ACTIVE install. INACTIVE copies of the same
    // mod (a second folder, a test copy, a different version) live in
    // <see cref="OtherInstalls"/>. Switching the active install swaps an entry
    // of OtherInstalls with the flat fields (see SnapshotActive/AdoptInstall).
    // An empty OtherInstalls == the legacy single-install shape, so every
    // existing reader and old build keeps working with ZERO migration. The
    // stock game never participates (stripped in NormalizeInstalls).

    /// <summary>
    /// Stable id of the ACTIVE install. Empty on a legacy single-install config
    /// (the flat fields are simply "the install"); assigned a GUID once the mod
    /// gains a second copy, so the active one can be referenced after it later
    /// rotates into <see cref="OtherInstalls"/>.
    /// </summary>
    [JsonPropertyName("activeInstallId")]
    public string ActiveInstallId { get; set; } = "";

    /// <summary>User-facing label of the active install ("Principal", "Prueba"…).
    /// Empty => the UI derives one from the folder name.</summary>
    [JsonPropertyName("activeInstallLabel")]
    public string ActiveInstallLabel { get; set; } = "";

    /// <summary>
    /// The mod's INACTIVE installs (the active one is the flat fields above).
    /// Empty for single-install users — round-trips and readers behave exactly
    /// as before. Populated by "install another copy" / adopt.
    /// </summary>
    [JsonPropertyName("otherInstalls")]
    public List<ModInstall> OtherInstalls { get; set; } = new();

    /// <summary>True when this mod has more than one registered install.</summary>
    [JsonIgnore]
    public bool HasMultipleInstalls => OtherInstalls.Count > 0;

    /// <summary>
    /// Snapshot the ACTIVE install (the flat fields) as a <see cref="ModInstall"/>,
    /// used when rotating it into <see cref="OtherInstalls"/> on a switch. Mints a
    /// stable id if the active install doesn't have one yet.
    /// </summary>
    public ModInstall SnapshotActive() => new()
    {
        Id = string.IsNullOrEmpty(ActiveInstallId) ? Guid.NewGuid().ToString("N") : ActiveInstallId,
        Label = ActiveInstallLabel,
        InstallPath = InstallPath,
        LastKnownVersion = LastKnownVersion,
        PinnedVersion = PinnedVersion,
        ActiveTranslationId = ActiveTranslationId,
        ActiveTranslationVersion = ActiveTranslationVersion,
    };

    /// <summary>
    /// Copy a stored install INTO the flat fields (make it the active one).
    /// Per-mod fields (<see cref="LastKnownLatestVersion"/>, notification dedup)
    /// are left untouched — they are not per-install.
    /// </summary>
    public void AdoptInstall(ModInstall slot)
    {
        ActiveInstallId = slot.Id;
        ActiveInstallLabel = slot.Label;
        InstallPath = slot.InstallPath;
        LastKnownVersion = slot.LastKnownVersion;
        PinnedVersion = slot.PinnedVersion;
        ActiveTranslationId = slot.ActiveTranslationId;
        ActiveTranslationVersion = slot.ActiveTranslationVersion;
    }

    /// <summary>Every registered install path for this mod (active + others),
    /// non-empty only. Used by the sibling-exclusion list so a new clone of one
    /// copy never scoops up another.</summary>
    public IEnumerable<string> AllInstallPaths()
    {
        if (!string.IsNullOrEmpty(InstallPath)) yield return InstallPath;
        foreach (var o in OtherInstalls)
            if (!string.IsNullOrEmpty(o.InstallPath)) yield return o.InstallPath;
    }

    /// <summary>
    /// Forget a single registered copy by id (the switcher's "remove" action). Only
    /// drops the registration — it does NOT touch files on disk. Returns true if an
    /// entry was removed.
    /// </summary>
    public bool RemoveInstall(string id)
        => !string.IsNullOrEmpty(id) && OtherInstalls.RemoveAll(i => i.Id == id) > 0;

    /// <summary>
    /// Register an EXISTING install folder as an inactive copy (the "add existing folder"
    /// action) — adopts a real install already on disk WITHOUT reinstalling. No-op (returns
    /// false) when the path is empty or already registered (active or another copy), so it
    /// can't create a duplicate. Returns true when a new copy was added.
    /// </summary>
    public bool RegisterInstall(string path, string label = "")
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (var p in AllInstallPaths())
            if (PathEquals(p, path)) return false;
        OtherInstalls.Add(new ModInstall
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = (label ?? "").Trim(),
            InstallPath = path,
        });
        return true;
    }

    /// <summary>
    /// Rename a registered install (the "edit label" action). Targets the active install
    /// when <paramref name="id"/> matches <see cref="ActiveInstallId"/>, else the matching
    /// <see cref="OtherInstalls"/> entry. An empty label reverts to the folder-derived
    /// display name. Returns true if something was renamed.
    /// </summary>
    public bool RenameInstall(string id, string label)
    {
        if (string.IsNullOrEmpty(id)) return false;
        label = (label ?? "").Trim();
        if (id == ActiveInstallId) { ActiveInstallLabel = label; return true; }
        var slot = OtherInstalls.Find(i => i.Id == id);
        if (slot == null) return false;
        slot.Label = label;
        return true;
    }

    /// <summary>
    /// Case-insensitive, full-path-normalized comparison of two install paths, so
    /// <c>bin\..\</c>, trailing slashes, and casing don't defeat dedup. Falls back to
    /// a trimmed ordinal compare when a path can't be fully qualified.
    /// </summary>
    public static bool PathEquals(string? a, string? b)
        => string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { p = Path.GetFullPath(p); } catch { /* keep raw on a malformed path */ }
        return p.TrimEnd('\\', '/');
    }

    /// <summary>
    /// Idempotent post-load normalization. A NO-OP for single-install configs
    /// (the common case, <see cref="OtherInstalls"/> empty): it only assigns a
    /// stable <see cref="ActiveInstallId"/> when the mod actually has extra
    /// copies but the active one lost its id (e.g. a hand-edited config). When
    /// <paramref name="isStock"/>, strips any multi-install state entirely — the
    /// detect-only base game must never carry copies.
    /// </summary>
    public void NormalizeInstalls(bool isStock)
    {
        if (isStock)
        {
            OtherInstalls.Clear();
            ActiveInstallId = "";
            ActiveInstallLabel = "";
            return;
        }

        // Drop empty entries and any copy whose path duplicates the active install or
        // an earlier-kept copy — a stale re-point / double registration would otherwise
        // surface a phantom duplicate in the switcher. Pure path compare, no disk I/O
        // (non-existent folders are filtered at render time + removable by hand).
        if (OtherInstalls.Count > 0)
        {
            var kept = new List<ModInstall>();
            foreach (var o in OtherInstalls)
            {
                if (string.IsNullOrWhiteSpace(o.InstallPath)) continue;
                if (PathEquals(o.InstallPath, InstallPath)) continue;
                if (kept.Any(k => PathEquals(k.InstallPath, o.InstallPath))) continue;
                kept.Add(o);
            }
            if (kept.Count != OtherInstalls.Count) OtherInstalls = kept;
        }

        if (OtherInstalls.Count > 0 && string.IsNullOrEmpty(ActiveInstallId))
            ActiveInstallId = Guid.NewGuid().ToString("N");
    }
}

/// <summary>
/// Kind of a <see cref="NotificationItem"/> shown in the bell panel. Serialized
/// as a string so adding a value later doesn't shift existing JSON, and so a
/// config written by a newer launcher degrades gracefully on an older one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationKind
{
    /// <summary>A newer version of a mod is available to download.</summary>
    UpdateAvailable,
    /// <summary>A mod update finished applying successfully.</summary>
    UpdateFinished,
    /// <summary>A new community translation was published for a mod.</summary>
    NewTranslation,
    /// <summary>A newer version of the LAUNCHER itself is available.</summary>
    LauncherUpdate,
    /// <summary>Connectivity changed — went offline, or came back online.</summary>
    Connectivity,
    /// <summary>A new community mod appeared in the Workshop catalog.</summary>
    NewMod,
    /// <summary>A fresh install (or a new copy) of a mod finished — distinct from an update.</summary>
    Installed,
    /// <summary>Any user created a new multiplayer room (for a mod you have installed).</summary>
    RoomCreated,
}

/// <summary>
/// One entry in the Steam-style notification bell. Persisted in
/// <see cref="LauncherConfig.Notifications"/> so the history survives launcher
/// restarts until the user clears it. Created by <see cref="Services.NotificationCenter"/>.
/// </summary>
public class NotificationItem : INotifyPropertyChanged
{
    /// <summary>Stable id (GUID string) — used as the list key and for removal.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("kind")]
    public NotificationKind Kind { get; set; }

    /// <summary>Mod profile id this notification is about (drives click navigation).</summary>
    [JsonPropertyName("modId")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    /// <summary>UTC timestamp the notification was raised (for "hace X" labels + ordering).</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the user has seen this item. Drives the per-row unread dot via a
    /// WPF DataTrigger, so the setter MUST raise <see cref="PropertyChanged"/> —
    /// without it the dot never hides when <c>MarkAllRead</c> flips the flag.
    /// </summary>
    private bool _read;
    [JsonPropertyName("read")]
    public bool Read
    {
        get => _read;
        set { if (_read != value) { _read = value; OnPropertyChanged(); } }
    }

    /// <summary>Local-time projection of <see cref="CreatedAtUtc"/> for display binding.</summary>
    [JsonIgnore]
    public DateTime CreatedLocal => CreatedAtUtc.ToLocalTime();

    /// <summary>
    /// Optional navigation payload (e.g. a translation id for
    /// <see cref="NotificationKind.NewTranslation"/>). Null/empty for kinds that
    /// only need <see cref="ModId"/>.
    /// </summary>
    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    /// IDs of mods the user has explicitly added to their personal
    /// collection from the Workshop. Drives the Dashboard's MODS
    /// popup, which lists only what the user has curated rather than
    /// the full catalog. Built-in profiles (WoL) are always treated
    /// as added regardless of this list (see <see cref="IsUserMod"/>),
    /// so a fresh install never has an empty MODS popup.
    ///
    /// Migration: on first launch with this field present but empty,
    /// MainWindow seeds it from the currently-installed mods so users
    /// upgrading from older configs don't lose their setup. After
    /// that, the list only changes via the Workshop's Add / Remove
    /// buttons.
    /// </summary>
    [JsonPropertyName("userModIds")]
    public List<string> UserModIds { get; set; } = new();

    /// <summary>
    /// True when the given mod id belongs to the user's collection
    /// (either explicitly added via Workshop or a built-in profile
    /// like WoL). Drives the Dashboard's MODS popup filter and the
    /// Workshop's per-row Add/Remove button state.
    /// </summary>
    public bool IsUserMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;
        if (ModRegistry.IsBuiltIn(modId)) return true;
        return UserModIds.Any(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds the given mod id to <see cref="UserModIds"/> if not
    /// already present. No-op for built-in ids (those are always
    /// implicitly present via <see cref="IsUserMod"/>). Caller is
    /// responsible for invoking <see cref="Save"/> after batching the
    /// changes that should land on disk.
    /// </summary>
    public void AddUserMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return;
        if (ModRegistry.IsBuiltIn(modId)) return;
        if (!UserModIds.Any(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)))
            UserModIds.Add(modId);
    }

    /// <summary>
    /// Removes the given mod id from <see cref="UserModIds"/>. No-op
    /// for built-in ids. Caller is responsible for <see cref="Save"/>.
    /// </summary>
    public void RemoveUserMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return;
        if (ModRegistry.IsBuiltIn(modId)) return;
        UserModIds.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Mod ids the user has starred via the right-click context
    /// menu (Add to Favorites). Favorites pin to the top of the
    /// Dashboard MODS popup so the user can switch between their
    /// most-played mods in one click. Distinct from
    /// <see cref="UserModIds"/> (which controls visibility);
    /// favorites only control ORDERING — a favorite mod must also
    /// be in UserModIds to appear at all.
    /// </summary>
    [JsonPropertyName("favoriteModIds")]
    public List<string> FavoriteModIds { get; set; } = new();

    public bool IsFavoriteMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;
        return FavoriteModIds.Any(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
    }

    public void AddFavoriteMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return;
        if (!IsFavoriteMod(modId))
            FavoriteModIds.Add(modId);
    }

    public void RemoveFavoriteMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return;
        FavoriteModIds.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
    }

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
            // NEVER exclude the stock base game (aoe3-tad, IsStockGame=true).
            // Its "install path" is the user's real AoE3 (e.g. ...\Age Of
            // Empires 3\bin) — which is exactly the base the installer CLONES
            // (then flattens bin\ into the mod root). Excluding it makes the
            // clone copy 0 base files, so the mod ships with no engine DLLs
            // (RockallDLL/binkw32/granny2/deformerdlly) or data\*.xml and the
            // game exits on launch. The stock game is detect-only and never a
            // "sibling mod" that could be scooped into another install.
            if (p.IsStockGame)
                continue;
            // Enumerate ALL of the sibling mod's copies (active install + its
            // other copies), not just the active one, so a clone never scoops a
            // non-active copy of a sibling mod into the new folder.
            paths.AddRange(GetState(p.Id).AllInstallPaths());
        }
        return paths;
    }

    /// <summary>
    /// Every registered install path across ALL non-stock mods (each mod's active
    /// install + its other copies). Used when installing an ADDITIONAL copy of a
    /// mod: the new clone must exclude every existing mod install — INCLUDING this
    /// mod's own other copies — so it doesn't scoop one into the fresh folder.
    /// (The plain <see cref="GetSiblingInstallPaths"/> excludes the whole current
    /// mod, which is right for a first/normal install but wrong for an extra copy.)
    /// The stock game is skipped for the same reason as above.
    /// </summary>
    public IReadOnlyList<string> GetAllInstallPaths()
    {
        var paths = new List<string>();
        foreach (var p in ModRegistry.All)
        {
            if (p.IsStockGame) continue;
            paths.AddRange(GetState(p.Id).AllInstallPaths());
        }
        return paths;
    }

    /// <summary>
    /// When to show the Radmin VPN connection assistant overlay.
    ///   "Auto"      — opens automatically when the Multiplayer tab
    ///                 loads and the user isn't confirmed in the
    ///                 AoE3 network. Default for new installs.
    ///   "OnRequest" — never opens automatically; user has to click
    ///                 "Show steps" in the compact banner.
    ///   "Never"     — assistant disabled entirely. The compact
    ///                 banner still shows but the "Show steps"
    ///                 button is hidden.
    /// String instead of enum so legacy configs that don't know the
    /// field default cleanly to "Auto" via the property initializer
    /// (an unknown enum value would have to be migrated explicitly).
    /// </summary>
    [JsonPropertyName("radminAssistantMode")]
    public string RadminAssistantMode { get; set; } = "Auto";

    /// <summary>
    /// One-shot "don't show again" flag set when the user ticks the
    /// checkbox at the bottom of the assistant overlay. Equivalent
    /// to switching <see cref="RadminAssistantMode"/> to "OnRequest"
    /// but cheaper for the user to set — and we keep them separate
    /// so a power-user who flips Mode to Never doesn't have to also
    /// flip this back to false to re-show on demand.
    /// </summary>
    [JsonPropertyName("radminAssistantSkipped")]
    public bool RadminAssistantSkipped { get; set; }

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

    /// <summary>
    /// The AoE3 base-game FOLDER the user confirmed by hand via "Change AoE3
    /// folder" — the root that contains <c>data\</c> (or the folder holding
    /// <c>age3y.exe</c>). Unlike <see cref="GameExecutable"/> (a volatile launch
    /// cache cleared on every mod switch), this is DURABLE: it survives switches
    /// so a manually-pointed, non-standard AoE3 install (e.g.
    /// <c>…\Microsoft Studios\Age of Empires III - Complete Collection</c>, which
    /// <see cref="Services.AoE3Detector.FindAll"/> can't auto-locate) stays
    /// recognized — including the detect-only stock <c>aoe3-tad</c> profile, whose
    /// install detection resolves through it. Empty = never set manually (auto-
    /// detection only). See <c>GameLauncher.FindAoe3InstallRoot</c>.
    /// </summary>
    [JsonPropertyName("aoe3ManualPath")]
    public string Aoe3ManualPath { get; set; } = "";

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
    /// When true (default), the launcher registers the <c>wol-launcher://</c> URI
    /// scheme (HKCU) so a Discord room "Join" link opens the launcher and joins
    /// the room. On by default so the deep link "just works" once the portable
    /// exe has run; users who want the portable exe to leave no registry trace can
    /// turn it off, which clears the key. <see cref="Services.DeepLinkService"/>
    /// applies / clears it on save and re-applies (self-heals the exe path) each
    /// launch.
    /// </summary>
    [JsonPropertyName("enableJoinLinks")]
    public bool EnableJoinLinks { get; set; } = true;

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
    /// When true (default), clicking the window's X (or Alt+F4) hides the
    /// launcher to the system tray instead of quitting — the process keeps
    /// running (so the user stays shown as connected) and the only way to
    /// fully exit is the tray icon → Exit (Discord/Steam pattern). The
    /// minimise button is unaffected (it still goes to the taskbar).
    ///
    /// Independent of the "Run in background" bundle (<see cref="MinimizeToTray"/>
    /// / <see cref="StartMinimized"/> / <see cref="StartWithWindows"/>): this
    /// governs ONLY the close-button behaviour and is toggled by its own
    /// "Minimize to tray on close" checkbox in Launcher Settings. Default true
    /// gives everyone close-to-tray with a one-click opt-out; turning it off
    /// restores the conventional "X = quit". Read by
    /// <c>MainWindow.OnClosing</c>. Adds NO antivirus signal (no registry key,
    /// no persistence) — the AV-weighted auto-start lives in the separate
    /// "Run in background" toggle.
    /// </summary>
    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Set to true after the launcher has shown the one-time "still running in
    /// the tray" balloon the first time <see cref="CloseToTray"/> hid the
    /// window on close — so the onboarding hint fires exactly once and never
    /// nags again. Written by <c>MainWindow.OnClosing</c>.
    /// </summary>
    [JsonPropertyName("closedToTrayHintShown")]
    public bool ClosedToTrayHintShown { get; set; } = false;

    /// <summary>
    /// When true, an AUTO-START launch (Windows login, recognised by the
    /// <c>--minimized</c> argument the Run-key registration appends) opens the
    /// launcher straight to the system tray instead of showing the window — so
    /// the "run in background" experience doesn't pop a window on every login.
    /// A MANUAL double-click still shows the window (it carries no
    /// <c>--minimized</c> arg). Set together with <see cref="StartWithWindows"/>
    /// + <see cref="MinimizeToTray"/> by the single "Run in background" toggle.
    /// Off by default — opt-in.
    /// </summary>
    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; } = false;

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
    /// When true, the launcher shows a Windows notification when ANY user creates
    /// a new multiplayer room for a mod you have installed (a background poll of
    /// the lobby list detects it). Independent of <see cref="ShowToastNotifications"/>
    /// so a user can keep update toasts but silence room notifications. Default true.
    /// </summary>
    [JsonPropertyName("notifyNewRooms")]
    public bool NotifyNewRooms { get; set; } = true;

    /// <summary>
    /// When true (default), the launcher plays short feedback sounds — a chat
    /// blip on an incoming message, a ding on a bell notification, and a pop
    /// when someone connects (joins your room / a new room appears / a player
    /// comes online). Independent of <see cref="ShowToastNotifications"/> and
    /// <see cref="NotifyNewRooms"/> so a user can keep visual notifications but
    /// silence audio. Wired to <see cref="Services.SoundService.Enabled"/> at
    /// startup and on settings save.
    /// </summary>
    [JsonPropertyName("enableSounds")]
    public bool EnableSounds { get; set; } = true;

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

    /// <summary>
    /// Opt-in switch for the local multiplayer telemetry log
    /// (<c>multiplayer-events.log</c>): plain event counters (sign-ins,
    /// lobby joins, error codes) appended next to the .exe, with NO network
    /// and NO third-party SDK. Off by default — a fresh install collects
    /// nothing until the user enables it in Launcher Settings → Privacy.
    /// Wired to <see cref="Services.Multiplayer.MultiplayerTelemetry.Enabled"/>
    /// at startup and on settings save. Disclosed in PRIVACY.md (the SignPath
    /// Foundation OSS terms require data collection to be both disclosed and
    /// disableable).
    /// </summary>
    [JsonPropertyName("multiplayerTelemetryEnabled")]
    public bool MultiplayerTelemetryEnabled { get; set; } = false;

    /// <summary>
    /// Public URL of the project's privacy policy (PRIVACY.md on GitHub).
    /// Opened from Launcher Settings → Privacy and linked from the Discord
    /// sign-in dialog (the point where multiplayer data collection begins).
    /// A const, not a serialised field — it's a fixed project link, not user
    /// state.
    /// </summary>
    public const string PrivacyPolicyUrl =
        "https://github.com/Gorgorito12/Updater/blob/main/PRIVACY.md";

    /// <summary>UI language: "en" (default) or "es".</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    // (Theme property removed — the launcher is dorado-imperial
    //  dark-only by design now. Old configs with a "theme" key
    //  deserialise harmlessly: System.Text.Json ignores unknown
    //  properties and the next Save drops it from the JSON.)

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
    /// Left-to-right order of the three top navigation tabs, as stable
    /// tab ids: "library", "workshop", "multiplayer". User-reorderable
    /// from Launcher Settings → Interface. The FIRST entry is also the
    /// tab that opens on launch — the user's mental model is "put the
    /// tab I want first, and it opens first", so order + startup are one
    /// setting, not two.
    ///
    /// Never read this raw — go through <see cref="GetTopTabOrder"/>,
    /// which sanitises a hand-edited / stale / corrupt value (drops
    /// unknown ids, de-dupes, and appends any canonical tab the saved
    /// list is missing) so a bad config can never permanently hide a
    /// tab.
    /// </summary>
    [JsonPropertyName("topTabOrder")]
    public string[] TopTabOrder { get; set; } = { "library", "workshop", "multiplayer" };

    /// <summary>The canonical set of top-tab ids, in their default order.</summary>
    public static readonly string[] CanonicalTopTabs = { "library", "workshop", "multiplayer" };

    /// <summary>
    /// Returns <see cref="TopTabOrder"/> sanitised against
    /// <see cref="CanonicalTopTabs"/>: keeps the saved order for ids we
    /// recognise (case-insensitive, de-duplicated), then appends any
    /// canonical tab the saved list omitted. Guarantees the result is
    /// exactly the canonical set, permuted — so the nav bar always shows
    /// all three tabs regardless of what's on disk.
    /// </summary>
    public string[] GetTopTabOrder()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(CanonicalTopTabs.Length);
        foreach (var id in TopTabOrder ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var norm = id.Trim().ToLowerInvariant();
            if (Array.IndexOf(CanonicalTopTabs, norm) < 0) continue; // unknown id
            if (seen.Add(norm)) result.Add(norm);
        }
        // Append any canonical tab the saved order forgot (e.g. config
        // written before a new tab existed, or a hand-deleted entry).
        foreach (var id in CanonicalTopTabs)
            if (seen.Add(id)) result.Add(id);
        return result.ToArray();
    }

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
    /// ETag from the last successful self-update check against the GitHub
    /// Releases API. Sent back as If-None-Match so GitHub can answer 304 Not
    /// Modified when the latest release is unchanged, sparing the unauthenticated
    /// rate-limit (60 req/h per IP). Opaque value — never parsed, just echoed.
    /// </summary>
    [JsonPropertyName("launcherUpdateETag")]
    public string LauncherUpdateETag { get; set; } = "";

    /// <summary>
    /// GitHub repository where community translations live (format
    /// "owner/repo"). The launcher discovers translations by listing
    /// the releases of this repo and reading the <c>translation.json</c>
    /// asset inside each one.
    /// </summary>
    [JsonPropertyName("translationsRepo")]
    public string TranslationsRepo { get; set; } = "papillo12/translations";

    /// <summary>
    /// DEPRECATED single-repo override. Superseded by
    /// <see cref="ExtraTranslationsFolderRepos"/> + <see cref="CommunityTranslationsDisabled"/>.
    /// Kept only so old configs deserialize; <see cref="MigrateTranslationsFolderRepo"/>
    /// folds any value into the new fields on load and clears this. Never read at
    /// runtime anymore.
    /// </summary>
    [JsonPropertyName("translationsFolderRepo")]
    public string TranslationsFolderRepo { get; set; } = "";

    /// <summary>
    /// EXTRA community-translation folder repos (each "owner/repo") the user has
    /// added by hand in Settings → TRANSLATIONS. These are fetched IN ADDITION to
    /// the active mod profile's own folder repo (the default), and all packs are
    /// merged — so translations from several people coexist. Different people can
    /// host their own repo; the user opts in explicitly, so this is not a trust
    /// escalation (apply-time MD5 + <c>targetMod</c> remain the compatibility
    /// authority). On an id collision the packs' versions are UNIONED into one
    /// entry's version picker (labelled by source repo); display + one-click-apply
    /// metadata comes from the default repo when it has that id.
    ///
    /// Never read raw — go through <see cref="GetExtraTranslationsFolderRepos"/>,
    /// which trims, de-dupes (case-insensitive) and drops entries that aren't a
    /// valid <c>owner/repo</c>.
    /// </summary>
    [JsonPropertyName("extraTranslationsFolderRepos")]
    public string[] ExtraTranslationsFolderRepos { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Master off-switch for ALL community translations (the default folder repo,
    /// the extra repos, and the legacy releases path). Toggled by the "Disable"
    /// checkbox in Settings → TRANSLATIONS. Default false = translations enabled.
    /// </summary>
    [JsonPropertyName("communityTranslationsDisabled")]
    public bool CommunityTranslationsDisabled { get; set; } = false;

    /// <summary>Matches a valid "owner/repo" GitHub identifier.</summary>
    private static readonly System.Text.RegularExpressions.Regex RepoIdRegex =
        new(@"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Returns <see cref="ExtraTranslationsFolderRepos"/> sanitised: trimmed,
    /// de-duplicated case-insensitively, and filtered to syntactically valid
    /// <c>owner/repo</c> entries — so a hand-edited config can't feed a garbage
    /// value into the fetch URL builder.
    /// </summary>
    public string[] GetExtraTranslationsFolderRepos()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var r in ExtraTranslationsFolderRepos ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            var norm = r.Trim();
            if (!RepoIdRegex.IsMatch(norm)) continue;
            if (seen.Add(norm)) result.Add(norm);
        }
        return result.ToArray();
    }

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
    /// URL of the central notification feed — a small JSON manifest published by
    /// the self-hosted notifier service (a second Oracle VM, separate from the
    /// lobby backend) that polls GitHub ONCE for everyone and reports each mod's
    /// latest version + published translation keys. The launcher reads it with a
    /// single cheap REST call (ETag/304) instead of the per-mod GitHub polling in
    /// <c>SweepInstalledModsForNotificationsAsync</c>, sparing the unauthenticated
    /// 60 req/h-per-IP budget. The launcher still does the version/translation
    /// DIFF and the dedup locally, so the feed only changes the data SOURCE.
    ///
    /// Three values are meaningful (same convention as <see cref="ModsCatalogRepo"/>):
    /// <list type="bullet">
    ///   <item><c>""</c> (empty, default) — use the launcher's built-in default
    ///     feed URL.</item>
    ///   <item><c>"none"</c> — opt-out: don't contact the notifier; fall back to
    ///     the per-mod GitHub checks for everyone.</item>
    ///   <item>any URL — use that endpoint (forks, mirrors, local test servers).</item>
    /// </list>
    /// If the feed is unreachable or returns bad JSON the launcher ALWAYS falls
    /// back to the direct-GitHub checks, so the notifier is never a single point
    /// of failure.
    /// </summary>
    [JsonPropertyName("notificationFeedUrl")]
    public string NotificationFeedUrl { get; set; } = "";

    /// <summary>
    /// ETag from the last successful notification-feed fetch. Sent back as
    /// If-None-Match so the notifier can answer 304 Not Modified when nothing
    /// changed — the launcher then serves its on-disk feed cache without
    /// re-downloading. Opaque value — never parsed, just echoed. Mirrors
    /// <see cref="LauncherUpdateETag"/>.
    /// </summary>
    [JsonPropertyName("notificationFeedETag")]
    public string NotificationFeedETag { get; set; } = "";

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

    /// <summary>
    /// Persisted history of the notification-bell items (newest-relevant kept,
    /// trimmed to the most recent ~50 by <see cref="Services.NotificationCenter"/>).
    /// Empty on a fresh config; older configs without this key deserialize to an
    /// empty list, so no migration is needed.
    /// </summary>
    [JsonPropertyName("notifications")]
    public List<NotificationItem> Notifications { get; set; } = new();

    /// <summary>
    /// Launcher release tag for which the bell has ALREADY raised a
    /// "launcher update available" item. Dedup key so a given launcher version
    /// only bells once (the gold self-update pill is separate). Empty until the
    /// first launcher-update notification.
    /// </summary>
    [JsonPropertyName("notifiedLauncherTag")]
    public string NotifiedLauncherTag { get; set; } = "";

    /// <summary>
    /// Catalog mod ids for which the bell has ALREADY raised a "new mod" item.
    /// Seeded silently on the first catalog fetch (so the whole existing catalog
    /// doesn't flood the bell on first launch); afterwards only genuinely-new ids
    /// bell. <see cref="CatalogBaselineSeeded"/> distinguishes "empty because first
    /// run" from "empty because no mods".
    /// </summary>
    [JsonPropertyName("notifiedCatalogModIds")]
    public List<string> NotifiedCatalogModIds { get; set; } = new();

    /// <summary>
    /// True once the catalog "new mod" baseline has been seeded (see
    /// <see cref="NotifiedCatalogModIds"/>). Prevents the first-ever catalog fetch
    /// from belling every existing mod.
    /// </summary>
    [JsonPropertyName("catalogBaselineSeeded")]
    public bool CatalogBaselineSeeded { get; set; }

    /// <summary>
    /// Deduplicates the "new room created" Windows notification so the same room
    /// isn't re-announced across a restart. Capped like <see cref="NotifiedCatalogModIds"/>.
    /// </summary>
    [JsonPropertyName("notifiedRoomIds")]
    public List<string> NotifiedRoomIds { get; set; } = new();

    private const string ConfigFileName = "launcher-config.json";

    public static LauncherConfig Load()
    {
        var path = Services.AppPaths.ConfigFile;
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
        cfg.MigrateTranslationsFolderRepo();
        cfg.NormalizeModInstalls();
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
    /// One-time migration of the DEPRECATED single-repo
    /// <see cref="TranslationsFolderRepo"/> into the multi-repo model
    /// (<see cref="ExtraTranslationsFolderRepos"/> + <see cref="CommunityTranslationsDisabled"/>):
    ///   * <c>"none"</c> → set <see cref="CommunityTranslationsDisabled"/> = true.
    ///   * a custom <c>"owner/repo"</c> → append to the extra-repos list.
    ///   * <c>""</c> → nothing to do.
    /// Then clears the old field so it never re-migrates. Idempotent.
    /// </summary>
    private void MigrateTranslationsFolderRepo()
    {
        if (!ApplyDeprecatedTranslationsFolderRepoMigration()) return;
        try { Save(); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Config translationsFolderRepo migration save failed: {ex.Message}");
        }
        DiagnosticLog.Write("Migrated translationsFolderRepo into the multi-repo model.");
    }

    /// <summary>
    /// Pure in-place migration of the deprecated <see cref="TranslationsFolderRepo"/>
    /// into the multi-repo model (<see cref="ExtraTranslationsFolderRepos"/> +
    /// <see cref="CommunityTranslationsDisabled"/>): <c>"none"</c> → disabled;
    /// a custom <c>owner/repo</c> → appended (de-duped) to the extra list;
    /// <c>""</c> → no-op. Clears the old field afterward. Returns true iff it
    /// changed anything. Split out (no disk write) so it's unit-testable without
    /// touching <c>launcher-config.json</c>; the <see cref="Save"/> lives in the
    /// caller <see cref="MigrateTranslationsFolderRepo"/>. Idempotent.
    /// </summary>
    internal bool ApplyDeprecatedTranslationsFolderRepoMigration()
    {
        var old = (TranslationsFolderRepo ?? "").Trim();
        if (old.Length == 0) return false;

        if (string.Equals(old, "none", StringComparison.OrdinalIgnoreCase))
        {
            CommunityTranslationsDisabled = true;
        }
        else
        {
            var list = ExtraTranslationsFolderRepos?.ToList() ?? new List<string>();
            if (!list.Contains(old, StringComparer.OrdinalIgnoreCase))
                list.Add(old);
            ExtraTranslationsFolderRepos = list.ToArray();
        }

        TranslationsFolderRepo = "";
        return true;
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

    /// <summary>
    /// Normalize the multi-install shape of every mod after load. Idempotent and
    /// a NO-OP for single-install configs. The stock game is stripped of any
    /// multi-install state. Runs after <see cref="MigrateLegacyState"/> so the
    /// legacy flat fields are already folded into <see cref="Mods"/>.
    /// </summary>
    private void NormalizeModInstalls()
    {
        foreach (var kv in Mods)
        {
            var profile = ModRegistry.Find(kv.Key);
            kv.Value.NormalizeInstalls(isStock: profile?.IsStockGame ?? false);
        }
    }

    public void Save()
    {
        var path = Services.AppPaths.ConfigFile;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, options));
    }
}
