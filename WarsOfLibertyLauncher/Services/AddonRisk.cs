using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>How dangerous an addon's file list is for updates and multiplayer.</summary>
public enum AddonRiskLevel
{
    /// <summary>No usable entries — nothing to apply.</summary>
    Empty,
    /// <summary>Only art / sound / UI. Safe to apply without a prompt.</summary>
    Cosmetic,
    /// <summary>
    /// Touches <c>data\</c> outside the three identity files. The lobby's
    /// fingerprint will NOT catch it, so the player joins normally and can then
    /// desync mid-match. Warned about, not blocked.
    /// </summary>
    SimulationRisk,
    /// <summary>
    /// Touches one of the three files the launcher's identity depends on.
    /// Refused outright.
    /// </summary>
    Blocked,
}

/// <summary>
/// Verdict for one addon, carrying the offending paths so the UI can name them
/// instead of asserting a vague danger.
/// </summary>
public sealed record AddonRiskAssessment(
    AddonRiskLevel Level,
    IReadOnlyList<string> BlockingFiles,
    IReadOnlyList<string> SimulationFiles,
    IReadOnlyList<string> ExecutableFiles);

/// <summary>
/// Decides whether an addon may be applied, from the file list of its ZIP.
///
/// This is the safety core of the addon feature, and the reasoning is worth
/// keeping in one place because three separate subsystems key off the SAME
/// three files (<see cref="UpdateService.ProtoRelativePath"/>,
/// <see cref="UpdateService.TechRelativePath"/>,
/// <see cref="UpdateService.StrRelativePath"/>):
///
///   * <b>version detection</b> — <c>UpdateService.DetectCurrentVersionAsync</c>
///     MD5s them to identify the installed version. One modified byte and the
///     install matches no known version, which makes the launcher queue the
///     ENTIRE patch chain instead of a real update;
///   * <b>the multiplayer gate</b> — <c>ModHashService.FingerprintAsync</c>
///     hashes them into the <c>CombinedHash</c> the lobby validates, so a
///     modified <c>protoy.xml</c> gets the player rejected from every room;
///   * <b>the translation system</b> — <c>stringtabley.xml</c> is snapshot into
///     <c>translations\_originals\</c>, and an addon writing it collides with
///     the canonical-English copy that both of the above read through.
///
/// Hence: those three are refused, everything else under <c>data\</c> is warned
/// about, and art/sound/UI applies freely.
///
/// The <b>SimulationRisk</b> tier exists because of an asymmetry that is easy to
/// miss: the fingerprint covers three files, not the whole simulation. An addon
/// editing some other <c>data\</c> file passes the lobby check and can still
/// desync the match — so the launcher can't detect the problem later and has to
/// say so up front.
///
/// Pure and WPF-free on purpose (like <c>SafeUrl</c> / <c>PathDisplay</c>) so it
/// is unit-testable off the STA thread — the rejection cases are the ones worth
/// pinning.
/// </summary>
public static class AddonRisk
{
    /// <summary>
    /// The files no addon may write. Sourced from <see cref="UpdateService"/>'s
    /// constants rather than re-typed, so the block list cannot drift from the
    /// paths the detection and fingerprint code actually reads.
    /// </summary>
    public static readonly IReadOnlyList<string> ProtectedFiles = new[]
    {
        UpdateService.ProtoRelativePath,
        UpdateService.TechRelativePath,
        UpdateService.StrRelativePath,
    };

    /// <summary>
    /// Extensions the launcher refuses to write into a game folder.
    ///
    /// Real addons ship these: the "building rotator" archive carries a
    /// UPX-packed PE32 alongside the config file that does the actual work. The
    /// launcher has no business silently placing a third-party binary — least of
    /// all a packed one, which is the exact heuristic that got this project's own
    /// executable quarantined by Defender. The addon still applies; the
    /// executable is skipped and reported.
    /// </summary>
    public static readonly IReadOnlyList<string> ExecutableExtensions = new[]
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".scr", ".ps1", ".vbs", ".cpl",
    };

    /// <summary>
    /// Author documentation. Belongs to the reader, not the game folder, so it is
    /// skipped for the same reason: nothing should land in an install that the
    /// game will never read.
    /// </summary>
    public static readonly IReadOnlyList<string> DocumentExtensions = new[]
    {
        ".pdf", ".txt", ".doc", ".docx", ".rtf", ".png", ".jpg", ".jpeg", ".gif", ".url", ".html",
    };

    public static AddonRiskAssessment Assess(IEnumerable<string>? entries)
    {
        var blocking = new List<string>();
        var simulation = new List<string>();
        var executables = new List<string>();
        bool anyFile = false;

        foreach (var raw in entries ?? Enumerable.Empty<string>())
        {
            var norm = Normalize(raw);
            if (norm.Length == 0) continue;

            // Directory entries carry no content — a zip lists them separately.
            if (norm.EndsWith('\\')) continue;

            anyFile = true;

            if (IsExecutable(norm)) { executables.Add(norm); continue; }
            if (IsProtected(norm)) blocking.Add(norm);
            else if (IsUnderData(norm)) simulation.Add(norm);
        }

        var level =
            blocking.Count > 0 ? AddonRiskLevel.Blocked
            : !anyFile ? AddonRiskLevel.Empty
            : simulation.Count > 0 ? AddonRiskLevel.SimulationRisk
            : AddonRiskLevel.Cosmetic;

        return new AddonRiskAssessment(level, blocking, simulation, executables);
    }

    /// <summary>True for a file the launcher will never copy into an install.</summary>
    public static bool IsExecutable(string path) => HasExtension(path, ExecutableExtensions);

    /// <summary>True for author documentation, skipped like executables.</summary>
    public static bool IsDocument(string path) => HasExtension(path, DocumentExtensions);

    /// <summary>True when a zip entry should not be written into the game folder at all.</summary>
    public static bool IsSkippable(string path) => IsExecutable(path) || IsDocument(path);

    private static bool HasExtension(string path, IReadOnlyList<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path.Trim());
        return !string.IsNullOrEmpty(ext)
            && extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes a zip entry to a backslash path for comparison. Zips store
    /// forward slashes regardless of platform, and casing is whatever the
    /// packager's filesystem produced — neither is a signal.
    /// </summary>
    private static string Normalize(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return "";
        var s = entry.Trim().Replace('/', '\\');
        while (s.StartsWith(".\\", StringComparison.Ordinal)) s = s[2..];
        return s.TrimStart('\\');
    }

    /// <summary>
    /// Matches on the path TAIL rather than the full path, so a protected file
    /// is caught no matter how many wrapper folders the packager nested it
    /// under (<c>MyAddon\data\protoy.xml</c> is the normal shape, not the
    /// exception). Over-blocking is the correct direction to fail here: the cost
    /// of a false positive is one addon someone has to apply by hand, and the
    /// cost of a false negative is a player silently locked out of every lobby.
    /// </summary>
    private static bool IsProtected(string norm) =>
        ProtectedFiles.Any(p =>
            norm.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            norm.EndsWith('\\' + p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Same wrapper-tolerant matching, for the whole <c>data\</c> tree.</summary>
    private static bool IsUnderData(string norm) =>
        norm.StartsWith(@"data\", StringComparison.OrdinalIgnoreCase) ||
        norm.Contains(@"\data\", StringComparison.OrdinalIgnoreCase);
}
