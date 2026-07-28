using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Helpers for the per-mod user-data folder under the user's Documents.
/// Each mod that opts into this feature declares a folder name (relative to
/// <c>%USERPROFILE%\Documents\My Games\</c>) in its catalog manifest as
/// <c>userDataFolder</c>; WoL's built-in profile sets it to
/// <c>"Wars of Liberty"</c>. Other mods can leave it empty to opt out.
///
/// Typical folder shape:
///
///   C:\Users\&lt;name&gt;\Documents\My Games\&lt;folder&gt;\
///
/// Holds saves (<c>Savegame\</c>), custom metropolises, replays and config
/// files. AoE3 itself uses the same "My Games" parent folder — that's the
/// standard Microsoft convention.
///
/// None of this is touched by the launcher's install/uninstall flow (it's
/// user data and should survive a reinstall), but if the user installs an
/// OLDER mod version on top of newer save files the game can crash on
/// startup — newer metropolis formats can't be parsed by older binaries.
///
/// This service only DETECTS and offers to back up. It never deletes.
/// </summary>
public static class UserDataService
{
    /// <summary>
    /// Resolves the absolute path of a mod's user-data folder. Returns null
    /// when <paramref name="folderName"/> is empty (mod doesn't opt in) or
    /// when we can't determine the user's Documents path.
    ///
    /// TWO candidate Documents roots are probed, because they can diverge and
    /// the 2007 engine's saves may live in either (the "backup went to a
    /// totally different path" report from a German user):
    ///   1. The SYSTEM Documents folder (GetFolderPath(MyDocuments)) — follows
    ///      Windows redirections, e.g. OneDrive Known Folder Move, where on a
    ///      German system the REAL path is "...\OneDrive\Dokumente".
    ///   2. The PHYSICAL "%USERPROFILE%\Documents" — where saves written
    ///      BEFORE a redirection was enabled (or by software that ignores it)
    ///      still live.
    /// The first candidate whose "<root>\My Games\<folder>" EXISTS wins; when
    /// neither exists the redirected one wins (creation case — new data should
    /// follow the system convention). Divergence is logged once per session so
    /// a diagnostic bundle carries the evidence.
    /// </summary>
    public static string? GetUserDataFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;
        try
        {
            var candidates = GetCandidateUserDataFolders(folderName);
            var chosen = PickUserDataFolder(candidates, Directory.Exists);
            LogDivergenceOnce(folderName, candidates, chosen);
            return chosen;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ordered, deduped candidate paths of "<Documents root>\My Games\<folder>"
    /// — redirected Documents first, physical %USERPROFILE%\Documents second
    /// (only when it differs). Exposed so the UI/diagnostics can surface both.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateUserDataFolders(string folderName)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(folderName)) return result;

        void Add(string? docsRoot)
        {
            if (string.IsNullOrEmpty(docsRoot)) return;
            string path;
            try { path = Path.Combine(docsRoot, "My Games", folderName); }
            catch { return; }
            if (!result.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                result.Add(path);
        }

        try { Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); } catch { }
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) Add(Path.Combine(profile, "Documents"));
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Pure candidate-selection rule (testable with an injected existence
    /// probe): first candidate folder that exists wins; none exists → the
    /// first candidate (creation follows the system Documents convention);
    /// empty candidate list → null.
    /// </summary>
    internal static string? PickUserDataFolder(
        IReadOnlyList<string> candidates, Func<string, bool> directoryExists)
    {
        if (candidates.Count == 0) return null;
        foreach (var c in candidates)
        {
            try { if (directoryExists(c)) return c; }
            catch { /* unreadable candidate — treat as absent */ }
        }
        return candidates[0];
    }

    /// <summary>
    /// A candidate "My Games" subfolder, as seen by the discovery rule below.
    /// <paramref name="LooksLikeAoE3Data"/> is the caller's disk probe result
    /// so the rule itself stays pure.
    /// </summary>
    internal readonly record struct UserDataCandidate(string Name, bool LooksLikeAoE3Data);

    /// <summary>
    /// The vanilla game's own folder. Never handed to a mod: its saves belong
    /// to the player's base game, and letting a mod claim it would make the
    /// settings-sharing feature write into vanilla's profile.
    /// </summary>
    internal const string VanillaFolderName = "Age of Empires 3";

    /// <summary>
    /// Works out which "My Games" folder belongs to a mod that never declared
    /// one, so the user-data features (settings sharing, backup/restore, the
    /// game artifacts in a diagnostics bundle) work for EVERY mod rather than
    /// only the ones whose author remembered the manifest field.
    ///
    /// Matching is on the NORMALIZED name — case, punctuation and an "AoE3" /
    /// "Age of Empires 3" prefix dropped — accepting an exact match or a folder
    /// that STARTS WITH the mod's name. That is what resolves the two real
    /// shapes: "AoE3 Improvement Mod" for <i>Improvement Mod</i>, and
    /// "Napoleonic Era Beta 2" for <i>Napoleonic Era</i>.
    ///
    /// <para>Three guards, and they are the point of this method. Picking the
    /// wrong folder means writing one mod's graphics/hotkeys into another mod's
    /// profile — silent, and not something the user could diagnose:</para>
    /// <list type="number">
    ///   <item>the vanilla folder is never returned;</item>
    ///   <item>a folder already spoken for by another profile is never returned
    ///         (<paramref name="claimed"/>) — one folder, one mod;</item>
    ///   <item>the folder must actually look like AoE3 user data.</item>
    /// </list>
    /// <para>Ambiguity resolves to null, deliberately: no answer leaves the UI
    /// offering a manual picker, which the user can fix, while a wrong answer
    /// corrupts another mod's settings.</para>
    /// </summary>
    internal static string? MatchUserDataFolder(
        string modDisplayName,
        IReadOnlyList<UserDataCandidate> candidates,
        IReadOnlyCollection<string> claimed)
    {
        var want = NormalizeFolderKey(modDisplayName);
        if (want.Length == 0) return null;

        var claimedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in claimed)
            if (!string.IsNullOrWhiteSpace(c)) claimedKeys.Add(c.Trim());

        string? exact = null;
        string? prefixed = null;
        var prefixedCount = 0;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Name)) continue;
            // Guard 1 + 2: the base game's folder, and anything another profile owns.
            if (string.Equals(candidate.Name, VanillaFolderName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (claimedKeys.Contains(candidate.Name)) continue;
            // Guard 3: it has to be an AoE3-family user-data folder.
            if (!candidate.LooksLikeAoE3Data) continue;

            var key = NormalizeFolderKey(candidate.Name);
            if (key.Length == 0) continue;

            if (key == want)
            {
                // Two folders normalizing to the same name is not something we
                // can resolve; refuse rather than guess.
                if (exact != null) return null;
                exact = candidate.Name;
            }
            else if (key.StartsWith(want, StringComparison.Ordinal))
            {
                prefixedCount++;
                prefixed = candidate.Name;
            }
        }

        if (exact != null) return exact;
        return prefixedCount == 1 ? prefixed : null;
    }

    /// <summary>
    /// Lowercase, alphanumerics only, with a leading "aoe3" / "ageofempires3" /
    /// "ageofempiresiii" dropped — the game's own folders carry that prefix
    /// inconsistently, so it can't be part of the comparison.
    /// </summary>
    internal static string NormalizeFolderKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));

        var key = sb.ToString();
        foreach (var prefix in new[] { "ageofempiresiii", "ageofempires3", "aoe3" })
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                return key[prefix.Length..];
        return key;
    }

    /// <summary>
    /// The NON-chosen candidate that exists and holds at least one file, or
    /// null. Surfaced as a warning in the USER DATA tab — data in two places
    /// means a Documents redirection happened mid-life and the user should
    /// know both locations exist.
    /// </summary>
    public static string? GetAlternateDataFolderWithFiles(string folderName)
    {
        try
        {
            var candidates = GetCandidateUserDataFolders(folderName);
            if (candidates.Count < 2) return null;
            var chosen = PickUserDataFolder(candidates, Directory.Exists);
            foreach (var c in candidates)
            {
                if (string.Equals(c, chosen, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(c)
                    && Directory.EnumerateFiles(c, "*", SearchOption.AllDirectories).Any())
                    return c;
            }
        }
        catch { /* best-effort informational probe */ }
        return null;
    }

    /// <summary>
    /// THE single source of truth for "which My Games folder is this mod's".
    /// Every user-data surface goes through here — settings sharing, backup and
    /// restore, and the game artifacts a diagnostics bundle collects — so they
    /// can never disagree about where a mod's data lives.
    ///
    /// <para>Order: the catalog's declared <c>userDataFolder</c> (authoritative,
    /// unchanged behaviour), then whatever was previously worked out and
    /// remembered in <see cref="ModState.UserDataFolder"/>, then a fresh
    /// discovery by name — which is persisted so the answer stays stable.</para>
    ///
    /// <para>Empty means "not resolved yet", NOT "this mod opts out". The one
    /// real opt-out is the stock game profile, which is excluded because the
    /// launcher does not manage the base game's data.</para>
    /// </summary>
    public static string ResolveFolderName(ModProfile profile, LauncherConfig config)
    {
        if (profile == null || config == null) return "";
        if (!string.IsNullOrWhiteSpace(profile.UserDataFolder)) return profile.UserDataFolder;
        if (profile.IsStockGame) return "";

        // Read through the dictionary rather than GetState: merely resolving a
        // folder must not create a blank entry for every mod the user never
        // touched (the same rule the mod switcher follows for LastPlayedUtc).
        if (config.Mods != null
            && config.Mods.TryGetValue(profile.Id, out var state)
            && !string.IsNullOrWhiteSpace(state?.UserDataFolder))
            return state.UserDataFolder;

        var found = DiscoverFolderName(profile.DisplayName, ClaimedFolderNames(config, profile.Id));
        if (string.IsNullOrWhiteSpace(found)) return "";

        RememberFolderName(profile.Id, found, config);
        DiagnosticLog.Write(
            $"User data: discovered folder '{found}' for '{profile.Id}' (manifest declares none).");
        return found;
    }

    /// <summary>
    /// Persists a resolved folder for a mod. Public so the learn-from-launch
    /// path can record what the game itself just told us.
    /// </summary>
    public static void RememberFolderName(string modId, string folderName, LauncherConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(folderName))
            return;
        try
        {
            config.GetState(modId).UserDataFolder = folderName;
            config.Save();
        }
        catch (Exception ex)
        {
            // Losing the note only costs a re-discovery next time.
            DiagnosticLog.Write($"User data: could not remember folder for '{modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Folder names already spoken for by some OTHER mod — declared in a
    /// manifest or previously remembered — plus the vanilla game's own. Feeds
    /// the "one folder, one mod" guard.
    /// </summary>
    public static IReadOnlyCollection<string> ClaimedFolderNames(LauncherConfig config, string exceptModId)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { VanillaFolderName };
        try
        {
            foreach (var p in ModRegistry.All)
            {
                if (string.Equals(p.Id, exceptModId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(p.UserDataFolder)) claimed.Add(p.UserDataFolder);
            }
            if (config?.Mods != null)
                foreach (var kv in config.Mods)
                {
                    if (string.Equals(kv.Key, exceptModId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(kv.Value?.UserDataFolder))
                        claimed.Add(kv.Value.UserDataFolder);
                }
        }
        catch { /* a partial claim set only risks refusing a match, never a wrong one */ }
        return claimed;
    }

    /// <summary>
    /// The name this player appears under INSIDE the game — the one a recorded game
    /// stores, and the only link between a replay and the person who played it.
    ///
    /// <para><b>It is not in <c>LastProfile3.dat</c>.</b> That file holds the active
    /// profile's FILE name, which on every install checked is the default
    /// <c>NewProfile3</c> — the same string for everyone, useless for telling players
    /// apart. The real name lives inside <c>Users3\&lt;profile&gt;.xml</c> as
    /// <c>&lt;OnlineName&gt;</c>. Assuming otherwise costs nothing until it silently
    /// matches every player against the same placeholder.</para>
    ///
    /// <para>Per MOD, not per machine: each mod keeps its own profile, and the same
    /// person can be "Gorgorito" in one and "gorgorito" in another — hence every
    /// comparison against this is case-insensitive.</para>
    ///
    /// <para>Null when it cannot be read, which callers must treat as "cannot identify
    /// this player" rather than falling back to a guess.</para>
    /// </summary>
    public static string? GetInGameName(ModProfile profile, LauncherConfig config)
    {
        try
        {
            var folder = GetUserDataFolder(ResolveFolderName(profile, config));
            if (string.IsNullOrEmpty(folder)) return null;

            var users3 = Path.Combine(folder, "Users3");
            if (!Directory.Exists(users3)) return null;

            var active = ReadActiveProfileFileName(users3);
            string? xml = null;
            if (!string.IsNullOrEmpty(active))
            {
                var candidate = Path.Combine(users3, active + ".xml");
                if (File.Exists(candidate)) xml = candidate;
            }
            // The pointer file can be missing or stale; the newest profile is the best
            // remaining guess at which one the game last wrote.
            xml ??= Directory.EnumerateFiles(users3, "*.xml")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (xml == null) return null;

            return ExtractInGameName(File.ReadAllText(xml, System.Text.Encoding.Unicode));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"User data: could not read the in-game name: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pulls the player's name out of a profile XML. <c>OnlineName</c> is the one a
    /// replay records; <c>optionskirmishnickname</c> is the fallback, identical on every
    /// profile inspected but present in its own right, so it costs nothing to accept.
    /// </summary>
    internal static string? ExtractInGameName(string profileXml)
    {
        if (string.IsNullOrEmpty(profileXml)) return null;

        var online = Between(profileXml, "<OnlineName>", "</OnlineName>");
        if (!string.IsNullOrWhiteSpace(online)) return online.Trim();

        var nick = Between(profileXml, "optionskirmishnickname\">", "<");
        return string.IsNullOrWhiteSpace(nick) ? null : nick.Trim();

        static string? Between(string haystack, string open, string close)
        {
            var a = haystack.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (a < 0) return null;
            a += open.Length;
            var b = haystack.IndexOf(close, a, StringComparison.Ordinal);
            return b < 0 ? null : haystack[a..b];
        }
    }

    /// <summary>
    /// The active profile's FILE name from <c>LastProfile3.dat</c> — a short UTF-16 blob
    /// with a couple of leading bytes, so the readable text is taken rather than the
    /// whole decode. Null when it can't be read.
    ///
    /// <para>This is a file name (<c>NewProfile3</c>), NOT the player's name — see
    /// <see cref="GetInGameName"/>.</para>
    /// </summary>
    public static string? ReadActiveProfileFileName(string users3Dir)
    {
        try
        {
            var dat = Path.Combine(users3Dir, "LastProfile3.dat");
            if (!File.Exists(dat)) return null;

            var decoded = System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(dat));
            var name = new string(decoded.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Every "My Games" subfolder across both Documents roots, each flagged with
    /// whether it looks like AoE3 user data. The disk half of
    /// <see cref="MatchUserDataFolder"/>.
    /// </summary>
    internal static IReadOnlyList<UserDataCandidate> EnumerateUserDataCandidates()
    {
        var seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        // GetCandidateUserDataFolders takes a folder NAME and returns the per-root
        // paths for it; passing a placeholder gives us the "My Games" parents.
        foreach (var probe in GetCandidateUserDataFolders("_"))
        {
            string? parent;
            try { parent = Path.GetDirectoryName(probe); }
            catch { continue; }
            if (string.IsNullOrEmpty(parent)) continue;

            try
            {
                if (!Directory.Exists(parent)) continue;
                foreach (var dir in Directory.EnumerateDirectories(parent))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    var looks = LooksLikeAoE3UserData(dir);
                    // A folder present under both roots counts as AoE3 data if
                    // EITHER copy does — the split-Documents case again.
                    seen[name] = seen.TryGetValue(name, out var had) ? (had || looks) : looks;
                }
            }
            catch { /* unreadable root — skip it */ }
        }

        return seen.Select(kv => new UserDataCandidate(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// The shape every AoE3-family user-data folder shares. <c>Users3\</c> is
    /// the meaningful one: it holds the profile file the settings-sharing
    /// feature reads, so a folder without it is useless to us even if the name
    /// matched perfectly.
    /// </summary>
    internal static bool LooksLikeAoE3UserData(string path)
    {
        try { return Directory.Exists(Path.Combine(path, "Users3")); }
        catch { return false; }
    }

    /// <summary>
    /// Discovers the "My Games" folder name for a mod that declares none.
    /// Returns null when nothing matches confidently — see
    /// <see cref="MatchUserDataFolder"/> for why refusing beats guessing.
    /// </summary>
    public static string? DiscoverFolderName(
        string modDisplayName, IReadOnlyCollection<string> claimed)
    {
        try
        {
            return MatchUserDataFolder(
                modDisplayName, EnumerateUserDataCandidates(), claimed);
        }
        catch { return null; }
    }

    // Divergence is interesting once per mod per session — GetUserDataFolder
    // is called from every tab refresh and would otherwise spam the log.
    private static readonly HashSet<string> s_divergenceLogged = new(StringComparer.OrdinalIgnoreCase);

    private static void LogDivergenceOnce(
        string folderName, IReadOnlyList<string> candidates, string? chosen)
    {
        if (candidates.Count < 2 || chosen == null) return;
        if (!s_divergenceLogged.Add(folderName)) return;
        var flags = candidates
            .Select(c => $"'{c}' (exists={SafeExists(c)})");
        DiagnosticLog.Write(
            $"User-data roots diverge for '{folderName}': {string.Join(" vs ", flags)} -> using '{chosen}'.");

        static bool SafeExists(string p)
        {
            try { return Directory.Exists(p); } catch { return false; }
        }
    }

    /// <summary>
    /// True if the user has a populated data folder for this mod under
    /// Documents. "Populated" means at least one file exists somewhere
    /// under it. Returns false when the mod doesn't opt into the feature.
    /// </summary>
    public static bool HasExistingUserData(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renames the user-data folder to "&lt;folderName&gt;.bak.&lt;timestamp&gt;"
    /// so the game starts with a clean slate. Returns the new backup path on
    /// success, or null if there was nothing to back up / the rename failed.
    ///
    /// We never DELETE — the user can manually clean up the .bak folder later
    /// once they've confirmed the new install works.
    /// </summary>
    public static string? BackupUserData(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = folder + ".bak." + stamp;

        try
        {
            Directory.Move(folder, backupPath);
            DiagnosticLog.Write($"Backed up user data: '{folder}' -> '{backupPath}'");
            return backupPath;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to back up user data ('{folderName}'): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens the user-data folder in Explorer so the user can inspect /
    /// move / delete files manually. No-op if the folder doesn't exist or
    /// the mod doesn't opt into the feature.
    /// </summary>
    public static void OpenUserDataFolder(string folderName)
    {
        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to open user-data folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Information about a single backup the launcher created on a previous
    /// install (renamed from <c>&lt;folder&gt;</c> to
    /// <c>&lt;folder&gt;.bak.&lt;ts&gt;</c>).
    /// </summary>
    public record BackupInfo(
        string Path,
        DateTime CreatedAt,
        int FileCount,
        int SavegameCount,
        long TotalBytes);

    /// <summary>
    /// Lists every backup folder that lives next to the active user-data
    /// folder — in EVERY candidate Documents root, so a backup made before a
    /// OneDrive/moved-Documents redirection still shows up. Sorted by
    /// creation time, most recent first. Returns an empty list when the mod
    /// doesn't opt into the feature or there are no backups.
    /// </summary>
    public static List<BackupInfo> ListBackups(string folderName)
    {
        var result = new List<BackupInfo>();
        var parents = GetCandidateUserDataFolders(folderName)
            .Select(Path.GetDirectoryName)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

        // Folders we created look like: "<folderName>.bak.20260507-123456"
        try
        {
            var pattern = $"{folderName}.bak.*";
            foreach (var dir in parents.SelectMany(parent => Directory.EnumerateDirectories(parent, pattern)))
            {
                int count = 0;
                long totalBytes = 0;
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        count++;
                        try { totalBytes += new FileInfo(f).Length; }
                        catch { /* unreadable file; skip its size */ }
                    }
                }
                catch { /* unreadable; report 0 */ }

                int savegameCount = 0;
                try
                {
                    var savegame = Path.Combine(dir, "Savegame");
                    if (Directory.Exists(savegame))
                        savegameCount = Directory.EnumerateFiles(savegame, "*", SearchOption.AllDirectories).Count();
                }
                catch { /* unreadable; report 0 */ }

                DateTime created;
                try { created = Directory.GetCreationTime(dir); }
                catch { created = DateTime.MinValue; }

                result.Add(new BackupInfo(dir, created, count, savegameCount, totalBytes));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Failed to enumerate user-data backups: {ex.Message}");
        }

        result.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return result;
    }

    /// <summary>
    /// Restores a backup folder by swapping it with the active user-data
    /// folder. If the active folder currently has files, those files are
    /// renamed to a new ".bak.&lt;ts&gt;" first so nothing is lost — the user
    /// can swap back and forth between snapshots indefinitely.
    /// </summary>
    /// <returns>
    /// The path of the new backup that was created from the active data
    /// (so the caller can mention it to the user), or null if the active
    /// folder was empty / didn't exist.
    /// </returns>
    public static string? RestoreBackup(string folderName, string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
            throw new DirectoryNotFoundException($"Backup not found: {backupPath}");

        var folder = GetUserDataFolder(folderName);
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException(
                "Could not resolve Documents path / mod doesn't declare userDataFolder.");

        string? newBackupOfCurrent = null;

        // Step 1: if the active folder has anything in it, snapshot it as a
        // fresh backup. We never overwrite without preserving.
        if (Directory.Exists(folder))
        {
            bool hasFiles = false;
            try { hasFiles = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(); }
            catch { hasFiles = true; /* be conservative — don't lose data we can't read */ }

            if (hasFiles)
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                newBackupOfCurrent = folder + ".bak." + stamp;
                Directory.Move(folder, newBackupOfCurrent);
                DiagnosticLog.Write($"Snapshotted active data before restore: '{folder}' -> '{newBackupOfCurrent}'");
            }
            else
            {
                // Empty folder; just delete so Move below can create cleanly.
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        // Step 2: rename the chosen backup back into the active path. With
        // dual-root listing the backup may live under the OTHER Documents
        // root (e.g. physical Documents while the active folder resolves to
        // OneDrive) — a cross-volume Move throws IOException, so fall back to
        // a recursive copy. The source backup is deliberately LEFT IN PLACE
        // on that path (never delete on a degraded path; the user can clean
        // it up once the restore is confirmed good).
        try
        {
            Directory.Move(backupPath, folder);
        }
        catch (IOException)
        {
            CopyDirectory(backupPath, folder);
            DiagnosticLog.Write(
                $"Restore crossed volumes; backup copied and the original was left at '{backupPath}'.");
        }
        DiagnosticLog.Write($"Restored backup: '{backupPath}' -> '{folder}'");

        return newBackupOfCurrent;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
