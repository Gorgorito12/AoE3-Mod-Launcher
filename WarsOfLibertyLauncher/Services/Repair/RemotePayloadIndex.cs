using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>One file entry of a payload zip's central directory, with what a range read needs.</summary>
internal sealed record PayloadEntry(
    string Name, uint Crc32, long CompressedSize, long Size, long LocalHeaderOffset, ushort Method);

/// <summary>
/// Reads the CENTRAL DIRECTORY of a mod payload that may be split across several release assets
/// (<c>.zip.001</c>, <c>.002</c>…) and may be Zip64, over HTTP range requests — no download.
///
/// <para><b>Why a second reader.</b> <see cref="RemoteZipIndex"/> fingerprints installed versions
/// and deliberately stops at a single-asset, non-Zip64 zip. Wars of Liberty's payload is three
/// parts and ~43,000 entries, so it is both — and it is the mod where a one-file repair costs the
/// most. The parts are one byte stream cut in pieces, so every offset in the directory is an offset
/// into their CONCATENATION; <see cref="PartReader"/> maps it back to a part.</para>
///
/// <para>Best-effort like its sibling: anything unexpected returns null and the caller carries on
/// exactly as before. Pure parsing is split from the network so it can be tested on in-memory zips.</para>
/// </summary>
internal static class RemotePayloadIndex
{
    /// <summary>Reads <paramref name="count"/> bytes at a LOGICAL offset of the concatenated payload.</summary>
    internal delegate Task<byte[]?> RangeReader(long offset, int count, CancellationToken ct);

    private const int TailProbeBytes = 66_000;
    private const int MaxCentralDirectoryBytes = 64 * 1024 * 1024;

    internal readonly record struct Eocd(long CdOffset, long CdSize, long Zip64RecordOffset);

    internal static async Task<IReadOnlyList<PayloadEntry>?> TryReadAsync(
        RangeReader read, long totalLength, CancellationToken ct)
    {
        if (totalLength <= 22) return null;
        try
        {
            var tailLength = (int)Math.Min(TailProbeBytes, totalLength);
            var tailStart = totalLength - tailLength;
            var tail = await read(tailStart, tailLength, ct);
            if (tail == null || tail.Length != tailLength) return null;

            if (!TryParseEocd(tail, tailStart, out var eocd)) return null;
            long cdOffset = eocd.CdOffset, cdSize = eocd.CdSize;
            if (eocd.Zip64RecordOffset >= 0)
            {
                var record = await read(eocd.Zip64RecordOffset, 56, ct);
                if (record == null || !TryParseZip64Eocd(record, out cdOffset, out cdSize)) return null;
            }
            if (cdSize <= 0 || cdSize > MaxCentralDirectoryBytes) return null;
            if (cdOffset < 0 || cdOffset + cdSize > totalLength) return null;

            var cd = await read(cdOffset, (int)cdSize, ct);
            if (cd == null || cd.Length != cdSize) return null;
            return ParseCentralDirectory(cd);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"RemotePayloadIndex: read failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the End Of Central Directory record in <paramref name="tail"/> (which starts at the
    /// logical offset <paramref name="tailStart"/>). When the archive is Zip64 — the classic fields
    /// hold the 0xFFFF/0xFFFFFFFF sentinels, or the Zip64 locator sits right before the record — the
    /// result carries the Zip64 record's offset instead, and the classic fields are not trusted.
    /// </summary>
    internal static bool TryParseEocd(byte[] tail, long tailStart, out Eocd eocd)
    {
        eocd = default;
        if (tail == null || tail.Length < 22) return false;

        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (!Sig(tail, i, 0x05, 0x06)) continue;

            ushort entries = BitConverter.ToUInt16(tail, i + 10);
            uint size = BitConverter.ToUInt32(tail, i + 12);
            uint offset = BitConverter.ToUInt32(tail, i + 16);

            bool locator = i >= 20 && Sig(tail, i - 20, 0x06, 0x07);
            if (locator)
            {
                long recordOffset = BitConverter.ToInt64(tail, i - 20 + 8);
                eocd = new Eocd(0, 0, recordOffset);
                return recordOffset >= 0;
            }
            if (entries == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
                return false;   // Zip64 without a locator in reach: not something to guess at

            eocd = new Eocd(offset, size, -1);
            return true;
        }
        return false;
    }

    internal static bool TryParseZip64Eocd(byte[] record, out long cdOffset, out long cdSize)
    {
        cdOffset = cdSize = 0;
        if (record == null || record.Length < 56 || !Sig(record, 0, 0x06, 0x06)) return false;
        cdSize = BitConverter.ToInt64(record, 40);
        cdOffset = BitConverter.ToInt64(record, 48);
        return cdSize > 0 && cdOffset >= 0;
    }

    /// <summary>File entries of a central directory, Zip64 extra fields applied. Directories skipped.</summary>
    internal static IReadOnlyList<PayloadEntry>? ParseCentralDirectory(byte[] cd)
    {
        if (cd == null || cd.Length < 46) return null;
        var result = new List<PayloadEntry>();
        int p = 0;
        while (p + 46 <= cd.Length)
        {
            if (!Sig(cd, p, 0x01, 0x02)) break;

            ushort method = BitConverter.ToUInt16(cd, p + 10);
            uint crc = BitConverter.ToUInt32(cd, p + 16);
            long compressed = BitConverter.ToUInt32(cd, p + 20);
            long uncompressed = BitConverter.ToUInt32(cd, p + 24);
            int nameLen = BitConverter.ToUInt16(cd, p + 28);
            int extraLen = BitConverter.ToUInt16(cd, p + 30);
            int commentLen = BitConverter.ToUInt16(cd, p + 32);
            long localOffset = BitConverter.ToUInt32(cd, p + 42);

            int nameStart = p + 46;
            if (nameStart + nameLen + extraLen > cd.Length) break;

            // Read as UTF-8 whether or not bit 11 says so. Without the flag the spec says CP437,
            // which .NET only has with a code-page provider; the payloads here are ASCII or flagged,
            // and a mismatch only makes one entry unfindable, which reads as "not coverable".
            var name = Encoding.UTF8.GetString(cd, nameStart, nameLen).Replace('\\', '/');

            // Zip64 extra (0x0001): only the fields whose classic slot holds the sentinel, in order.
            int e = nameStart + nameLen, eEnd = e + extraLen;
            while (e + 4 <= eEnd)
            {
                ushort id = BitConverter.ToUInt16(cd, e);
                ushort len = BitConverter.ToUInt16(cd, e + 2);
                int q = e + 4, qEnd = Math.Min(eEnd, q + len);
                if (id == 0x0001)
                {
                    if (uncompressed == uint.MaxValue && q + 8 <= qEnd) { uncompressed = BitConverter.ToInt64(cd, q); q += 8; }
                    if (compressed == uint.MaxValue && q + 8 <= qEnd) { compressed = BitConverter.ToInt64(cd, q); q += 8; }
                    if (localOffset == uint.MaxValue && q + 8 <= qEnd) { localOffset = BitConverter.ToInt64(cd, q); }
                }
                e += 4 + len;
            }

            if (!name.EndsWith("/", StringComparison.Ordinal))
                result.Add(new PayloadEntry(name, crc, compressed, uncompressed, localOffset, method));

            p = nameStart + nameLen + extraLen + commentLen;
        }
        return result.Count > 0 ? result : null;
    }

    private static bool Sig(byte[] b, int i, byte c, byte d)
        => i >= 0 && i + 3 < b.Length && b[i] == 0x50 && b[i + 1] == 0x4B && b[i + 2] == c && b[i + 3] == d;

    /// <summary>
    /// A <see cref="RangeReader"/> over release assets that together form one payload. A logical
    /// range crossing a part boundary becomes one ranged GET per part. Only a 206 is accepted: a 200
    /// means the server ignored the range and is about to send gigabytes.
    /// </summary>
    internal sealed class PartReader
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly IReadOnlyList<string> _urls;
        private readonly long[] _starts;
        private readonly IReadOnlyList<long> _sizes;
        private readonly Func<string, long, long, CancellationToken, Task<byte[]?>> _fetch;

        internal PartReader(IReadOnlyList<string> urls, IReadOnlyList<long> sizes)
            : this(urls, sizes, GetRangeAsync) { }

        /// <summary>With the fetch injected, so the part arithmetic is testable without a network.</summary>
        internal PartReader(IReadOnlyList<string> urls, IReadOnlyList<long> sizes,
            Func<string, long, long, CancellationToken, Task<byte[]?>> fetch)
        {
            _fetch = fetch;
            if (urls.Count == 0 || urls.Count != sizes.Count) throw new ArgumentException("One size per part.");
            _urls = urls;
            _sizes = sizes;
            _starts = new long[urls.Count];
            for (int i = 1; i < urls.Count; i++) _starts[i] = _starts[i - 1] + sizes[i - 1];
            Total = _starts[^1] + sizes[^1];
        }

        internal long Total { get; }

        internal async Task<byte[]?> ReadAsync(long offset, int count, CancellationToken ct)
        {
            if (offset < 0 || count < 0 || offset + count > Total) return null;
            var result = new byte[count];
            int written = 0;
            for (int i = 0; i < _urls.Count && written < count; i++)
            {
                long partStart = _starts[i], partEnd = partStart + _sizes[i];
                long want = offset + written;
                if (want >= partEnd) continue;
                long from = want - partStart;
                int take = (int)Math.Min(count - written, partEnd - want);
                var chunk = await _fetch(_urls[i], from, from + take - 1, ct);
                if (chunk == null || chunk.Length != take) return null;
                Buffer.BlockCopy(chunk, 0, result, written, take);
                written += take;
            }
            return written == count ? result : null;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher");
            return client;
        }

        private static async Task<byte[]?> GetRangeAsync(string url, long from, long to, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            {
                DiagnosticLog.Write($"RemotePayloadIndex: range request returned {(int)response.StatusCode} (expected 206).");
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }
}
