using System;
using System.Collections.Generic;
using System.IO;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The LAN helper's one-shot elevated copy: when the game's folder is one this account cannot
/// write (Program Files), the launcher runs ITSELF elevated with <see cref="InstallFlag"/>, the
/// copy happens in that process, and the launcher carries on unelevated — one consent prompt, no
/// restart. The same shape as <see cref="PrivateKeyHelper"/>, and dispatched beside it, first
/// thing in <c>App.OnStartup</c> (before the single-instance mutex and the log).
///
/// <para><b>The command line is treated as hostile</b>: it is the one thing a process running as
/// administrator takes from outside. The path must be absolute, canonical, free of quotes and
/// control characters, and end in <c>.exe</c>; and the job itself is
/// <see cref="LanHelperService.Install"/> / <see cref="LanHelperService.Remove"/>, which refuse
/// an executable they do not know, another tool's <c>dinput8.dll</c> and a mod that ships the
/// key. The launcher re-checks the folder itself afterwards rather than trusting the exit code.</para>
/// </summary>
public static class LanHelperElevated
{
    public const string InstallFlag = "--install-lan-helper";
    public const string RemoveFlag = "--remove-lan-helper";

    public enum Exit
    {
        Ok = 0,
        Failed = 1,
        BadArguments = 2,
    }

    /// <summary>Either verb, as the FIRST argument.</summary>
    public static bool IsInvocation(IReadOnlyList<string>? args)
        => IsVerb(args, InstallFlag) || IsVerb(args, RemoveFlag);

    private static bool IsVerb(IReadOnlyList<string>? args, string verb)
        => args is { Count: > 0 } && string.Equals(args[0], verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>A game executable the install verb may name.</summary>
    internal static bool IsAllowedExe(string? path)
        => PrivateKeyHelper.IsAllowedSetupPath(path)
           && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Exactly <c>verb "&lt;path&gt;"</c>, the path passing its check. False for anything else.</summary>
    public static bool TryParse(IReadOnlyList<string>? args, out bool install, out string path)
    {
        install = IsVerb(args, InstallFlag);
        path = "";
        if (args == null || args.Count != 2 || !IsInvocation(args)) return false;
        if (install ? !IsAllowedExe(args[1]) : !PrivateKeyHelper.IsAllowedSetupPath(args[1])) return false;
        path = args[1];
        return true;
    }

    /// <summary>The install verb's command line, or null when the path cannot cross one intact.</summary>
    public static string? BuildInstallArguments(string exePath)
        => IsAllowedExe(exePath) ? $"{InstallFlag} \"{exePath}\"" : null;

    /// <summary>The removal verb's command line, or null.</summary>
    public static string? BuildRemoveArguments(string folder) => BuildRemoveArguments(new[] { folder });

    /// <summary>
    /// How many folders one removal may name. Generous for a real machine (one entry per game
    /// folder the player ever entered a room with) and small enough that a command line can never
    /// grow without bound.
    /// </summary>
    public const int MaxFoldersPerRemoval = 16;

    /// <summary>
    /// One removal for several folders, so taking the helper out of every protected folder costs
    /// ONE administrator prompt instead of one per folder. Null when any folder fails its check,
    /// two are the same, or there are none or too many: a partial command would be a surprise.
    /// </summary>
    public static string? BuildRemoveArguments(IReadOnlyList<string> folders)
    {
        if (folders == null || folders.Count == 0 || folders.Count > MaxFoldersPerRemoval) return null;
        var trimmed = new List<string>();
        foreach (var folder in folders)
        {
            var one = folder?.TrimEnd('\\', '/') ?? "";
            if (!PrivateKeyHelper.IsAllowedSetupPath(one)) return null;
            if (trimmed.Exists(t => string.Equals(t, one, StringComparison.OrdinalIgnoreCase))) return null;
            trimmed.Add(one);
        }
        return RemoveFlag + " " + string.Join(" ", trimmed.ConvertAll(f => $"\"{f}\""));
    }

    /// <summary>
    /// The removal verb with one to <see cref="MaxFoldersPerRemoval"/> distinct folders, every one
    /// passing its check. Anything else is refused whole: one bad path among good ones is a command
    /// line somebody else wrote.
    /// </summary>
    public static bool TryParseRemove(IReadOnlyList<string>? args, out IReadOnlyList<string> folders)
    {
        folders = Array.Empty<string>();
        if (!IsVerb(args, RemoveFlag) || args!.Count < 2 || args.Count - 1 > MaxFoldersPerRemoval) return false;
        var list = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            if (!PrivateKeyHelper.IsAllowedSetupPath(args[i])) return false;
            if (list.Exists(f => string.Equals(f, args[i], StringComparison.OrdinalIgnoreCase))) return false;
            list.Add(args[i]);
        }
        folders = list;
        return true;
    }

    /// <summary>The helper: validate, do the one job, answer with the exit code.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            if (IsVerb(args, RemoveFlag))
            {
                if (!TryParseRemove(args, out var folders)) return (int)Exit.BadArguments;
                var allGone = true;
                foreach (var folder in folders) allGone &= LanHelperService.Remove(folder);
                return allGone ? (int)Exit.Ok : (int)Exit.Failed;
            }
            if (!TryParse(args, out _, out var exe)) return (int)Exit.BadArguments;
            return LanHelperService.Install(exe) == null ? (int)Exit.Ok : (int)Exit.Failed;
        }
        catch
        {
            return (int)Exit.Failed;
        }
    }
}
