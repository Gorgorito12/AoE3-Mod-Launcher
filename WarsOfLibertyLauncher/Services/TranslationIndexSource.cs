using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One version listed by a <c>translations-index.json</c>, already validated.</summary>
public sealed record IndexRecord(
    string Id, string Name, string Author, string Language, string TargetMod, string Version,
    List<string> CompatibleWith, string ZipUrl, string Sha256, long Size, string ContentHash,
    string Date, string? Description);

public sealed class IndexParseResult
{
    /// <summary>The index's own top-level <c>name</c> ("Traducciones de Juan"), when it declares one.</summary>
    public string? SourceName { get; set; }
    public List<IndexRecord> Records { get; } = new();
    /// <summary>Why individual items were dropped — logged, never shown as an error.</summary>
    public List<string> Warnings { get; } = new();
    public string? ErrorKey { get; set; }
    public string? ErrorDetail { get; set; }
    public bool Ok => ErrorKey == null;

    internal static IndexParseResult Error(string key, string? detail = null) =>
        new() { ErrorKey = key, ErrorDetail = detail };
}

/// <summary>
/// A translation source that is not GitHub: one <c>translations-index.json</c> at an https address
/// — a Google Drive or Dropbox file, a gist, a translator's own website. The player adds the
/// address once; the translator keeps editing that same file, and every new version they list
/// shows up in the player's Language tab.
///
/// <para>The format, every item being one version of one pack:</para>
/// <code>
/// { "name": "Traducciones de Juan",
///   "translations": [ { "id": "ES-LA", "name": "Español", "author": "Juan", "targetMod": "wol",
///     "version": "1.2.0e-r2", "compatibleWith": ["1.2.0e"], "zip": "https://…/wol-ES-LA.zip",
///     "sha256": "…64 hex…", "size": 1249053, "contentHash": "18ca36a3d84d2352",
///     "date": "2026-09-09T03:06:52Z", "description": "…" } ] }
/// </code>
///
/// <para><b><c>sha256</c> and <c>targetMod</c> are required.</b> Anything at an arbitrary address
/// can change under the player, so the bytes must match what the index promised before a single
/// one is used, and a source the player added must say which mod a pack is for. A malformed item
/// is dropped (and logged) rather than failing the whole index, so one typo by a translator
/// doesn't hide the rest of their work.</para>
/// </summary>
public static class TranslationIndexSource
{
    public const long MaxIndexBytes = 1024 * 1024;
    public const int MaxItems = 200;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(20);

    private static readonly Regex Sha256Regex = new(@"^[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ContentHashRegex = new(@"^[0-9a-fA-F]{16}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ModIdRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses and validates an index. <paramref name="indexUri"/> is where it was downloaded from,
    /// which a relative <c>zip</c> resolves against — allowed only when
    /// <paramref name="relativeZipAllowed"/>, because on Drive or Dropbox one file's address says
    /// nothing about another's.
    /// </summary>
    public static IndexParseResult Parse(string? json, Uri indexUri, bool relativeZipAllowed)
    {
        if (string.IsNullOrWhiteSpace(json)) return IndexParseResult.Error("TxSrcErrNotIndex", "empty");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
        }
        catch (JsonException ex)
        {
            return IndexParseResult.Error("TxSrcErrNotIndex", ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("translations", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return IndexParseResult.Error("TxSrcErrNotIndex", "no \"translations\" array");

            var result = new IndexParseResult { SourceName = NullIfEmpty(Clean(Str(root, "name"), 80)) };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (index++ >= MaxItems)
                {
                    result.Warnings.Add($"more than {MaxItems} items — the rest were ignored");
                    break;
                }
                var record = ParseItem(item, indexUri, relativeZipAllowed, out var why);
                if (record == null)
                {
                    result.Warnings.Add($"item {index}: {why}");
                    continue;
                }
                if (!seen.Add(record.Id + "|" + record.Version))
                {
                    result.Warnings.Add($"item {index}: '{record.Id}' {record.Version} is listed twice — kept the first");
                    continue;
                }
                result.Records.Add(record);
            }
            return result;
        }
    }

    private static IndexRecord? ParseItem(JsonElement item, Uri indexUri, bool relativeZipAllowed, out string why)
    {
        why = "";
        if (item.ValueKind != JsonValueKind.Object) { why = "not an object"; return null; }

        var id = Str(item, "id")?.Trim() ?? "";
        if (!TranslationPathPolicy.IsSafePackId(id)) { why = $"id '{id}' is not a valid folder name"; return null; }

        var version = Str(item, "version")?.Trim() ?? "";
        if (!TranslationPathPolicy.IsSafeVersionSegment(version)) { why = $"version '{version}' is not valid"; return null; }

        var name = Clean(Str(item, "name"), 100);
        if (name.Length == 0) { why = "no name"; return null; }

        var targetMod = Str(item, "targetMod")?.Trim() ?? "";
        if (!ModIdRegex.IsMatch(targetMod)) { why = "no valid targetMod (required)"; return null; }

        var sha256 = Str(item, "sha256")?.Trim() ?? "";
        if (!Sha256Regex.IsMatch(sha256)) { why = "no valid sha256 (required)"; return null; }

        var zip = Str(item, "zip")?.Trim() ?? "";
        var zipUrl = ResolveZip(zip, indexUri, relativeZipAllowed, out var zipWhy);
        if (zipUrl == null) { why = zipWhy; return null; }

        var compat = new List<string>();
        if (item.TryGetProperty("compatibleWith", out var cw) && cw.ValueKind == JsonValueKind.Array)
            foreach (var v in cw.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.String) continue;
                var s = Clean(v.GetString(), 32);
                if (s.Length > 0 && compat.Count < 20 && !compat.Contains(s, StringComparer.OrdinalIgnoreCase))
                    compat.Add(s);
            }

        long size = 0;
        if (item.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n) && n > 0)
            size = n;

        var contentHash = Str(item, "contentHash")?.Trim() ?? "";
        if (!ContentHashRegex.IsMatch(contentHash)) contentHash = "";

        return new IndexRecord(
            id, name,
            Clean(Str(item, "author"), 100),
            Clean(Str(item, "language"), 32),
            targetMod, version, compat, zipUrl,
            sha256.ToLowerInvariant(), size,
            contentHash.ToLowerInvariant(),
            Clean(Str(item, "date"), 40),
            NullIfEmpty(Clean(Str(item, "description"), 2000)));
    }

    /// <summary>An absolute https link a share host doesn't refuse, or a relative path where that makes sense.</summary>
    private static string? ResolveZip(string zip, Uri indexUri, bool relativeZipAllowed, out string why)
    {
        why = "";
        if (zip.Length == 0 || zip.Length > TranslationSourceRef.MaxInputLength) { why = "no zip link"; return null; }

        Uri? target;
        if (zip.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(zip, UriKind.Absolute, out target)) { why = "zip link is not a valid address"; return null; }
        }
        else
        {
            if (!relativeZipAllowed) { why = "a relative zip path can't be used on this host — give the full link"; return null; }
            if (!Uri.TryCreate(indexUri, zip, out target)) { why = "zip path can't be resolved"; return null; }
        }

        var url = target.AbsoluteUri;
        if (target.Scheme != Uri.UriSchemeHttps || !SafeUrl.IsAllowed(url)) { why = "zip link must be https"; return null; }
        if (ShareLinkResolver.Resolve(url).Kind == ShareLinkKind.Rejected) { why = "zip link points to a host that can't be downloaded from"; return null; }
        return url;
    }

    /// <summary>
    /// Downloads and parses the index at <paramref name="source"/>. Never throws for a broken
    /// source — it returns an error key the UI can show — except when the caller cancels.
    /// </summary>
    public static async Task<IndexParseResult> FetchAsync(HttpClient http, TranslationSourceRef source, CancellationToken ct)
    {
        var resolved = ShareLinkResolver.Resolve(source.Location);
        if (resolved.Kind == ShareLinkKind.Rejected)
            return IndexParseResult.Error(resolved.ReasonKey ?? "TxSrcErrInvalid");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(FetchTimeout);

        var url = resolved.Url;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string body;
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return IndexParseResult.Error(KeyForStatus(response.StatusCode), $"HTTP {(int)response.StatusCode}");
                if (response.Content.Headers.ContentLength is long declared && declared > MaxIndexBytes)
                    return IndexParseResult.Error("TxSrcErrTooLarge");
                body = await ReadCappedAsync(response, MaxIndexBytes, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return IndexParseResult.Error("TxSrcErrTimeout");
            }
            catch (InvalidDataException)
            {
                return IndexParseResult.Error("TxSrcErrTooLarge");
            }
            catch (HttpRequestException ex)
            {
                return IndexParseResult.Error("TxSrcErrUnreachable", ex.Message);
            }

            if (LooksLikeHtml(body))
            {
                if (attempt == 0 && ShareLinkResolver.IsDriveHost(url)
                    && ShareLinkResolver.TryParseDriveConfirmForm(body, out var next) && next != null)
                {
                    url = next;
                    continue;
                }
                return IndexParseResult.Error("TxSrcErrHtml");
            }

            var parsed = Parse(body, new Uri(url), relativeZipAllowed: !resolved.IsShareHost);
            foreach (var w in parsed.Warnings)
                DiagnosticLog.Write($"  index '{source.Location}': {w}");
            return parsed;
        }
        return IndexParseResult.Error("TxSrcErrHtml");
    }

    /// <summary>The label a card shows for an index: its declared name, else the host.</summary>
    public static string LabelFor(TranslationSourceRef source, string? declaredName)
    {
        if (!string.IsNullOrWhiteSpace(declaredName)) return declaredName!;
        if (Uri.TryCreate(source.Location, UriKind.Absolute, out var u))
        {
            var host = u.IdnHost;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        return source.DisplayLocation;
    }

    private static string KeyForStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Gone => "TxSrcErrNotFound",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "TxSrcErrNotPublic",
        _ => "TxSrcErrUnreachable",
    };

    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, long max, CancellationToken ct)
    {
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await src.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) throw new InvalidDataException("index too large");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>A share link that answers with its viewer page instead of the file.</summary>
    internal static bool LooksLikeHtml(string? body) =>
        body != null && body.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('<');

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Text from a stranger's file, made safe to show: control characters and the bidirectional
    /// overrides that can make a label read differently from what it is are removed, then it is
    /// trimmed and capped.
    /// </summary>
    internal static string Clean(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsControl(c)) continue;
            if (c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩')) continue;
            sb.Append(c);
        }
        var t = sb.ToString().Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
