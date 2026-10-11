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
/// </summary>
public static class PrivateKeyHelper
{
    /// <summary>The first argument that turns a launch into this helper.</summary>
    public const string Flag = "--write-private-key";

    /// <summary>The helper's process exit codes.</summary>
    public enum Exit
    {
        Ok = 0,
        Failed = 1,
        BadArguments = 2,
        /// <summary>The base game's own key is missing, so there is nothing to clone the mod's from.</summary>
        BaseKeyMissing = 3,
        AccessDenied = 4,
    }

    /// <summary>Whether this process was started as the helper — the flag must come FIRST.</summary>
    public static bool IsInvocation(IReadOnlyList<string>? args)
        => args is { Count: > 0 } && string.Equals(args[0], Flag, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The helper's arguments: exactly <c>--write-private-key "&lt;key&gt;" "&lt;folder&gt;"</c>,
    /// both values passing their checks. False for anything else.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string>? args, out string key, out string setupPath)
    {
        key = "";
        setupPath = "";
        if (args == null || args.Count != 3 || !IsInvocation(args)) return false;
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
    /// The helper itself: validate, write the key, report through the exit code. Never logs — see
    /// <see cref="DiagnosticLog.DisableForHelperProcess"/>; the launcher that started it logs the
    /// outcome and re-checks the key itself.
    /// </summary>
    public static int Run(IReadOnlyList<string> args)
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
