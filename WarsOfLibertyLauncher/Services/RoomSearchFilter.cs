using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The rooms-browser search: "Buscar sala, mod o jugador" (design handoff 1a).
///
/// <para>Pure and WPF-free so the matching rule is unit-testable — the same reason
/// <see cref="RoomsTableLayout"/> and <see cref="PathDisplay"/> live out here. What
/// counts as a match is a judgement call, and a judgement call that only exists inside
/// a render method can't be pinned.</para>
/// </summary>
public static class RoomSearchFilter
{
    /// <summary>
    /// Rooms whose title, mod id or host matches <paramref name="query"/>.
    ///
    /// <para>An empty or whitespace query returns the list UNCHANGED — including its
    /// order, since the caller sorts afterwards. That is the common case (the box is
    /// empty most of the time), so it must not cost a copy or disturb anything.</para>
    ///
    /// <para>Matching is case- and accent-insensitive on purpose. The player base is
    /// largely Spanish-speaking, so a room called "Partida rápida" has to be findable
    /// by typing "rapida" — most people don't reach for the accent, and a search that
    /// silently returns nothing reads as "there are no rooms".</para>
    ///
    /// <para>Substring rather than prefix, and across three fields at once, because the
    /// box is one field for all of them: someone typing a host's name has no way to say
    /// which column they mean.</para>
    /// </summary>
    public static IReadOnlyList<LobbySummary> Apply(
        IReadOnlyList<LobbySummary> rooms, string? query)
    {
        if (rooms == null || rooms.Count == 0) return rooms ?? Array.Empty<LobbySummary>();
        if (string.IsNullOrWhiteSpace(query)) return rooms;

        var needle = Normalize(query);
        if (needle.Length == 0) return rooms;

        return rooms.Where(r => Matches(r, needle)).ToList();
    }

    private static bool Matches(LobbySummary room, string needle)
    {
        if (room == null) return false;
        if (Normalize(room.Title).Contains(needle, StringComparison.Ordinal)) return true;
        if (Normalize(room.ModId).Contains(needle, StringComparison.Ordinal)) return true;

        var host = room.Host;
        if (host == null) return false;
        return Normalize(host.DisplayName).Contains(needle, StringComparison.Ordinal)
            || Normalize(host.DiscordUsername).Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lowercases and strips diacritics, so "rápida" and "rapida" compare equal.
    ///
    /// <para>Decomposing to FormD splits an accented letter into its base plus a
    /// combining mark, which can then be dropped by category. Comparing the result with
    /// <see cref="StringComparison.Ordinal"/> is deliberate: the folding has already
    /// happened here, and a culture-sensitive Contains on top would be both slower and
    /// dependent on the machine's locale.</para>
    /// </summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
