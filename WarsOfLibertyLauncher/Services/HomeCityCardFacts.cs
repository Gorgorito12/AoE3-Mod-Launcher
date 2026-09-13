using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The two things the GAME draws on a home-city card that the tech tree does not carry: the
/// number in the corner, and the age the card can be sent in.
/// </summary>
/// <param name="Count">
/// <c>&lt;displayunitcount&gt;</c> — literally the figure the game paints in the icon's corner
/// (<c>3</c> on "3 Cholos", <c>700</c> on a crate). Null when the card has none, which is not a
/// gap: measured across Wars of Liberty's 57 home cities, 4,325 of 7,593 entries carry it and the
/// rest are technology cards, on which <b>the game draws no number either</b>.
/// </param>
/// <param name="Age">
/// The age the card may be sent in, <b>as the game displays it</b> — 1 to 4. The file stores it
/// zero-based (<c>&lt;age&gt;0&lt;/age&gt;</c> is Discovery), and the +1 is applied HERE, once, so
/// nothing downstream can print "Age 0". Verified against the crate ladder: 300 food at age 0,
/// 1000 at age 2 — Discovery and Fortress.
/// </param>
public readonly record struct HomeCityCardFact(int? Count, int? Age);

/// <summary>
/// Reads <c>data\homecity&lt;civ&gt;.xml</c>, the file the game itself gets a card's corner number
/// and age from.
///
/// <para><b>Neither is in the tech tree.</b> A census of all 4,517 <c>&lt;Flag&gt;HomeCity&lt;/Flag&gt;</c>
/// techs of Wars of Liberty finds no count field at all, and <c>&lt;Prereqs&gt;</c> on 35 of them —
/// not an age gate. <see cref="CardNameResolver"/> reads that file for the name, the art and the
/// effects; these two live somewhere else and need their own pass.</para>
///
/// <para><b>⚠ KEYED BY (CIVILIZATION, CARD), and that is not tidiness.</b> Measured over the 57
/// files: of 4,151 distinct card names, <b>62 carry a different age depending on the
/// civilization</b> — <c>HCAlchemy</c>, <c>HCUnlockFactory</c>, <c>HCAdvancedPlantations</c> among
/// them — and the count varies the same way (<c>HCMercsHolyRoman</c> is 16 for the British, the sum
/// of its three mercenary effects). A map keyed by card alone is a wrong answer for those, not a
/// simpler one, and a wrong number in the corner is the kind of confident mistake
/// <see cref="CardEffectText.Format"/> refuses to make elsewhere.</para>
///
/// <para><b>Do not call this on the UI thread the first time.</b> Each civilization is a ~60 KB
/// parse; it is cheap beside the 12 MB tech-tree pass it rides along with, and it rides along with
/// it precisely so it is off the UI thread for free.</para>
///
/// <para><b>A mod whose files are packed gets nothing, honestly.</b> Improvement Mod and Napoleonic
/// Era keep <c>civs.xml</c> — and therefore their home cities — inside a <c>.bar</c>. The answer is
/// an empty map: no corner number, no age, the name and the effects exactly as before. Never a
/// special case by mod id.</para>
/// </summary>
public static class HomeCityCardFacts
{
    /// <summary>The dictionary key. One place, so the writer and every reader cannot disagree
    /// about the separator — which is the whole failure mode of a composite string key.</summary>
    public static string Key(string? civ, string? card) => (civ ?? "") + "|" + (card ?? "");

    /// <summary>
    /// A guard against a malformed file, not a real limit: Wars of Liberty's biggest home city
    /// holds a few hundred cards.
    /// </summary>
    private const int MaxCardsPerCiv = 4000;

    /// <summary>Per install path, per civilization. Process-lifetime like the resolvers beside
    /// it: these files change when the mod is reinstalled and not otherwise.</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, HomeCityCardFact>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The facts for every card of every civilization in <paramref name="civs"/>, keyed by
    /// <see cref="Key"/>. Never throws and never returns null: a file that cannot be read leaves
    /// its civilization out, and the grid then draws what it drew before.
    /// </summary>
    public static IReadOnlyDictionary<string, HomeCityCardFact> Resolve(
        string? installPath,
        IEnumerable<string> civs)
    {
        var facts = new Dictionary<string, HomeCityCardFact>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(installPath)) return facts;

        IReadOnlyDictionary<string, string> files;
        try
        {
            files = HomeCityFiles(installPath!);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HomeCityCardFacts: could not read civs.xml — {ex.Message}");
            return facts;
        }

        foreach (var civ in civs)
        {
            if (string.IsNullOrWhiteSpace(civ)) continue;
            if (!files.TryGetValue(civ!, out var file) || string.IsNullOrWhiteSpace(file)) continue;

            foreach (var (card, fact) in ForCiv(installPath!, file))
                facts[Key(civ, card)] = fact;
        }

        return facts;
    }

    /// <summary>One civilization's file, cached by the path it was read from.</summary>
    private static IReadOnlyDictionary<string, HomeCityCardFact> ForCiv(string installPath, string file)
    {
        // The file name comes out of the mod's own civs.xml, so it is data rather than input —
        // but it still names a file, and GetFileName is what keeps it inside data\.
        var path = Path.Combine(installPath, "data", Path.GetFileName(file));
        return Cache.GetOrAdd(path, ReadCards);
    }

    /// <summary>
    /// Every <c>&lt;card&gt;</c> of one home city, by internal name.
    ///
    /// <para><c>ReadSubtree</c> bounds each block, so a card can never read into the next one.
    /// It does NOT save you from the other trap — see <see cref="ReadCard"/>.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, HomeCityCardFact> ReadCards(string path)
    {
        var cards = new Dictionary<string, HomeCityCardFact>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return cards;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(stream, ModStringTable.Settings());

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!string.Equals(reader.Name, "card", StringComparison.OrdinalIgnoreCase)) continue;
                if (cards.Count >= MaxCardsPerCiv) break;

                var (name, fact) = ReadCard(reader);
                // Placeholder blocks with no name are ordinary in these files — 534 of 8,127
                // across Wars of Liberty — and they are empty slots, not cards.
                if (!string.IsNullOrWhiteSpace(name)) cards[name!] = fact;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"HomeCityCardFacts: could not read '{path}' — {ex.Message}");
        }

        return cards;
    }

    /// <summary>
    /// The card element the reader is on, consumed whole.
    ///
    /// <para><b>⚠ ADVANCES BY HAND, and must.</b> <c>ReadElementContentAsString</c> already
    /// leaves the reader on the node AFTER the element it read, so a plain
    /// <c>while (reader.Read())</c> around it steps over whatever comes next — and this block
    /// holds three fields in a row (<c>&lt;name&gt; … &lt;age&gt; &lt;displayunitcount&gt;</c>),
    /// which is exactly the shape that loses one. It cost a first version every single number:
    /// the reader was skipping the element after the name. Same <c>advanced</c> flag
    /// <see cref="CardNameResolver"/> and <see cref="Multiplayer.CivNameResolver"/> both carry,
    /// and for the same reason.</para>
    /// </summary>
    private static (string? Name, HomeCityCardFact Fact) ReadCard(XmlReader outer)
    {
        string? name = null;
        int? count = null;
        int? age = null;

        using var reader = outer.ReadSubtree();
        reader.Read();   // onto <card> itself

        var advanced = false;
        while (advanced || reader.Read())
        {
            advanced = false;
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (name == null && Is(reader, "name"))
            {
                name = reader.ReadElementContentAsString()?.Trim();
                advanced = true;
                continue;
            }

            if (count == null && Is(reader, "displayunitcount"))
            {
                count = ReadInt(reader);
                advanced = true;
                continue;
            }

            if (age == null && Is(reader, "age"))
            {
                var raw = ReadInt(reader);
                advanced = true;
                // Zero-based in the file, one-based on screen. Converted here so no caller can
                // print "Age 0", and clamped rather than trusted: this is a modder's file.
                if (raw is >= 0 and <= 9) age = raw + 1;
            }
        }

        return (name, new HomeCityCardFact(count, age));
    }

    private static int? ReadInt(XmlReader reader)
    {
        var text = reader.ReadElementContentAsString();
        return int.TryParse(
            text?.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) ? value : null;
    }

    private static bool Is(XmlReader reader, string name)
        => string.Equals(reader.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Internal civilization name to the home-city file it owns, from the mod's own
    /// <c>civs.xml</c> — <c>&lt;homecityfilename&gt;homecityabyssinian.xml&lt;/homecityfilename&gt;</c>.
    /// Reading the MOD's mapping is what makes a reskin come out right, the same reason
    /// <see cref="Multiplayer.CivNameResolver.ResolvePortraits"/> reads its art from there.
    /// </summary>
    private static IReadOnlyDictionary<string, string> HomeCityFiles(string installPath)
    {
        var byCiv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var civsPath = Path.Combine(installPath, "data", "civs.xml");
        if (!File.Exists(civsPath)) return byCiv;   // packed: no answer, and that is the answer

        using var stream = File.OpenRead(civsPath);
        using var reader = XmlReader.Create(stream, ModStringTable.Settings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != 1) continue;
            if (!Is(reader, "civ")) continue;

            var (name, file) = ReadCivFile(reader);
            if (name != null && file != null) byCiv[name] = file;
        }

        return byCiv;
    }

    /// <summary>
    /// The civ element the reader is on, consumed whole. Same <c>advanced</c> flag as
    /// <see cref="ReadCard"/>, and the same reason.
    ///
    /// <para><b>⚠ The depth is taken from the SUBTREE's own root, never written as a literal.</b>
    /// A subtree reader keeps the depths of the document it came from, so a hard-coded
    /// <c>Depth != 1</c> matched nothing at all here and the whole map came out empty — which
    /// looked exactly like a mod that ships no home cities. Direct children only, because
    /// <c>&lt;matchmakingtextures&gt;</c> holds elements of its own.</para>
    /// </summary>
    private static (string? Name, string? File) ReadCivFile(XmlReader outer)
    {
        string? name = null;
        string? file = null;

        using var reader = outer.ReadSubtree();
        reader.Read();
        var childDepth = reader.Depth + 1;

        var advanced = false;
        while (advanced || reader.Read())
        {
            advanced = false;
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Depth != childDepth) continue;

            if (name == null && Is(reader, "name"))
            {
                name = reader.ReadElementContentAsString()?.Trim();
                advanced = true;
                continue;
            }

            if (file == null && Is(reader, "homecityfilename"))
            {
                file = reader.ReadElementContentAsString()?.Trim();
                advanced = true;
            }
        }

        return (name, file);
    }
}
