using System.IO;
using System.IO.Compression;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Seeds a mod's <c>Documents\My Games\&lt;mod&gt;</c> folder from a second release asset
/// (<see cref="ModProfile.UserDataPayload"/>) — the folder skeleton, AI personalities and starter
/// profile some mods need to exist BEFORE they will start. <i>Knights and Barbarians</i> is the
/// mod this was built for: without its subfolders the engine falls back to the stock maps.
///
/// <para><b>The rule this class exists to enforce: copy-if-absent, never overwrite.</b> The
/// destination is not ours — it holds the player's saves, home-city decks, profile and hotkeys.
/// A payload able to replace a file there would be a data-loss bug wearing a feature's clothes,
/// and it would be silent, because the launcher would report a perfectly successful install. So
/// the decision is pure and pinned (<c>UserDataPayloadTests</c>), and the writer opens with
/// <see cref="FileMode.CreateNew"/> so even a race cannot clobber.</para>
///
/// <para>The mirror rule lives here too: <see cref="SelectForRemoval"/> decides what uninstall
/// may take back. Keeping both halves in one file is deliberate — "what we created" and "what we
/// may delete" are the same invariant read from two ends, and splitting them is how they drift.</para>
/// </summary>
public static class UserDataPayloadService
{
    /// <summary>One file the seed would create: zip entry name → root-relative destination.</summary>
    public readonly record struct PlannedFile(string EntryName, string RelativePath);

    /// <summary>
    /// What a seed would do: the files to create, the directories to create (parents first), and
    /// how many entries were skipped because the player already has that file.
    /// </summary>
    public sealed record SeedPlan(
        IReadOnlyList<PlannedFile> Files,
        IReadOnlyList<string> Dirs,
        int Skipped);

    public enum ApplyStatus
    {
        /// <summary>The mod declares no payload — the case for almost every mod.</summary>
        NotDeclared,
        /// <summary>No user-data folder could be resolved, or it resolved to vanilla's.</summary>
        NoFolder,
        /// <summary>The release carries no asset with that name.</summary>
        AssetMissing,
        /// <summary>Resolving or downloading the asset failed (offline, rate limit, bad zip).</summary>
        Failed,
        /// <summary>Everything recorded is still in place; nothing was fetched.</summary>
        AlreadySeeded,
        /// <summary>The payload was applied (possibly creating nothing, if the player had it all).</summary>
        Applied,
    }

    public sealed record ApplyResult(
        ApplyStatus Status, int FilesCreated, int FilesSkipped, int DirsCreated, string Root);

    // ------------------------------------------------------------------ pure rules

    /// <summary>
    /// A zip entry name turned into a root-relative forward-slash path, or null when the entry is
    /// refused: empty, rooted (<c>C:\…</c> or a leading slash) or climbing out with <c>..</c>.
    ///
    /// <para>The refusals are the point. Both existing extractors clamp to their own root, and
    /// this destination needs it more than either: one <c>..\Age of Empires 3\Users3\</c> entry
    /// would write a mod's files into the player's BASE GAME profile. The textual check here is
    /// belt to the writer's braces — that also re-resolves every path against the real root.</para>
    /// </summary>
    internal static string? NormalizeEntry(string? entryName, string prefix)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;

        var raw = entryName.Replace('\\', '/').Trim();
        if (raw.Length == 0) return null;
        if (raw.StartsWith('/')) return null;                  // rooted
        if (raw.Length >= 2 && raw[1] == ':') return null;      // drive-qualified

        if (prefix.Length > 0)
        {
            var head = prefix + "/";
            if (!raw.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return null;
            raw = raw[head.Length..];
            if (raw.Length == 0) return null;                   // the wrapper folder entry itself
        }

        var parts = new List<string>();
        foreach (var seg in raw.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") return null;                        // escapes the root
            parts.Add(seg);
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    /// <summary>
    /// The wrapper folder to strip, or "" when the zip is already rooted at the folder's contents.
    ///
    /// <para>Deliberately NOT <c>NativeInstallService.ResolvePayloadPrefix</c>, which strips ANY
    /// single shared top-level folder. That is right for a mod payload and wrong here: a userdata
    /// zip holding only <c>Users3\…</c> would have <c>Users3</c> stripped and its profile written
    /// loose in the root. Only the folder the payload is FOR is ever stripped, so the zip may be
    /// rooted either at <c>Knights and Barbarians\</c> or at its contents, and nothing else is
    /// guessed at.</para>
    /// </summary>
    internal static string ResolveWrapper(IEnumerable<string> entryNames, string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return "";
        var head = folderName.Trim() + "/";
        bool any = false;
        foreach (var e in entryNames)
        {
            var raw = (e ?? "").Replace('\\', '/').Trim();
            if (raw.Length == 0) continue;
            any = true;
            if (!raw.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return "";
        }
        return any ? folderName.Trim() : "";
    }

    /// <summary>
    /// THE decision: given the zip's entries and two disk probes, what may be written.
    /// A file whose destination already exists is DROPPED, never queued — so it cannot be
    /// overwritten, and (because only queued files are recorded) uninstall can never reach it.
    /// Directory entries and the parents of queued files are created when missing.
    /// </summary>
    internal static SeedPlan Plan(
        IReadOnlyList<string> entryNames,
        string folderName,
        Func<string, bool> fileExists,
        Func<string, bool> dirExists)
    {
        var prefix = ResolveWrapper(entryNames, folderName);
        var files = new List<PlannedFile>();
        var dirs = new List<string>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        void WantDir(string rel)
        {
            if (rel.Length == 0) return;
            var acc = "";
            foreach (var seg in rel.Split('/'))
            {
                acc = acc.Length == 0 ? seg : acc + "/" + seg;
                if (!seenDirs.Add(acc)) continue;
                if (!dirExists(acc)) dirs.Add(acc);
            }
        }

        foreach (var entry in entryNames)
        {
            var name = entry ?? "";
            var isDir = name.EndsWith('/') || name.EndsWith('\\');
            var rel = NormalizeEntry(name, prefix);
            if (rel == null) continue;

            if (isDir) { WantDir(rel); continue; }

            var slash = rel.LastIndexOf('/');
            if (slash > 0) WantDir(rel[..slash]);

            if (fileExists(rel)) { skipped++; continue; }
            files.Add(new PlannedFile(name, rel));
        }

        return new SeedPlan(files, dirs, skipped);
    }

    /// <summary>
    /// Which recorded files uninstall may remove: only those whose bytes are still EXACTLY what
    /// the launcher wrote. A file the player has since played with — the game rewrites its
    /// profile on every run, and a deck the moment it is edited — has stopped being ours and is
    /// kept. Pure, because this is the one decision in the feature that can destroy a save.
    /// </summary>
    internal static IReadOnlyList<string> SelectForRemoval(
        IReadOnlyDictionary<string, FileFingerprint> recorded,
        Func<string, FileFingerprint?> currentFingerprint)
    {
        var take = new List<string>();
        foreach (var (rel, wrote) in recorded)
        {
            if (wrote == null || string.IsNullOrEmpty(wrote.Sha256)) continue;
            var now = currentFingerprint(rel);
            if (now == null) continue;                      // already gone: nothing to remove
            if (now.Size != wrote.Size) continue;           // changed: theirs now
            if (!string.Equals(now.Sha256, wrote.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            take.Add(rel);
        }
        take.Sort(StringComparer.Ordinal);
        return take;
    }

    // ------------------------------------------------------------------ the disk/network shell

    /// <summary>
    /// Resolve → download → copy-if-absent → record in the install manifest. Best-effort: this
    /// never throws to the caller, exactly like the addon re-apply it sits beside. A mod that
    /// still starts must not report a failed install, and Repair re-runs the whole thing.
    /// </summary>
    public static async Task<ApplyResult> ApplyAsync(
        ModProfile profile,
        LauncherConfig config,
        string installPath,
        string releaseTag,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        var none = new ApplyResult(ApplyStatus.NotDeclared, 0, 0, 0, "");
        if (profile == null || string.IsNullOrWhiteSpace(profile.UserDataPayload)) return none;
        if (profile.IsStockGame) return none;

        try
        {
            var folderName = UserDataService.ResolveFolderName(profile, config);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                DiagnosticLog.Write(
                    $"User-data payload for '{profile.Id}': no folder resolved — skipping.");
                return new ApplyResult(ApplyStatus.NoFolder, 0, 0, 0, "");
            }

            // Already done? Checked BEFORE any network call, so repeat repairs of an intact
            // install cost nothing — this runs on every repair, update and delta hop.
            var manifest = InstallManifest.TryLoad(installPath);
            if (SeedLooksComplete(manifest, folderName))
                return new ApplyResult(ApplyStatus.AlreadySeeded, 0, 0, 0, manifest!.UserDataRoot);

            var (root, rootCreated) = UserDataService.EnsureUserDataFolder(folderName);
            if (string.IsNullOrEmpty(root))
                return new ApplyResult(ApplyStatus.NoFolder, 0, 0, 0, "");

            var gh = profile.GitHubReleases;
            if (gh == null || string.IsNullOrWhiteSpace(gh.SourceRepo) || string.IsNullOrWhiteSpace(releaseTag))
                return new ApplyResult(ApplyStatus.Failed, 0, 0, 0, root);

            statusProgress?.Report(Localization.Strings.Get("StatusApplyingUserData"));

            var assets = await new GitHubReleaseDownloader()
                .ListAssetsAsync(gh.SourceRepo, releaseTag, ct);
            var asset = assets.FirstOrDefault(a =>
                string.Equals(a.Name, profile.UserDataPayload, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                DiagnosticLog.Write(
                    $"User-data payload '{profile.UserDataPayload}' is not on release " +
                    $"'{releaseTag}' of {gh.SourceRepo} — skipping.");
                return new ApplyResult(ApplyStatus.AssetMissing, 0, 0, 0, root);
            }

            var tempDir = Path.Combine(AppPaths.InstallTempRoot, "userdata");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, $"{profile.Id}-userdata.zip");
            await new DownloadService().DownloadFileAsync(asset.Url, zipPath, null, ct);

            var result = Seed(zipPath, root, folderName, rootCreated, installPath, ct);
            TryDelete(zipPath);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Non-fatal on purpose: the mod is installed and playable, the seed is not.
            DiagnosticLog.Write($"User-data payload failed (non-fatal): {ex.Message}");
            return new ApplyResult(ApplyStatus.Failed, 0, 0, 0, "");
        }
    }

    /// <summary>
    /// True when the manifest says this exact folder was already seeded and every file and
    /// directory it recorded is still there. Deliberately strict: one missing piece re-runs the
    /// whole seed, which is safe because the seed only ever ADDS what is absent.
    /// </summary>
    private static bool SeedLooksComplete(InstallManifest? manifest, string folderName)
    {
        if (manifest == null || string.IsNullOrEmpty(manifest.UserDataRoot)) return false;
        if (manifest.UserDataFiles.Count == 0 && manifest.UserDataDirs.Count == 0) return false;

        // The recorded root has to still be the folder this mod resolves to; a renamed or
        // re-discovered folder is a different destination and must be seeded again.
        if (!string.Equals(
                Path.GetFileName(manifest.UserDataRoot.TrimEnd(Path.DirectorySeparatorChar)),
                folderName.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (!Directory.Exists(manifest.UserDataRoot)) return false;
            foreach (var d in manifest.UserDataDirs)
                if (!Directory.Exists(Path.Combine(
                        manifest.UserDataRoot, d.Replace('/', Path.DirectorySeparatorChar))))
                    return false;
            foreach (var f in manifest.UserDataFiles.Keys)
                if (!File.Exists(Path.Combine(
                        manifest.UserDataRoot, f.Replace('/', Path.DirectorySeparatorChar))))
                    return false;
            return true;
        }
        catch { return false; }
    }

    private static ApplyResult Seed(
        string zipPath, string root, string folderName, bool rootCreated,
        string installPath, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        var rootFull = Path.GetFullPath(root);
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull : rootFull + Path.DirectorySeparatorChar;

        string Abs(string rel) => Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar));

        var plan = Plan(entryNames, folderName,
            rel => File.Exists(Abs(rel)), rel => Directory.Exists(Abs(rel)));

        var createdDirs = new List<string>();
        foreach (var d in plan.Dirs)
        {
            ct.ThrowIfCancellationRequested();
            var abs = Abs(d);
            if (!Under(abs, rootWithSep)) continue;
            if (Directory.Exists(abs)) continue;
            Directory.CreateDirectory(abs);
            createdDirs.Add(d);
        }

        var written = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in archive.Entries) byName[e.FullName] = e;

        foreach (var f in plan.Files)
        {
            ct.ThrowIfCancellationRequested();
            var abs = Abs(f.RelativePath);
            if (!Under(abs, rootWithSep)) continue;
            if (!byName.TryGetValue(f.EntryName, out var entry)) continue;

            var parent = Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            try
            {
                // CreateNew, never Create: the plan already dropped existing files, and this is
                // what makes a race lose safely rather than overwrite a save.
                using var dest = new FileStream(abs, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var src = entry.Open();
                src.CopyTo(dest);
            }
            catch (IOException) { continue; }   // appeared underneath us — the player's file wins

            var info = new FileInfo(abs);
            written[f.RelativePath] = new FileFingerprint(
                info.Length, HashService.ComputeSha256Async(abs, ct).GetAwaiter().GetResult());
        }

        Record(installPath, root, rootCreated, written, createdDirs);

        DiagnosticLog.Write(
            $"User-data payload seeded into '{root}': {written.Count} file(s) created, " +
            $"{plan.Skipped} already present, {createdDirs.Count} folder(s) created.");
        return new ApplyResult(ApplyStatus.Applied, written.Count, plan.Skipped, createdDirs.Count, root);
    }

    private static bool Under(string absolutePath, string rootWithSep)
    {
        var full = Path.GetFullPath(absolutePath);
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Merge what this run created into the install manifest. A union, not a replacement: an
    /// earlier run's records must survive, or uninstall forgets the half it created first.
    /// </summary>
    private static void Record(
        string installPath, string root, bool rootCreated,
        Dictionary<string, FileFingerprint> written, List<string> createdDirs)
    {
        try
        {
            var manifest = InstallManifest.TryLoad(installPath);
            if (manifest == null) return;

            // A different root means the folder moved; the old record no longer describes
            // anything we could find, so it is replaced rather than merged into.
            if (!string.Equals(manifest.UserDataRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                manifest.UserDataRoot = root;
                manifest.UserDataFiles = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
                manifest.UserDataDirs = new List<string>();
                manifest.UserDataRootCreated = rootCreated;
            }

            foreach (var (rel, fp) in written) manifest.UserDataFiles[rel] = fp;
            foreach (var d in createdDirs)
                if (!manifest.UserDataDirs.Contains(d, StringComparer.OrdinalIgnoreCase))
                    manifest.UserDataDirs.Add(d);
            manifest.UserDataRootCreated |= rootCreated;
            manifest.Save();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"User-data payload: recording into the manifest failed: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
