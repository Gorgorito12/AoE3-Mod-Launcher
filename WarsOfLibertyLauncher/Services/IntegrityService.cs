using System.IO;
using System.Threading;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>What one integrity finding is about. The order is also the dedupe priority.</summary>
internal enum IntegrityKind
{
    /// <summary>A mod (overlay) file is gone.</summary>
    Missing,
    /// <summary>A mod file's size or hash is wrong.</summary>
    Damaged,
    /// <summary>Another process holds the file — not damage, and nothing a re-lay could fix.</summary>
    Unreadable,
    /// <summary>An antivirus refused the read.</summary>
    Blocked,
    /// <summary>A file of the base engine the install was cloned from (not in the mod payload).</summary>
    Engine,
    /// <summary>A folder-shape or required-file check of the profile failed.</summary>
    Structural,
    /// <summary>
    /// A base-game file the install pipeline removes at every overlay, still on disk from before
    /// that rule (see <see cref="Repair.LeftoverCleanup"/>). Fixed without a download.
    /// </summary>
    Leftover,
}

internal sealed record IntegrityFinding(IntegrityKind Kind, string Path, string Detail = "");

/// <summary>
/// The ONE verdict on an install, shared by "Verify files" and Repair.
///
/// <para><b>Why it exists.</b> The two used to run different checks: Verify hashed the overlay AND
/// the engine map, Repair hashed the overlay only — so Verify could report a damaged engine DLL,
/// offer Repair, and Repair answer "nothing to repair". A file held open by the game or an
/// antivirus scan counted as corrupt in both, which cost a multi-GB re-download that then failed on
/// that same file.</para>
/// </summary>
internal sealed record IntegrityReport(
    IReadOnlyList<IntegrityFinding> Findings, bool HashesAvailable, int FilesChecked)
{
    public bool IsHealthy => Findings.Count == 0;

    public int CountOf(params IntegrityKind[] kinds) => Findings.Count(f => kinds.Contains(f.Kind));

    public IReadOnlyList<string> PathsOf(params IntegrityKind[] kinds)
        => Findings.Where(f => kinds.Contains(f.Kind)).Select(f => f.Path).ToList();
}

/// <summary>What Repair should do about a report. See <see cref="IntegrityService.Route"/>.</summary>
internal enum RepairRoute
{
    /// <summary>Nothing to fix.</summary>
    Nothing,
    /// <summary>Re-lay the mod's files.</summary>
    Relay,
    /// <summary>Files are locked: re-laying would fail on them. Tell the player to close the program.</summary>
    InUse,
    /// <summary>An antivirus blocks the files: re-downloading gets blocked again.</summary>
    Antivirus,
    /// <summary>Only the base engine is damaged, which the mod payload does not contain.</summary>
    EngineOnly,
    /// <summary>Only leftovers remain, and they could not be removed (held open).</summary>
    Cleanup,
}

/// <param name="HashPass">
/// False: structural checks + the legacy zero-byte spot check only (the fast post-install check).
/// </param>
/// <param name="TranslationActive">
/// True when a translation is applied: covered files are then compared through the canonical
/// <c>_originals</c> snapshot. With no translation the LIVE file must equal the recorded hash, so a
/// corrupted string table is caught instead of hidden behind a healthy snapshot.
/// </param>
/// <param name="HashOnly">
/// With <see cref="HashPass"/>: every recorded file gets existence + size, only these are hashed —
/// the recheck after a re-lay.
/// </param>
internal sealed record DiagnoseOptions(
    bool HashPass = true,
    bool TranslationActive = false,
    IReadOnlyCollection<string>? HashOnly = null);

internal static class IntegrityService
{
    /// <summary>
    /// Read-only: never writes the install, the manifest, the snapshot or the registry. Keyed on
    /// profile data only (install type, update mechanism, structural checks), never a mod id.
    /// </summary>
    internal static IntegrityReport Diagnose(
        string installPath,
        ModProfile profile,
        DiagnoseOptions options,
        IProgress<VerifyService.VerifyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var findings = new List<IntegrityFinding>();
        int filesChecked = CheckStructure(installPath, profile, findings);

        var manifest = InstallManifest.TryLoad(installPath);
        // Another mod's hashes say nothing about this one: treat it like a manifest-less install.
        bool hashes = manifest != null
                      && InstallIdentity.BelongsTo(manifest, profile)
                      && VerifyService.HasFileHashes(manifest);

        if (options.HashPass && hashes)
        {
            var covered = options.TranslationActive ? profile.Translations?.CoveredFiles : null;

            var overlay = VerifyService.InspectOverlay(
                installPath, manifest!.FileHashes, covered, options.HashOnly, progress, ct);
            filesChecked += overlay.FilesChecked;
            foreach (var (path, problem) in overlay.Problems)
                findings.Add(new IntegrityFinding(OverlayKind(problem), path, Describe(problem)));

            var engine = VerifyService.InspectEngine(installPath, manifest, covered, ct);
            filesChecked += engine.FilesChecked;
            foreach (var (path, problem) in engine.Problems)
            {
                var kind = problem switch
                {
                    VerifyService.FileProblem.Unreadable => IntegrityKind.Unreadable,
                    VerifyService.FileProblem.Blocked => IntegrityKind.Blocked,
                    _ => IntegrityKind.Engine,
                };
                findings.Add(new IntegrityFinding(kind, path, Describe(problem)));
            }

            foreach (var rel in Leftovers(installPath, profile, manifest!))
                findings.Add(new IntegrityFinding(IntegrityKind.Leftover, rel,
                    "base-game file the mod's install removes"));

            if (options.HashOnly == null) LogUnexpectedFiles(installPath, manifest);
        }
        else
        {
            filesChecked += SpotCheckZeroByte(installPath, findings);
        }

        return new IntegrityReport(Dedupe(findings), hashes, filesChecked);
    }

    /// <summary>
    /// What Repair does with a report. An antivirus block or a locked file wins over everything:
    /// the re-lay would fail on exactly those files after downloading the whole payload.
    /// </summary>
    internal static RepairRoute Route(IntegrityReport report)
    {
        if (report.CountOf(IntegrityKind.Blocked) > 0) return RepairRoute.Antivirus;
        if (report.CountOf(IntegrityKind.Unreadable) > 0) return RepairRoute.InUse;
        if (report.CountOf(IntegrityKind.Missing, IntegrityKind.Damaged, IntegrityKind.Structural) > 0)
            return RepairRoute.Relay;
        if (report.CountOf(IntegrityKind.Engine) > 0) return RepairRoute.EngineOnly;
        if (report.CountOf(IntegrityKind.Leftover) > 0) return RepairRoute.Cleanup;
        return RepairRoute.Nothing;
    }

    /// <summary>
    /// The files the recheck after a re-lay hashes: what was damaged before, plus the probe and the
    /// three version-key files. Everything else is checked for existence and size only.
    /// </summary>
    internal static IReadOnlyCollection<string> RecheckHashSet(IntegrityReport? before, ModProfile profile)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (before != null)
            foreach (var f in before.Findings) set.Add(Normalize(f.Path));
        foreach (var key in VerifyService.EngineCandidates.Take(3)) set.Add(key);
        if (!string.IsNullOrWhiteSpace(profile.InstallProbeFile)) set.Add(Normalize(profile.InstallProbeFile));
        return set;
    }

    /// <summary>One report line, for the log and the status line. Tags stay English, as before.</summary>
    internal static string FormatLine(IntegrityFinding f)
    {
        var tag = f.Kind switch
        {
            IntegrityKind.Missing => "[missing]",
            IntegrityKind.Damaged => "[corrupt]",
            IntegrityKind.Unreadable => "[in use]",
            IntegrityKind.Blocked => "[antivirus]",
            IntegrityKind.Engine => "[engine]",
            IntegrityKind.Leftover => "[leftover]",
            _ => "[check]",
        };
        return string.IsNullOrEmpty(f.Detail) || f.Kind != IntegrityKind.Structural
            ? $"{tag} {f.Path}"
            : $"{tag} {f.Path} ({f.Detail})";
    }

    /// <summary>
    /// The legacy <see cref="VerifyService.VerifyResult"/> shape, for the post-install check that
    /// still speaks it. Structural failures read as missing, engine damage carries its suffix.
    /// </summary>
    internal static VerifyService.VerifyResult ToLegacy(IntegrityReport report, string engineSuffix)
    {
        var missing = new List<string>();
        var corrupt = new List<string>();
        foreach (var f in report.Findings)
        {
            switch (f.Kind)
            {
                case IntegrityKind.Missing: missing.Add(f.Path); break;
                case IntegrityKind.Structural:
                    missing.Add(string.IsNullOrEmpty(f.Detail) ? f.Path : $"{f.Path} ({f.Detail})"); break;
                case IntegrityKind.Engine: corrupt.Add(f.Path + engineSuffix); break;
                default: corrupt.Add(f.Path); break;
            }
        }
        return new VerifyService.VerifyResult(missing, corrupt, report.FilesChecked);
    }

    // ------------------------------------------------------------------------

    private static int CheckStructure(string installPath, ModProfile profile, List<IntegrityFinding> findings)
    {
        int checkedCount = 0;

        if (!string.IsNullOrWhiteSpace(profile.InstallProbeFile))
        {
            checkedCount++;
            if (!File.Exists(Path.Combine(installPath, profile.InstallProbeFile)))
                findings.Add(new IntegrityFinding(IntegrityKind.Structural,
                    Normalize(profile.InstallProbeFile), "the mod's probe file"));
        }

        // Every native install (cloned AoE3 + overlay) has the three version-key data files; a
        // PARTIAL clone that slipped past the 0-file gate lacks them, and the game exits on launch.
        if (profile.UpdateMechanism is ModUpdateMechanism.WolPatcher or ModUpdateMechanism.GitHubReleases)
        {
            foreach (var rel in VerifyService.EngineCandidates.Take(3))
            {
                checkedCount++;
                if (!File.Exists(Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar))))
                    findings.Add(new IntegrityFinding(IntegrityKind.Structural, rel,
                        "AoE3 base file — the game can't launch without it"));
            }
        }

        foreach (var check in profile.StructuralChecks)
        {
            var folder = Normalize(check.Folder).TrimEnd('/');
            var dir = Path.Combine(installPath, folder.Replace('/', Path.DirectorySeparatorChar));
            checkedCount++;
            if (!Directory.Exists(dir))
            {
                findings.Add(new IntegrityFinding(IntegrityKind.Structural, folder + "/", "folder missing"));
                continue;
            }
            if (check.MinFiles <= 0 && check.MinBytesEach <= 0) continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, string.IsNullOrEmpty(check.Pattern) ? "*" : check.Pattern,
                    check.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"Structural check could not list '{folder}': {ex.Message}");
                continue;
            }
            checkedCount += files.Length;

            if (files.Length < check.MinFiles)
                findings.Add(new IntegrityFinding(IntegrityKind.Structural, $"{folder}/{check.Pattern}",
                    $"found {files.Length}, expected at least {check.MinFiles}"));

            if (check.MinBytesEach > 0)
            {
                foreach (var file in files)
                {
                    long len;
                    try { len = new FileInfo(file).Length; } catch { continue; }
                    if (len < check.MinBytesEach)
                        findings.Add(new IntegrityFinding(IntegrityKind.Structural,
                            Path.GetRelativePath(installPath, file).Replace('\\', '/'),
                            $"only {len} bytes"));
                }
            }
        }
        return checkedCount;
    }

    /// <summary>
    /// The legacy check for installs with no per-file hashes: a sample of content files at zero
    /// bytes is almost always a broken download or extraction.
    /// </summary>
    private static int SpotCheckZeroByte(string installPath, List<IntegrityFinding> findings)
    {
        try
        {
            var allFiles = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
            var sample = allFiles.Length > 200
                ? allFiles.OrderBy(_ => Guid.NewGuid()).Take(200)
                : allFiles.AsEnumerable();
            foreach (var file in sample)
            {
                var info = new FileInfo(file);
                if (info.Length == 0 && !info.Name.StartsWith(".")
                    && info.Extension is ".bar" or ".xml" or ".xmb" or ".dll" or ".exe" or ".ddt")
                    findings.Add(new IntegrityFinding(IntegrityKind.Damaged,
                        Path.GetRelativePath(installPath, file).Replace('\\', '/'), "zero bytes"));
            }
            return Math.Min(allFiles.Length, 200);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// The leftovers on disk (<see cref="Repair.LeftoverCleanup.Select"/>). Read-only; any failure
    /// reads as none, because a verdict must never be made worse by a helper that could not run.
    /// </summary>
    private static IReadOnlyList<string> Leftovers(string installPath, ModProfile profile, InstallManifest manifest)
    {
        try
        {
            var owned = AddonOwnership.Load(installPath).Values.SelectMany(v => v);
            return Repair.LeftoverCleanup.Select(profile, manifest, owned,
                rel => File.Exists(Path.Combine(installPath, rel.Replace('/', Path.DirectorySeparatorChar))));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Leftover check skipped: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static void LogUnexpectedFiles(string installPath, InstallManifest manifest)
    {
        // Diagnostic only: a patched install legitimately gains untracked files.
        try
        {
            var extras = VerifyService.FindUnexpectedFiles(installPath, manifest);
            if (extras.Count == 0) return;
            DiagnosticLog.Write($"Verify: {extras.Count} unexpected/untracked file(s) (first 50):");
            foreach (var x in extras.Take(50)) DiagnosticLog.Write($"  [extra] {x}");
        }
        catch { }
    }

    /// <summary>One finding per path; the hashed verdict outranks engine, which outranks shape.</summary>
    private static IReadOnlyList<IntegrityFinding> Dedupe(List<IntegrityFinding> findings)
    {
        static int Rank(IntegrityKind k) => k switch
        {
            IntegrityKind.Engine => 1,
            IntegrityKind.Structural => 2,
            _ => 0,
        };
        return findings
            .GroupBy(f => Normalize(f.Path).TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => Rank(f.Kind)).First())
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static IntegrityKind OverlayKind(VerifyService.FileProblem p) => p switch
    {
        VerifyService.FileProblem.Missing => IntegrityKind.Missing,
        VerifyService.FileProblem.Unreadable => IntegrityKind.Unreadable,
        VerifyService.FileProblem.Blocked => IntegrityKind.Blocked,
        _ => IntegrityKind.Damaged,
    };

    private static string Describe(VerifyService.FileProblem p) => p switch
    {
        VerifyService.FileProblem.SizeMismatch => "size differs",
        VerifyService.FileProblem.HashMismatch => "content differs",
        VerifyService.FileProblem.Unreadable => "held open by another program",
        VerifyService.FileProblem.Blocked => "blocked by an antivirus",
        _ => "",
    };

    private static string Normalize(string rel) => rel.Replace('\\', '/');
}
