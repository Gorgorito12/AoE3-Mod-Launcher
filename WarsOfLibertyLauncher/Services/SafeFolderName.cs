using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Turns a mod's NAME into a name Windows accepts for a folder, a shortcut or a registry key.
/// The one rule every such place goes through.
///
/// <para>It exists because a display name is free text and a folder name is not. The catalog
/// shipped "Age of Empires 1: HD Edition", and the install folder, the Desktop shortcut and the
/// Start Menu folder were all built from it as written. NTFS reads a colon as the start of an
/// alternate data stream, so the install downloaded and extracted 301 MB and only then died
/// creating its own folder (ERROR_DIRECTORY), while the shortcut was silently written as a
/// hidden stream on a 0-byte file named "Age of Empires 1".</para>
///
/// <para><b>The rule (the maintainer's choice): drop it.</b> A run of forbidden characters,
/// together with the whitespace around it, becomes ONE space — "Age of Empires 1: HD Edition"
/// becomes "Age of Empires 1 HD Edition", which is also the folder name the mod's own catalog
/// entry uses for its <c>defaultFolder</c> and its My Games folder.</para>
///
/// <para><b>A valid name comes back unchanged, byte for byte</b>, and cleaning twice changes
/// nothing. That is what keeps every existing install, shortcut and private registry key where
/// it is: for every name that already worked, nothing moves.</para>
///
/// <para>Pure and WPF-free, like <see cref="SafeUrl"/>. <see cref="PathDisplay"/> is the wrong
/// home: it is display-only by its own documentation, and these results go to the file
/// system.</para>
/// </summary>
internal static class SafeFolderName
{
    /// <summary>The NTFS limit for one path component. Not lower: the identity rule has to hold
    /// for every name Windows accepts, or a long copy label would get a renamed shortcut.</summary>
    internal const int MaxLength = 255;

    /// <summary>What a name falls back to when neither it nor the mod id leaves anything.</summary>
    internal const string LastResort = "Mod";

    /// <summary>
    /// Names Windows reserves for devices, with or without an extension (<c>con.txt</c> is CON).
    /// The superscript COM/LPT variants are on Microsoft's list too.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>A character Windows refuses in a file or folder name.</summary>
    internal static bool IsInvalidChar(char c) =>
        c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c);

    /// <summary>How a refused character is named to the player: itself, or its code point when
    /// it would print as nothing (a tab, a control character).</summary>
    internal static string DescribeChar(char c) =>
        char.IsControl(c) ? $"U+{(int)c:X4}" : c.ToString();

    /// <summary>
    /// The folder name for <paramref name="name"/>: the drop rule above, then trimmed, cut to
    /// <see cref="MaxLength"/> without splitting a character, and stripped of trailing dots and
    /// whitespace (Windows drops those silently, so the folder on disk would not match the
    /// string recorded for it). A reserved device name gets a <c>_</c> prefix, like
    /// <c>ReplayUploadService.SafeReplayFileName</c>. Nothing left falls back to the mod id
    /// cleaned the same way, then to <see cref="LastResort"/>.
    /// </summary>
    internal static string From(string? name, string? fallbackId)
    {
        var cleaned = Clean(name);
        if (cleaned.Length == 0) cleaned = Clean(fallbackId);
        return cleaned.Length == 0 ? LastResort : cleaned;
    }

    /// <summary>The folder name for a mod: <see cref="From"/> over its display name, its id as
    /// the fallback.</summary>
    internal static string ForProfile(ModProfile profile) => From(profile.DisplayName, profile.Id);

    /// <summary>
    /// True when <paramref name="name"/> is already a usable generated name — exactly the names
    /// <see cref="From"/> returns unchanged. Stricter than a typed path segment
    /// (<see cref="CheckSegment"/>): a generated name never starts or ends with whitespace.
    /// </summary>
    internal static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxLength
        && name == name.Trim()
        && CheckSegment(name) == null;

    /// <summary>
    /// Windows' rules for ONE segment of a path the player typed, first problem first: a refused
    /// character, a trailing dot or space (Win32 strips a middle folder's trailing dot and every
    /// trailing dot or space at the end of a path, so the folder on disk would not be the one the
    /// player typed), a reserved device name, more than 255 characters. A LEADING space is
    /// allowed: Windows accepts it.
    /// </summary>
    internal static InstallPathProblem? CheckSegment(string segment)
    {
        foreach (var c in segment)
            if (IsInvalidChar(c))
                return new InstallPathProblem(InstallPathProblemKind.InvalidCharacter, segment, DescribeChar(c));

        if (segment.Length > 0 && (segment[^1] == '.' || segment[^1] == ' '))
            return new InstallPathProblem(InstallPathProblemKind.TrailingDotOrSpace, segment);

        if (IsReservedDeviceName(segment))
            return new InstallPathProblem(InstallPathProblemKind.ReservedName, segment);

        if (segment.Length > MaxLength)
            return new InstallPathProblem(InstallPathProblemKind.SegmentTooLong, segment);

        return null;
    }

    /// <summary>A device name with or without an extension: the stem before the first dot,
    /// trailing spaces ignored (<c>CON .txt</c> is CON too).</summary>
    internal static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }

    /// <summary>
    /// The narrower test for a folder name a catalog manifest DECLARES (<c>userDataFolder</c>):
    /// refuses only what cannot be one folder under <c>My Games</c> at all — a forbidden
    /// character (which also catches a rooted <c>C:x</c>, the one that escapes My Games through
    /// <c>Path.Combine</c>), a name made only of dots and spaces (<c>..</c> is the parent, and
    /// <c>...</c> or <c>. .</c> resolve to My Games itself), or a device name. A leading space
    /// is accepted: Windows keeps it, and dropping a declared folder is not free — for a
    /// <c>userDataRedirect</c> mod it sends the game back to the shared vanilla folder. (The
    /// caller stores the name without trailing dots and spaces — the folder Windows really
    /// uses — so the guards that compare folder names compare the right one.)
    /// </summary>
    internal static bool IsUsableDeclaredName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.All(c => c == '.' || char.IsWhiteSpace(c))) return false;
        if (name.Any(IsInvalidChar)) return false;
        if (IsReservedDeviceName(name.Trim())) return false;
        return name.Length <= MaxLength;
    }

    /// <summary>
    /// True when <paramref name="name"/> names working shortcuts exactly as written. Looser than
    /// <see cref="IsValid"/> on purpose: a copy labelled "WoL v1." has always had working
    /// shortcuts, and cleaning its name now would leave the old ones behind next to new ones.
    ///
    /// <para>"Working" is precise, and each refusal is a way the name cannot work:</para>
    /// <list type="bullet">
    ///   <item>a forbidden character or a device name;</item>
    ///   <item>whitespace at either end — a re-overlay carries the name TRIMMED, so an install
    ///   named " Foo" would get "Foo" shortcuts at its first repair and orphan the first ones;</item>
    ///   <item>a name that, past at most ONE final dot, still ends in a dot or a space. The Start
    ///   Menu folder is the last segment when it is created (Windows strips every trailing dot and
    ///   space there) and a middle segment when the shortcut inside it is saved (Windows strips one
    ///   final dot there, nothing else), so "Mod v1..", "Mod ." or "Foo " create one folder and
    ///   then look for the shortcut in another. "WoL v1." resolves the same way both times.</item>
    /// </list>
    /// </summary>
    internal static bool CanBeUsedAsIs(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength) return false;
        if (name != name.Trim()) return false;
        if (name.Any(IsInvalidChar) || IsReservedDeviceName(name)) return false;
        var core = name.EndsWith('.') ? name[..^1] : name;
        return core.Length > 0 && core[^1] != '.' && core[^1] != ' ';
    }

    /// <summary>
    /// Every leaf that names THIS mod's folder: the display name as written, its cleaned form,
    /// and the leaf of the catalog's <c>defaultFolder</c>. For COMPARING a saved path's leaf only
    /// — the raw name may not be a valid folder name, so a path is never built from this list.
    /// </summary>
    internal static IReadOnlyList<string> KnownLeaves(ModProfile profile)
    {
        var leaves = new List<string>();
        void Add(string? leaf)
        {
            if (!string.IsNullOrWhiteSpace(leaf) && !leaves.Contains(leaf, StringComparer.OrdinalIgnoreCase))
                leaves.Add(leaf);
        }

        Add(profile.DisplayName);
        Add(ForProfile(profile));
        Add(LeafOf(profile.DefaultInstallFolder));
        return leaves;
    }

    private static string LeafOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFileName(path.Trim().TrimEnd('\\', '/')); }
        catch { return ""; }
    }

    private static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var sb = new StringBuilder(name.Length);
        var i = 0;
        while (i < name.Length)
        {
            var c = name[i];
            if (!IsInvalidChar(c))
            {
                sb.Append(c);
                i++;
                continue;
            }

            // A run of forbidden characters goes together with the whitespace on BOTH sides of
            // it, leaving one space — so "1: HD" and "1 : HD" both become "1 HD". Whitespace
            // anywhere else is kept, which is what makes a valid name come back unchanged.
            while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
            while (i < name.Length && (IsInvalidChar(name[i]) || char.IsWhiteSpace(name[i]))) i++;
            if (sb.Length > 0 && i < name.Length) sb.Append(' ');
        }

        var s = sb.ToString().Trim();
        if (s.Length > MaxLength) s = Cut(s, MaxLength);
        s = TrimTrailingDotsAndWhitespace(s);
        if (s.Length == 0) return "";

        if (IsReservedDeviceName(s))
        {
            s = "_" + s;
            if (s.Length > MaxLength) s = TrimTrailingDotsAndWhitespace(Cut(s, MaxLength));
        }
        return s;
    }

    /// <summary>Cut to <paramref name="max"/> characters without leaving half a surrogate pair.</summary>
    private static string Cut(string s, int max)
    {
        var n = max;
        if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }

    private static string TrimTrailingDotsAndWhitespace(string s)
    {
        var end = s.Length;
        while (end > 0 && (s[end - 1] == '.' || char.IsWhiteSpace(s[end - 1]))) end--;
        return s[..end];
    }
}
