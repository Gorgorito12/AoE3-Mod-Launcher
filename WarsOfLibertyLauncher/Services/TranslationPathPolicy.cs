using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// What a translation pack is allowed to name. A pack is UNTRUSTED input: it can come from a
/// repository the player added by hand, and its <c>translation.json</c> chooses both the folder
/// the launcher extracts it into (its <c>id</c>) and the files the launcher then overwrites (each
/// <c>files[].path</c>). Before this existed, <c>Apply</c> wrote to
/// <c>Path.Combine(install, file.Path)</c> as given — so a <c>..</c> or a rooted path wrote
/// anywhere on the disk — and the installer deleted <c>translations\&lt;id&gt;</c> recursively
/// with an id nobody had looked at.
///
/// <para>The rule is an allow-list, not a filter: a pack may only replace a file the MOD's own
/// profile declares in <c>Translations.CoveredFiles</c>, and the destination is built from that
/// declared path, never from the pack's spelling of it. Structural checks run first so a path
/// that could mean two things on Windows (a trailing dot, a device name, an alternate data
/// stream) is refused outright rather than compared.</para>
///
/// <para>Pure and WPF-free, like <see cref="SafeUrl"/>, so the rejection cases can be pinned by
/// tests — those are the ones that matter.</para>
/// </summary>
internal static class TranslationPathPolicy
{
    /// <summary>Longest relative path a pack may name. Covered files are a few dozen characters.</summary>
    public const int MaxRelativePathLength = 260;

    // \z, not $: in .NET a $ also matches just before a trailing newline, which would let
    // "es\n" through as a pack id.
    private static readonly Regex PackIdRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant);

    private static readonly Regex VersionSegmentRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}\z", RegexOptions.CultureInvariant);

    /// <summary>Characters Windows refuses in a file name, beyond the separators and controls.</summary>
    private static readonly char[] ForbiddenNameChars = { '<', '>', '"', '|', '?', '*' };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Normalizes an install-relative path to forward slashes, or refuses it. Refused: empty,
    /// surrounding whitespace, control characters, a rooted path in any spelling (<c>C:\</c>,
    /// <c>C:x</c>, <c>\\server</c>, a leading slash), any colon (a drive letter or an alternate
    /// data stream), an empty segment, a <c>.</c> or <c>..</c> segment, a segment that ends in a
    /// dot or a space (Windows strips both, so the name would alias another), and Windows device
    /// names. The case is preserved; comparisons decide case-insensitivity.
    /// </summary>
    public static bool TryNormalizeRelative(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxRelativePathLength) return false;
        if (!string.Equals(raw, raw.Trim(), StringComparison.Ordinal)) return false;
        foreach (var c in raw)
            if (char.IsControl(c)) return false;

        var s = raw.Replace('\\', '/');
        if (s.StartsWith('/')) return false;
        if (s.Contains(':')) return false;
        if (s.IndexOfAny(ForbiddenNameChars) >= 0) return false;

        var segments = s.Split('/');
        foreach (var seg in segments)
        {
            if (seg.Length == 0) return false;
            if (seg == "." || seg == "..") return false;
            if (seg.EndsWith('.') || seg.EndsWith(' ') || seg.StartsWith(' ')) return false;
            if (IsReservedDeviceName(seg)) return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    /// <summary>
    /// Maps a path named by a pack onto one of the mod's covered files. Matches
    /// case-insensitively, as Windows does, and returns the COVERED entry's spelling with the
    /// platform separator — so the destination is always the path the mod declared, never the
    /// pack's. A covered entry that is itself malformed is ignored.
    /// </summary>
    public static bool TryResolveCoveredTarget(
        string? manifestPath, IReadOnlyList<string>? covered, out string canonicalRelative)
    {
        canonicalRelative = "";
        if (covered == null || covered.Count == 0) return false;
        if (!TryNormalizeRelative(manifestPath, out var wanted)) return false;

        foreach (var entry in covered)
        {
            if (!TryNormalizeRelative(entry, out var allowed)) continue;
            if (!string.Equals(allowed, wanted, StringComparison.OrdinalIgnoreCase)) continue;
            canonicalRelative = allowed.Replace('/', Path.DirectorySeparatorChar);
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="id"/> can be used as the pack's folder name under
    /// <c>translations\</c>. It has to start with a letter or digit, which by itself keeps out
    /// <c>_originals</c> and the launcher's own <c>.incoming-*</c> / <c>.old-*</c> scratch
    /// folders, and it may hold only letters, digits, <c>.</c>, <c>_</c> and <c>-</c>.
    /// </summary>
    public static bool IsSafePackId(string? id) => IsSafeSegment(id, PackIdRegex);

    /// <summary>
    /// True when <paramref name="version"/> can be used as the version folder
    /// (<c>translations/&lt;id&gt;/&lt;version&gt;/</c>). Same rule as an id, plus <c>+</c>.
    /// </summary>
    public static bool IsSafeVersionSegment(string? version) => IsSafeSegment(version, VersionSegmentRegex);

    /// <summary>
    /// The last line of defence after a path has been built: true only when
    /// <paramref name="fullPath"/> sits strictly inside <paramref name="root"/>.
    /// </summary>
    public static bool IsUnderRoot(string root, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath)) return false;
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
            var f = Path.GetFullPath(fullPath);
            return f.StartsWith(r, StringComparison.OrdinalIgnoreCase) && f.Length > r.Length;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when two of <paramref name="paths"/> share a file name. The pack folder and the
    /// <c>_originals</c> snapshot are flat, so two such files would silently overwrite each other.
    /// </summary>
    public static bool HasDuplicateFileNames(IEnumerable<string?> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var name = Path.GetFileName((p ?? "").Replace('/', Path.DirectorySeparatorChar));
            if (name.Length == 0) continue;
            if (!seen.Add(name)) return true;
        }
        return false;
    }

    private static bool IsSafeSegment(string? value, Regex rx) =>
        !string.IsNullOrEmpty(value)
        && rx.IsMatch(value)
        && !value.EndsWith('.')
        && !IsReservedDeviceName(value);

    /// <summary>Windows device names are reserved with or without an extension (<c>con.txt</c>).</summary>
    private static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }
}
