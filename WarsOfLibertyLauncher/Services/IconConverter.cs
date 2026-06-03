using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Wraps a PNG into a valid single-frame Windows <c>.ico</c> container so it
/// can be used as a shortcut (<c>.lnk</c>) IconLocation. Windows refuses to
/// render a raw <c>.png</c> as a shortcut icon (only <c>.ico</c> / <c>.exe</c>
/// / <c>.dll</c> are accepted, and a <c>.png</c> silently falls back to the
/// target exe's embedded icon), but it does accept a PNG-compressed icon frame
/// inside an <c>.ico</c> container (supported since Windows Vista). No pixel
/// re-encoding happens: the PNG bytes are embedded verbatim and we only read
/// the dimensions for the directory entry.
///
/// Dependency-free — uses WPF's <see cref="BitmapDecoder"/> from PresentationCore
/// (already loaded by this WPF app), NOT System.Drawing. Best-effort: returns
/// false on any failure and leaves the caller to fall back to the exe icon.
/// </summary>
internal static class IconConverter
{
    /// <summary>
    /// Reads the PNG at <paramref name="pngPath"/> and writes a single-frame
    /// <c>.ico</c> wrapping it to <paramref name="icoPath"/>. Returns true on
    /// success. Only PNG sources are wrapped (the ICO PNG-frame format requires
    /// the embedded payload to actually be a PNG); other formats return false.
    /// </summary>
    public static bool TryWritePngAsIco(string pngPath, string icoPath)
    {
        try
        {
            if (string.IsNullOrEmpty(pngPath) || string.IsNullOrEmpty(icoPath))
                return false;
            if (!string.Equals(Path.GetExtension(pngPath), ".png", StringComparison.OrdinalIgnoreCase))
                return false;

            var pngBytes = File.ReadAllBytes(pngPath);
            if (pngBytes.Length == 0) return false;

            // Read dimensions only — do not re-encode. OnLoad so the backing
            // stream can be disposed immediately after.
            int width, height;
            using (var ms = new MemoryStream(pngBytes, writable: false))
            {
                var decoder = BitmapDecoder.Create(
                    ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }
            if (width <= 0 || height <= 0) return false;

            using var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var w = new BinaryWriter(fs);

            // ICONDIR (6 bytes)
            w.Write((ushort)0);   // reserved, must be 0
            w.Write((ushort)1);   // type: 1 = icon
            w.Write((ushort)1);   // image count

            // ICONDIRENTRY (16 bytes). The width/height byte is 0 when the
            // dimension is >= 256 (per the ICO spec).
            w.Write((byte)(width  >= 256 ? 0 : width));
            w.Write((byte)(height >= 256 ? 0 : height));
            w.Write((byte)0);     // color palette count (0 = no palette)
            w.Write((byte)0);     // reserved, must be 0
            w.Write((ushort)1);   // color planes
            w.Write((ushort)32);  // bits per pixel
            w.Write((uint)pngBytes.Length);  // size of the image data
            w.Write((uint)22);    // offset of image data: 6 (ICONDIR) + 16 (entry)

            // Image data: the raw PNG, embedded verbatim.
            w.Write(pngBytes);
            w.Flush();
            return true;
        }
        catch
        {
            // Leave no half-written file behind.
            try { if (File.Exists(icoPath)) File.Delete(icoPath); } catch { /* ignore */ }
            return false;
        }
    }
}
