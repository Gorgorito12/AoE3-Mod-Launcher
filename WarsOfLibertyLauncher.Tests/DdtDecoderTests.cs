using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The <c>RTS3</c> texture decoder behind every card icon.
///
/// <para><b>The rejections are the point.</b> These files come from whatever mod the player
/// installed, they are read on a background thread while a deck is being drawn, and a throw
/// there would cost the whole grid rather than one picture. So every malformed shape has to come
/// back as null — and "null" has to mean it, not an exception the caller happens to catch.</para>
///
/// <para>The happy cases are pinned against hand-built bytes because the real proof of the
/// decoder lives outside a unit test: the install ships format-1 <c>.ddt</c> files with 32-bit
/// TGA siblings, and the payload matches those flipped vertically with no channel swap, which is
/// what established BGRA and top-down in the first place.</para>
/// </summary>
public class DdtDecoderTests
{
    private const byte Raw = DdtDecoder.FormatRaw;
    private const byte Dxt1 = DdtDecoder.FormatDxt1;
    private const byte Dxt5 = DdtDecoder.FormatDxt5;

    /// <summary>A whole file: the 24-byte header followed by the payload it points at.</summary>
    private static byte[] File(byte format, int width, int height, byte[] payload,
                              int? offsetOverride = null, int? lengthOverride = null)
    {
        var header = new byte[DdtDecoder.HeaderBytes];
        header[0] = 0x52; header[1] = 0x54; header[2] = 0x53; header[3] = 0x33;   // RTS3
        header[4] = 1;            // usage
        header[5] = 8;            // alpha bits
        header[6] = format;
        header[7] = 1;            // mip levels
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), height);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),
            offsetOverride ?? DdtDecoder.HeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20),
            lengthOverride ?? payload.Length);

        var file = new byte[header.Length + payload.Length];
        header.CopyTo(file, 0);
        payload.CopyTo(file, header.Length);
        return file;
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(DdtImage image, int x, int y)
    {
        var at = (y * image.Width + x) * 4;
        return (image.Bgra[at], image.Bgra[at + 1], image.Bgra[at + 2], image.Bgra[at + 3]);
    }

    // ------------------------------------------------------------------ rejections

    public static IEnumerable<object[]> Malformed()
    {
        yield return new object[] { new byte[] { 0x52, 0x54 } };                       // truncated
        yield return new object[] { File(Raw, 2, 2, new byte[16]).Replace(0, 0x44) };  // bad magic
        yield return new object[] { File(Raw, 0, 2, new byte[16]) };                   // zero width
        yield return new object[] { File(Raw, 2, -1, new byte[16]) };                  // negative height
        yield return new object[] { File(Raw, 99999, 2, new byte[16]) };               // absurd width
        yield return new object[] { File(Raw, 2, 2, new byte[16], offsetOverride: 4) }; // offset in header
        yield return new object[] { File(Raw, 2, 2, new byte[16], lengthOverride: 999) }; // past the end
        yield return new object[] { File(Raw, 2, 2, new byte[16], lengthOverride: 0) };   // empty slice
        yield return new object[] { File(3, 2, 2, new byte[16]) };                     // palette: unsupported
        yield return new object[] { File(7, 2, 2, new byte[16]) };                     // grayscale: unsupported
        yield return new object[] { File(Raw, 4, 4, new byte[16]) };                   // payload too small
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedFileReturnsNullAndNeverThrows(byte[] file)
    {
        var error = Record.Exception(() => { DdtDecoder.Decode(file); });
        Assert.Null(error);
        Assert.Null(DdtDecoder.Decode(file));
    }

    [Fact]
    public void AnEmptySpanIsRejected() => Assert.Null(DdtDecoder.Decode(ReadOnlySpan<byte>.Empty));

    // ------------------------------------------------------------------ raw

    [Fact]
    public void RawPixelsComeBackByteForByteInTheOrderTheyWereWritten()
    {
        // Deliberately asymmetric per channel: a decoder that swapped R and B, or flipped the
        // rows, would still pass on a uniform image.
        var payload = new byte[]
        {
            1, 2, 3, 255,      4, 5, 6, 254,
            7, 8, 9, 253,     10, 11, 12, 252,
        };

        var image = DdtDecoder.Decode(File(Raw, 2, 2, payload));

        Assert.NotNull(image);
        Assert.Equal(2, image!.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal((byte)1, PixelAt(image, 0, 0).B);
        Assert.Equal((byte)3, PixelAt(image, 0, 0).R);
        Assert.Equal((byte)7, PixelAt(image, 0, 1).B);      // row 1 is the SECOND row, not the first
        Assert.Equal((byte)252, PixelAt(image, 1, 1).A);
    }

    /// <summary>
    /// A file whose mip-0 slice is shorter than the rest of it — the six-level chains the game
    /// ships. Reading past the recorded length would decode the smaller mips as image content.
    /// </summary>
    [Fact]
    public void OnlyTheFirstMipIsRead()
    {
        var mip0 = new byte[] { 9, 9, 9, 255, 9, 9, 9, 255, 9, 9, 9, 255, 9, 9, 9, 255 };
        var withTail = new byte[mip0.Length + 64];
        mip0.CopyTo(withTail, 0);

        var image = DdtDecoder.Decode(File(Raw, 2, 2, withTail, lengthOverride: mip0.Length));

        Assert.NotNull(image);
        Assert.Equal(2 * 2 * 4, image!.Bgra.Length);
    }

    // ------------------------------------------------------------------ block formats

    /// <summary>One DXT1 block: colour 0 white, colour 1 black, every pixel taking colour 0.</summary>
    [Fact]
    public void Dxt1DecodesItsFirstPaletteEntry()
    {
        var block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0), 0xFFFF);   // white
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), 0x0000);   // black
        // indices all zero

        var image = DdtDecoder.Decode(File(Dxt1, 4, 4, block));

        Assert.NotNull(image);
        var (b, g, r, a) = PixelAt(image!, 2, 2);
        Assert.Equal((byte)255, b);
        Assert.Equal((byte)255, g);
        Assert.Equal((byte)255, r);
        Assert.Equal((byte)255, a);
    }

    /// <summary>
    /// DXT1's other mode: with <c>c0 &lt;= c1</c> the fourth palette entry is transparent. This
    /// is the half a naive decoder gets wrong, and it shows up as black boxes behind icons.
    /// </summary>
    [Fact]
    public void Dxt1HonoursItsOneBitAlphaMode()
    {
        var block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(0), 0x0000);   // c0 <= c1
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), 0xFFFF);
        for (var i = 4; i < 8; i++) block[i] = 0xFF;                          // every index = 3

        var image = DdtDecoder.Decode(File(Dxt1, 4, 4, block));

        Assert.NotNull(image);
        Assert.Equal((byte)0, PixelAt(image!, 1, 1).A);
    }

    /// <summary>
    /// DXT5 carries its own alpha, so its colour block must NOT be read in the one-bit mode even
    /// when <c>c0 &lt;= c1</c> — doing so punches transparent holes through opaque artwork.
    /// </summary>
    [Fact]
    public void Dxt5KeepsItsColourBlockOpaqueAndReadsAlphaSeparately()
    {
        var block = new byte[16];
        block[0] = 200;      // a0
        block[1] = 10;       // a1, so a0 > a1 and index 0 means a0
        // alpha indices all zero

        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), 0x0000);   // c0 <= c1
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), 0xFFFF);
        for (var i = 12; i < 16; i++) block[i] = 0xFF;                        // colour index 3

        var image = DdtDecoder.Decode(File(Dxt5, 4, 4, block));

        Assert.NotNull(image);
        Assert.Equal((byte)200, PixelAt(image!, 1, 1).A);
    }

    /// <summary>
    /// 65x64 and 64x65 exist in the install. The block grid rounds up, so the decoder has to
    /// drop the pixels past the edge instead of writing them.
    /// </summary>
    [Fact]
    public void ASizeThatIsNotAMultipleOfFourStillDecodes()
    {
        var blocks = new byte[2 * 2 * 8];   // 5x5 needs a 2x2 grid of DXT1 blocks
        for (var i = 0; i < blocks.Length; i += 8)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(blocks.AsSpan(i), 0xFFFF);
        }

        var image = DdtDecoder.Decode(File(Dxt1, 5, 5, blocks));

        Assert.NotNull(image);
        Assert.Equal(5 * 5 * 4, image!.Bgra.Length);
    }
}

internal static class ByteArrayPatch
{
    /// <summary>Returns a copy with one byte changed, for building a deliberately broken file.</summary>
    public static byte[] Replace(this byte[] bytes, int index, byte value)
    {
        var copy = (byte[])bytes.Clone();
        copy[index] = value;
        return copy;
    }
}
