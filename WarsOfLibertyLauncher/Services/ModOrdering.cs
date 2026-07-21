using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Ordering rules for the dashboard's MODS switcher.
///
/// Pure + WPF-free ON PURPOSE so it's unit-testable off the UI thread
/// (<c>ModOrderingTests</c>) — same rationale as <see cref="PathDisplay"/> /
/// <see cref="RoomAgeFormat"/>: MainWindow's static brush fields blow up on a
/// thread with no STA, so anything worth testing has to live outside it.
/// State is passed in as lambdas rather than a <c>LauncherConfig</c> so the
/// caller keeps ownership of HOW it reads that state — in particular the
/// last-played lookup must be non-mutating (see the caller's note about
/// <c>GetState</c> creating entries).
/// </summary>
public static class ModOrdering
{
    /// <summary>
    /// Orders the switcher list: favourites first (Steam pins them to the top),
    /// then most-recently-played, then alphabetically by display name.
    ///
    /// Recency before name is what makes the list "accommodate the last mod you
    /// played" — the mod you actually use floats to the top instead of sitting
    /// wherever the alphabet put it. Mods never played (<c>null</c>) sort last
    /// within their group, which keeps a fresh install in plain alphabetical
    /// order until the user plays something.
    /// </summary>
    public static List<ModProfile> OrderForSwitcher(
        IEnumerable<ModProfile> mods,
        Func<string, bool> isFavorite,
        Func<string, DateTime?> lastPlayed)
    {
        if (mods == null) return new List<ModProfile>();

        return mods
            .OrderByDescending(p => isFavorite(p.Id))
            // DateTime.MinValue for "never played" puts those last without needing a
            // separate key — every real timestamp is greater.
            .ThenByDescending(p => lastPlayed(p.Id) ?? DateTime.MinValue)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
