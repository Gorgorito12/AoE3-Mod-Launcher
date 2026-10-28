using System;
using System.IO;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Localization;

/// <summary>
/// The sentence the player reads for an <see cref="InstallPathProblem"/>. Kept apart from
/// <see cref="InstallFolderRules"/> so the rules stay free of the string table, and split into a
/// pure <see cref="KeyAndArgs"/> so tests can check every kind in both languages without racing
/// the current language.
/// </summary>
internal static class InstallFolderProblemText
{
    /// <summary>The string key for a problem and the arguments it takes.</summary>
    /// <param name="modFolderName">The mod's clean folder name, used to build an example path.</param>
    /// <param name="inPlaceOverlay">The mod installs INTO the player's game: a drive root is then
    /// where the game sits, not a folder the player picked, and the message says so.</param>
    internal static (string Key, object[] Args) KeyAndArgs(InstallPathProblem problem, string modFolderName,
        bool inPlaceOverlay = false) =>
        problem.Kind switch
        {
            InstallPathProblemKind.Empty => ("WarnPathEmpty", Array.Empty<object>()),
            InstallPathProblemKind.NotFullyQualified =>
                ("WarnPathNotFullPath", new object[] { Path.Combine(@"D:\", modFolderName) }),
            InstallPathProblemKind.UnsupportedForm => ("WarnPathInvalid", Array.Empty<object>()),
            InstallPathProblemKind.DriveRoot when inPlaceOverlay =>
                ("WarnPathDriveRootOverlay", new object[] { problem.Detail }),
            InstallPathProblemKind.DriveRoot => ("WarnPathDriveRoot", new object[] { problem.Detail }),
            InstallPathProblemKind.DriveMissing => ("WarnPathDriveMissing", new object[] { problem.Detail }),
            InstallPathProblemKind.InvalidCharacter =>
                ("WarnPathBadChar", new object[] { problem.Detail, problem.Segment }),
            InstallPathProblemKind.ReservedName => ("WarnPathReservedName", new object[] { problem.Segment }),
            InstallPathProblemKind.TrailingDotOrSpace =>
                ("WarnPathTrailingDotOrSpace", new object[] { problem.Segment }),
            InstallPathProblemKind.SegmentTooLong =>
                ("WarnPathNameTooLong", new object[] { Shorten(problem.Segment), SafeFolderName.MaxLength }),
            InstallPathProblemKind.SystemFolder => ("WarnPathSystem", Array.Empty<object>()),
            InstallPathProblemKind.FileInTheWay => ("WarnPathFileInTheWay", new object[] { problem.Detail }),
            _ => ("WarnPathInvalid", Array.Empty<object>()),
        };

    /// <summary>The sentence, in the launcher's current language.</summary>
    internal static string Describe(InstallPathProblem problem, string modFolderName, bool inPlaceOverlay = false)
    {
        var (key, args) = KeyAndArgs(problem, modFolderName, inPlaceOverlay);
        return args.Length == 0 ? Strings.Get(key) : Strings.Format(key, args);
    }

    /// <summary>A name too long to print whole: its first 40 characters, one fewer when the 40th
    /// is the first half of a character (an emoji), which would print as garbage on its own.</summary>
    internal static string Shorten(string segment)
    {
        if (segment.Length <= 40) return segment;
        var n = char.IsHighSurrogate(segment[39]) ? 39 : 40;
        return segment[..n] + "…";
    }
}
