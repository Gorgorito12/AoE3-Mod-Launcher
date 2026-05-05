using System;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// File hashing utilities.
///
/// MD5 is used to identify the user's current mod version (compatible with the
/// existing UpdateInfo.xml format which uses MD5 of three key data files).
/// CRC32 is used to verify downloaded .tar.xz patches.
/// </summary>
public static class HashService
{
    /// <summary>Compute the MD5 of a file as lowercase hex. Returns empty string if file missing.</summary>
    public static async Task<string> ComputeMd5Async(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        using var md5 = MD5.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1024 * 1024, useAsync: true);
        var hash = await md5.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compute CRC32 of a file as lowercase hex (8 chars, zero-padded).
    /// Compatible with Java's Guava Hashing.crc32() which uses the same polynomial.
    /// </summary>
    public static async Task<string> ComputeCrc32Async(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        var crc = new Crc32();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1024 * 1024, useAsync: true);

        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            crc.Append(buffer.AsSpan(0, read));
        }

        // Crc32.GetCurrentHash returns 4 bytes in little-endian; we need standard
        // big-endian hex representation (matches Java/Guava output).
        var bytes = crc.GetCurrentHash();
        Array.Reverse(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
