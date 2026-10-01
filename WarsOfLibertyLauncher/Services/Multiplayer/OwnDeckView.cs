using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The rules the Profile's DECKS section applies to the player's OWN decks before drawing them.
/// Pure and WPF-free, so every refusal here is pinned by a test rather than by a screenshot.
/// </summary>
internal static class OwnDeckView
{
    /// <summary>
    /// The decks that belong to this mod, and how many were left out because they do not.
    ///
    /// <para><b>Why a mod's folder can hold another mod's decks at all.</b> The game writes its
    /// home cities into whatever <c>My Games</c> folder the EXECUTABLE writes to, and a player who
    /// ran one mod's exe from another mod's folder — a test install, a copy made by hand — leaves
    /// that mod's decks behind. Measured on a real Wars of Liberty folder: Struggle of Indonesia's
    /// Toba, Surakarta and NetherlandsIndie beside the player's own Bulgarians, drawn under WoL's
    /// name with raw internal names because WoL has no such civilizations to name them by.</para>
    ///
    /// <para><b>The test is membership of the mod's civ list, never whether a DISPLAY name
    /// resolved.</b> Napoleonic Era names 36 of its civilizations with ids its string table does
    /// not carry, so "no display name" would hide real decks. <paramref name="playableCivs"/> is
    /// the internal names from <c>civs.xml</c> (<see cref="CivNameResolver.ResolvePlayableCivs"/>).
    /// And <b>an empty list hides NOTHING</b>: it means the list could not be read (a mod not on
    /// disk, a packed file that failed), which says nothing about any deck.</para>
    /// </summary>
    internal static (IReadOnlyList<HomeCityProfile> Own, int ForeignDecks) SplitForeign(
        IEnumerable<HomeCityProfile> profiles, IReadOnlyCollection<string> playableCivs)
    {
        var all = profiles.ToList();
        if (playableCivs.Count == 0) return (all, 0);

        var known = new HashSet<string>(playableCivs, StringComparer.OrdinalIgnoreCase);
        var own = new List<HomeCityProfile>();
        var foreign = 0;
        foreach (var p in all)
        {
            // A file with no civilization cannot be judged either way; it stays.
            if (string.IsNullOrWhiteSpace(p.Civ) || known.Contains(p.Civ.Trim())) own.Add(p);
            else foreign += Math.Max(1, p.Decks.Count);
        }
        return (own, foreign);
    }

    /// <summary>
    /// The names the game gives a deck nobody named: they say nothing, so a pill does not spend
    /// half its width repeating one beside every civilization.
    /// </summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "My Deck", "Static Deck", "Default", "Deck", "New Deck", "Mi mazo", "Mazo",
    };

    internal static bool IsGenericName(string? name)
        => string.IsNullOrWhiteSpace(name) || GenericNames.Contains(name.Trim());

    /// <summary>
    /// One label per deck, in order: the civilization, the deck's own name when it has a real one,
    /// and — only where two labels would otherwise read the same — the home city, then a number.
    ///
    /// <para>Two "Chinese · Static Deck" pills sat side by side on a real profile. They are two
    /// cities (Beijing, Beijings), and that is the one thing the file says that tells them apart.
    /// A number is the last resort, for two decks of one city with no names.</para>
    /// </summary>
    internal static IReadOnlyList<string> Labels(IReadOnlyList<(string Civ, string? City, string? DeckName)> decks)
    {
        var baseLabels = decks
            .Select(d => IsGenericName(d.DeckName) ? d.Civ : d.Civ + " · " + d.DeckName!.Trim())
            .ToList();

        var labels = new string[decks.Count];
        foreach (var group in Enumerable.Range(0, decks.Count)
                     .GroupBy(i => baseLabels[i], StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count == 1) { labels[members[0]] = baseLabels[members[0]]; continue; }

            // With the city where the city tells them apart…
            var withCity = members.ToDictionary(i => i, i =>
                string.IsNullOrWhiteSpace(decks[i].City) ? baseLabels[i] : baseLabels[i] + " · " + decks[i].City!.Trim());

            // …and a number for whatever the city could not separate.
            foreach (var clash in members.GroupBy(i => withCity[i], StringComparer.OrdinalIgnoreCase))
            {
                var same = clash.ToList();
                for (var k = 0; k < same.Count; k++)
                    labels[same[k]] = same.Count == 1 ? withCity[same[k]] : $"{withCity[same[k]]} #{k + 1}";
            }
        }
        return labels;
    }

    /// <summary>
    /// The age as the game writes it, I to V. Null for anything the file could not mean — an age
    /// the launcher cannot name is left out of the sentence rather than printed as "Age 0".
    /// </summary>
    internal static string? AgeNumeral(int? age) => age switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        _ => null,
    };
}
