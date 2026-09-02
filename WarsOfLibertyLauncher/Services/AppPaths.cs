using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Single source of truth for the launcher's per-user DATA directory and the
/// runtime files it generates (config, debug log, update snapshot, telemetry).
///
/// These used to be written next to the .exe (via <c>AppContext.BaseDirectory</c>),
/// which cluttered whatever folder the user ran the .exe from (Downloads, Desktop,
/// …). They now live under <c>%LocalAppData%\AoE3ModLauncher\</c> — the SAME
/// per-user base the icon/catalog/news caches already use
/// (<see cref="ModAssetCacheService"/>'s <c>mod-assets</c> is a sibling), outside
/// Program Files so there's no UAC dance. This keeps the .exe's own folder clean
/// (just the .exe) and decouples the config from the .exe location, which also
/// makes the self-update more robust (the new .exe finds the config regardless of
/// where it lives). It is NOT an antivirus concern: writing benign data to
/// %LocalAppData% is the standard Windows pattern, unrelated to the single-file
/// compression packer heuristic.
///
/// <see cref="EnsureReady"/> MUST run once at startup (App.OnStartup) before the
/// first <see cref="DiagnosticLog"/> / <see cref="Models.LauncherConfig"/> access:
/// it creates the directory and migrates a pre-existing next-to-exe config.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// The environment variable that redirects everything below somewhere else.
    ///
    /// <para><b>It exists because without it an automated run writes over real user
    /// data.</b> The launcher settings dialog applies and PERSISTS a setting the moment it
    /// is touched, so any test that constructs the dialog and flips a control calls
    /// <c>LauncherConfig.Save()</c> — against a fresh <c>LauncherConfig</c>, over the
    /// developer's own <c>launcher-config.json</c>, losing their installed mods and their
    /// multiplayer sign-in. That is not hypothetical; it happened. The test assembly sets
    /// this variable from a <c>ModuleInitializer</c>, before any code can read
    /// <see cref="DataDir"/>.</para>
    ///
    /// <para>Read ONCE, into a static property: the value cannot change halfway through a
    /// run and leave half the files in one directory and half in another.</para>
    /// </summary>
    public const string DataDirOverrideVariable = "AOE3ML_DATA_DIR";

    /// <summary>Per-user data directory: <c>%LocalAppData%\AoE3ModLauncher\</c>.</summary>
    public static string DataDir { get; } = ResolveDataDir();

    private static string ResolveDataDir()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AoE3ModLauncher");
    }

    internal const string ConfigFileName = "launcher-config.json";

    /// <summary>Full path to <c>launcher-config.json</c> in the data directory.</summary>
    public static string ConfigFile => Path.Combine(DataDir, ConfigFileName);

    /// <summary>Full path to <c>launcher-debug.log</c> in the data directory.</summary>
    public static string LogFile => Path.Combine(DataDir, "launcher-debug.log");

    /// <summary>Full path to the opt-in <c>multiplayer-events.log</c>.</summary>
    public static string TelemetryFile => Path.Combine(DataDir, "multiplayer-events.log");

    /// <summary>Full path for a named diagnostic snapshot (e.g. UpdateInfo-snapshot.xml).</summary>
    public static string SnapshotFile(string name) => Path.Combine(DataDir, name);

    /// <summary>
    /// Root of the launcher's install scratch space:
    /// <c>%TEMP%\WarsOfLibertyLauncher\</c>, parent of <c>native-install\</c>
    /// (<see cref="NativeInstallService.TempDirectory"/>) and <c>installer\</c>
    /// (<see cref="InstallerService.TempDirectory"/>).
    ///
    /// <para>Deliberately NOT under <see cref="DataDir"/>: this holds multi-GB
    /// throwaway payload parts and their extraction, which belong in %TEMP% where
    /// the OS can reclaim them, not in the per-user data folder next to the config
    /// and logs.</para>
    ///
    /// <para><b>Why it is centralised here.</b> Both services used to build this
    /// path inline, and it is the folder the user must add to their antivirus
    /// exclusions — a known-permanent Defender false positive on a WoL payload file
    /// quarantines it mid-install. The exclusion advice names this exact path, so a
    /// second copy of the string could tell the user to exclude a folder the code no
    /// longer writes to, which fails silently and looks like the advice was wrong.</para>
    /// </summary>
    public static string InstallTempRoot =>
        Path.Combine(Path.GetTempPath(), "WarsOfLibertyLauncher");

    /// <summary>The legacy next-to-exe config path (pre-relocation).</summary>
    private static string LegacyConfigFile =>
        Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    /// <summary>
    /// Creates the data directory and performs a one-time migration of an existing
    /// next-to-exe <c>launcher-config.json</c> into it. Idempotent and best-effort:
    /// if the new config already exists we leave it; the old file is COPIED (not
    /// moved) so a rollback to an older launcher build still finds its config.
    /// Call once at startup before any config/log access.
    /// </summary>
    public static void EnsureReady()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* best-effort */ }

        try
        {
            if (!File.Exists(ConfigFile) && File.Exists(LegacyConfigFile))
            {
                File.Copy(LegacyConfigFile, ConfigFile, overwrite: false);
                DiagnosticLog.Write(
                    $"Migrated launcher-config.json from '{LegacyConfigFile}' to '{ConfigFile}'.");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: a failed migration just means the launcher starts with
            // fresh defaults in the new location (the old file is untouched).
            DiagnosticLog.Write($"Config migration skipped: {ex.Message}");
        }
    }
}
