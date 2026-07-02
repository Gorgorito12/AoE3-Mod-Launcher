using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Thrown when a downloaded launcher update fails integrity (SHA-256) or
/// authenticity (Authenticode) verification. The caller surfaces this to the
/// user and the suspect binary is deleted rather than swapped in.
/// </summary>
public class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
}

/// <summary>
/// Checks GitHub Releases for a newer version of the launcher and applies
/// the update by replacing the running executable.
///
/// Release tags must follow semantic versioning (e.g. "v1.0.0" or "1.0.0").
/// The release must contain a single .exe asset — the new launcher binary.
/// </summary>
public class LauncherUpdateService
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/Gorgorito12/Updater/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public record UpdateCheckResult(
        bool UpdateAvailable,
        string CurrentVersion,
        string LatestVersion,
        string? DownloadUrl,
        long DownloadSize,
        string RemoteTag,
        string? ExpectedSha256 = null,
        string? ReleaseNotes = null,
        string? ResponseETag = null);

    /// <summary>
    /// The AssemblyVersion baked into this binary (the release build stamps it
    /// via <c>build-release.ps1 -Version</c> → <c>-p:Version</c>). Update
    /// detection is still tag-based; this is the <em>fallback</em> "current
    /// version" when there is no saved <c>LastInstalledLauncherTag</c> — e.g. a
    /// binary obtained outside the in-app self-updater (a manual download from
    /// GitHub Releases, or a freshly published build run straight from
    /// <c>publish\</c>). Without it such a binary can't recognise its own version
    /// and offers an "update" to the version it already is. See
    /// <see cref="EvaluateUpdate"/>.
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// The running binary's INFORMATIONAL version as a release tag — this is the
    /// one that can carry a WoL-style letter suffix ("v1.0.5a"), because the
    /// numeric <see cref="CurrentVersion"/> (AssemblyVersion) physically cannot
    /// (System.Version is integers-only). build-release.ps1 stamps
    /// <c>InformationalVersion=1.0.5a</c> while AssemblyVersion stays "1.0.5.0".
    /// Used as the self-recognition fallback for a binary with no saved tag (a
    /// manual download) so it doesn't offer an "update" to the very letter version
    /// it already is. Falls back to <see cref="FormatVersionTag(Version)"/> when no
    /// informational version is stamped (or it's just the numeric one).
    /// </summary>
    public static string CurrentInformationalTag
    {
        get
        {
            var raw = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(raw)) return FormatVersionTag(CurrentVersion);

            // Strip SourceLink build metadata ("1.0.5a+abc123" → "1.0.5a").
            var plus = raw.IndexOf('+');
            if (plus >= 0) raw = raw[..plus];
            raw = raw.Trim();
            if (raw.Length == 0) return FormatVersionTag(CurrentVersion);

            return raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? raw : "v" + raw;
        }
    }

    /// <summary>
    /// Queries GitHub for the latest release. Update detection is tag-based:
    /// the launcher considers an update available when GitHub's latest release
    /// tag differs from <paramref name="lastInstalledTag"/> (and isn't the tag
    /// the user previously dismissed via "Later").
    ///
    /// This decouples the update flow from the binary's AssemblyVersion, which
    /// means publishing a new release is just "upload to GitHub" — no need to
    /// bump csproj or coordinate version numbers.
    /// </summary>
    /// <param name="lastInstalledTag">
    /// The tag of the currently-running launcher (saved after the last
    /// successful in-app self-update). Empty for a binary that never self-updated
    /// in-app (a manual download / freshly published build) — in that case we
    /// fall back to the binary's stamped <see cref="CurrentVersion"/> as the
    /// effective current version (see <see cref="EvaluateUpdate"/>), so it doesn't
    /// offer an "update" to the very version it is already running.
    /// </param>
    /// <param name="skippedTag">
    /// A tag the user previously dismissed. We won't re-prompt for it.
    /// </param>
    /// <param name="cachedETag">
    /// The ETag returned by the previous successful check (persisted in config).
    /// Sent as If-None-Match so GitHub can answer 304 Not Modified when the
    /// latest release is unchanged — avoids burning the unauthenticated API
    /// rate-limit (60 req/h per IP, a real concern behind shared NAT). The
    /// caller persists <see cref="UpdateCheckResult.ResponseETag"/> for next time.
    /// </param>
    public static async Task<UpdateCheckResult> CheckAsync(
        string? lastInstalledTag = null,
        string? skippedTag = null,
        string? cachedETag = null,
        CancellationToken ct = default)
    {
        DiagnosticLog.Write(
            $"Launcher self-update check. Current tag: '{lastInstalledTag ?? ""}', " +
            $"AssemblyVersion: {CurrentVersion}");

        // Distinguishes "couldn't reach the server" (offline → report it) from "got a
        // response but it was an HTTP error" (server-side / rate-limit → NOT offline).
        bool reachedServer = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            if (!string.IsNullOrEmpty(cachedETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", cachedETag);

            using var response = await Http.SendAsync(request, ct);
            reachedServer = true;
            ConnectivityState.ReportSuccess();   // we reached the network

            // Latest release unchanged since last check — nothing to do, and we
            // keep the same ETag cached. Safe because any update the user hasn't
            // already installed would have been saved as the skipped tag on the
            // prior prompt, so the full path would also return NoUpdate here.
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                DiagnosticLog.Write("GitHub returned 304 Not Modified; release unchanged.");
                return NoUpdate(lastInstalledTag) with { ResponseETag = cachedETag };
            }

            response.EnsureSuccessStatusCode();
            var newETag = response.Headers.ETag?.ToString();

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct);
            if (release == null || string.IsNullOrEmpty(release.TagName))
                return NoUpdate(lastInstalledTag) with { ResponseETag = newETag };

            var remoteTag = release.TagName;

            // Decide whether this remote tag is genuinely newer than what we're
            // running. EvaluateUpdate falls back to the binary's stamped
            // AssemblyVersion when there's no saved tag (a manual download /
            // freshly published build), so we don't offer an "update" to the
            // version already running. Distinct reasons (already-on-latest,
            // dismissed, not-newer) all fold into a single not-offered branch.
            var (offer, currentLabel) =
                EvaluateUpdate(lastInstalledTag, CurrentVersion, skippedTag, remoteTag,
                    CurrentInformationalTag);
            if (!offer)
            {
                DiagnosticLog.Write(
                    $"No launcher update: remote {remoteTag} is not newer than current " +
                    $"{currentLabel} (saved tag '{lastInstalledTag ?? ""}', " +
                    $"skipped '{skippedTag ?? ""}').");
                return NoUpdate(lastInstalledTag) with { ResponseETag = newETag };
            }

            var asset = FindExeAsset(release);
            if (asset == null)
            {
                DiagnosticLog.Write("Remote release has no .exe asset.");
                return NoUpdate(lastInstalledTag) with { ResponseETag = newETag };
            }

            var expectedSha = ExtractExpectedSha256(asset.Digest, release.Body);

            DiagnosticLog.Write(
                $"Launcher update available: {currentLabel} -> {remoteTag} " +
                $"({asset.Name}, {asset.Size} bytes, sha256={expectedSha ?? "(none published)"})");

            return new UpdateCheckResult(
                UpdateAvailable: true,
                CurrentVersion: currentLabel,
                LatestVersion: remoteTag,
                DownloadUrl: asset.BrowserDownloadUrl,
                DownloadSize: asset.Size,
                RemoteTag: remoteTag,
                ExpectedSha256: expectedSha,
                ReleaseNotes: string.IsNullOrWhiteSpace(release.Body) ? null : release.Body.Trim(),
                ResponseETag: newETag);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Launcher update check failed: {ex.Message}");
            // Only an actual connectivity failure (no response received) is "offline";
            // a deliberate cancellation or a post-response HTTP error is not.
            if (!reachedServer && !ct.IsCancellationRequested)
                ConnectivityState.ReportFailure(ex);
            // Preserve the cached ETag so a transient failure doesn't force a
            // full (non-conditional) fetch on the next check.
            return NoUpdate(lastInstalledTag) with { ResponseETag = cachedETag };
        }
    }

    /// <summary>
    /// Downloads the new launcher .exe to a sibling temp file, then verifies its
    /// integrity (SHA-256) and authenticity (Authenticode) before declaring the
    /// download usable. Doesn't replace the running binary yet — call
    /// <see cref="RelaunchUpdated"/> after the user confirms.
    /// </summary>
    /// <param name="expectedSha256">
    /// Lowercase hex SHA-256 the downloaded .exe must match, as published by the
    /// release (GitHub asset digest or a "SHA256:" line in the release notes).
    /// When null/empty no hash was published — we log a warning and proceed (so
    /// older releases that predate published hashes still self-update), relying
    /// on the Authenticode check below as the remaining signal.
    /// </param>
    public static async Task DownloadUpdateAsync(
        string downloadUrl,
        string? expectedSha256 = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            throw new InvalidOperationException("Cannot determine current executable path.");

        var newExe = GetPendingUpdatePath(currentExe);

        DiagnosticLog.Write($"Downloading launcher update from: {downloadUrl}");
        DiagnosticLog.Write($"  -> {newExe}");

        // Best-effort cleanup of any prior aborted attempt
        try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }

        var downloader = new DownloadService();
        await downloader.DownloadFileAsync(downloadUrl, newExe, progress, ct);

        DiagnosticLog.Write("Launcher update download complete; verifying.");

        try
        {
            await VerifyDownloadAsync(newExe, currentExe, expectedSha256, ct);
        }
        catch
        {
            // Never leave an unverified binary sitting next to the .exe where a
            // later RelaunchUpdated could swap it in.
            try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
            throw;
        }

        DiagnosticLog.Write("Launcher update verified.");
    }

    /// <summary>
    /// Verifies the freshly-downloaded launcher binary before it can replace the
    /// running one. Two independent checks:
    ///   * SHA-256 — when a hash was published, the bytes on disk must match it
    ///     exactly (rejects truncated/corrupted/MITM'd downloads). No published
    ///     hash → warn and skip (back-compat with pre-hash releases).
    ///   * Authenticode — the downloaded .exe must carry a code signature whose
    ///     signer matches the CURRENTLY-RUNNING binary's signer. We read the
    ///     current signer at runtime instead of hard-coding "CN=Gorgorito" so a
    ///     future cert rotation needs no code change. If the running binary
    ///     itself is unsigned (signing is "automatic but optional" per the build
    ///     setup) we can't establish an expected signer, so we only warn.
    /// Either failed check throws <see cref="UpdateVerificationException"/>.
    /// </summary>
    private static async Task VerifyDownloadAsync(
        string newExe, string currentExe, string? expectedSha256, CancellationToken ct)
    {
        // 1. Integrity: SHA-256.
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = await HashService.ComputeSha256Async(newExe, ct);
            if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticLog.Write(
                    $"Update SHA-256 mismatch. expected={expectedSha256} actual={actual}");
                throw new UpdateVerificationException(
                    "The downloaded update failed its SHA-256 integrity check.");
            }
            DiagnosticLog.Write("Update SHA-256 matches published hash.");
        }
        else
        {
            DiagnosticLog.Write(
                "No SHA-256 published for this release; skipping integrity check.");
        }

        // 2. Authenticity: Authenticode signer must match the running binary's.
        var expectedSigner = TryGetAuthenticodeSubject(currentExe);
        var actualSigner = TryGetAuthenticodeSubject(newExe);

        if (expectedSigner == null)
        {
            DiagnosticLog.Write(
                "Running launcher is unsigned; cannot enforce signer match (skipping).");
            return;
        }

        if (actualSigner == null)
        {
            throw new UpdateVerificationException(
                "The downloaded update is not Authenticode-signed.");
        }

        if (!string.Equals(actualSigner, expectedSigner, StringComparison.OrdinalIgnoreCase))
        {
            DiagnosticLog.Write(
                $"Update signer mismatch. expected='{expectedSigner}' actual='{actualSigner}'");
            throw new UpdateVerificationException(
                "The downloaded update is signed by an unexpected publisher.");
        }

        DiagnosticLog.Write($"Update signer matches running binary ({actualSigner}).");
    }

    /// <summary>
    /// Returns the subject of the Authenticode certificate embedded in the given
    /// file, or null if the file carries no usable signature. Note this reads
    /// the embedded cert subject only — it does not validate the trust chain
    /// (that's the OS's job at load time); the meaningful guarantee here is
    /// "same signer as the binary the user already trusts and is running".
    /// </summary>
    private static string? TryGetAuthenticodeSubject(string filePath)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Renames the running executable to .old, swaps in the freshly downloaded
    /// one, and starts it. Caller should shut down the current process
    /// immediately after this returns.
    /// </summary>
    public static void RelaunchUpdated()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
            throw new InvalidOperationException("Cannot determine current executable path.");

        var newExe = GetPendingUpdatePath(currentExe);
        if (!File.Exists(newExe))
            throw new InvalidOperationException("No pending launcher update was downloaded.");

        var oldExe = currentExe + ".old";

        try { if (File.Exists(oldExe)) File.Delete(oldExe); } catch { }

        DiagnosticLog.Write("Replacing launcher executable...");

        // Step 1: move the running binary aside. On Windows a running .exe can be
        // renamed (the open handle keeps pointing at the moved file).
        File.Move(currentExe, oldExe);

        // Step 2: swap in the new binary. If this fails (AV lock, partial write,
        // disk full) we must NOT leave the launcher with no executable at its own
        // path — roll the original back so the user still has a working launcher.
        try
        {
            File.Move(newExe, currentExe);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write(
                $"Swap-in of new launcher failed ({ex.Message}); rolling back original.");
            try { File.Move(oldExe, currentExe); } catch { /* original already at oldExe */ }
            throw new InvalidOperationException(
                "Could not replace the launcher executable; the original was restored.", ex);
        }

        DiagnosticLog.Write("Starting updated launcher...");
        Process.Start(new ProcessStartInfo
        {
            FileName = currentExe,
            UseShellExecute = true
        });
    }

    private static string GetPendingUpdatePath(string currentExe)
    {
        var dir = Path.GetDirectoryName(currentExe)!;
        return Path.Combine(dir, "WarsOfLibertyLauncher_new.exe");
    }

    /// <summary>
    /// Removes leftover files from a previous self-update: the renamed-aside
    /// <c>.old</c> binary, and any <c>_new.exe</c> orphaned by a download that
    /// was aborted before the swap. Call this early on startup.
    /// </summary>
    public static void CleanupOldVersion()
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return;

        foreach (var stale in new[] { currentExe + ".old", GetPendingUpdatePath(currentExe) })
        {
            try
            {
                if (File.Exists(stale))
                {
                    File.Delete(stale);
                    DiagnosticLog.Write($"Cleaned up stale self-update file: {Path.GetFileName(stale)}");
                }
            }
            catch
            {
                // File may still be locked briefly after startup; ignore.
            }
        }
    }

    private static UpdateCheckResult NoUpdate(string? currentTag)
    {
        var label = string.IsNullOrEmpty(currentTag) ? "—" : currentTag!;
        return new(false, label, label, null, 0, currentTag ?? "", null, null);
    }

    /// <summary>
    /// Formats an assembly <see cref="Version"/> as a GitHub-style release tag
    /// ("0.9.9.0" → "v0.9.9"). <see cref="Version.Build"/> is floored at 0 so a
    /// 2-part version ("1.0", whose Build is -1) still yields a valid "v1.0.0".
    /// </summary>
    public static string FormatVersionTag(Version v) =>
        $"v{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>
    /// Pure, network-free update decision: given the saved tag, the running
    /// binary's <paramref name="assemblyVersion"/>, the dismissed tag, and the
    /// latest remote tag, decides whether to OFFER an update and what to show as
    /// the "current" version.
    ///
    /// Key rule: when <paramref name="lastInstalledTag"/> is empty (a binary that
    /// never self-updated in-app — a manual download or a freshly published
    /// build), the stamped <paramref name="assemblyVersion"/> is the effective
    /// current version. Without this, an empty tag always reads as "different
    /// from remote" and the launcher offers an "update" to the very version it is
    /// already running (shown as "current: —"). A saved tag, when present, takes
    /// precedence over the AssemblyVersion (it's the authoritative record).
    ///
    /// Extracted from <see cref="CheckAsync"/> so it can be unit-tested without
    /// touching the network.
    /// </summary>
    public static (bool offer, string currentLabel) EvaluateUpdate(
        string? lastInstalledTag, Version assemblyVersion, string? skippedTag, string remoteTag,
        string? currentInformationalTag = null)
    {
        // Effective-current = saved tag (authoritative) → else the informational
        // tag (can carry a letter, e.g. "v1.0.5a") → else the numeric AssemblyVersion.
        var effective = !string.IsNullOrEmpty(lastInstalledTag)
            ? lastInstalledTag!
            : (!string.IsNullOrWhiteSpace(currentInformationalTag)
                ? currentInformationalTag!
                : FormatVersionTag(assemblyVersion));

        // Already on this tag, or the user dismissed it via "Later".
        if (string.Equals(remoteTag, effective, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(remoteTag, skippedTag, StringComparison.OrdinalIgnoreCase))
            return (false, effective);

        // Semantic-version guard: only offer when the remote tag is strictly
        // NEWER. A re-published or rolled-back "latest" can carry an older
        // version, and without this we'd "update" the user backwards. Applied
        // whenever both the effective-current and remote tags parse as SemVer; a
        // non-SemVer saved tag keeps the prompt-on-difference fallback so we
        // never silently miss an update.
        if (TryParseSemVer(effective, out var installedVer) &&
            TryParseSemVer(remoteTag, out var remoteVer) &&
            remoteVer <= installedVer)
            return (false, effective);

        return (true, effective);
    }

    /// <summary>
    /// Parses a release tag into a comparable <see cref="Version"/>. Tolerates a
    /// leading "v"/"V" and a trailing pre-release/build suffix ("-rc1", "+commit"),
    /// and a WoL-style LETTER suffix on the patch ("1.0.5a", "1.0.15d"). The letter
    /// is packed into the <see cref="Version.Revision"/> component (a→1, b→2, …, and
    /// "aa"→27 for the unlikely 2-letter case) so the existing numeric comparison
    /// orders <c>1.0.5 &lt; 1.0.5a &lt; 1.0.5b &lt; 1.0.6</c> with no extra logic.
    /// No letter yields revision 0 (so a plain "1.0.5" is "1.0.5.0", consistently
    /// BELOW "1.0.5a"). Returns false for empty or non-numeric tags so callers can
    /// fall back to tag-difference behaviour rather than mis-ordering them.
    /// </summary>
    private static bool TryParseSemVer(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var core = tag.Trim().TrimStart('v', 'V');
        // Drop any pre-release ("-rc1") or build ("+sha") metadata.
        var cut = core.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) core = core[..cut];

        // Split a trailing letter suffix off the numeric core ("1.0.5a" → "1.0.5" + "a").
        int letterStart = core.Length;
        while (letterStart > 0 && char.IsAsciiLetter(core[letterStart - 1]))
            letterStart--;
        var numeric = core[..letterStart];
        var letters = core[letterStart..];

        if (!Version.TryParse(numeric, out var baseVer) || baseVer == null)
            return false;

        int letterRank = LetterRank(letters);
        version = new Version(
            Math.Max(0, baseVer.Major),
            Math.Max(0, baseVer.Minor),
            Math.Max(0, baseVer.Build),
            letterRank);
        return true;
    }

    /// <summary>
    /// Ordinal rank of a lowercase letter suffix: "" → 0, "a" → 1 … "z" → 26,
    /// "aa" → 27 (base-26). Used to pack the WoL-style patch letter into the
    /// version's Revision so versions sort naturally.
    /// </summary>
    private static int LetterRank(string letters)
    {
        if (string.IsNullOrEmpty(letters)) return 0;
        int rank = 0;
        foreach (var ch in letters.ToLowerInvariant())
        {
            if (ch < 'a' || ch > 'z') return 0;   // unexpected → treat as no suffix
            rank = rank * 26 + (ch - 'a' + 1);
        }
        return rank;
    }

    private static GitHubAsset? FindExeAsset(GitHubRelease release)
    {
        if (release.Assets == null) return null;

        // Prefer the canonical launcher binary by exact name so a release that
        // also ships, say, a helper or installer .exe doesn't get picked by
        // accident. Fall back to the first .exe only when there's no exact match.
        GitHubAsset? firstExe = null;
        foreach (var asset in release.Assets)
        {
            if (!asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(asset.Name, "Aoe3ModLauncher.exe", StringComparison.OrdinalIgnoreCase))
                return asset;
            firstExe ??= asset;
        }
        return firstExe;
    }

    /// <summary>
    /// Resolves the expected SHA-256 (lowercase hex, 64 chars) of the update
    /// asset from, in order of preference: (1) GitHub's per-asset <c>digest</c>
    /// field ("sha256:…", populated for newer releases); (2) a "SHA256:" /
    /// "SHA-256:" line in the release notes body — which is exactly what
    /// build-release.ps1 prints for the maintainer to paste. Returns null when
    /// neither is present.
    /// </summary>
    private static string? ExtractExpectedSha256(string? assetDigest, string? releaseBody)
    {
        if (!string.IsNullOrWhiteSpace(assetDigest))
        {
            var m = Regex.Match(assetDigest, @"sha256:([0-9a-fA-F]{64})", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(releaseBody))
        {
            var m = Regex.Match(releaseBody, @"sha-?256\s*[:=]\s*([0-9a-fA-F]{64})",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Cap the wait so a slow/unreachable GitHub doesn't stall the startup
            // WhenAll for the default 100 s. The self-update is non-critical at
            // boot; if the check times out we just proceed from cached state.
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WarsOfLibertyLauncher");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    // Minimal DTOs for the GitHub Releases API response
    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// Content digest GitHub computes for the asset, e.g. "sha256:abc…".
        /// Present on newer releases; null on older ones (we fall back to the
        /// release body — see <see cref="ExtractExpectedSha256"/>).
        /// </summary>
        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
