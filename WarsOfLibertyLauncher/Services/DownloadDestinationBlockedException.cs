namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// A download finished (or never started) because the file it must be written to could not be
/// replaced — a read-only attribute, an antivirus holding a handle, Windows' Controlled Folder
/// Access on Desktop/Documents, or a destination that is some process's running image.
///
/// <para>The sibling of <see cref="PayloadFileBlockedException"/>, and it exists for the same
/// reason: without it this surfaces as a bare <see cref="UnauthorizedAccessException"/> whose
/// English .NET message ("Access to the path 'X' is denied.") is shown verbatim to the user by
/// <c>LauncherUpdateDialog</c>'s generic catch, with nothing to distinguish it from a dropped
/// connection or a full disk.</para>
///
/// <para><b>Always pass the inner exception.</b> That is a contract, not a nicety:
/// <see cref="DownloadService.IsTransientDownloadFailure"/> walks the whole inner-exception chain
/// for <see cref="UnauthorizedAccessException"/> in a dedicated first pass, and that walk is what
/// keeps this failure fast. Construct one bare and a permission error silently becomes
/// "transient" again — four full re-downloads of a ~178 MB binary, or of a multi-GB mod payload,
/// to reach the same conclusion. Pinned by <c>DownloadRetryTests</c>.</para>
/// </summary>
public sealed class DownloadDestinationBlockedException : Exception
{
    /// <summary>The file that could not be written or replaced.</summary>
    public string DestinationPath { get; }

    public DownloadDestinationBlockedException(string destinationPath, Exception inner)
        : base($"The download destination could not be replaced: '{destinationPath}'.", inner)
    {
        DestinationPath = destinationPath;
    }
}
