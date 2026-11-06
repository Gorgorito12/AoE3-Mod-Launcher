using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Whether another copy of a mod holds a newer version than the one in use, for the notice in
/// the mod window's copies list (design 71).
///
/// <para>The answer is deliberately "no" whenever the two tags cannot be ordered. GitHub tags
/// have no total order, and Improvement Mod's are dates written day first, which read as
/// version numbers and compare wrongly: 24.07.2026 would beat 01.10.2026.</para>
/// </summary>
internal static partial class CopyVersions
{
    [GeneratedRegex(@"^v?\d{1,2}\.\d{1,2}\.\d{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex DayFirstDate();

    internal static bool IsNewer(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current)) return false;
        if (DayFirstDate().IsMatch(candidate.Trim()) || DayFirstDate().IsMatch(current.Trim())) return false;
        return LauncherUpdateService.TryParseSemVer(candidate, out var a)
            && LauncherUpdateService.TryParseSemVer(current, out var b)
            && a > b;
    }

    /// <summary>The newest copy ahead of <paramref name="current"/>, or null if there is none.</summary>
    internal static (string Label, string Version)? NewestAhead(
        string? current, IEnumerable<(string Label, string Version)> others)
    {
        (string Label, string Version)? best = null;
        foreach (var other in others)
        {
            if (!IsNewer(other.Version, current)) continue;
            if (best is null || IsNewer(other.Version, best.Value.Version)) best = other;
        }
        return best;
    }
}
