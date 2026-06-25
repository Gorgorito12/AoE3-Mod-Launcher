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
    /// <summary>Per-user data directory: <c>%LocalAppData%\AoE3ModLauncher\</c>.</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoE3ModLauncher");

    internal const string ConfigFileName = "launcher-config.json";

    /// <summary>Full path to <c>launcher-config.json</c> in the data directory.</summary>
    public static string ConfigFile => Path.Combine(DataDir, ConfigFileName);

    /// <summary>Full path to <c>launcher-debug.log</c> in the data directory.</summary>
    public static string LogFile => Path.Combine(DataDir, "launcher-debug.log");

    /// <summary>Full path to the opt-in <c>multiplayer-events.log</c>.</summary>
    public static string TelemetryFile => Path.Combine(DataDir, "multiplayer-events.log");

    /// <summary>Full path for a named diagnostic snapshot (e.g. UpdateInfo-snapshot.xml).</summary>
    public static string SnapshotFile(string name) => Path.Combine(DataDir, name);

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
