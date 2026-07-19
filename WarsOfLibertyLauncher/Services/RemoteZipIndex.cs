using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Reads a remote ZIP's CENTRAL DIRECTORY over HTTP range requests, without
/// downloading the archive.
///
/// A zip stores, at the very end, an index listing every entry's name,
/// uncompressed size and CRC-32. GitHub's release-asset CDN serves
/// <c>Accept-Ranges: bytes</c>, so ~250 KB of range reads yield a complete
/// fingerprint of a 1+ GB release. That's what makes
/// <see cref="ModVersionFingerprint"/> able to identify which release a user
/// actually has on disk — the piece GitHubReleases mods otherwise lack (WoL
/// gets it from the MD5s published in UpdateInfo.xml).
///
/// EVERYTHING here is best-effort: any problem (no range support, 404, a
/// Zip64 archive, a truncated read) returns null so the caller degrades to its
/// previous behaviour. It must never throw and never block a version check.
/// </summary>
public static class RemoteZipIndex
{
    /// <summary>One entry of the central directory.</summary>
    public readonly record struct ZipEntryInfo(uint Crc32, long Size);

    /// <summary>
    /// How much of the file tail to fetch when hunting for the End Of Central
    /// Directory record. The EOCD is 22 bytes plus a comment of at most 64 KB,
    /// so this always covers it.
    /// </summary>
    private const int TailProbeBytes = 66_000;

    /// <summary>
    /// Refuse absurd central directories rather than buffering them. 32 MB is
    /// far beyond any real mod payload (Improvement Mod's ~2200 entries index
    /// in ~250 KB).
    /// </summary>
    private const int MaxCentralDirectoryBytes = 32 * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher");
        return client;
    }

    /// <summary>
    /// Fetch and parse the archive's central directory. Returns null when the
    /// index can't be obtained for ANY reason — callers treat that as "can't
    /// fingerprint this release" and move on.
    /// </summary>
    /// <param name="url">Direct URL of the .zip asset.</param>
    /// <param name="totalSize">The asset's byte length (from the release JSON).</param>
    public static async Task<Dictionary<string, ZipEntryInfo>?> TryReadAsync(
        string url, long totalSize, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || totalSize <= 0) return null;

        try
        {
            var tailLength = (int)Math.Min(TailProbeBytes, totalSize);
            var tail = await GetRangeAsync(url, totalSize - tailLength, totalSize - 1, ct);
            if (tail == null) return null;

            if (!TryLocateCentralDirectory(tail, out var cdOffset, out var cdSize))
                return null;
            if (cdSize <= 0 || cdSize > MaxCentralDirectoryBytes) return null;
            if (cdOffset < 0 || cdOffset + cdSize > totalSize) return null;

            var cd = await GetRangeAsync(url, cdOffset, cdOffset + cdSize - 1, ct);
            if (cd == null || cd.Length < cdSize) return null;

            return ParseCentralDirectory(cd);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RemoteZipIndex: read failed for '{url}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Locate the End Of Central Directory record in the archive's tail and
    /// pull the central directory's offset + size out of it.
    ///
    /// Returns false for a **Zip64** archive: the classic EOCD stores those
    /// fields in 32 bits and writes 0xFFFFFFFF when the real value doesn't
    /// fit (payloads over 4 GB, or 65535+ entries). Parsing the Zip64 locator
    /// isn't worth it for the sizes mods actually ship — bailing degrades to
    /// "can't fingerprint", which is safe.
    /// </summary>
    internal static bool TryLocateCentralDirectory(byte[] tail, out long offset, out long size)
    {
        offset = 0;
        size = 0;
        if (tail == null || tail.Length < 22) return false;

        // Scan backwards for the EOCD signature (PK\x05\x06). Searching from the
        // end matters: a stored file could legitimately contain those bytes.
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] != 0x50 || tail[i + 1] != 0x4B
                || tail[i + 2] != 0x05 || tail[i + 3] != 0x06)
                continue;

            uint cdSize = BitConverter.ToUInt32(tail, i + 12);
            uint cdOffset = BitConverter.ToUInt32(tail, i + 16);
            if (cdSize == uint.MaxValue || cdOffset == uint.MaxValue)
                return false; // Zip64 — see the doc-comment.

            size = cdSize;
            offset = cdOffset;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parse central-directory bytes into name → (CRC-32, uncompressed size).
    /// Directory entries are skipped. Separated from the network so it can be
    /// unit-tested against an in-memory zip.
    /// </summary>
    internal static Dictionary<string, ZipEntryInfo>? ParseCentralDirectory(byte[] cd)
    {
        if (cd == null || cd.Length < 46) return null;

        var result = new Dictionary<string, ZipEntryInfo>(StringComparer.OrdinalIgnoreCase);
        int p = 0;
        while (p + 46 <= cd.Length)
        {
            // Central file header signature: PK\x01\x02
            if (cd[p] != 0x50 || cd[p + 1] != 0x4B || cd[p + 2] != 0x01 || cd[p + 3] != 0x02)
                break;

            uint crc = BitConverter.ToUInt32(cd, p + 16);
            uint uncompressed = BitConverter.ToUInt32(cd, p + 24);
            int nameLen = BitConverter.ToUInt16(cd, p + 28);
            int extraLen = BitConverter.ToUInt16(cd, p + 30);
            int commentLen = BitConverter.ToUInt16(cd, p + 32);

            int nameStart = p + 46;
            if (nameStart + nameLen > cd.Length) break;

            var name = Encoding.UTF8.GetString(cd, nameStart, nameLen).Replace('\\', '/');
            if (!name.EndsWith("/", StringComparison.Ordinal))
                result[name] = new ZipEntryInfo(crc, uncompressed);

            p = nameStart + nameLen + extraLen + commentLen;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Fetch a byte range. Returns null unless the server honours it with a
    /// 206 — a 200 would mean it ignored the Range header and is about to send
    /// the whole multi-GB asset, which we must not buffer.
    /// </summary>
    private static async Task<byte[]?> GetRangeAsync(
        string url, long from, long to, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            DiagnosticLog.Write(
                $"RemoteZipIndex: range request returned {(int)response.StatusCode} " +
                $"(expected 206) — cannot index this asset.");
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
