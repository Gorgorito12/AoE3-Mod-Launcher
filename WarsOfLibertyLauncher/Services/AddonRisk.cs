using System;
using System.Collections.Generic;
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
    IReadOnlyList<string> SimulationFiles);

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

    public static AddonRiskAssessment Assess(IEnumerable<string>? entries)
    {
        var blocking = new List<string>();
        var simulation = new List<string>();
        bool anyFile = false;

        foreach (var raw in entries ?? Enumerable.Empty<string>())
        {
            var norm = Normalize(raw);
            if (norm.Length == 0) continue;

            // Directory entries carry no content — a zip lists them separately.
            if (norm.EndsWith('\\')) continue;

            anyFile = true;

            if (IsProtected(norm)) blocking.Add(norm);
            else if (IsUnderData(norm)) simulation.Add(norm);
        }

        var level =
            blocking.Count > 0 ? AddonRiskLevel.Blocked
            : !anyFile ? AddonRiskLevel.Empty
            : simulation.Count > 0 ? AddonRiskLevel.SimulationRisk
            : AddonRiskLevel.Cosmetic;

        return new AddonRiskAssessment(level, blocking, simulation);
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
