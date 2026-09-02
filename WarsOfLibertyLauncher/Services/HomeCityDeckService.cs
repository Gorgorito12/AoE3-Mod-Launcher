using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads the decks a player has built, from the home city files the game keeps in
/// <c>My Games\&lt;mod&gt;\Savegame\</c>.
///
/// <para><b>Why this file is read at all: the deck is on disk and it is NOT in the recording.</b>
/// With a real 25-card deck in hand, searching its exact ids through an inflated <c>.age3Yrec</c>
/// finds at most 5 of 25 inside 250 bytes and 11 of 25 inside a kilobyte — noise, in a file whose
/// card references cluster in the mod's own data tables long before the command stream. So no
/// recording, anyone's own included, can say which cards were played; this file is the only place
/// card choices survive.</para>
///
/// <para>Shaped like <see cref="AiGameStats"/> beside it: the parse is pure and takes the file's
/// TEXT so it is tested against a fixture with no disk, and exactly one method opens a file.</para>
/// </summary>
public static class HomeCityDeckService
{
    /// <summary>The game writes these next to the savegames, one per civilization played.</summary>
    public const string FolderName = "Savegame";

    private const string FilePattern = "*homecity*.xml";

    /// <summary>A guard against a pathological file. A real deck holds 25.</summary>
    private const int MaxCardsPerDeck = 512;

    /// <summary>Likewise — the largest seen holds two.</summary>
    private const int MaxDecks = 64;

    /// <summary>
    /// The home city a file describes, or null when it is not one.
    ///
    /// <para>Returns null rather than throwing for anything unreadable: this runs to fill a
    /// panel, and a mod's malformed file must cost that one civilization's decks and nothing
    /// else.</para>
    /// </summary>
    /// <param name="sourceFile">File name without extension, kept for display and diagnosis.</param>
    /// <param name="xml">The file's text, already decoded — see <see cref="Read"/> on the encoding.</param>
    public static HomeCityProfile? Parse(string sourceFile, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XDocument doc;
        try { doc = XDocument.Parse(xml, LoadOptions.None); }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HomeCityDeck: '{sourceFile}' did not parse — {ex.Message}");
            return null;
        }

        var root = doc.Root;
        if (root == null || !string.Equals(root.Name.LocalName, "savedhomecity",
                StringComparison.OrdinalIgnoreCase))
            return null;

        var profile = new HomeCityProfile
        {
            SourceFile = sourceFile,
            Civ = Text(root.Element("civ")),
            CityName = Text(root.Element("name")),
            Level = ReadInt(root.Element("level")),
        };

        var decks = root.Element("decks");
        if (decks == null) return profile;

        foreach (var deck in decks.Elements("deck"))
        {
            if (profile.Decks.Count >= MaxDecks) break;

            var entry = new HomeCityDeckEntry
            {
                Name = Text(deck.Element("name")),
                GameId = ReadInt(deck.Element("gameid")),
            };

            var cards = deck.Element("cards");
            if (cards != null)
            {
                foreach (var card in cards.Elements("card"))
                {
                    if (entry.Cards.Count >= MaxCardsPerDeck) break;

                    var name = (card.Value ?? "").Trim();
                    if (name.Length == 0) continue;

                    entry.Cards.Add(new HomeCityCard
                    {
                        // The position IS the slot the player sees, so it comes from the file's
                        // order and never from a sort.
                        Slot = entry.Cards.Count,
                        Dbid = ReadInt(card.Attribute("dbid")?.Value),
                        InternalName = name,
                    });
                }
            }

            profile.Decks.Add(entry);
        }

        return profile;
    }

    private static string Text(XElement? element) => element == null ? "" : element.Value.Trim();

    private static int ReadInt(XElement? element) => ReadInt(element?.Value);

    private static int ReadInt(string? raw)
        => raw != null
           && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;

    // ------------------------------------------------------------------ the one method with IO

    /// <summary>
    /// Every home city this mod's user data holds, by city name.
    ///
    /// <para><paramref name="userDataDir"/> is the resolved <c>My Games\&lt;mod&gt;</c> folder — a
    /// plain string, never a profile and never the config, so this stays testable and the caller
    /// keeps the dual-root resolution it already owns.</para>
    ///
    /// <para><b>The files are UTF-16 with a BOM and reading them as UTF-8 yields nothing without
    /// erroring</b> — the same silent failure the <c>.personality</c> files have, and one that cost
    /// a wasted diagnosis once. <c>File.ReadAllText</c> with no encoding detects the BOM, which is
    /// why none is passed.</para>
    /// </summary>
    public static IReadOnlyList<HomeCityProfile> Read(string userDataDir)
    {
        var all = new List<HomeCityProfile>();
        if (string.IsNullOrWhiteSpace(userDataDir)) return all;

        try
        {
            var dir = Path.Combine(userDataDir, FolderName);
            if (!Directory.Exists(dir)) return all;

            foreach (var file in Directory.EnumerateFiles(dir, FilePattern))
            {
                try
                {
                    var parsed = Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                    if (parsed != null) all.Add(parsed);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"HomeCityDeck: could not read '{file}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HomeCityDeck: could not scan '{userDataDir}' — {ex.Message}");
        }

        return Deduplicate(all);
    }

    /// <summary>
    /// Drops the <c>LastHomeCity*.xml</c> copy, which is a byte-for-byte duplicate of whichever
    /// city was used last — shown as a second civilization it would read as a deck the player does
    /// not have. Pure so the rule is pinned rather than trusted.
    /// </summary>
    internal static List<HomeCityProfile> Deduplicate(IReadOnlyList<HomeCityProfile> profiles)
    {
        var kept = new List<HomeCityProfile>();

        foreach (var p in profiles.OrderBy(p => p.SourceFile.StartsWith(
                     "LastHomeCity", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            // Same civ AND same city is the duplicate; two cities of one civ are two real
            // profiles, and a player who renamed one keeps both.
            if (kept.Any(k => string.Equals(k.Civ, p.Civ, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(k.CityName, p.CityName, StringComparison.OrdinalIgnoreCase)))
                continue;

            kept.Add(p);
        }

        return kept;
    }
}
