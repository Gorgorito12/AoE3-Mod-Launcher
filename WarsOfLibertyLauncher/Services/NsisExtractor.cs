using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Raised when an installer could not be unpacked, with a reason worth showing.</summary>
public sealed class NsisExtractionException : Exception
{
    public NsisExtractionException(string message, bool declinedByUser = false) : base(message)
        => DeclinedByUser = declinedByUser;

    /// <summary>
    /// True when the player dismissed Windows' permission prompt. Separated from
    /// every other failure because it isn't one: telling someone "it failed"
    /// straight after they chose that it shouldn't happen reads as a bug.
    /// </summary>
    public bool DeclinedByUser { get; }
}

/// <summary>
/// Unpacks an NSIS self-extracting installer by running it in silent mode
/// against a scratch folder, so its payload can then be applied like any other
/// addon.
///
/// <b>Why running it is the safer option here, not the reckless one.</b> The
/// transparent-UI addon is distributed only as an NSIS installer whose payload
/// is 36 ordinary game files. Parsing NSIS in-process was rejected — no existing
/// dependency handles it and a hand-written parser for untrusted input is a poor
/// trade for one addon. That leaves two real choices, and the alternative to this
/// one is NOT "do nothing": it is telling the player to download the installer
/// and run it against their game folder themselves, with no backup, no manifest
/// entry, no way to revert, and no re-apply after an update.
///
/// Running it HERE inverts all of that. The installer only ever writes to a
/// throwaway folder — never the game — and the launcher then applies the result
/// through the normal path, so the files get backed up, recorded, reverted on
/// disable and restored after an update.
/// </summary>
public static class NsisExtractor
{
    /// <summary>
    /// Long enough for a large installer on a slow disk, short enough that a
    /// stuck one doesn't hang the launcher forever.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Builds the command line, kept pure because NSIS's rules here are unusual
    /// enough that getting them wrong fails SILENTLY — the installer runs, ignores
    /// the destination, and writes wherever it defaults to.
    ///
    /// <c>/D=</c> must be the LAST argument, must NOT be quoted, and must have no
    /// trailing separator. Unquoted is what lets it contain spaces at all: NSIS
    /// reads everything after <c>/D=</c> to the end of the command line, so a path
    /// like <c>C:\Users\Ana María\...</c> works unquoted and breaks if quoted.
    /// This is also why the caller must use <see cref="ProcessStartInfo.Arguments"/>
    /// rather than <c>ArgumentList</c>, which would add quotes of its own.
    /// </summary>
    public static string BuildArguments(string destinationDir)
    {
        var dir = (destinationDir ?? "").Trim();
        dir = dir.TrimEnd('\\', '/');
        return $"/S /D={dir}";
    }

    /// <summary>
    /// Runs <paramref name="installerPath"/> silently so it unpacks into
    /// <paramref name="destinationDir"/>, and returns the files it produced.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ExtractAsync(
        string installerPath, string destinationDir, CancellationToken ct = default)
    {
        if (!File.Exists(installerPath))
            throw new NsisExtractionException($"Installer not found: {installerPath}");

        // Never the game folder. The whole safety argument rests on the installer
        // writing somewhere disposable, so this is asserted rather than assumed.
        var full = Path.GetFullPath(destinationDir);
        if (!full.StartsWith(Path.GetFullPath(AppPaths.DataDir), StringComparison.OrdinalIgnoreCase))
            throw new NsisExtractionException(
                $"Refusing to run an installer outside the launcher's own data folder: {full}");

        Directory.CreateDirectory(full);

        // UseShellExecute is required, not a style choice. Some of these installers
        // declare requireAdministrator in their manifest, and with UseShellExecute
        // = false Windows refuses to start them outright ("The requested operation
        // requires elevation") because the launcher deliberately runs asInvoker.
        // ShellExecute is what lets Windows show its own consent prompt instead.
        // CreateNoWindow does not apply in this mode; WindowStyle does.
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = BuildArguments(full),   // raw: see BuildArguments
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = full,
        };

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — the user declined the elevation prompt. That is a
            // decision, not a failure, and reporting it as an error would tell them
            // something broke right after they chose that it shouldn't happen.
            throw new NsisExtractionException(
                $"You declined the permission prompt, so {Path.GetFileName(installerPath)} " +
                "was not run and nothing was changed.",
                declinedByUser: true);
        }

        using var process = started
            ?? throw new NsisExtractionException($"Could not start {Path.GetFileName(installerPath)}.");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // A custom installer page can ignore /S and sit waiting for a human.
            // Without this the launcher would wait with it, forever.
            TryKill(process);
            throw new NsisExtractionException(
                $"{Path.GetFileName(installerPath)} did not finish in silent mode — it may be " +
                "asking for input. Install it from its own page instead.");
        }

        var produced = Directory
            .EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(full, p))
            .ToList();

        if (produced.Count == 0)
            throw new NsisExtractionException(
                $"{Path.GetFileName(installerPath)} exited with code {process.ExitCode} but wrote " +
                "nothing — it probably ignored the destination.");

        DiagnosticLog.Write(
            $"NSIS: unpacked {Path.GetFileName(installerPath)} → {produced.Count} file(s), exit {process.ExitCode}.");
        return produced;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) { DiagnosticLog.Write($"NSIS: could not kill installer: {ex.Message}"); }
    }
}
