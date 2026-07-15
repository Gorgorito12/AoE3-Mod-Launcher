using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Thrown by <see cref="NativeInstallService"/> when a payload file is lost to
/// real-time antivirus (Windows Defender) during an install. A KNOWN false
/// positive on some WoL files (e.g. <c>AI3\wolai.upl</c>). The install flow
/// catches this specifically and shows an actionable "add an exclusion" message
/// instead of a raw IOException — or, worse, no message at all.
///
/// Raised for the three ways the loss shows up:
///
///   • The AV fails the write/read outright — <see cref="File.Copy(string,string,bool)"/>
///     or extraction throws HRESULT <c>0x800700E1</c> (ERROR_VIRUS_INFECTED) /
///     <c>0x800700E2</c> (ERROR_VIRUS_DELETED). Use the ctor taking an inner.
///   • The file was written fine and quarantined a moment LATER, so it is simply
///     GONE when we look again. No exception exists to wrap — use the ctor
///     without an inner. This is the silent case the guards exist for: the
///     payload extract sits in %TEMP% for MINUTES while the AoE3 clone runs,
///     giving the AV a wide window.
///
/// A retry does not help (the %TEMP% source is already quarantined), which is
/// why the flow aborts with guidance rather than looping.
/// </summary>
public sealed class PayloadFileBlockedException : Exception
{
    /// <summary>The install-relative path of the file the AV blocked.</summary>
    public string BlockedFile { get; }

    public PayloadFileBlockedException(string blockedFile, Exception inner)
        : base($"A payload file was blocked by antivirus during install: '{blockedFile}'.", inner)
    {
        BlockedFile = blockedFile;
    }

    /// <summary>
    /// The file vanished after a successful write — nothing threw, so there is no
    /// inner exception to carry.
    /// </summary>
    public PayloadFileBlockedException(string blockedFile)
        : base($"A payload file disappeared after extraction (antivirus quarantine): '{blockedFile}'.")
    {
        BlockedFile = blockedFile;
    }
}
