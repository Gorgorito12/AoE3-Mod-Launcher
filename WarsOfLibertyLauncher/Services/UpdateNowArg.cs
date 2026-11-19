using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// <c>--update-now[=fresh|copy]</c>: the elevated relaunch that applies a mod update, carrying
/// whether that update is the tail of a FIRST install or of a new COPY (2026-11-18). Without the
/// context, an install that needed administrator rights finished with an "Update complete" bell
/// for what the player experienced as an installation. Anything after the <c>=</c> that is not one
/// of the two words is ignored, never guessed. Pure, pinned by <c>UpdateNowArgTests</c>.
/// </summary>
internal static class UpdateNowArg
{
    internal const string Name = "--update-now";

    internal const string Fresh = "fresh";
    internal const string Copy = "copy";

    /// <summary>The argument for a context token (<see cref="Fresh"/>, <see cref="Copy"/> or null).</summary>
    internal static string For(string? context)
        => context is Fresh or Copy ? $"{Name}={context}" : Name;

    /// <summary>Any form of the argument. <c>App.OnStartup</c> must use THIS, not an exact match:
    /// with the context attached an exact match would miss it and the startup self-update would
    /// restart out from under the elevated job.</summary>
    internal static bool IsPresent(IEnumerable<string>? args)
        => (args ?? Enumerable.Empty<string>()).Any(a =>
            string.Equals((a ?? "").Trim(), Name, StringComparison.OrdinalIgnoreCase)
            || (a ?? "").Trim().StartsWith(Name + "=", StringComparison.OrdinalIgnoreCase));

    /// <summary>The context it carries, or null.</summary>
    internal static string? ContextOf(IEnumerable<string>? args)
    {
        foreach (var raw in args ?? Enumerable.Empty<string>())
        {
            var a = (raw ?? "").Trim();
            if (!a.StartsWith(Name + "=", StringComparison.OrdinalIgnoreCase)) continue;
            var value = a[(Name.Length + 1)..].ToLowerInvariant();
            return value is Fresh or Copy ? value : null;
        }
        return null;
    }
}
