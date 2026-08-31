using System;
using System.IO;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads and writes the real profile files behind <see cref="GameSettingsSync"/>: captures a
/// mod's graphics / sound / hotkeys into one shared copy when you stop playing, and grafts that
/// copy into the next mod you launch.
///
/// <para><b>Everything here is best-effort and silent on failure.</b> It runs on the launch path
/// and on the game-exit path; nothing it does is worth failing a launch or interrupting a
/// player, so every entry point returns a bool and swallows into the diagnostic log.</para>
///
/// <para><b>UTF-16 is not optional.</b> Age of Empires III writes these profiles as UTF-16 and
/// reads them at startup; round-tripping one through UTF-8 leaves a file the game can't load.
/// Reads use the byte-order mark, writes force <see cref="Encoding.Unicode"/>.</para>
/// </summary>
public static class GameSettingsStore
{
    /// <summary>Where the shared copy lives — beside <c>addons\</c> and <c>taunts\</c>.</summary>
    public static string SharedFolder => Path.Combine(AppPaths.DataDir, "settings");

    /// <summary>The one shared copy, holding just the sections that travel.</summary>
    public static string SharedFile => Path.Combine(SharedFolder, "shared-profile.xml");

    /// <summary>True once a mod has been played with sharing on, so there is something to apply.</summary>
    public static bool HasSharedSettings() => File.Exists(SharedFile);

    /// <summary>
    /// Stores this mod's settings as the shared copy. Called when the game exits, so the last
    /// mod played is the one whose settings the others pick up.
    /// </summary>
    public static bool CaptureFrom(ModProfile profile, LauncherConfig config)
    {
        try
        {
            var profilePath = ResolveProfilePath(profile, config);
            if (profilePath == null) return false;

            var shared = GameSettingsSync.ExtractSections(ReadProfile(profilePath));
            if (shared == null)
            {
                DiagnosticLog.Write($"GameSettings: '{profile.DisplayName}' has no settings sections to share.");
                return false;
            }

            Directory.CreateDirectory(SharedFolder);
            File.WriteAllText(SharedFile, shared, Encoding.Unicode);
            DiagnosticLog.Write($"GameSettings: captured from '{profile.DisplayName}'.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameSettings: capture from '{profile.DisplayName}' failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Grafts the shared copy into this mod's profile. Called just before launching, after the
    /// My Games junction is in place so the write lands in the folder the game will actually read.
    /// </summary>
    public static bool ApplyTo(ModProfile profile, LauncherConfig config)
    {
        try
        {
            if (!HasSharedSettings()) return false;

            var profilePath = ResolveProfilePath(profile, config);
            if (profilePath == null) return false;

            var current = ReadProfile(profilePath);
            var shared = ReadProfile(SharedFile);

            // Nothing to do is the common case once two mods agree — and skipping the write
            // keeps us off a file the game may be about to open.
            if (GameSettingsSync.SectionsMatch(current, shared)) return false;

            var grafted = GameSettingsSync.Graft(current, shared);
            if (grafted == null)
            {
                DiagnosticLog.Write($"GameSettings: nothing safe to graft into '{profile.DisplayName}' — left alone.");
                return false;
            }

            BackUpOnce(profilePath);
            File.WriteAllText(profilePath, grafted, Encoding.Unicode);
            DiagnosticLog.Write($"GameSettings: applied the shared settings to '{profile.DisplayName}'.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameSettings: applying to '{profile.DisplayName}' failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// What <see cref="ImportFrom"/> did — and, when it did nothing, WHY.
    ///
    /// <para><b>It used to be a bare bool, and that was a real defect long before anything
    /// depended on it.</b> Four unrelated causes returned the same <c>false</c>, so the settings
    /// page told a player whose mod had simply never been launched that "the settings couldn't be
    /// read" — which is not true and names nothing to fix. Two of those causes did not even
    /// reach the log.</para>
    ///
    /// <para>The one distinction that carries weight is <see cref="NoTargetProfile"/>: it means
    /// <b>not yet</b>, not <b>no</b>. Same shape and same reason as
    /// <see cref="GameRecordingWrite.NoProfile"/> right below.</para>
    /// </summary>
    public enum SettingsImportResult
    {
        /// <summary>The target profile now carries the source's graphics, sound and hotkeys.</summary>
        Imported,
        /// <summary>
        /// The target mod has no profile on disk. Age of Empires III writes one on its FIRST run,
        /// so a mod that was just installed and never opened lands here — and this is the whole
        /// reason the import can be left pending instead of discarded.
        /// </summary>
        NoTargetProfile,
        /// <summary>
        /// The source has no profile, or none carrying anything we share. Not a "not yet": it
        /// will read the same on every future launch.
        /// </summary>
        SourceUnavailable,
        /// <summary>Unreadable XML, or the write failed. The target is left exactly as it was.</summary>
        Failed,
    }

    /// <summary>
    /// Whether an import that did not happen is worth trying again on the next launch.
    ///
    /// <para>Pure, so the one rule that decides between "wait" and "give up" is pinned by a test
    /// rather than inferred from the I/O around it — the same treatment
    /// <see cref="PlanGameRecording"/> gets.</para>
    ///
    /// <para><b>Only a missing target profile waits.</b> Everything else describes something that
    /// will not change by itself, and retrying it every launch would be noise forever — while
    /// giving up on a mod that simply has not been opened yet would silently throw away a choice
    /// the player made during the install.</para>
    /// </summary>
    public static bool KeepPending(SettingsImportResult result)
        => result == SettingsImportResult.NoTargetProfile;

    /// <summary>
    /// Copies <paramref name="source"/>'s settings straight into <paramref name="target"/>, on
    /// demand — the "import settings from…" button, and the choice made in the install dialog.
    ///
    /// <para>Deliberately does NOT touch the shared copy: this is a one-off the player asked for
    /// between two named mods, not a statement about which settings are canonical. A mod outside
    /// the sharing group can both give and receive an import without joining it.</para>
    /// </summary>
    public static SettingsImportResult ImportFrom(
        ModProfile source, ModProfile target, LauncherConfig config)
    {
        try
        {
            // The TARGET is asked first, and separately, because its absence is the only answer
            // here that means "try again later" — see SettingsImportResult.NoTargetProfile.
            var to = ResolveProfilePath(target, config);
            if (to == null)
            {
                DiagnosticLog.Write(
                    $"GameSettings: '{target.DisplayName}' has no profile yet — it is written on the " +
                    "game's first run, so the import waits.");
                return SettingsImportResult.NoTargetProfile;
            }

            var from = ResolveProfilePath(source, config);
            if (from == null)
            {
                DiagnosticLog.Write($"GameSettings: '{source.DisplayName}' has no profile to copy from.");
                return SettingsImportResult.SourceUnavailable;
            }

            var shared = GameSettingsSync.ExtractSections(ReadProfile(from));
            if (shared == null)
            {
                DiagnosticLog.Write(
                    $"GameSettings: '{source.DisplayName}' carries no settings worth copying.");
                return SettingsImportResult.SourceUnavailable;
            }

            var grafted = GameSettingsSync.Graft(ReadProfile(to), shared);
            if (grafted == null)
            {
                DiagnosticLog.Write($"GameSettings: '{target.DisplayName}' profile could not be read.");
                return SettingsImportResult.Failed;
            }

            BackUpOnce(to);
            File.WriteAllText(to, grafted, Encoding.Unicode);
            DiagnosticLog.Write(
                $"GameSettings: imported '{source.DisplayName}' settings into '{target.DisplayName}'.");
            return SettingsImportResult.Imported;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"GameSettings: import '{source.DisplayName}' → '{target.DisplayName}' failed: {ex.Message}");
            return SettingsImportResult.Failed;
        }
    }

    /// <summary>
    /// Whether a mod may appear in the "import from" list for <paramref name="targetModId"/>.
    ///
    /// <para>Pure, so the rules are pinned by tests rather than buried in the dialog. A mod
    /// qualifies when it is installed, keeps a user-data folder of its own, is not the base game
    /// (detect-only — the launcher manages none of its files) and is not the mod being imported
    /// INTO, which would be a no-op the player could pick by accident.</para>
    /// </summary>
    public static bool CanImportFrom(
        string candidateModId, bool isStockGame, string? userDataFolder, bool installed, string targetModId)
    {
        if (string.IsNullOrWhiteSpace(candidateModId)) return false;
        if (string.Equals(candidateModId, targetModId, StringComparison.OrdinalIgnoreCase)) return false;
        if (isStockGame) return false;
        if (string.IsNullOrWhiteSpace(userDataFolder)) return false;
        return installed;
    }

    /// <summary>
    /// Carry out the copy the player asked for while installing this mod, if one is still owed.
    ///
    /// <para>Runs on every launch and is a no-op with no marker set, so the cost for everyone
    /// else is one string comparison. Best-effort throughout: this happens moments before the
    /// game starts, and the worst outcome would be a launch that fails over a convenience.</para>
    ///
    /// <para><b>The marker survives a launch that found no profile</b>, because that is exactly
    /// the case it exists for: the very first run of a freshly installed mod is what CREATES the
    /// profile, so the copy lands on the run after it. Any other outcome clears it — see
    /// <see cref="KeepPending"/>.</para>
    ///
    /// <para>Returns what happened, for the caller's log. The install path uses the same method
    /// so a reinstall — whose profile is already there — gets its settings immediately instead of
    /// waiting for a launch it does not need.</para>
    /// </summary>
    public static SettingsImportResult? ApplyPendingImport(ModProfile target, LauncherConfig config)
    {
        if (target == null || config == null) return null;

        // Read THROUGH the dictionary rather than GetState: merely launching a mod must not
        // create a blank state entry for one the player never configured. Same rule the mod
        // switcher and UserDataService.ResolveFolderName follow.
        if (config.Mods == null
            || !config.Mods.TryGetValue(target.Id, out var state)
            || state == null
            || string.IsNullOrWhiteSpace(state.PendingSettingsImportFrom))
            return null;

        var sourceId = state.PendingSettingsImportFrom;
        var source = ModRegistry.Find(sourceId);
        if (source == null)
        {
            // The mod was removed from the catalog, or uninstalled, between the install and now.
            // Nothing will bring it back, so stop owing the copy.
            DiagnosticLog.Write(
                $"GameSettings: the pending import for '{target.Id}' named '{sourceId}', which no " +
                "longer resolves — dropping it.");
            state.PendingSettingsImportFrom = "";
            SaveQuietly(config);
            return SettingsImportResult.SourceUnavailable;
        }

        var result = ImportFrom(source, target, config);
        if (KeepPending(result)) return result;

        state.PendingSettingsImportFrom = "";
        SaveQuietly(config);
        return result;
    }

    /// <summary>Whether this mod actually has a profile on disk to read settings from.</summary>
    public static bool HasReadableProfile(ModProfile profile, LauncherConfig config)
        => ResolveProfilePath(profile, config) != null;

    /// <summary>
    /// The profile file this mod's settings live in, or null when there isn't one yet.
    ///
    /// <para>Exposed so a caller can READ a setting back — the multiplayer tab checks whether
    /// recording is still enabled before telling the host why a match went unrecorded. Resolution
    /// is non-trivial (dual Documents roots, the active profile named inside
    /// <c>LastProfile3.dat</c>), so it must not be rebuilt by hand elsewhere.</para>
    /// </summary>
    public static string? ProfilePathFor(ModProfile profile, LauncherConfig config)
        => ResolveProfilePath(profile, config);

    // ---------------- game recording ----------------

    /// <summary>What <see cref="EnsureGameRecording"/> did, for the log and for the caller's notice.</summary>
    public enum GameRecordingWrite
    {
        /// <summary>Nothing to do: already where we left it, or a preference we have never acted on.</summary>
        NotNeeded,
        /// <summary>The profile now says what the player asked for.</summary>
        Wrote,
        /// <summary>No profile on disk yet — the game creates one on its first run. Try again next launch.</summary>
        NoProfile,
        /// <summary>A profile we could not understand or could not write. Left alone; try again next launch.</summary>
        Failed,
    }

    /// <param name="Write">Whether to touch the profile at all.</param>
    /// <param name="Value">What <c>optionrecordgame</c> should say.</param>
    /// <param name="ShowNotice">First time only: tell the player we turned recording on.</param>
    public readonly record struct GameRecordingPlan(bool Write, bool Value, bool ShowNotice);

    /// <summary>
    /// Decides whether to write game recording into one mod's profile. Pure, so the rule that
    /// keeps the launcher out of a player's profile is pinned by tests rather than inferred from
    /// the I/O around it.
    ///
    /// <para><b>The invariant: an opt-out is final.</b> Recording is on by default, so something
    /// has to write it for a new player — but if that write were driven by "the setting is off"
    /// instead of by <paramref name="applied"/>, then turning recording off would re-enable itself
    /// on the next launch, forever. Same reasoning as
    /// <see cref="StartupRegistrationService.PlanStartup"/>, and the same test guards it.</para>
    ///
    /// <para>Once <paramref name="applied"/> matches the preference there is nothing to do — even
    /// when the game has since changed the setting itself, which happens because Age of Empires III
    /// rewrites the whole profile when it exits. That is the player's own choice, made in the
    /// game's options screen, and the launcher must not sit on top of it every launch.</para>
    /// </summary>
    /// <param name="applied">What we last wrote here, or null if we never have.</param>
    /// <param name="wants">The player's launcher-wide preference.</param>
    public static GameRecordingPlan PlanGameRecording(bool? applied, bool wants, bool noticeAlreadyShown)
    {
        // Never touched this profile and recording isn't wanted: there is nothing to undo, and
        // writing "false" would mean editing a profile on behalf of a switched-off feature.
        // This is the branch the nullable marker exists for.
        if (applied == null && !wants) return default;

        if (applied == wants) return default;

        return new GameRecordingPlan(
            Write: true,
            Value: wants,
            ShowNotice: applied == null && wants && !noticeAlreadyShown);
    }

    /// <summary>
    /// Brings one mod's <c>optionrecordgame</c> in line with the player's preference, at most once
    /// per change. Saves the config when it acts.
    ///
    /// <para><b>Independent of <see cref="ModState.SyncGameSettings"/></b>, despite living beside
    /// it and being called two lines away from it: recording is a launcher-wide preference, not
    /// one of the settings the player asked to share between mods. <c>GameSettingsSync</c> keeps
    /// the two apart in the other direction too, by refusing to let recording travel in the shared
    /// copy.</para>
    ///
    /// <para><b>The marker is set only once the profile has actually been reached</b> — not before
    /// the attempt, which is where <see cref="LauncherConfig.BackgroundDefaultSeeded"/>
    /// deliberately differs. The reasoning inverts: a Run-key write that fails has hit a machine
    /// policy that will fail identically forever, so retrying is noise; but a missing
    /// <c>Users3\</c> is a legitimate "not yet" — the game creates it on its first run — and
    /// marking that mod as done would mean it never records at all, which is the bug this whole
    /// feature exists to fix. Retrying costs one no-op per launch.</para>
    /// </summary>
    public static GameRecordingWrite EnsureGameRecording(ModProfile profile, LauncherConfig config)
    {
        try
        {
            var state = config.GetState(profile.Id);
            var plan = PlanGameRecording(
                state.GameRecordingApplied, config.EnableGameRecording, config.GameRecordingNoticeShown);
            if (!plan.Write) return GameRecordingWrite.NotNeeded;

            var profilePath = ResolveProfilePath(profile, config);
            if (profilePath == null)
            {
                DiagnosticLog.Write(
                    $"GameRecording: no profile for '{profile.DisplayName}' yet — will try again next launch.");
                return GameRecordingWrite.NoProfile;
            }

            var wanted = plan.Value ? "true" : "false";
            var current = ReadProfile(profilePath);

            // Already correct in the file, just not recorded by us — adopt it without a write.
            if (string.Equals(
                    GameSettingsSync.ReadSetting(
                        current, GameSettingsSync.GameOptionsSection, GameSettingsSync.RecordGameSetting),
                    wanted, StringComparison.Ordinal))
            {
                state.GameRecordingApplied = plan.Value;
                SaveQuietly(config);
                return GameRecordingWrite.NotNeeded;
            }

            var updated = GameSettingsSync.EnsureSetting(
                current, GameSettingsSync.GameOptionsSection, GameSettingsSync.RecordGameSetting, wanted);
            if (updated == null)
            {
                // Unreadable, or a shape we don't recognise. Leave the marker alone so a profile
                // that becomes readable later still gets its turn.
                DiagnosticLog.Write(
                    $"GameRecording: could not set it in '{profile.DisplayName}' ({profilePath}) — left alone.");
                return GameRecordingWrite.Failed;
            }

            BackUpOnce(profilePath);
            File.WriteAllText(profilePath, updated, Encoding.Unicode);

            state.GameRecordingApplied = plan.Value;
            if (plan.ShowNotice) config.GameRecordingNoticePending = true;
            SaveQuietly(config);

            // Naming the file matters: ResolveProfilePath falls back to the newest .xml when the
            // active profile can't be resolved, so on a multi-profile install this can land
            // somewhere the player isn't using — a silent no-effect that is otherwise invisible.
            DiagnosticLog.Write(
                $"GameRecording: set {GameSettingsSync.RecordGameSetting}={wanted} " +
                $"for '{profile.DisplayName}' in {profilePath}");
            return GameRecordingWrite.Wrote;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"GameRecording: '{profile.DisplayName}' failed: {ex.Message}");
            return GameRecordingWrite.Failed;
        }
    }

    private static void SaveQuietly(LauncherConfig config)
    {
        try { config.Save(); }
        catch (Exception ex) { DiagnosticLog.Write($"GameRecording: config save failed: {ex.Message}"); }
    }

    /// <summary>
    /// Keeps one untouched copy of the profile as it was before this feature ever wrote to it.
    /// Written once and never overwritten: the point is the state the player had BEFORE settings
    /// started travelling, which a per-write backup would lose after the second launch.
    /// </summary>
    private static void BackUpOnce(string profilePath)
    {
        var backup = profilePath + ".bak";
        if (File.Exists(backup)) return;
        try { File.Copy(profilePath, backup); }
        catch (Exception ex) { DiagnosticLog.Write($"GameSettings: could not back up the profile: {ex.Message}"); }
    }

    /// <summary>
    /// The active profile file for a mod, or null when the mod doesn't participate.
    ///
    /// <para>The folder NAME comes from <see cref="UserDataService.ResolveFolderName"/>, not from
    /// <see cref="ModProfile.UserDataFolder"/> directly: most mods never declare that field, and
    /// reading it raw is what made this whole feature invisible for them. The stock game still
    /// resolves to nothing (the launcher manages none of its files) and is skipped exactly as the
    /// backup feature skips it. The absolute path then goes through the same service, so a
    /// Documents folder redirected to OneDrive lands where everything else looks.</para>
    /// </summary>
    private static string? ResolveProfilePath(ModProfile profile, LauncherConfig config)
    {
        var folderName = UserDataService.ResolveFolderName(profile, config);
        if (string.IsNullOrWhiteSpace(folderName)) return null;

        var folder = UserDataService.GetUserDataFolder(folderName);
        if (folder == null) return null;

        var users3 = Path.Combine(folder, "Users3");
        if (!Directory.Exists(users3)) return null;

        var profiles = Directory.GetFiles(users3, "*.xml");
        if (profiles.Length == 0) return null;
        if (profiles.Length == 1) return profiles[0];

        // More than one profile: the game records which is active in LastProfile3.dat. Falling
        // back to the newest file rather than guessing keeps us from writing settings into a
        // profile the player isn't using.
        var active = UserDataService.ReadActiveProfileFileName(users3);
        if (active != null)
        {
            var match = profiles.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), active, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return profiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    /// <summary>Reads a profile honouring its byte-order mark (these files are UTF-16).</summary>
    private static string ReadProfile(string path) =>
        File.ReadAllText(path, Encoding.Unicode);
}
