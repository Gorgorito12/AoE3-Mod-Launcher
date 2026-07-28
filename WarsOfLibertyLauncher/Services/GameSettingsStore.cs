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
    /// Copies <paramref name="source"/>'s settings straight into <paramref name="target"/>, on
    /// demand — the "import settings from…" button.
    ///
    /// <para>Deliberately does NOT touch the shared copy: this is a one-off the player asked for
    /// between two named mods, not a statement about which settings are canonical. A mod outside
    /// the sharing group can both give and receive an import without joining it.</para>
    /// </summary>
    public static bool ImportFrom(ModProfile source, ModProfile target, LauncherConfig config)
    {
        try
        {
            var from = ResolveProfilePath(source, config);
            var to = ResolveProfilePath(target, config);
            if (from == null || to == null) return false;

            var shared = GameSettingsSync.ExtractSections(ReadProfile(from));
            if (shared == null) return false;

            var grafted = GameSettingsSync.Graft(ReadProfile(to), shared);
            if (grafted == null) return false;

            BackUpOnce(to);
            File.WriteAllText(to, grafted, Encoding.Unicode);
            DiagnosticLog.Write(
                $"GameSettings: imported '{source.DisplayName}' settings into '{target.DisplayName}'.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"GameSettings: import '{source.DisplayName}' → '{target.DisplayName}' failed: {ex.Message}");
            return false;
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

    /// <summary>Whether this mod actually has a profile on disk to read settings from.</summary>
    public static bool HasReadableProfile(ModProfile profile, LauncherConfig config)
        => ResolveProfilePath(profile, config) != null;

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
