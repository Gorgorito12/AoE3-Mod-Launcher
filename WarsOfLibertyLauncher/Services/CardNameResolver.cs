using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Turns a home city card's internal name — <c>HCShipWoodCrates3</c>,
/// <c>YPHCExpandedTradingPost</c> — into what the mod calls it on screen.
///
/// <para>The sibling of <see cref="ProtoNameResolver"/>, one file along: a card is a tech with
/// <c>&lt;Flag&gt;HomeCity&lt;/Flag&gt;</c> in <c>data\techtree*.xml</c>, carrying a
/// <c>&lt;DisplayNameID&gt;</c> that <see cref="ModStringTable"/> turns into text. Measured on Wars
/// of Liberty: <b>4,390 of its 4,517 cards resolve, 97.2%</b>.</para>
///
/// <para><b>An unresolved card falls back to its internal name</b>, the same choice
/// <see cref="ProtoNameResolver"/> makes and for the same reason: the internal name is already a
/// word that identifies the card to anyone who mods, and it claims nothing false.</para>
/// </summary>
public static class CardNameResolver
{
    /// <summary>The layers, base first so later ones win — the engine's own order.</summary>
    private static readonly string[] BaseFiles = { "techtree.xml", "techtreex.xml", "techtreey.xml" };

    /// <summary>A guard against a hostile file — Wars of Liberty ships ~4,500 cards.</summary>
    private const int MaxCards = 65536;

    /// <summary>Card name to display-name id, one map per install. Built by a 12 MB scan.</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, int>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void ResetCache() => Cache.Clear();

    /// <summary>
    /// Display names for the cards asked for. A card the mod does not describe is simply absent,
    /// and the caller shows the internal name instead.
    ///
    /// <para><b>Do not call this on the UI thread the first time for a given install</b> — it
    /// streams every <c>techtree*.xml</c> the mod ships, which is 12 MB for Wars of Liberty.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(
        string? installPath, string? gameExecutable, IEnumerable<string> cardNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installPath)) return result;

        var ids = IdsFor(installPath!, gameExecutable);
        if (ids.Count == 0) return result;

        var wanted = new HashSet<int>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in cardNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!ids.TryGetValue(name, out var id)) continue;
            byName[name] = id;
            wanted.Add(id);
        }

        if (wanted.Count == 0) return result;

        var strings = ModStringTable.Resolve(installPath!, wanted);
        foreach (var (name, id) in byName)
        {
            if (strings.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text))
                result[name] = text.Trim();
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> IdsFor(string installPath, string? gameExecutable)
    {
        string key;
        try { key = Path.GetFullPath(installPath); }
        catch { return new Dictionary<string, int>(); }

        return Cache.GetOrAdd(key, k => BuildIds(k, gameExecutable));
    }

    private static IReadOnlyDictionary<string, int> BuildIds(string installPath, string? gameExecutable)
    {
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dataDir = Path.Combine(installPath, "data");

        foreach (var file in TechFilesFor(gameExecutable))
        {
            var path = Path.Combine(dataDir, file);
            if (!File.Exists(path)) continue;

            try { ReadTechs(path, ids); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"CardNameResolver: could not read '{path}' — {ex.Message}");
            }
        }

        DiagnosticLog.Write(
            $"CardNameResolver: '{Path.GetFileName(installPath)}' — {ids.Count} techs indexed.");
        return ids;
    }

    /// <summary>
    /// The tech files to read, base first so a later layer overrides an earlier one. Same
    /// executable-suffix convention as <see cref="ProtoNameResolver.ProtoFilesFor"/> — Napoleonic
    /// Era runs <c>age3n.exe</c> and ships the <c>n</c> layer. Pure and internal so the derivation
    /// is tested rather than trusted.
    /// </summary>
    internal static IReadOnlyList<string> TechFilesFor(string? gameExecutable)
    {
        var files = new List<string>(BaseFiles);

        var match = Regex.Match(gameExecutable ?? "", @"^age3([a-z]?)\.exe$", RegexOptions.IgnoreCase);
        if (!match.Success) return files;

        var suffix = match.Groups[1].Value.ToLowerInvariant();
        if (suffix.Length == 0) return files;

        var own = "techtree" + suffix + ".xml";
        if (!files.Contains(own, StringComparer.OrdinalIgnoreCase)) files.Add(own);
        return files;
    }

    /// <summary>
    /// Streams one tech file, collecting <c>name</c> to <c>DisplayNameID</c>.
    ///
    /// <para><b>Every tech is indexed, not only the HomeCity-flagged ones</b>, and that is
    /// deliberate: the flag arrives AFTER the display id inside the element, so filtering on it
    /// would mean either buffering each tech or reading the file twice, to save a dictionary that
    /// costs a few hundred kilobytes. The caller only ever asks about cards.</para>
    /// </summary>
    private static void ReadTechs(string path, Dictionary<string, int> ids)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, ModStringTable.Settings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!string.Equals(reader.Name, "Tech", StringComparison.OrdinalIgnoreCase)) continue;
            if (ids.Count >= MaxCards) return;

            var name = reader.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name) || reader.IsEmptyElement) continue;

            var id = ReadDisplayNameId(reader);
            if (id.HasValue) ids[name!.Trim()] = id.Value;
        }
    }

    /// <summary>
    /// The display-name id of the Tech element the reader is on, consuming exactly that element.
    /// Mirrors <c>ProtoNameResolver.ReadDisplayNameId</c>, including the re-check after
    /// <c>ReadElementContentAsString</c> has already moved the reader on — the trap that once
    /// skipped every second entry in this exact shape of loop.
    /// </summary>
    private static int? ReadDisplayNameId(XmlReader reader)
    {
        var depth = reader.Depth;
        int? id = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) break;

            if (id == null
                && reader.NodeType == XmlNodeType.Element
                && string.Equals(reader.Name, "DisplayNameID", StringComparison.OrdinalIgnoreCase)
                && !reader.IsEmptyElement
                && int.TryParse(reader.ReadElementContentAsString().Trim(), out var parsed))
            {
                id = parsed;
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) break;
            }
        }

        return id;
    }
}
