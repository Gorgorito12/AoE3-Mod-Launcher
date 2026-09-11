using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The handful of settings the startup auto-update needs, read from — and written back to —
/// the config file WITHOUT going through <see cref="LauncherConfig"/>.
///
/// <para><b>Why not just <c>LauncherConfig.Load()</c>.</b> Load runs six migrations and can
/// rewrite the file, and <c>MainWindow</c>'s constructor is about to do all of that properly
/// a moment later. Doing it twice is, in this file's own words elsewhere, how a startup path
/// acquires a second opinion about the config — <c>App.ReadTextScaleSetting</c> is the
/// precedent and this is the same shape.</para>
///
/// <para><b>Why the writes are surgical.</b> Serialising a whole <see cref="LauncherConfig"/>
/// from here would stamp every default this process happens to hold over the user's real
/// file. Read-modify-write on a <see cref="JsonNode"/> touches the named keys and leaves
/// every other one byte-for-byte, including keys a NEWER build wrote that this one has never
/// heard of.</para>
///
/// <para>Nothing here throws. A config that cannot be read means "use the defaults and do
/// not auto-update"; a write that cannot land means the attempt latch does not advance,
/// which the next launch treats as a fresh attempt — bounded, and never a crash at startup.</para>
/// </summary>
public static class StartupUpdateState
{
    /// <summary>What the gate needs before it decides anything.</summary>
    public readonly record struct Snapshot(
        bool CheckUpdatesOnStartup,
        string LastInstalledLauncherTag,
        string LauncherUpdateETag,
        string AttemptTag,
        int AttemptCount,
        string Language,
        bool LanguageExplicitlyChosen);

    public static Snapshot Read() => Read(AppPaths.ConfigFile);

    /// <summary>
    /// <see cref="Read()"/> against a named file. The seam exists so the rules can be pinned
    /// by tests without writing to the config the rest of the run shares — the same reasoning
    /// as <c>DiskSpaceService.Check</c>'s injected free-space reader.
    /// </summary>
    internal static Snapshot Read(string path)
    {
        try
        {
            var root = ReadRoot(path);
            if (root == null) return Defaults();

            return new Snapshot(
                CheckUpdatesOnStartup: Bool(root, "checkUpdatesOnStartup", true),
                LastInstalledLauncherTag: Str(root, "lastInstalledLauncherTag"),
                LauncherUpdateETag: Str(root, "launcherUpdateETag"),
                AttemptTag: Str(root, "autoUpdateAttemptTag"),
                AttemptCount: Int(root, "autoUpdateAttemptCount"),
                Language: Str(root, "language"),
                LanguageExplicitlyChosen: Bool(root, "languageExplicitlyChosen", false));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"StartupUpdateState.Read: {ex.Message}");
            return Defaults();
        }
    }

    /// <summary>
    /// Record that we are about to try <paramref name="tag"/> unattended. Called BEFORE the
    /// download, not after the restart: an attempt that dies anywhere in between — a failed
    /// swap, a machine switched off mid-download, a binary that comes back reporting the old
    /// version — still has to count, or the loop guard guards nothing.
    /// </summary>
    public static void RecordAttempt(string tag, int count)
        => RecordAttempt(AppPaths.ConfigFile, tag, count);

    /// <inheritdoc cref="RecordAttempt(string,int)"/>
    internal static void RecordAttempt(string path, string tag, int count)
        => Write(path, root =>
        {
            root["autoUpdateAttemptTag"] = tag;
            root["autoUpdateAttemptCount"] = count;
        });

    /// <summary>
    /// Record the tag we are restarting into, so the new binary recognises itself instead of
    /// offering the update it just installed. The manual dialog does exactly this before its
    /// own relaunch; on this path there is no <c>MainWindow</c> to do it for us.
    /// </summary>
    public static void CommitInstalled(string tag)
        => CommitInstalled(AppPaths.ConfigFile, tag);

    /// <inheritdoc cref="CommitInstalled(string)"/>
    internal static void CommitInstalled(string path, string tag)
        => Write(path, root =>
        {
            root["lastInstalledLauncherTag"] = tag;
            root["skippedLauncherTag"] = "";
        });

    /// <summary>
    /// The UI language this launch should use, by the same rule
    /// <c>MainWindow.ApplyStartupLanguage</c> applies a moment later: follow the Windows
    /// display language until the user has picked one in Settings.
    ///
    /// <para>It has to be resolved HERE as well, because the auto-update window can be the
    /// only thing a user sees on a launch — and <see cref="Localization.Strings.Language"/>
    /// defaults to English until MainWindow's constructor runs. A Spanish player being told
    /// in English that their launcher is restarting itself is the worst version of this
    /// feature. This one only READS; MainWindow still owns the write.</para>
    /// </summary>
    public static string ResolveLanguage(Snapshot s)
    {
        try
        {
            if (!s.LanguageExplicitlyChosen)
                return LauncherConfig.DefaultLanguageForCulture(
                    System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"StartupUpdateState.ResolveLanguage: {ex.Message}");
        }
        return string.IsNullOrWhiteSpace(s.Language) ? Localization.Strings.LangEn : s.Language;
    }

    private static Snapshot Defaults() => new(true, "", "", "", 0, "", false);

    private static JsonObject? ReadRoot(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
    }

    private static void Write(string path, Action<JsonObject> mutate)
    {
        try
        {
            var root = ReadRoot(path) ?? new JsonObject();
            mutate(root);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"StartupUpdateState.Write: {ex.Message}");
        }
    }

    private static string Str(JsonObject root, string key)
        => root.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s)
            ? s ?? ""
            : "";

    private static bool Bool(JsonObject root, string key, bool fallback)
        => root.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b)
            ? b
            : fallback;

    private static int Int(JsonObject root, string key)
        => root.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<int>(out var i)
            ? i
            : 0;
}
