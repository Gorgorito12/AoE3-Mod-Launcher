using System;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Thrown by <see cref="NativeInstallService"/> when real-time antivirus
/// (Windows Defender) quarantines a payload file during the overlay copy —
/// <see cref="File.Copy(string,string,bool)"/> fails with HRESULT
/// <c>0x800700E1</c> (ERROR_VIRUS_INFECTED) or <c>0x800700E2</c>
/// (ERROR_VIRUS_DELETED). This is a KNOWN false positive on some WoL files
/// (e.g. <c>AI3\wolai.upl</c>); the source in <c>%TEMP%</c> is already gone, so
/// a plain retry re-fails. The install flow catches this specifically and shows
/// an actionable "add an exclusion" message instead of the raw IOException.
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
}
