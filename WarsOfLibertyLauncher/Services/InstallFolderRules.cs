using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>What is wrong with a destination folder, as <see cref="InstallFolderRules"/> sees it.</summary>
internal enum InstallPathProblemKind
{
    Empty,
    NotFullyQualified,
    UnsupportedForm,
    DriveRoot,
    DriveMissing,
    InvalidCharacter,
    ReservedName,
    TrailingDotOrSpace,
    SegmentTooLong,
    SystemFolder,
    FileInTheWay,
}

/// <summary>
/// One refusal. <see cref="Segment"/> is the folder name at fault (when there is one) and
/// <see cref="Detail"/> what to name in the message: the refused character, the drive, the file
/// in the way.
/// </summary>
internal sealed record InstallPathProblem(InstallPathProblemKind Kind, string Segment = "", string Detail = "");

/// <summary>The outcome of <see cref="InstallFolderRules.Validate"/>: the canonical full path, or why not.</summary>
internal sealed record InstallPathCheck(string? FullPath, InstallPathProblem? Problem);

/// <summary>
/// Decides, BEFORE anything is downloaded, whether a destination folder can exist at all.
///
/// <para>The install dialog used to rely on <c>Path.GetFullPath</c> throwing, which .NET Core
/// never does for <c>: ? * &lt; &gt; | "</c> — so a destination named after
/// "Age of Empires 1: HD Edition" passed, the payload downloaded and extracted, and only then did
/// <c>Directory.CreateDirectory</c> refuse it. A hand-typed <c>Age-of-Empires-1:-HD-Edition</c>
/// failed the same way, after another full download.</para>
///
/// <para><b>Segments are checked RAW, before <c>GetFullPath</c>,</b> because Win32 normalisation
/// removes one trailing dot from a middle folder (<c>C:\Games.\Mod</c> comes back as
/// <c>C:\Games\Mod</c>) and every trailing dot and space at the end of the path, while it keeps a
/// middle folder's trailing space — either way the folder on disk would not be the one the player
/// typed.</para>
///
/// <para>Pure apart from <see cref="FindFileInTheWay"/> and <see cref="NearestExistingAncestor"/>,
/// which only read. Nothing here creates anything. The drive list is passed in
/// (<see cref="CurrentDriveRoots"/> is a bitmask read, no disk I/O), so the dialog can call
/// <see cref="Validate"/> on every keystroke and the tests can say which drives exist.</para>
/// </summary>
internal static class InstallFolderRules
{
    /// <summary>
    /// The path as the player meant it: trimmed, one pair of surrounding quotes removed
    /// (Explorer's "Copy as path" adds them), <c>/</c> turned into <c>\</c>, and a trailing
    /// separator dropped — except a root's, so a typed <c>D:\</c> never becomes the
    /// drive-RELATIVE <c>D:</c>, which resolves against the process's current folder.
    /// </summary>
    internal static string Normalize(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1].Trim();
        s = s.Replace('/', '\\');
        return s.Length == 0 ? s : Path.TrimEndingDirectorySeparator(s);
    }

    /// <summary>
    /// Whether <paramref name="text"/> can be an install folder, first problem first. On success
    /// <see cref="InstallPathCheck.FullPath"/> is the canonical full path to install into.
    /// </summary>
    /// <param name="existingDriveRoots">The drives on this PC (<see cref="CurrentDriveRoots"/>);
    /// null skips the check.</param>
    /// <param name="systemFolders">Folders an install may never go into; null means Windows and
    /// <c>WindowsApps</c>.</param>
    /// <remarks>A drive root is refused for EVERY install, an in-place overlay into a game that
    /// sits at the root of a drive included: Uninstall refuses a drive root outright, and the
    /// manifest cannot record a folder whose subfolders it cannot list, so such an install could
    /// be made and never removed. The overlay case gets its own message
    /// (<c>InstallFolderProblemText</c>).</remarks>
    internal static InstallPathCheck Validate(string? text,
        IReadOnlyCollection<string>? existingDriveRoots = null,
        IReadOnlyCollection<string>? systemFolders = null)
    {
        var n = Normalize(text);
        if (n.Length == 0) return Fail(InstallPathProblemKind.Empty);

        // Device paths, and a UNC path with a separator too many in front: GetFullPath keeps
        // "\\\server\share" as typed, and Windows cannot open it.
        if (n.StartsWith(@"\\?\", StringComparison.Ordinal)
            || n.StartsWith(@"\\.\", StringComparison.Ordinal)
            || n.StartsWith(@"\??\", StringComparison.Ordinal)
            || n.StartsWith(@"\\\", StringComparison.Ordinal))
            return Fail(InstallPathProblemKind.UnsupportedForm, n);

        // C:, C:Games, Games\Mod and \Games all resolve against something the player cannot see.
        if (!Path.IsPathFullyQualified(n)) return Fail(InstallPathProblemKind.NotFullyQualified, n);

        // The root's length in the text AS TYPED: the string overload of GetPathRoot collapses
        // doubled separators, and slicing with its length would skip part of the first folder.
        int rootLength;
        try { rootLength = Path.GetPathRoot(n.AsSpan()).Length; }
        catch { return Fail(InstallPathProblemKind.UnsupportedForm, n); }

        // A UNC root is a server and a share, and neither is a name Windows lets hold a colon or
        // a question mark ("\\server\C$:x" passed before and failed on the first disk access).
        if (n.StartsWith(@"\\", StringComparison.Ordinal))
        {
            foreach (var part in n[2..rootLength].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            foreach (var c in part)
            {
                if (SafeFolderName.IsInvalidChar(c))
                    return new InstallPathCheck(null, new InstallPathProblem(
                        InstallPathProblemKind.InvalidCharacter, part, SafeFolderName.DescribeChar(c)));
            }
        }

        foreach (var segment in n[rootLength..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") continue;
            var problem = SafeFolderName.CheckSegment(segment);
            if (problem != null) return new InstallPathCheck(null, problem);
        }

        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(n)); }
        catch { return Fail(InstallPathProblemKind.UnsupportedForm, n); }

        var fullRoot = Path.GetPathRoot(full) ?? "";
        if (string.Equals(full.TrimEnd('\\'), fullRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return Fail(InstallPathProblemKind.DriveRoot, n, fullRoot);

        foreach (var system in systemFolders ?? DefaultSystemFolders())
        {
            if (IsSameOrUnder(full, system))
                return Fail(InstallPathProblemKind.SystemFolder, n, system);
        }

        if (existingDriveRoots != null && IsDriveLetterPath(full)
            && !existingDriveRoots.Any(r => IsDriveLetterPath(r)
                                            && char.ToUpperInvariant(r[0]) == char.ToUpperInvariant(full[0])))
            return Fail(InstallPathProblemKind.DriveMissing, n, char.ToUpperInvariant(full[0]) + ":");

        return new InstallPathCheck(full, null);
    }

    /// <summary>The drives on this PC, or null when they cannot be listed (then the check is skipped).</summary>
    internal static IReadOnlyCollection<string>? CurrentDriveRoots()
    {
        try { return Environment.GetLogicalDrives(); }
        catch { return null; }
    }

    /// <summary>
    /// A FILE standing where the destination, or one of the folders above it, would have to be —
    /// <c>Directory.CreateDirectory</c> fails there, and the pure rules cannot see it. Read-only:
    /// it walks up to the first folder that exists and stops. UNC paths (<c>\\server\share</c>)
    /// are skipped, since an unreachable share can hold this up for seconds; a mapped drive
    /// letter is walked like any other drive.
    /// </summary>
    internal static string? FindFileInTheWay(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        try
        {
            var p = Path.TrimEndingDirectorySeparator(fullPath);
            while (!string.IsNullOrEmpty(p))
            {
                if (Directory.Exists(p)) return null;
                if (File.Exists(p)) return p;
                var parent = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, p, StringComparison.OrdinalIgnoreCase))
                    return null;
                p = parent;
            }
        }
        catch { /* unreadable: let the install report its own error */ }
        return null;
    }

    /// <summary>
    /// The closest folder that already exists at or above <paramref name="fullPath"/>, or null
    /// (a drive that is not there). This is the folder whose permissions decide whether the
    /// install can create its own — not the immediate parent, which may not exist yet.
    /// </summary>
    internal static string? NearestExistingAncestor(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;
        try
        {
            var p = Path.TrimEndingDirectorySeparator(fullPath);
            while (!string.IsNullOrEmpty(p))
            {
                if (Directory.Exists(p)) return p;
                var parent = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, p, StringComparison.OrdinalIgnoreCase))
                    return null;
                p = parent;
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private static InstallPathCheck Fail(InstallPathProblemKind kind, string segment = "", string detail = "")
        => new(null, new InstallPathProblem(kind, segment, detail));

    private static bool IsDriveLetterPath(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]);

    private static bool IsSameOrUnder(string full, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        var f = Path.TrimEndingDirectorySeparator(folder.Trim());
        return string.Equals(full, f, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DefaultSystemFolders()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows)) yield return windows;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, "WindowsApps");
    }
}
