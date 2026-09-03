using System;
using System.Buffers.Binary;

namespace WarsOfLibertyLauncher.Services;

/// <summary>One decoded mip level: 32-bit BGRA, top-down, stride <c>Width * 4</c>.</summary>
/// <remarks>
/// The byte order and the row order are not a guess. The install ships 60 uncompressed format-1
/// <c>.ddt</c> files that have a same-name 32-bit TGA sibling, and the DDT payload matches the
/// TGA <b>vertically flipped, with no channel swap</b>, byte for byte, on every pair checked.
/// A TGA of that shape is BGRA and bottom-up by its own header, so the DDT is BGRA and top-down.
/// That makes this a direct hand-off to
/// <c>BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Bgra, w * 4)</c>.
/// </remarks>
public sealed record DdtImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads Ensemble's <c>RTS3</c> texture container — the format every Age of Empires III icon is
/// stored in, loose under <c>art\</c> or inside a <c>.bar</c>.
///
/// <para>WPF has no codec for it and none is being added for one feature, so the three formats
/// the game's card icons actually use are decoded here: raw BGRA, DXT1 and DXT5.</para>
///
/// <para><b>Every rejection returns null rather than throwing</b>, and that is the contract the
/// callers rely on: an icon that cannot be read leaves one card without a picture, which is
/// visible, where a thrown exception would take a whole deck's grid down with it.</para>
/// </summary>
public static class DdtDecoder
{
    /// <summary>Magic, usage, alpha bits, format, mip count, width, height, mip-0 offset+length.</summary>
    public const int HeaderBytes = 24;

    /// <summary>Card icons are 64x64 or 128x128; the largest atlas in the install is 576x448.</summary>
    internal const int MaxDimension = 4096;

    public const byte FormatRaw = 1;
    public const byte FormatDxt1 = 4;
    public const byte FormatDxt5 = 8;

    /// <summary>
    /// Decodes mip 0, or null if this is not a texture we can read.
    ///
    /// <para><b>Mip 0 only, and that matters:</b> plenty of these carry a full chain —
    /// <c>hc_wood_crate_128</c> has six levels — so the length at offset 0x14 is the slice to
    /// read. Taking the rest of the file instead would decode the whole pyramid as one image.</para>
    /// </summary>
    public static DdtImage? Decode(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderBytes) return null;
        if (file[0] != 0x52 || file[1] != 0x54 || file[2] != 0x53 || file[3] != 0x33) return null;

        var format = file[6];
        var width = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(8, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(12, 4));
        var offset = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(16, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(20, 4));

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension) return null;
        if (offset < HeaderBytes || length <= 0) return null;
        if ((long)offset + length > file.Length) return null;

        // A 32-bit surface of the largest allowed texture is 64 MB, so nothing below can overflow;
        // the guard is on the DIMENSIONS above, which is where a hostile header would attack.
        var payload = file.Slice(offset, length);

        return format switch
        {
            FormatRaw => DecodeRaw(payload, width, height),
            FormatDxt1 => DecodeBlocks(payload, width, height, dxt5: false),
            FormatDxt5 => DecodeBlocks(payload, width, height, dxt5: true),
            _ => null,
        };
    }

    private static DdtImage? DecodeRaw(ReadOnlySpan<byte> payload, int width, int height)
    {
        var needed = (long)width * height * 4;
        if (payload.Length < needed) return null;

        var bgra = new byte[needed];
        payload.Slice(0, (int)needed).CopyTo(bgra);
        return new DdtImage(width, height, bgra);
    }

    /// <summary>
    /// BC1 (DXT1) and BC3 (DXT5). They share the 8-byte colour block; DXT5 prefixes an 8-byte
    /// interpolated-alpha block and always uses the colour block's four-colour mode.
    /// </summary>
    private static DdtImage? DecodeBlocks(ReadOnlySpan<byte> payload, int width, int height, bool dxt5)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var blockBytes = dxt5 ? 16 : 8;
        if (payload.Length < (long)blocksX * blocksY * blockBytes) return null;

        var bgra = new byte[(long)width * height * 4];
        var stride = width * 4;

        Span<byte> alpha = stackalloc byte[8];
        Span<int> colours = stackalloc int[4];

        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var block = payload.Slice((by * blocksX + bx) * blockBytes, blockBytes);
                var colourBlock = dxt5 ? block.Slice(8, 8) : block;

                ulong alphaIndices = 0;
                if (dxt5)
                {
                    BuildAlphaPalette(block[0], block[1], alpha);
                    alphaIndices = block[2]
                        | ((ulong)block[3] << 8) | ((ulong)block[4] << 16)
                        | ((ulong)block[5] << 24) | ((ulong)block[6] << 32) | ((ulong)block[7] << 40);
                }

                var c0 = (ushort)(colourBlock[0] | (colourBlock[1] << 8));
                var c1 = (ushort)(colourBlock[2] | (colourBlock[3] << 8));

                // DXT5 carries its own alpha, so its colour block never uses the 1-bit-alpha mode.
                // Honouring c0 > c1 there would punch transparent holes in opaque pixels.
                BuildColourPalette(c0, c1, opaqueOnly: dxt5, colours);

                var indices = (uint)(colourBlock[4]
                    | (colourBlock[5] << 8) | (colourBlock[6] << 16) | (colourBlock[7] << 24));

                for (var py = 0; py < 4; py++)
                {
                    var y = by * 4 + py;
                    if (y >= height) break;

                    for (var px = 0; px < 4; px++)
                    {
                        var x = bx * 4 + px;
                        if (x >= width) break;

                        var i = py * 4 + px;
                        var packed = colours[(int)((indices >> (i * 2)) & 3)];
                        var at = y * stride + x * 4;

                        bgra[at] = (byte)packed;
                        bgra[at + 1] = (byte)(packed >> 8);
                        bgra[at + 2] = (byte)(packed >> 16);
                        bgra[at + 3] = dxt5
                            ? alpha[(int)((alphaIndices >> (i * 3)) & 7)]
                            : (byte)(packed >> 24);
                    }
                }
            }
        }

        return new DdtImage(width, height, bgra);
    }

    private static void BuildColourPalette(ushort c0, ushort c1, bool opaqueOnly, Span<int> palette)
    {
        var (b0, g0, r0) = From565(c0);
        var (b1, g1, r1) = From565(c1);

        palette[0] = Pack(b0, g0, r0, 255);
        palette[1] = Pack(b1, g1, r1, 255);

        if (opaqueOnly || c0 > c1)
        {
            palette[2] = Pack((b0 * 2 + b1) / 3, (g0 * 2 + g1) / 3, (r0 * 2 + r1) / 3, 255);
            palette[3] = Pack((b0 + b1 * 2) / 3, (g0 + g1 * 2) / 3, (r0 + r1 * 2) / 3, 255);
        }
        else
        {
            palette[2] = Pack((b0 + b1) / 2, (g0 + g1) / 2, (r0 + r1) / 2, 255);
            palette[3] = 0;   // the 1-bit-alpha mode's transparent entry
        }
    }

    private static void BuildAlphaPalette(byte a0, byte a1, Span<byte> palette)
    {
        palette[0] = a0;
        palette[1] = a1;

        if (a0 > a1)
        {
            for (var i = 1; i <= 6; i++)
                palette[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (var i = 1; i <= 4; i++)
                palette[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            palette[6] = 0;
            palette[7] = 255;
        }
    }

    /// <summary>RGB565 to 8-bit channels, replicating the high bits so white stays white.</summary>
    private static (int B, int G, int R) From565(ushort c)
    {
        var r = (c >> 11) & 0x1F;
        var g = (c >> 5) & 0x3F;
        var b = c & 0x1F;
        return ((b << 3) | (b >> 2), (g << 2) | (g >> 4), (r << 3) | (r >> 2));
    }

    private static int Pack(int b, int g, int r, int a) => b | (g << 8) | (r << 16) | (a << 24);
}
