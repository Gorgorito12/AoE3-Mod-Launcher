using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One file inside a <c>.bar</c>: where it starts and how long it is.</summary>
public readonly record struct BarEntry(string Name, long Offset, int Size);

/// <summary>
/// Reads the table of contents of an Age of Empires III <c>.bar</c> archive, and pulls one file
/// out of it. This is how the launcher reaches the card icons that are not loose on disk —
/// measured on a real deck, <b>26 of 35 cards</b> have their icon only in here.
///
/// <para><b>No decompressor, because there is nothing to decompress.</b> Across the eight
/// archives Wars of Liberty ships, 44,997 entries were read and <c>size</c> differed from
/// <c>sizeUncompressed</c> in <b>none</b> of them; the first 400 <c>.ddt</c> entries of
/// <c>Art5.bar</c> all begin with the plain <c>RTS3</c> magic at their recorded offset. So an
/// entry is a seek and a read.</para>
///
/// <para><b>An entry whose two sizes disagree is SKIPPED rather than read.</b> None exists
/// today; the rule is what stops a future one from being handed to the decoder as if it were
/// raw, which would silently produce garbage pixels instead of a missing icon.</para>
/// </summary>
public static class BarArchive
{
    /// <summary>The file count and the TOC offset sit at fixed places in the 0x120-byte header.</summary>
    private const int FileCountOffset = 0x118;
    private const int TocOffsetOffset = 0x11C;
    private const int MinHeaderBytes = 0x120;

    /// <summary>Art5.bar, the largest here, holds 11,273 files.</summary>
    private const int MaxEntries = 200_000;

    /// <summary>Longest real path seen is ~120 characters.</summary>
    private const int MaxNameChars = 512;

    /// <summary>
    /// Every readable entry, or an empty list if this is not a <c>.bar</c> we understand.
    ///
    /// <para>A LIST rather than a dictionary on purpose: an archive may name the same path twice,
    /// and building the map is the caller's job — it merges five archives into one index anyway,
    /// where a duplicate has to be resolved rather than thrown.</para>
    /// </summary>
    public static IReadOnlyList<BarEntry> ReadIndex(string barPath)
    {
        var entries = new List<BarEntry>();
        if (string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath)) return entries;

        try
        {
            using var stream = File.OpenRead(barPath);
            if (stream.Length < MinHeaderBytes) return entries;

            using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

            if (reader.ReadByte() != 0x45 || reader.ReadByte() != 0x53
                || reader.ReadByte() != 0x50 || reader.ReadByte() != 0x4E)
            {
                return entries;   // not "ESPN"
            }

            stream.Position = FileCountOffset;
            var count = reader.ReadUInt32();
            var tocOffset = reader.ReadUInt32();

            if (count == 0 || count > MaxEntries) return entries;
            if (tocOffset < MinHeaderBytes || tocOffset >= stream.Length) return entries;

            stream.Position = tocOffset;

            // The TOC opens with the archive's own root name, then one field nobody needs.
            if (!TrySkipName(reader, stream)) return entries;
            reader.ReadUInt32();

            for (var i = 0; i < count; i++)
            {
                if (stream.Position + 32 > stream.Length) break;

                var offset = reader.ReadUInt32();
                var size = reader.ReadUInt32();
                var sizeUncompressed = reader.ReadUInt32();
                stream.Position += 16;   // the timestamp, which nothing here uses

                var name = TryReadName(reader, stream);
                if (name == null) break;

                if (size != sizeUncompressed) continue;
                if (size == 0 || offset + (long)size > stream.Length) continue;

                entries.Add(new BarEntry(name, offset, (int)size));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"BarArchive: could not index '{barPath}' — {ex.Message}");
            return new List<BarEntry>();
        }

        return entries;
    }

    /// <summary>The entry's bytes, or null if the archive no longer matches the index.</summary>
    public static byte[]? ReadEntry(string barPath, in BarEntry entry)
    {
        if (string.IsNullOrWhiteSpace(barPath) || entry.Size <= 0) return null;

        try
        {
            using var stream = File.OpenRead(barPath);
            if (entry.Offset + entry.Size > stream.Length) return null;

            stream.Position = entry.Offset;
            var buffer = new byte[entry.Size];
            var read = 0;
            while (read < buffer.Length)
            {
                var got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0) return null;
                read += got;
            }
            return buffer;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"BarArchive: could not read '{entry.Name}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>A length-prefixed UTF-16 name. The length counts CHARACTERS, not bytes.</summary>
    private static string? TryReadName(BinaryReader reader, Stream stream)
    {
        if (stream.Position + 4 > stream.Length) return null;

        var chars = reader.ReadUInt32();
        if (chars == 0 || chars > MaxNameChars) return null;

        var bytes = (int)chars * 2;
        if (stream.Position + bytes > stream.Length) return null;

        return Encoding.Unicode.GetString(reader.ReadBytes(bytes)).TrimEnd('\0');
    }

    private static bool TrySkipName(BinaryReader reader, Stream stream) =>
        TryReadName(reader, stream) != null;
}
