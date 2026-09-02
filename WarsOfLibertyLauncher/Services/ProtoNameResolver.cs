using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Turns an internal unit or building name — <c>gwtank</c>, <c>ypsettlerasian</c>, <c>wolxiangu</c>
/// — into what the mod calls it on screen.
///
/// <para>The chain is the same one <see cref="Multiplayer.CivNameResolver"/> follows, one file
/// along: <c>data\proto*.xml</c> holds <c>&lt;Unit name='RocketAir'&gt;</c> with a
/// <c>&lt;DisplayNameID&gt;</c>, and <see cref="ModStringTable"/> turns that id into text. Both
/// share the table reader, so the canonical-English rule and the reader-advance trap live in one
/// place.</para>
///
/// <para><b>Unlike a civilization, an unresolved name falls back to the internal one</b>, and the
/// difference is deliberate. A civ index that resolves to nothing is a NUMBER, and printing it
/// would put a value nobody can interpret into a stored match; an unresolved proto is already a
/// word, it identifies the unit to anyone who mods, and it claims nothing false. Showing
/// <c>gwtank</c> is worse than showing "Tank" and much better than showing nothing.</para>
/// </summary>
public static class ProtoNameResolver
{
    /// <summary>
    /// The layers, base first so later ones win — the engine's own order. The mod's own file is
    /// appended by <see cref="ProtoFilesFor"/> when its executable names a different one.
    /// </summary>
    private static readonly string[] BaseFiles = { "proto.xml", "protox.xml", "protoy.xml" };

    /// <summary>A guard against a hostile file, not a real limit — Wars of Liberty ships ~4,000.</summary>
    private const int MaxProtos = 65536;

    /// <summary>Internal name to display-name id, one map per install. Built by a 12 MB scan.</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, int>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void ResetCache() => Cache.Clear();

    /// <summary>
    /// Display names for the protos asked for. A name the mod does not describe is simply absent,
    /// and the caller shows the internal name instead.
    ///
    /// <para><b>Do not call this on the UI thread the first time for a given install</b> — it
    /// streams every <c>proto*.xml</c> the mod ships, which is 12 MB for Wars of Liberty.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(
        string? installPath, string? gameExecutable, IEnumerable<string> protoNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installPath)) return result;

        var ids = IdsFor(installPath!, gameExecutable);
        if (ids.Count == 0) return result;

        var wanted = new HashSet<int>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in protoNames)
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

        foreach (var file in ProtoFilesFor(gameExecutable))
        {
            var path = Path.Combine(dataDir, file);
            if (!File.Exists(path)) continue;

            try { ReadProtos(path, ids); }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"ProtoNameResolver: could not read '{path}' — {ex.Message}");
            }
        }

        DiagnosticLog.Write(
            $"ProtoNameResolver: '{Path.GetFileName(installPath)}' — {ids.Count} protos indexed.");
        return ids;
    }

    /// <summary>
    /// The proto files to read, base first so a later layer overrides an earlier one.
    ///
    /// <para>A mod's own layer is named after its executable — Napoleonic Era runs
    /// <c>age3n.exe</c> and ships <c>proton.xml</c>, Improvement Mod <c>age3m.exe</c> and
    /// <c>protom.xml</c> — which is the same convention the mod's own <c>data\</c> already
    /// follows. Pure and internal so the derivation is tested rather than trusted.</para>
    /// </summary>
    internal static IReadOnlyList<string> ProtoFilesFor(string? gameExecutable)
    {
        var files = new List<string>(BaseFiles);

        var match = Regex.Match(gameExecutable ?? "", @"^age3([a-z]?)\.exe$", RegexOptions.IgnoreCase);
        if (!match.Success) return files;

        var suffix = match.Groups[1].Value.ToLowerInvariant();
        if (suffix.Length == 0) return files;

        var own = "proto" + suffix + ".xml";
        if (!files.Contains(own, StringComparer.OrdinalIgnoreCase)) files.Add(own);
        return files;
    }

    /// <summary>
    /// Streams one proto file, collecting <c>name</c> to <c>DisplayNameID</c>. Streaming and not
    /// <c>XDocument</c> because these run to 12 MB; a later layer overwrites an earlier one.
    /// </summary>
    private static void ReadProtos(string path, Dictionary<string, int> ids)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, ModStringTable.Settings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!string.Equals(reader.Name, "Unit", StringComparison.OrdinalIgnoreCase)) continue;
            if (ids.Count >= MaxProtos) return;

            var name = reader.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name) || reader.IsEmptyElement) continue;

            var id = ReadDisplayNameId(reader);
            if (id.HasValue) ids[name!.Trim()] = id.Value;
        }
    }

    /// <summary>
    /// The display-name id of the Unit element the reader is on, consuming exactly that element.
    /// Mirrors <c>CivNameResolver.ReadDisplayNameId</c>, including the re-check after
    /// <c>ReadElementContentAsString</c> has already moved the reader on.
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
