using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One version's notification key, with the card and version it came from.</summary>
public sealed record TranslationVersionKey(string Key, TranslationIndexEntry Entry, TranslationVersion? Version);

/// <param name="SeedSilently">Keys to record WITHOUT ringing (the first look at a mod).</param>
/// <param name="Bell">Versions that are genuinely new and ring the bell.</param>
/// <param name="MarkBaselineSeeded">True when this was a complete look and the baseline is now set.</param>
public sealed record TranslationNotificationPlan(
    IReadOnlyList<string> SeedSilently, IReadOnlyList<TranslationVersionKey> Bell, bool MarkBaselineSeeded);

/// <summary>
/// Decides which translations ring the bell. Every VERSION has a key — not just each card's
/// newest — so when a translator the player follows publishes a new version, it rings even if
/// it isn't the newest of that language overall. Pure, so the two traps below are pinned by tests.
///
/// <para><b>Trap 1, the flood.</b> The first look at a mod records every key silently; otherwise a
/// player upgrading to per-version keys would be rung for every old version at once.
/// <b>Trap 2, the partial look.</b> The background sweep only fetches the sources the player
/// ADDED (the central feed covers the rest), so it may record keys but must not declare the
/// baseline done — only a complete look does, or the mod's own older versions would ring the
/// first time it is opened.</para>
/// </summary>
public static class TranslationNotificationPlanner
{
    public static List<TranslationVersionKey> KeysOf(IEnumerable<TranslationIndexEntry>? entries)
    {
        var result = new List<TranslationVersionKey>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries ?? Enumerable.Empty<TranslationIndexEntry>())
        {
            if (e == null) continue;
            if (e.Versions != null && e.Versions.Count > 0)
            {
                foreach (var v in e.Versions)
                {
                    var key = TranslationCompat.KeyOfVersion(e.Id, v);
                    if (key.Length > 0 && seen.Add(key)) result.Add(new TranslationVersionKey(key, e, v));
                }
            }
            else
            {
                var key = TranslationCompat.KeyOf(e);
                if (key.Length > 0 && seen.Add(key)) result.Add(new TranslationVersionKey(key, e, null));
            }
        }
        return result;
    }

    public static TranslationNotificationPlan Decide(
        IEnumerable<string>? alreadyNotified, bool baselineSeeded, bool lookIsComplete,
        IReadOnlyList<TranslationVersionKey> keys)
    {
        var known = new HashSet<string>(alreadyNotified ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var fresh = keys.Where(k => !known.Contains(k.Key)).ToList();
        if (!baselineSeeded)
            return new TranslationNotificationPlan(fresh.Select(k => k.Key).ToList(),
                Array.Empty<TranslationVersionKey>(), lookIsComplete);
        return new TranslationNotificationPlan(Array.Empty<string>(), fresh, false);
    }
}
