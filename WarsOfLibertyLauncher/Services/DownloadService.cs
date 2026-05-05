using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services;

/// <summary>Reports download progress.</summary>
public record DownloadProgress(long BytesReceived, long TotalBytes, double Percentage);

/// <summary>
/// HTTP download with progress, resume (HTTP Range), pause/resume, and
/// primary/alt URL fallback.
///
/// Pause vs cancel:
///   - Cancel deletes the .part file and starts fresh next time.
///   - Pause keeps the .part file. The next call to DownloadFileAsync with
///     the same destination resumes from where it left off (HTTP Range).
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
        catch when (!string.IsNullOrEmpty(alternateUrl) && !ct.IsCancellationRequested)
        {
            await DownloadFileAsync(alternateUrl, destinationPath, progress, ct);
        }
    }

    /// <summary>
    /// Downloads a file with progress and resume support.
    /// </summary>
    public async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempPath = destinationPath + ".part";
        long existingBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        // If server doesn't support Range, restart from zero.
        if (existingBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            existingBytes = 0;
            File.Delete(tempPath);
        }

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

    /// <summary>Downloads a string (used for delete-list files).</summary>
    public async Task<string> DownloadStringAsync(string url, CancellationToken ct = default)
    {
        return await _http.GetStringAsync(url, ct);
    }
}
