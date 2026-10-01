using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One version, as the packager writes it into a <c>translations-index.json</c>.</summary>
public sealed record IndexItemDraft(
    string Id, string Name, string Author, string Language, string TargetMod, string Version,
    IReadOnlyList<string> CompatibleWith, string Zip, string Sha256, long Size,
    string ContentHash, string Date, string? Description);

/// <summary>
/// The packager's half of the any-host sources: it writes (or updates) the
/// <c>translations-index.json</c> a translator publishes, so nobody ever types a hash by hand.
/// What it writes is exactly what <see cref="TranslationIndexSource.Parse"/> accepts — a test
/// holds the two to that.
///
/// <para>Updating the SAME file is the whole point for a translator on Drive or Dropbox: the link
/// players added stays valid, and the new version shows up for all of them. So <see cref="Merge"/>
/// keeps every existing item and only replaces the one with the same id and version — and refuses
/// to touch a file it cannot parse, rather than "fixing" it into something with half the
/// translator's work missing.</para>
/// </summary>
public static class TranslationIndexWriter
{
    /// <summary>
    /// Indented and with accents left as they are ("Español", not "Español"): translators
    /// open this file by hand, and it is never embedded in a page, so HTML-safe escaping buys nothing.
    /// </summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// <paramref name="existingJson"/> with <paramref name="item"/> added (or replacing the item
    /// with the same id and version). Null <paramref name="existingJson"/> starts a new index.
    /// Returns false — and leaves <paramref name="json"/> null — when the existing text is not an
    /// index this can safely extend.
    /// </summary>
    public static bool Merge(string? existingJson, IndexItemDraft item, string? sourceName, out string? json, out string? error)
    {
        json = null;
        error = null;
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            root = new JsonObject();
            if (!string.IsNullOrWhiteSpace(sourceName)) root["name"] = sourceName;
            root["translations"] = new JsonArray();
        }
        else
        {
            try
            {
                root = JsonNode.Parse(existingJson, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }) as JsonObject ?? throw new JsonException("the file is not a JSON object");
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
            if (root["translations"] is not JsonArray)
            {
                if (root.ContainsKey("translations"))
                {
                    error = "\"translations\" is not a list";
                    return false;
                }
                root["translations"] = new JsonArray();
            }
            if (!root.ContainsKey("name") && !string.IsNullOrWhiteSpace(sourceName)) root["name"] = sourceName;
        }

        var items = (JsonArray)root["translations"]!;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is JsonObject o
                && string.Equals(StringOf(o["id"]), item.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(StringOf(o["version"]), item.Version, StringComparison.OrdinalIgnoreCase))
                items.RemoveAt(i);
        }
        items.Add(ToNode(item));
        json = root.ToJsonString(Indented);
        return true;
    }

    /// <summary>A node's string value; null for anything else (a hand-edited <c>"version": 1.1</c>).</summary>
    private static string? StringOf(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static JsonObject ToNode(IndexItemDraft item)
    {
        var o = new JsonObject
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
        };
        if (!string.IsNullOrWhiteSpace(item.Author)) o["author"] = item.Author;
        if (!string.IsNullOrWhiteSpace(item.Language)) o["language"] = item.Language;
        o["targetMod"] = item.TargetMod;
        o["version"] = item.Version;
        o["compatibleWith"] = new JsonArray(item.CompatibleWith.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
        o["zip"] = item.Zip;
        o["sha256"] = item.Sha256.ToLowerInvariant();
        if (item.Size > 0) o["size"] = item.Size;
        if (!string.IsNullOrWhiteSpace(item.ContentHash)) o["contentHash"] = item.ContentHash;
        if (!string.IsNullOrWhiteSpace(item.Date)) o["date"] = item.Date;
        if (!string.IsNullOrWhiteSpace(item.Description)) o["description"] = item.Description;
        return o;
    }

    /// <summary>
    /// The version the packager proposes: the mod version plus the next free revision —
    /// <c>1.2.0e-r1</c>, then <c>1.2.0e-r2</c>… — so a version names the mod version it is for
    /// and two packs never share a label ("1.1" for 1.2.0d and "1.1" for 1.2.0e were different
    /// packs). <paramref name="existing"/> are the version folders already taken.
    /// </summary>
    public static string NextRevision(string? modVersion, IEnumerable<string>? existing)
    {
        var mod = (modVersion ?? "").Trim();
        if (mod.Length == 0 || mod == "?" || !TranslationPathPolicy.IsSafeVersionSegment(mod)) return "1.0";

        var rx = new Regex("^" + Regex.Escape(mod) + @"-r(\d{1,4})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int max = 0;
        foreach (var v in existing ?? Enumerable.Empty<string>())
        {
            var m = rx.Match((v ?? "").Trim());
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max) max = n;
        }
        return $"{mod}-r{max + 1}";
    }

    /// <summary>True for the <c>&lt;mod version&gt;-rN</c> shape <see cref="NextRevision"/> proposes.</summary>
    public static bool IsRevisionOfModVersion(string? version) =>
        version != null && Regex.IsMatch(version.Trim(), @"-r\d{1,4}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
