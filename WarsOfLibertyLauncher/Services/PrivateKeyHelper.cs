using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The launcher's own executable, run elevated with ONE job: create a mod's private Age of
/// Empires III registry key (<see cref="SetupPathPatcher.EnsurePrivateKey"/>) and exit.
///
/// <para><b>Why it exists.</b> A mod that ships the stock executable
/// (<see cref="Models.ModProfile.PrivateSetupPath"/>) needs a key under HKLM, and writing HKLM
/// needs administrator rights even when the install folder itself is writable — which a Steam
/// library usually is. The launcher used to restart ITSELF as administrator for that: the player
/// lost what they had chosen in the install dialog, had to press Install again, and then ran the
/// rest of the session — the game included — elevated. Asking Windows for this single write
/// instead costs one consent prompt and nothing else.</para>
///
/// <para><b>Everything it is handed is checked, on purpose.</b> It is started by the launcher,
/// but a command line is text any program can write, and this one runs with the rights of the
/// Windows account that approved it. The key must be exactly the shape the patcher itself
/// produces for a mod (never a base-game key, see <see cref="IsAllowedKey"/>) and the folder a
/// plain absolute path (<see cref="IsAllowedSetupPath"/>). Nothing else is ever written.</para>
///
/// <para><b>Only HKLM.</b> The per-user EULA marker
/// (<see cref="SetupPathPatcher.CarryOverFirstRunMarker"/>) stays with the launcher: an elevation
/// may run as ANOTHER Windows account, whose HKCU is somebody else's.</para>
///
/// <para><b>A second verb, <see cref="RemoveFlag"/>,</b> removes LEFTOVER keys for the launcher's
/// own "Uninstall from this PC" (<see cref="PrivateKeySweep"/>). It re-checks every key it is
/// handed — the name, the key's licence values, and that its folder is provably gone — rather
/// than trusting the command line, and refuses the whole job if one no longer qualifies.</para>
/// </summary>
public static class PrivateKeyHelper
{
    /// <summary>The first argument that turns a launch into this helper.</summary>
    public const string Flag = "--write-private-key";

    /// <summary>The first argument of the removal verb: <c>--remove-private-key "&lt;key&gt;" …</c>.</summary>
    public const string RemoveFlag = "--remove-private-key";

    /// <summary>The most keys one removal takes: one mod can have nine (its name, then " #2" to
    /// " #9"), and a command line has a length limit worth staying far from.</summary>
    public const int MaxKeysPerRemoval = 16;

    /// <summary>The helper's process exit codes.</summary>
    public enum Exit
    {
        Ok = 0,
        Failed = 1,
        BadArguments = 2,
        /// <summary>The base game's own key is missing, so there is nothing to clone the mod's from.</summary>
        BaseKeyMissing = 3,
        AccessDenied = 4,
        /// <summary>A key handed to the removal verb is no longer a leftover, so it was kept.</summary>
        Refused = 5,
    }

    /// <summary>
    /// Whether this process was started as the helper — either verb, and it must come FIRST.
    /// Both verbs are recognised HERE, which is what <c>App.OnStartup</c> asks: a verb this did
    /// not know would reach the single-instance mutex, forward "show yourself" to the running
    /// launcher and exit 0 — a helper that "succeeds" having done nothing.
    /// </summary>
    public static bool IsInvocation(IReadOnlyList<string>? args)
        => IsVerb(args, Flag) || IsVerb(args, RemoveFlag);

    private static bool IsVerb(IReadOnlyList<string>? args, string verb)
        => args is { Count: > 0 } && string.Equals(args[0], verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The helper's arguments: exactly <c>--write-private-key "&lt;key&gt;" "&lt;folder&gt;"</c>,
    /// both values passing their checks. False for anything else.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string>? args, out string key, out string setupPath)
    {
        key = "";
        setupPath = "";
        if (args == null || args.Count != 3 || !IsVerb(args, Flag)) return false;
        if (!IsAllowedKey(args[1]) || !IsAllowedSetupPath(args[2])) return false;
        key = args[1];
        setupPath = args[2];
        return true;
    }

    /// <summary>
    /// A key this helper may create: exactly <c>Software\Microsoft\Microsoft Games\&lt;name&gt;\1.0</c>
    /// for a name <see cref="SetupPathPatcher.PrivateKeyFor"/> accepts — plain ASCII, short enough
    /// for the executable's slot, and never the name of one of the player's own games. Proving the
    /// round trip (name → key equals the key given) is what rules out an extra segment, a missing
    /// <c>\1.0</c> or a trailing separator, which the product-name check alone would let through.
    /// No quotes, ever: they cannot cross a command line intact.
    /// </summary>
    internal static bool IsAllowedKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOf('"') >= 0) return false;

        var product = SetupPathPatcher.ProductNameToRemove(key);
        if (product == null) return false;

        try
        {
            return string.Equals(SetupPathPatcher.PrivateKeyFor(product), key, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A folder the key may point at: absolute, already in its canonical form (so no <c>..</c>),
    /// not a bare drive, no device path, and no quotes or control characters.
    /// </summary>
    internal static bool IsAllowedSetupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.IndexOf('"') >= 0 || path.Any(char.IsControl)) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return false;
        if (!Path.IsPathFullyQualified(path)) return false;

        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!string.Equals(full, path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return false;

            var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\', '/');
            return !string.Equals(root, full, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The command line for the helper, or <b>null</b> when the values cannot be expressed on one
    /// — the caller then falls back to restarting the whole launcher elevated, as before.
    ///
    /// <para>The folder loses any trailing separator: a backslash right before the closing quote
    /// would escape it. The key already ends in <c>1.0</c>.</para>
    /// </summary>
    public static string? BuildArguments(string key, string setupPath)
    {
        if (!IsAllowedKey(key) || !IsAllowedSetupPath(setupPath)) return null;
        var folder = setupPath.TrimEnd('\\', '/');
        return $"{Flag} \"{key}\" \"{folder}\"";
    }

    /// <summary>
    /// The removal verb's arguments: <c>--remove-private-key</c> then 1 to
    /// <see cref="MaxKeysPerRemoval"/> DISTINCT keys, each one a key this helper could have
    /// created (<see cref="IsAllowedKey"/>). False for anything else — a stray extra argument is
    /// not a key to ignore, it is a command line nobody here wrote.
    /// </summary>
    public static bool TryParseRemove(IReadOnlyList<string>? args, out IReadOnlyList<string> keys)
    {
        keys = Array.Empty<string>();
        if (args == null || !IsVerb(args, RemoveFlag)) return false;
        var count = args.Count - 1;
        if (count < 1 || count > MaxKeysPerRemoval) return false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>(count);
        for (var i = 1; i < args.Count; i++)
        {
            if (!IsAllowedKey(args[i]) || !seen.Add(args[i])) return false;
            list.Add(args[i]);
        }
        keys = list;
        return true;
    }

    /// <summary>The removal verb's command line, or null when the keys cannot be expressed on one.</summary>
    public static string? BuildRemoveArguments(IReadOnlyList<string> keys)
    {
        if (keys == null || keys.Count < 1 || keys.Count > MaxKeysPerRemoval) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
            if (!IsAllowedKey(key) || !seen.Add(key)) return null;
        return RemoveFlag + string.Concat(keys.Select(k => $" \"{k}\""));
    }

    /// <summary>
    /// The helper itself: validate, do the one job, report through the exit code. Never logs — see
    /// <see cref="DiagnosticLog.DisableForHelperProcess"/>; the launcher that started it logs the
    /// outcome and re-checks the registry itself.
    /// </summary>
    public static int Run(IReadOnlyList<string> args)
        => IsVerb(args, RemoveFlag) ? RunRemove(args) : RunWrite(args);

    /// <summary>
    /// Removes leftover keys, HKLM only. Every key is checked again here — it exists, its licence
    /// values are the base game's, and its folder is provably gone — and one that no longer
    /// qualifies is KEPT and makes the run report <see cref="Exit.Refused"/>. A key already gone
    /// is not an error: the launcher may have removed it itself.
    /// </summary>
    private static int RunRemove(IReadOnlyList<string> args)
    {
        if (!TryParseRemove(args, out var keys)) return (int)Exit.BadArguments;

        var refused = 0;
        var failed = 0;
        foreach (var key in keys)
        {
            var facts = SetupPathPatcher.ReadKeyFacts(key);
            if (facts == null) continue;
            if (!PrivateKeySweep.IsOrphan(facts, PrivateKeySweep.ProbePath)) { refused++; continue; }
            if (!SetupPathPatcher.RemovePrivateKeyMachine(key)) failed++;
        }
        if (refused > 0) return (int)Exit.Refused;
        return failed > 0 ? (int)Exit.Failed : (int)Exit.Ok;
    }

    private static int RunWrite(IReadOnlyList<string> args)
    {
        if (!TryParse(args, out var key, out var setupPath)) return (int)Exit.BadArguments;
        if (!SetupPathPatcher.BaseGameKeyExists()) return (int)Exit.BaseKeyMissing;

        try
        {
            SetupPathPatcher.EnsurePrivateKey(key, setupPath);
            return (int)Exit.Ok;
        }
        catch (SetupPathKeyAccessException)
        {
            return (int)Exit.AccessDenied;
        }
        catch
        {
            return (int)Exit.Failed;
        }
    }
}
