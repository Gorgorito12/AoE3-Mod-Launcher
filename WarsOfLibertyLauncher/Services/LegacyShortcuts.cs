using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Cleans up the shortcuts an older build wrote as NTFS STREAMS.
///
/// <para>Shortcuts used to be named after the display name as written. For
/// "Age of Empires 1: HD Edition" the Desktop shortcut became
/// <c>Desktop\Age of Empires 1: HD Edition.lnk</c>, which NTFS reads as a stream named
/// " HD Edition.lnk" on a file named "Age of Empires 1". No error: the launcher recorded the path
/// in the install manifest, the player found an empty file on the Desktop and no shortcut, and
/// uninstalling removed only the stream (<c>File.Delete</c> on a stream path deletes the stream),
/// leaving the empty file behind for ever.</para>
///
/// <para><b>Deliberately narrow</b> (the maintainer's call): only a path the launcher itself
/// RECORDED, only a <c>.lnk</c> whose file name contains a colon, and the base file only when it
/// is still empty (0 bytes). Anything that does not match is left alone, so a file the player
/// owns can never be removed on the strength of an on-disk manifest.</para>
///
/// <para>Every operation uses the <c>\\?\</c> form. Without it Win32 would strip a trailing space
/// from the base name: for "Foo : Bar.lnk" the base is "Foo " (with the space), and the checks
/// and the delete would land on a DIFFERENT file, the neighbouring "Foo".</para>
/// </summary>
internal static class LegacyShortcuts
{
    /// <summary>
    /// True for a recorded shortcut path that names a stream: fully qualified, a <c>.lnk</c>, and
    /// a colon inside its file name. (.NET's <c>Path.GetFileName</c> keeps <c>file:stream</c>
    /// whole, which is what makes the file-name part visible here.)
    /// </summary>
    internal static bool IsStreamPath(string? recorded)
    {
        if (string.IsNullOrWhiteSpace(recorded)) return false;
        try
        {
            if (!Path.IsPathFullyQualified(recorded)) return false;
            var name = Path.GetFileName(recorded);
            return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && name.IndexOf(':') > 0;
        }
        catch { return false; }
    }

    /// <summary>The file the stream hangs off: the file name up to the colon.</summary>
    internal static string? BaseFileOf(string streamPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(streamPath);
            var name = Path.GetFileName(streamPath);
            var colon = name.IndexOf(':');
            if (string.IsNullOrEmpty(dir) || colon <= 0) return null;
            return Path.Combine(dir, name[..colon]);
        }
        catch { return null; }
    }

    /// <summary>
    /// Removes the stream, then its base file only if that is an existing file of length 0.
    /// False (with the reason) only when the stream itself could not be removed; a base file
    /// that stays is logged, never an error. A folder that is gone altogether (a moved Desktop, a
    /// removed drive) leaves nothing to remove, so it counts as removed.
    /// </summary>
    internal static bool TryRemove(string streamPath, out string? error)
    {
        error = null;
        try
        {
            File.Delete(Extended(streamPath));
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        try
        {
            var baseFile = BaseFileOf(streamPath);
            if (baseFile != null)
            {
                var extended = Extended(baseFile);
                var info = new FileInfo(extended);
                if (info.Exists && info.Length == 0)
                    File.Delete(extended);
                else if (info.Exists)
                    DiagnosticLog.Write($"Legacy shortcut: kept '{baseFile}' ({info.Length} bytes — not ours to delete).");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Legacy shortcut: could not remove the empty file behind '{streamPath}': {ex.Message}");
        }
        return true;
    }

    /// <summary>
    /// <see cref="TryRemove"/> for every recorded path that is a stream; everything else is
    /// ignored. Logs each one and never throws. Returns the stream paths that are NOT cleaned up
    /// yet — the stream could not be removed, or its empty base file is still there — for the
    /// caller to keep recording, so the next repair or the uninstall tries again. Retrying is
    /// safe: deleting a stream that is already gone is not an error.
    /// </summary>
    internal static IReadOnlyList<string> RemoveRecorded(IEnumerable<string>? recorded)
    {
        var stillThere = new List<string>();
        if (recorded == null) return stillThere;
        foreach (var path in recorded.ToList())
        {
            if (!IsStreamPath(path)) continue;
            var removed = TryRemove(path, out var error);
            if (removed)
                DiagnosticLog.Write($"Legacy shortcut: removed the stream shortcut '{path}'.");
            else
                DiagnosticLog.Write($"Legacy shortcut: could not remove '{path}': {error}");
            // A failed removal stays recorded even when nothing can be SEEN there: a stream on
            // another Windows account's Desktop answers "access denied" to every check, and only
            // that account can ever clean it.
            if (!removed || IsStillThere(path)) stillThere.Add(path);
        }
        return stillThere;
    }

    /// <summary>
    /// The stream still exists, or its base file is still there and empty — as far as THIS
    /// account can see: a folder it may not read answers "not there", exactly as
    /// <c>File.Exists</c> does for an ordinary shortcut.
    /// </summary>
    internal static bool IsStillThere(string streamPath)
    {
        try
        {
            if (File.Exists(Extended(streamPath))) return true;
            var baseFile = BaseFileOf(streamPath);
            if (baseFile == null) return false;
            var info = new FileInfo(Extended(baseFile));
            return info.Exists && info.Length == 0;
        }
        catch { return false; }
    }

    /// <summary>The <c>\\?\</c> form of a full path, so Win32 hands it to NTFS untouched.</summary>
    private static string Extended(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) return p;
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + p[2..];
        return @"\\?\" + p;
    }
}
