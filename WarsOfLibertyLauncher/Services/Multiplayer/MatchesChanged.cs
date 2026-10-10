using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The server's <c>matches_changed</c> frame on <c>/global/ws</c>: a match was stored, or its
/// result or rating changed (a late reading, a team confirmation, a founding, a void). It is
/// sent to EVERY connected launcher, so a page showing the community's matches can refresh when
/// something actually happened instead of on a clock — which is what made people go to Library
/// and back to see who had just won.
///
/// <para>Before it existed the launcher learned nothing when a match it was not in ended: the
/// community block refreshed at most once a minute and only on the Rooms page with the window in
/// front, the Ranking page never by itself, and Ranking › Matches and the profile history once a
/// run. An older server simply never sends it; the pages then fall back to refreshing when they
/// are entered.</para>
/// </summary>
internal static class MatchesChanged
{
    /// <summary>
    /// How long a launcher waits before refreshing, at least. Several matches can change within a
    /// second (a report, then its late rating), and one refresh covers them all.
    /// </summary>
    internal const int DebounceMs = 2000;

    /// <summary>
    /// Up to this much more, at random, so every launcher online does not ask in the same instant.
    /// The server shares concurrent misses anyway; this keeps the burst off the per-IP limits of
    /// a house or a CGNAT where several launchers share one address.
    /// </summary>
    internal const int JitterMs = 2000;

    /// <summary>
    /// How close to the top Ranking › Matches has to be scrolled for a refresh to redraw it. A
    /// refresh replaces the list, so it must never happen under somebody reading further down —
    /// for them the list is marked stale and refreshed the next time they open the view.
    /// </summary>
    internal const double NearTopPx = 48;

    /// <summary>The user ids the frame names; empty when it names none or is malformed.</summary>
    internal static IReadOnlyList<string> UserIds(JsonElement frame) => ReadIds(frame, "userIds");

    /// <summary>The match ids the frame names; empty when it names none or is malformed.</summary>
    internal static IReadOnlyList<string> MatchIds(JsonElement frame) => ReadIds(frame, "matchIds");

    /// <summary>Whether the viewer played in one of the changed matches — their own history and
    /// standing then need fetching too, not only the community's lists.</summary>
    internal static bool IncludesViewer(IReadOnlyList<string> userIds, string? viewerId)
    {
        if (string.IsNullOrEmpty(viewerId)) return false;
        foreach (var id in userIds)
            if (string.Equals(id, viewerId, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Whether an open Ranking › Matches may be redrawn in place.</summary>
    internal static bool MayRedrawOpenList(double verticalOffset) => verticalOffset <= NearTopPx;

    private static IReadOnlyList<string> ReadIds(JsonElement frame, string name)
    {
        var list = new List<string>();
        if (frame.ValueKind != JsonValueKind.Object
            || !frame.TryGetProperty(name, out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s);
        return list;
    }
}
