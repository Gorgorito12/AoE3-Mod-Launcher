using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Reports download progress.</summary>
public record DownloadProgress(long BytesReceived, long TotalBytes, double Percentage);

/// <summary>
/// HTTP download with progress, resume (HTTP Range), pause/resume, automatic
/// retry of transient network failures, and primary/alt URL fallback.
///
/// Pause vs cancel: both keep the .part file, so the next call to
/// DownloadFileAsync with the same destination resumes from where it left off
/// (HTTP Range). Only an explicit temp sweep (NativeInstallService.TryCleanupTemp /
/// CleanupTempPayload) discards partial downloads.
/// </summary>
public class DownloadService
{
    private readonly HttpClient _http;

    /// <summary>
    /// Pause flag. While true, ongoing downloads stop writing data and
    /// idle until either Pause is set back to false or the operation is
    /// cancelled.
    /// </summary>
    public bool Pause { get; set; }

    public DownloadService(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler)
        {
            // Long timeout — patches can be 100+ MB
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WarsOfLibertyLauncher/0.2");
        return client;
    }

    /// <summary>
    /// Downloads a file, falling back from <paramref name="primaryUrl"/> to
    /// <paramref name="alternateUrl"/> if the primary fails. Supports resume.
    /// </summary>
    public async Task DownloadWithFallbackAsync(
        string primaryUrl,
        string alternateUrl,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            await DownloadFileAsync(primaryUrl, destinationPath, progress, ct);
        }
        catch (Exception ex) when (!string.IsNullOrEmpty(alternateUrl) && !ct.IsCancellationRequested)
        {
            // Log before falling over. This used to be a bare `catch when`, so a
            // primary mirror that failed for EVERY user was invisible in the
            // diagnostic bundle — the download simply appeared to work, from the
            // alternate, with nothing saying the first host was dead.
            DiagnosticLog.Write(
                $"Download from primary '{primaryUrl}' failed ({ex.GetType().Name}: {ex.Message}); " +
                $"falling back to '{alternateUrl}'.");
            await DownloadFileAsync(alternateUrl, destinationPath, progress, ct);
        }
    }

    /// <summary>
    /// How many times in a row an attempt may fail with a transient network error
    /// before the download gives up. Retrying is cheap because the <c>.part</c> file
    /// survives and the next attempt resumes from it via HTTP Range — nothing already
    /// downloaded is fetched twice.
    /// </summary>
    private const int MaxAttemptsWithoutProgress = 4;

    /// <summary>
    /// Absolute cap on attempts, so a server that accepts the connection and then
    /// drops it after a handful of bytes can't spin here forever. Only reached when
    /// attempts keep making SOME progress; a genuinely dead host stops after
    /// <see cref="MaxAttemptsWithoutProgress"/>.
    /// </summary>
    private const int MaxTotalAttempts = 12;

    /// <summary>
    /// Whether a failed download attempt is worth retrying.
    ///
    /// <para><paramref name="userCancelled"/> is the caller's token state, and it is
    /// what separates the two things that both surface as
    /// <see cref="OperationCanceledException"/>: a user pressing Cancel (never retry)
    /// and an <see cref="HttpClient"/> TIMEOUT (exactly what we want to retry). The
    /// exception type alone cannot tell them apart — <see cref="TaskCanceledException"/>
    /// is used for both — which is the same trap
    /// <see cref="ConnectivityState.IsNetworkError"/> documents.</para>
    ///
    /// Pure and static so the policy is unit-testable without a socket.
    /// </summary>
    internal static bool IsTransientDownloadFailure(Exception? ex, bool userCancelled)
    {
        if (ex == null) return false;

        // The user's own cancellation outranks everything, including a wrapped
        // network error that happened on the way down.
        if (userCancelled) return false;

        // Two passes, not one, and the order is the point: a permission problem
        // ANYWHERE in the chain wins over a transport error wrapped around it.
        // Walking once and returning on the first match would retry a read-only
        // destination four times just because the framework reported it as an
        // IOException with the real cause inside.
        for (var e = ex; e != null; e = e.InnerException)
        {
            // Disk / permission problems are not going to fix themselves, and
            // retrying would just rewrite the same failure three more times.
            if (e is UnauthorizedAccessException) return false;
        }

        for (var e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case HttpRequestException:
                case SocketException:
                case TimeoutException:
                // Covers TaskCanceledException (an HttpClient timeout) — the
                // userCancelled check above has already ruled out a real cancel.
                case OperationCanceledException:
                case IOException:
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Downloads a file with progress and resume support, retrying transient network
    /// failures.
    ///
    /// <para>WHY the retry lives here rather than at the call sites: the WoL payload is
    /// ~4 GB split across three parts and <c>NativeInstallService</c> downloads them in
    /// a loop. Retrying per PART means a blip while fetching part 3 never re-downloads
    /// parts 1 and 2 — and within a part, the <c>.part</c> file plus HTTP Range means
    /// the retry picks up at the byte it stopped on. Before this, a single dropped
    /// connection threw an <see cref="IOException"/> straight out of the install (which
    /// is not the <see cref="InvalidDataException"/> the installer's own retry loop
    /// catches), so a multi-gigabyte download died on a momentary hiccup.</para>
    /// </summary>
    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var tempPath = destinationPath + ".part";
        int attemptsWithoutProgress = 0;

        for (int attempt = 1; ; attempt++)
        {
            long bytesBefore = SafePartLength(tempPath);
            try
            {
                await DownloadOnceAsync(url, destinationPath, progress, ct);
                return;
            }
            catch (Exception ex)
            {
                if (!IsTransientDownloadFailure(ex, ct.IsCancellationRequested))
                    throw;

                // Progress since the last attempt resets the budget: a long download
                // over a flaky link should be allowed to hiccup more than a handful of
                // times, as long as it keeps advancing.
                long bytesAfter = SafePartLength(tempPath);
                attemptsWithoutProgress = bytesAfter > bytesBefore ? 0 : attemptsWithoutProgress + 1;

                if (attemptsWithoutProgress >= MaxAttemptsWithoutProgress
                    || attempt >= MaxTotalAttempts)
                {
                    DiagnosticLog.Write(
                        $"Download of '{url}' gave up after {attempt} attempt(s) " +
                        $"({bytesAfter} bytes on disk): {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                var delay = RetryDelay(attemptsWithoutProgress);
                DiagnosticLog.Write(
                    $"Download of '{url}' failed on attempt {attempt} " +
                    $"({ex.GetType().Name}: {ex.Message}); {bytesAfter} bytes already on disk, " +
                    $"resuming in {delay.TotalSeconds:0}s.");
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>Backoff for the Nth consecutive fruitless attempt: 2s, 4s, 8s…</summary>
    private static TimeSpan RetryDelay(int consecutiveFailures) =>
        TimeSpan.FromSeconds(Math.Min(8, 1 << Math.Max(1, consecutiveFailures)));

    /// <summary>
    /// Length of the partial file, or 0 when it isn't there / can't be read. Used only
    /// to decide whether an attempt advanced, so a failure to measure must not throw
    /// over the top of the download error we're already handling.
    /// </summary>
    private static long SafePartLength(string tempPath)
    {
        try { return File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// One download attempt. Resumes from an existing <c>.part</c> file when the server
    /// supports Range. Kept separate from <see cref="DownloadFileAsync"/> so each retry
    /// re-reads the partial file's length and issues a fresh request.
    /// </summary>
    private async Task DownloadOnceAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempPath = destinationPath + ".part";
        long existingBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

        // First request: ask for the partial range if we already have something on disk.
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        // 416 Range Not Satisfiable: our .part file is at or past the remote
        // size. This typically happens when a previous attempt finished the
        // download but the launcher was closed/cancelled before the file got
        // renamed and the patch applied. Do a HEAD to learn the real size:
        //   - if our local size matches remote → file is already complete,
        //     just rename and report 100%.
        //   - otherwise → wipe and restart from byte 0.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            response.Dispose();
            request.Dispose();

            long remoteSize = await TryGetRemoteSizeAsync(url, ct);
            if (remoteSize > 0 && existingBytes == remoteSize)
            {
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(tempPath, destinationPath);
                progress?.Report(new DownloadProgress(remoteSize, remoteSize, 100.0));
                return;
            }

            // Local file is wrong size (corrupt or mismatched). Start over.
            try { File.Delete(tempPath); } catch { }
            existingBytes = 0;

            request = new HttpRequestMessage(HttpMethod.Get, url);
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        // If server doesn't support Range, restart from zero.
        if (existingBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            existingBytes = 0;
            try { File.Delete(tempPath); } catch { }
        }

        try
        {
            response.EnsureSuccessStatusCode();

            // Try to learn the total size up-front. Some servers send Content-Length
            // on a HEAD request even when they use chunked transfer for GET.
            long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;
            if (totalBytes <= 0)
            {
                try
                {
                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                    using var headResponse = await _http.SendAsync(headRequest, ct);
                    if (headResponse.IsSuccessStatusCode)
                    {
                        var headLen = headResponse.Content.Headers.ContentLength;
                        if (headLen.HasValue) totalBytes = headLen.Value + existingBytes;
                    }
                }
                catch
                {
                    // HEAD not supported on this server — proceed without a known total
                }
            }

            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            await using var destStream = new FileStream(
                tempPath, FileMode.Append, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true);

            var buffer = new byte[1024 * 1024];
            long received = existingBytes;
            int read;

            // Initial 0-byte report so the UI shows "Downloading..." instead of
            // staying blank until the first chunk arrives.
            progress?.Report(new DownloadProgress(received, totalBytes,
                totalBytes > 0 ? (double)received / totalBytes * 100.0 : 0));

            while ((read = await sourceStream.ReadAsync(buffer, ct)) > 0)
            {
                // Honor the pause flag — if the user pauses mid-download, stop
                // pulling bytes from the server. The .part file stays on disk so
                // the next call resumes via HTTP Range.
                while (Pause && !ct.IsCancellationRequested)
                {
                    await Task.Delay(200, ct);
                }
                ct.ThrowIfCancellationRequested();

                await destStream.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;

                // Always report progress, even when total is unknown — the UI
                // can decide what to display when TotalBytes is 0 (typically:
                // bytes received + speed without a percentage).
                double pct = totalBytes > 0
                    ? (double)received / totalBytes * 100.0
                    : 0;
                progress?.Report(new DownloadProgress(received, totalBytes, pct));
            }

            await destStream.FlushAsync(ct);
            destStream.Close();

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);
        }
        finally
        {
            response.Dispose();
            request.Dispose();
        }
    }

    /// <summary>
    /// HEAD-probe the URL to learn its byte length. Returns -1 if the server
    /// doesn't supply a Content-Length or HEAD isn't supported. Public so the
    /// install pipeline can pre-compute total download size before starting,
    /// which keeps the progress bar usable from the very first byte.
    /// </summary>
    public async Task<long> TryGetRemoteSizeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await _http.SendAsync(headRequest, ct);
            if (headResponse.IsSuccessStatusCode)
                return headResponse.Content.Headers.ContentLength ?? -1;
        }
        catch
        {
            // Falls through to -1
        }
        return -1;
    }

    /// <summary>Downloads a string (used for delete-list files).</summary>
    public async Task<string> DownloadStringAsync(string url, CancellationToken ct = default)
    {
        return await _http.GetStringAsync(url, ct);
    }
}
