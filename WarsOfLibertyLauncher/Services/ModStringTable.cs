using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Looks display-name ids up in a mod's string tables — the last step of every "what does this
/// mod call it" question, shared by the civilization and the unit resolvers.
///
/// <para>It is one class because the rules below are the same for both and are each a bug that
/// has already happened once. Two copies would be two chances to fix only one of them.</para>
/// </summary>
public static class ModStringTable
{
    /// <summary>
    /// The layers, in the engine's own override order: the expansion-2 table wins, then the
    /// expansion, then the base. Mods write into the first. Reading them the other way round
    /// prints the BASE GAME's name for everything a mod renamed.
    /// </summary>
    private static readonly string[] Files =
    {
        "stringtabley.xml", "stringtablex.xml", "stringtable.xml",
    };

    /// <summary>
    /// The text for each wanted id, skipping any the tables do not carry.
    ///
    /// <para>Stops as soon as every id is accounted for, and keeps only what was asked for: the
    /// Wars of Liberty table holds 55,334 entries and there is no reason to hold them.</para>
    /// </summary>
    public static Dictionary<int, string> Resolve(string installPath, HashSet<int> wanted)
    {
        var found = new Dictionary<int, string>();
        if (wanted.Count == 0 || string.IsNullOrWhiteSpace(installPath)) return found;

        var dataDir = Path.Combine(installPath, "data");
        foreach (var file in Files)
        {
            var path = CanonicalPath(installPath, dataDir, file);
            if (path == null) continue;

            try { ReadFrom(path, wanted, found); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"ModStringTable: could not read '{path}' — {ex.Message}");
            }

            if (found.Count == wanted.Count) break;
        }

        return found;
    }

    /// <summary>
    /// The canonical-English copy when the launcher has one, else the live file. Same rule as
    /// <c>TranslationService.ResolveHashableFile</c>, and for the same reason: with a translation
    /// applied the live table is the translated one, and a name resolved here can be stored and
    /// read by somebody else.
    /// </summary>
    private static string? CanonicalPath(string installPath, string dataDir, string fileName)
    {
        var snapshot = Path.Combine(VerifyService.OriginalsFolderOf(installPath), fileName);
        if (File.Exists(snapshot)) return snapshot;

        var live = Path.Combine(dataDir, fileName);
        return File.Exists(live) ? live : null;
    }

    /// <summary>
    /// <b>Advances by hand, and must.</b> <c>ReadElementContentAsString</c> already moves past the
    /// element it read, so a plain <c>while (reader.Read())</c> loop steps over whatever follows —
    /// which in this file is the NEXT string. That skipped every second entry it was looking for:
    /// with two civilizations the first name resolved and the second came back null, and with more
    /// the misses scatter and read as a bad string table rather than as a parser bug. Caught by
    /// the tests, not by reading the code.
    /// </summary>
    private static void ReadFrom(string path, HashSet<int> wanted, Dictionary<int, string> found)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, Settings());

        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element
                || !string.Equals(reader.Name, "String", StringComparison.OrdinalIgnoreCase)
                || reader.IsEmptyElement)
            {
                reader.Read();
                continue;
            }

            var raw = reader.GetAttribute("_locID");
            if (raw != null && int.TryParse(raw.Trim(), out var id)
                && wanted.Contains(id) && !found.ContainsKey(id))
            {
                found[id] = reader.ReadElementContentAsString();   // already advanced
                if (found.Count == wanted.Count) return;
                continue;
            }

            reader.Read();
        }
    }

    /// <summary>
    /// Lenient on purpose. These files are shipped by mod authors, not by us: a DTD reference or a
    /// stray control character must cost one unresolved name, never an exception on a path that
    /// runs while a match is being reported.
    /// </summary>
    public static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        CheckCharacters = false,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    };
}
